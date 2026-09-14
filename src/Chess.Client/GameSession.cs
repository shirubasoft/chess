using Chess.Contracts;
using System.Text.Json;

namespace Chess.Client;

public sealed class GameSession : IDisposable
{
    private readonly string settingsPath;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private HttpClient http;
    private ChessClient client;
    private readonly List<PendingEntry> pendingEntries = [];

    public GameSession(string clientName, string settingsPath, string defaultServer = "http://localhost:5080/")
    {
        ClientName = clientName;
        this.settingsPath = settingsPath;
        Server = defaultServer;
        RejectLink(settingsPath);
        if (File.Exists(settingsPath))
        {
            var settings = File.ReadAllLines(settingsPath);
            if (settings.Length > 0 && IsServer(settings[0])) Server = settings[0];
            if (settings.Length > 1) SavedCode = settings[1];
            if (settings.Length > 2)
                pendingEntries.AddRange(JsonSerializer.Deserialize<PendingEntry[]>(settings[2], GameJson.Options) ?? []);
        }
        http = CreateHttp(Server);
        client = new ChessClient(http, ClientName);
    }

    public string ClientName { get; }
    public string Server { get; private set; }
    public string SavedCode { get; private set; } = "";
    public GameAccess? Access { get; private set; }
    public GameSnapshot? Snapshot { get; private set; }
    public string Message { get; private set; } = "Create a game, find an opponent, or resume with your side's code.";
    public bool IsBusy { get; private set; }
    public string? SelectedSquare { get; private set; }
    public string? PromotionMove { get; private set; }
    public event Action? Changed;

    public bool CanMove => Access is { } access && Snapshot is { Status: GameStatus.Active } snapshot
        && access.Side == snapshot.SideToMove && !IsBusy;

    public bool CanAct(GameAction action) => !IsBusy && Access is { } access && Snapshot is { Status: GameStatus.Active } snapshot
        && action switch
        {
            GameAction.Move => snapshot.SideToMove == access.Side,
            GameAction.AcceptDraw or GameAction.DeclineDraw => snapshot.DrawOfferedBy is { } side && side != access.Side,
            GameAction.OfferDraw => snapshot.DrawOfferedBy is null,
            GameAction.ClaimFiftyMoves or GameAction.ClaimThreefold => snapshot.SideToMove == access.Side,
            GameAction.Resign => true,
            _ => false
        };

    public string Opponent => Access is { } access && Snapshot is { } snapshot
        ? (access.Side == PlayerSide.White ? snapshot.Black : snapshot.White).ClientName ?? "Waiting for an opponent"
        : "No opponent yet";

    public string Status => Snapshot is not { } snapshot ? "Ready to play" : snapshot.Status switch
    {
        GameStatus.Waiting => "Waiting for an opponent",
        GameStatus.Finished => snapshot.Result?.Winner is { } winner
            ? $"{winner} wins · {ResultReason(snapshot.Result.Reason)}" : $"Draw · {ResultReason(snapshot.Result?.Reason)}",
        _ => $"{snapshot.SideToMove} to move" + (snapshot.DrawOfferedBy is { } side ? $" · {side} offers a draw" : "")
    };

    public string History => Snapshot is not { } snapshot || snapshot.Moves.Length == 0 ? "No moves yet."
        : string.Join("  ", snapshot.Moves.Select(move => move.Side == PlayerSide.White
            ? $"{(move.Ply + 1) / 2}. {move.San}" : move.San));

    public Task CreateAsync(string server) => OpenAsync(server, false);

    public Task MatchmakeAsync(string server) => OpenAsync(server, true);

    private Task OpenAsync(string server, bool matchmaking) => RunAsync(async () =>
    {
        server = NormalizeServer(server);
        var pending = pendingEntries.FirstOrDefault(p => p.Server == server && p.Matchmaking == matchmaking && p.ClientName == ClientName);
        if (pending is null)
        {
            pending = new PendingEntry(server, matchmaking, ClientName, Guid.NewGuid());
            pendingEntries.Add(pending);
        }
        Save();
        using var requestHttp = CreateHttp(server);
        var requestClient = new ChessClient(requestHttp, ClientName);
        var access = matchmaking
            ? await requestClient.MatchmakeAsync(pending.RequestId, lifetime.Token)
            : await requestClient.CreateAsync(requestId: pending.RequestId, cancellationToken: lifetime.Token);
        pendingEntries.Remove(pending);
        SetAccess(server, access);
        Message = !matchmaking ? "Share the opponent code. Keep your own code to resume this side."
            : Snapshot?.Status == GameStatus.Waiting ? "Looking for another player. You can close the app and resume by code."
            : "Opponent found. Your game is ready.";
    });

    public Task JoinAsync(string server, string code) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Enter your side's game code.");
        server = NormalizeServer(server);
        using var requestHttp = CreateHttp(server);
        var access = await new ChessClient(requestHttp, ClientName).JoinAsync(code.Trim(), lifetime.Token);
        SetAccess(server, access);
        Message = "Game restored. Your side is saved on this device.";
    });

    public Task RefreshAsync() => RunAsync(async () =>
    {
        if (Access is { } access) Update(await client.GetAsync(access, lifetime.Token));
        Message = Access is null ? "Join or create a game first." : "Up to date.";
    });

    public Task<bool> MoveAsync(string notation) => RunAsync(async () =>
    {
        var (access, snapshot) = Current();
        var value = notation.Trim();
        if (value.Length == 0) throw new ArgumentException("Enter a move in SAN or UCI notation, such as e4 or e2e4.");
        var format = value.Length is 4 or 5 && value[0] is >= 'a' and <= 'h' && value[1] is >= '1' and <= '8'
            && value[2] is >= 'a' and <= 'h' && value[3] is >= '1' and <= '8' ? MoveNotation.Uci : MoveNotation.San;
        Update(await client.MoveAsync(access, snapshot, value, format, cancellationToken: lifetime.Token));
        Message = $"Played {value}.";
    });

    public Task ActAsync(GameAction action) => RunAsync(async () =>
    {
        var (access, snapshot) = Current();
        Update(await client.CommandAsync(access, new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = snapshot.Revision, Action = action
        }, lifetime.Token));
        Message = Status;
    });

    public async Task SelectSquareAsync(string square)
    {
        if (!CanMove || Snapshot is not { } snapshot) return;
        PromotionMove = null;
        if (SelectedSquare is { } from)
        {
            var prefix = from + square;
            var candidates = snapshot.LegalMoves.Where(move => move.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (candidates.Length == 1)
            {
                await MoveAsync(candidates[0]);
                return;
            }
            if (candidates.Length > 1)
            {
                PromotionMove = prefix;
                Message = "Choose the promotion piece.";
                Changed?.Invoke();
                return;
            }
        }
        SelectedSquare = snapshot.LegalMoves.Any(move => move.StartsWith(square, StringComparison.Ordinal)) ? square : null;
        Message = SelectedSquare is null ? "Select one of your pieces with a legal move." : $"Choose a destination for {square}.";
        Changed?.Invoke();
    }

    public Task PromoteAsync(char piece) => PromotionMove is { } move ? MoveAsync(move + piece) : Task.CompletedTask;

    public bool IsDestination(string square) => SelectedSquare is { } from && Snapshot is { } snapshot
        && snapshot.LegalMoves.Any(move => move.StartsWith(from + square, StringComparison.Ordinal));

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(linked.Token))
            {
                if (Access is null || IsBusy) continue;
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }

    private (GameAccess, GameSnapshot) Current() => Access is { } access && Snapshot is { } snapshot
        ? (access, snapshot) : throw new ArgumentException("Join or create a game first.");

    private static string NormalizeServer(string server)
    {
        server = server.Trim().TrimEnd('/') + "/";
        if (!IsServer(server)) throw new ArgumentException("Enter an absolute HTTP or HTTPS server address.");
        return server;
    }

    private void SetAccess(string server, GameAccess access)
    {
        if (server != Server)
        {
            http.Dispose();
            http = CreateHttp(server);
            client = new ChessClient(http, ClientName);
            Server = server;
        }
        Access = access;
        SavedCode = access.Code;
        Snapshot = access.Snapshot;
        SelectedSquare = null;
        PromotionMove = null;
        Save();
    }

    private void Update(GameSnapshot snapshot)
    {
        if (Snapshot is { } previous && previous.GameId == snapshot.GameId && previous.Revision > snapshot.Revision) return;
        var changed = Snapshot?.Revision != snapshot.Revision;
        Snapshot = snapshot;
        if (changed) { SelectedSquare = null; PromotionMove = null; }
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        if (!await operation.WaitAsync(0, lifetime.Token)) return false;
        try
        {
            IsBusy = true;
            Changed?.Invoke();
            await action();
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or ChessServerException or ArgumentException or IOException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Message = exception is TaskCanceledException ? "The server did not respond. Check the address and refresh."
                : exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            operation.Release();
            Changed?.Invoke();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
        RejectLink(settingsPath);
        var temporary = settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine(Server);
                writer.WriteLine(SavedCode);
                writer.WriteLine(JsonSerializer.Serialize(pendingEntries, GameJson.Options));
                writer.Flush();
                stream.Flush(true);
            }
            RejectLink(settingsPath);
            File.Move(temporary, settingsPath, true);
        }
        finally { File.Delete(temporary); }
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("The chess settings file cannot be a symbolic link.");
    }

    private sealed record PendingEntry(string Server, bool Matchmaking, string ClientName, Guid RequestId);

    private static bool IsServer(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static HttpClient CreateHttp(string server) => new() { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(20) };

    private static string ResultReason(string? reason) => reason switch
    {
        "Checkmate" => "Checkmate", "Resignation" => "Resignation", "Stalemate" => "Stalemate",
        "DeadPosition" => "No possible checkmate", "Agreement" => "By agreement",
        "ThreefoldRepetition" => "Threefold repetition", "FivefoldRepetition" => "Fivefold repetition",
        "FiftyMoveRule" => "50-move rule", "SeventyFiveMoveRule" => "75-move rule",
        "ResignationWithoutMatingMaterial" => "Opponent cannot checkmate", _ => reason ?? "Game over"
    };

    public void Dispose()
    {
        lifetime.Cancel();
        http.Dispose();
        lifetime.Dispose();
    }
}
