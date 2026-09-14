using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chess.Contracts;
using Chess.Notation;
using Npgsql;
using NpgsqlTypes;

namespace Chess.Server.Application;

public sealed class PostgresGameStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(714203001);
            CREATE TABLE IF NOT EXISTS chess_games (
                id uuid PRIMARY KEY,
                revision bigint NOT NULL,
                document jsonb NOT NULL,
                white_code_hash bytea NOT NULL UNIQUE,
                black_code_hash bytea NOT NULL UNIQUE,
                waiting boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS chess_waiting_games ON chess_games(created_at) WHERE waiting;
            CREATE TABLE IF NOT EXISTS chess_updates (
                game_id uuid NOT NULL REFERENCES chess_games(id),
                revision bigint NOT NULL,
                snapshot jsonb NOT NULL,
                PRIMARY KEY(game_id, revision)
            );
            CREATE TABLE IF NOT EXISTS chess_commands (
                game_id uuid NOT NULL REFERENCES chess_games(id),
                side integer NOT NULL,
                request_id uuid NOT NULL,
                fingerprint bytea NOT NULL,
                snapshot jsonb NOT NULL,
                PRIMARY KEY(game_id, side, request_id)
            );
            CREATE TABLE IF NOT EXISTS chess_entries (
                request_id uuid PRIMARY KEY,
                fingerprint bytea NOT NULL,
                access jsonb NOT NULL
            );
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<GameAccess> CreateAsync(CreateGameRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequestId(request.RequestId);
        var clientName = ValidateClient(request.ClientName);
        var position = request.InitialFen is null ? Position.Initial : Fen.Parse(request.InitialFen) switch
        {
            Parsed<Position> parsed => parsed.Value,
            NotationError error => throw GameFault.Invalid(error.Message)
        };
        var fingerprint = Fingerprint("create", request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockEntryAsync(connection, transaction, request.RequestId, cancellationToken);
        if (await ReadEntryAsync(connection, transaction, request.RequestId, fingerprint, cancellationToken) is { } previous) return previous;
        var (game, whiteCode, blackCode) = NewGame(clientName, Fen.Format(position));
        var snapshot = GameDecisions.Snapshot(game);
        await InsertGameAsync(connection, transaction, game, whiteCode, blackCode, false, snapshot, cancellationToken);
        var access = Access(game, whiteCode, PlayerSide.White, snapshot) with { OpponentCode = blackCode };
        await WriteEntryAsync(connection, transaction, request.RequestId, fingerprint, access, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return access;
    }

    public async Task<GameAccess> MatchmakeAsync(MatchmakingRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequestId(request.RequestId);
        var clientName = ValidateClient(request.ClientName);
        var fingerprint = Fingerprint("matchmake", request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockEntryAsync(connection, transaction, request.RequestId, cancellationToken);
        if (await ReadEntryAsync(connection, transaction, request.RequestId, fingerprint, cancellationToken) is { } previous) return previous;
        await using (var queueLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(714203002)", connection, transaction))
            await queueLock.ExecuteNonQueryAsync(cancellationToken);
        GameDocument? waiting;
        await using (var find = new NpgsqlCommand("SELECT document::text FROM chess_games WHERE waiting ORDER BY created_at, id LIMIT 1 FOR UPDATE", connection, transaction))
            waiting = await find.ExecuteScalarAsync(cancellationToken) is string json ? Deserialize<GameDocument>(json) : null;
        GameAccess access;
        if (waiting is null)
        {
            var (game, whiteCode, blackCode) = NewGame(clientName, Fen.Format(Position.Initial));
            var snapshot = GameDecisions.Snapshot(game);
            await InsertGameAsync(connection, transaction, game, whiteCode, blackCode, true, snapshot, cancellationToken);
            access = Access(game, whiteCode, PlayerSide.White, snapshot);
        }
        else
        {
            var code = NewCode();
            var joined = waiting with { Revision = waiting.Revision + 1, BlackClient = clientName };
            var snapshot = GameDecisions.Snapshot(joined);
            await using (var replaceCode = new NpgsqlCommand("UPDATE chess_games SET black_code_hash = @hash WHERE id = @id", connection, transaction))
            {
                replaceCode.Parameters.AddWithValue("hash", HashCode(code));
                replaceCode.Parameters.AddWithValue("id", joined.Id);
                await replaceCode.ExecuteNonQueryAsync(cancellationToken);
            }
            await UpdateGameAsync(connection, transaction, joined, snapshot, cancellationToken);
            access = Access(joined, code, PlayerSide.Black, snapshot);
        }
        await WriteEntryAsync(connection, transaction, request.RequestId, fingerprint, access, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return access;
    }

    public async Task<GameAccess> JoinAsync(JoinGameRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashCode(request.Code);
        var clientName = ValidateClient(request.ClientName);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        GameDocument game;
        PlayerSide side;
        await using (var find = new NpgsqlCommand("""
            SELECT document::text, white_code_hash FROM chess_games
            WHERE white_code_hash = @hash OR black_code_hash = @hash FOR UPDATE
            """, connection, transaction))
        {
            find.Parameters.AddWithValue("hash", hash);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw GameFault.Unauthorized();
            game = Deserialize<GameDocument>(reader.GetString(0));
            side = CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(1), hash) ? PlayerSide.White : PlayerSide.Black;
        }
        var currentName = side == PlayerSide.White ? game.WhiteClient : game.BlackClient;
        if (currentName != clientName)
        {
            game = side == PlayerSide.White
                ? game with { WhiteClient = clientName, Revision = game.Revision + 1 }
                : game with { BlackClient = clientName, Revision = game.Revision + 1 };
            await UpdateGameAsync(connection, transaction, game, GameDecisions.Snapshot(game), cancellationToken);
        }
        var access = Access(game, request.Code, side, GameDecisions.Snapshot(game));
        await transaction.CommitAsync(cancellationToken);
        return access;
    }

    public async Task<GameSnapshot> ReadAsync(GameId gameId, string code, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await AuthorizeAsync(connection, gameId, code, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT snapshot::text FROM chess_updates WHERE game_id = @id ORDER BY revision DESC LIMIT 1", connection);
        command.Parameters.AddWithValue("id", gameId.Value);
        return Deserialize<GameSnapshot>((string)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Missing game snapshot.")));
    }

    public async Task<GameSnapshot[]> ReadUpdatesAsync(GameId gameId, string code, long afterRevision, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await AuthorizeAsync(connection, gameId, code, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT snapshot::text FROM chess_updates WHERE game_id = @id AND revision > @revision ORDER BY revision LIMIT 100", connection);
        command.Parameters.AddWithValue("id", gameId.Value);
        command.Parameters.AddWithValue("revision", afterRevision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var snapshots = new List<GameSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) snapshots.Add(Deserialize<GameSnapshot>(reader.GetString(0)));
        return snapshots.ToArray();
    }

    public async Task<GameSnapshot> CommandAsync(GameId gameId, string code, GameCommandRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequestId(request.RequestId);
        var hash = HashCode(code);
        var fingerprint = Fingerprint("command", request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        GameDocument game;
        PlayerSide side;
        await using (var find = new NpgsqlCommand("SELECT document::text, white_code_hash, black_code_hash FROM chess_games WHERE id = @id FOR UPDATE", connection, transaction))
        {
            find.Parameters.AddWithValue("id", gameId.Value);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw GameFault.Unauthorized();
            if (CryptographicOperations.FixedTimeEquals(hash, reader.GetFieldValue<byte[]>(1))) side = PlayerSide.White;
            else if (CryptographicOperations.FixedTimeEquals(hash, reader.GetFieldValue<byte[]>(2))) side = PlayerSide.Black;
            else throw GameFault.Unauthorized();
            game = Deserialize<GameDocument>(reader.GetString(0));
        }
        await using (var receipt = new NpgsqlCommand("SELECT fingerprint, snapshot::text FROM chess_commands WHERE game_id = @id AND side = @side AND request_id = @request", connection, transaction))
        {
            receipt.Parameters.AddWithValue("id", game.Id);
            receipt.Parameters.AddWithValue("side", (int)side);
            receipt.Parameters.AddWithValue("request", request.RequestId);
            await using var reader = await receipt.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                EnsureSameRequest(fingerprint, reader.GetFieldValue<byte[]>(0));
                return Deserialize<GameSnapshot>(reader.GetString(1));
            }
        }
        var updated = GameDecisions.Apply(game, side, request);
        var snapshot = GameDecisions.Snapshot(updated);
        await UpdateGameAsync(connection, transaction, updated, snapshot, cancellationToken);
        await using (var receipt = new NpgsqlCommand("INSERT INTO chess_commands(game_id, side, request_id, fingerprint, snapshot) VALUES(@id, @side, @request, @fingerprint, @snapshot)", connection, transaction))
        {
            receipt.Parameters.AddWithValue("id", game.Id);
            receipt.Parameters.AddWithValue("side", (int)side);
            receipt.Parameters.AddWithValue("request", request.RequestId);
            receipt.Parameters.AddWithValue("fingerprint", fingerprint);
            Json(receipt, "snapshot", snapshot);
            await receipt.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    private static async Task AuthorizeAsync(NpgsqlConnection connection, GameId gameId, string code, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM chess_games WHERE id = @id AND (white_code_hash = @hash OR black_code_hash = @hash)", connection);
        command.Parameters.AddWithValue("id", gameId.Value);
        command.Parameters.AddWithValue("hash", HashCode(code));
        if (await command.ExecuteScalarAsync(cancellationToken) is null) throw GameFault.Unauthorized();
    }

    private static async Task InsertGameAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, GameDocument game,
        string whiteCode, string blackCode, bool waiting, GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO chess_games(id, revision, document, white_code_hash, black_code_hash, waiting)
            VALUES(@id, @revision, @document, @white, @black, @waiting)
            """, connection, transaction);
        command.Parameters.AddWithValue("id", game.Id);
        command.Parameters.AddWithValue("revision", game.Revision);
        Json(command, "document", game);
        command.Parameters.AddWithValue("white", HashCode(whiteCode));
        command.Parameters.AddWithValue("black", HashCode(blackCode));
        command.Parameters.AddWithValue("waiting", waiting);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteSnapshotAsync(connection, transaction, snapshot, cancellationToken);
    }

    private static async Task UpdateGameAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, GameDocument game, GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE chess_games SET revision = @revision, document = @document, waiting = CASE WHEN @joined THEN false ELSE waiting END WHERE id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", game.Id);
        command.Parameters.AddWithValue("revision", game.Revision);
        Json(command, "document", game);
        command.Parameters.AddWithValue("joined", game.BlackClient is not null);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteSnapshotAsync(connection, transaction, snapshot, cancellationToken);
    }

    private static async Task WriteSnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("INSERT INTO chess_updates(game_id, revision, snapshot) VALUES(@id, @revision, @snapshot)", connection, transaction);
        command.Parameters.AddWithValue("id", snapshot.GameId);
        command.Parameters.AddWithValue("revision", snapshot.Revision);
        Json(command, "snapshot", snapshot);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockEntryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@request, 714203003))", connection, transaction);
        command.Parameters.AddWithValue("request", requestId.ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<GameAccess?> ReadEntryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT fingerprint, access::text FROM chess_entries WHERE request_id = @request", connection, transaction);
        command.Parameters.AddWithValue("request", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        EnsureSameRequest(fingerprint, reader.GetFieldValue<byte[]>(0));
        return Deserialize<GameAccess>(reader.GetString(1));
    }

    private static async Task WriteEntryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, byte[] fingerprint, GameAccess access, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("INSERT INTO chess_entries(request_id, fingerprint, access) VALUES(@request, @fingerprint, @access)", connection, transaction);
        command.Parameters.AddWithValue("request", requestId);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        Json(command, "access", access);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (GameDocument Game, string WhiteCode, string BlackCode) NewGame(string clientName, string initialFen) =>
        (new GameDocument { Id = Guid.NewGuid(), Revision = 0, InitialFen = initialFen, WhiteClient = clientName }, NewCode(), NewCode());

    private static GameAccess Access(GameDocument game, string code, PlayerSide side, GameSnapshot snapshot) =>
        new() { GameId = game.Id, Code = code, Side = side, Snapshot = snapshot };

    private static void Json<T>(NpgsqlCommand command, string name, T value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, GameJson.Options));

    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, GameJson.Options)
        ?? throw new InvalidOperationException("Missing stored game data.");

    private static byte[] Fingerprint<T>(string operation, T value) => SHA256.HashData(Encoding.UTF8.GetBytes(operation + JsonSerializer.Serialize(value, GameJson.Options)));

    private static void EnsureSameRequest(byte[] expected, byte[] actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new GameFault(409, "request_id_reused", "This requestId was already used for a different request.");
    }

    private static string NewCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static byte[] HashCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 32 || !code.All(Uri.IsHexDigit)) throw GameFault.Unauthorized();
        return SHA256.HashData(Encoding.ASCII.GetBytes(code.ToLowerInvariant()));
    }

    private static string ValidateClient(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl))
            throw GameFault.Invalid("clientName must contain 1 to 64 printable characters.");
        return name.Trim();
    }

    private static void ValidateRequestId(Guid id)
    {
        if (id == Guid.Empty) throw GameFault.Invalid("A nonempty requestId is required.");
    }
}
