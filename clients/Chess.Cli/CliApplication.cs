using System.Net.Http;
using System.Text.Json;
using Chess.Client;
using Chess.Contracts;
using Chess.Notation;

namespace Chess.Cli;

public static class CliApplication
{
    public const string ClientName = "Chess CLI";
    public const string Help = """
        dotnet chess [tui] [--server URL] [--session FILE]
        dotnet chess create [--fen FEN]
        dotnet chess join CODE
        dotnet chess resume CODE
        dotnet chess random
        dotnet chess show
        dotnet chess history
        dotnet chess move SAN [--notation san|uci]
        dotnet chess wait [--timeout SECONDS] [--after-ply PLY]
        dotnet chess resign
        dotnet chess draw offer|accept|decline
        dotnet chess claim threefold|fifty

        Every command accepts --server URL and --session FILE. Set CHESS_SERVER_URL
        or save a server with your first command. Use --match UUID --code CODE to
        address a different game. Codes grant access to a side; keep your code private
        and share only opponentCode from create. 'join' and 'resume' both restore a side.
        Mutating commands accept --request-id UUID for safe retries.

        Commands write one JSON result to stdout. Errors go to stderr. Exit codes:
        0 success, 1 server/network error, 2 usage/session error, 124 wait timeout,
        130 cancellation. Commands never prompt. Only tui or no command starts the TUI.
        'wait' returns after an opponent move following the saved snapshot, or game
        completion. --after-ply selects a known move boundary. Timeout defaults to 300s.
        SAN promotion: a8=Q. UCI promotion: a7a8q --notation uci.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = CliArguments.Parse(args);
            if (parsed.Command == "help") { await output.WriteLineAsync(Help); return 0; }
            var sessionFile = new SessionFile(parsed.Option("session") ?? SessionFile.DefaultPath);
            var session = await sessionFile.ReadAsync(cancellationToken);
            var address = ServerUri(parsed.Option("server") ?? Environment.GetEnvironmentVariable("CHESS_SERVER_URL") ?? session?.Server);
            if (session?.Server != address.AbsoluteUri) session = new() { Server = address.AbsoluteUri };
            using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(45) };
            var client = new ChessClient(http, ClientName);
            var context = new CliContext(client, sessionFile, session ?? new() { Server = address.AbsoluteUri });
            if (parsed.Command == "tui")
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected) throw new CliUsageException("The TUI needs a terminal. Use show, move, or other commands for automation.");
                return await ChessTui.RunAsync(context, cancellationToken);
            }
            if (parsed.Command is "create" or "join" or "resume" or "random")
            {
                var access = parsed.Command switch
                {
                    "create" => await context.CreateAsync(parsed.Option("fen"), parsed.RequestId, cancellationToken),
                    "random" => await context.MatchmakeAsync(parsed.RequestId, cancellationToken),
                    _ => await client.JoinAsync(parsed.RequiredArgument(0, "game code"), cancellationToken)
                };
                await context.SaveAsync(access, cancellationToken);
                await WriteAsync(output, access);
                return 0;
            }
            var current = await context.ResolveAsync(parsed.Option("match"), parsed.Option("code"), cancellationToken);
            if (parsed.Command == "wait")
            {
                var timeout = parsed.Number("timeout", 300, 1, 86400);
                var afterPly = parsed.Number("after-ply", current.Snapshot.Moves.LastOrDefault()?.Ply ?? 0, 0, int.MaxValue);
                var next = await context.WaitForOpponentAsync(current, afterPly, TimeSpan.FromSeconds(timeout), cancellationToken);
                if (next is null) { await WriteAsync(output, new { @event = "timeout", current.GameId }); return 124; }
                await WriteAsync(output, next);
                return 0;
            }
            var snapshot = await client.GetAsync(current, cancellationToken);
            if (parsed.Command == "move")
                snapshot = await context.MoveAsync(current, snapshot, parsed.RequiredArgument(0, "move"), parsed.Notation, parsed.RequestId, cancellationToken);
            else if (parsed.Command is "resign" or "draw" or "claim")
                snapshot = await context.ActionAsync(current, snapshot, ActionOf(parsed), parsed.RequestId, cancellationToken);
            await context.SaveAsync(current with { Snapshot = snapshot }, cancellationToken);
            if (parsed.Command == "history") await WriteAsync(output, snapshot.Moves);
            else await WriteAsync(output, snapshot);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { await WriteAsync(error, new GameError { Code = "cancelled", Message = "Command cancelled." }); return 130; }
        catch (ChessServerException exception)
        { await WriteAsync(error, exception.Error); return 1; }
        catch (Exception exception) when (exception is CliUsageException or JsonException or IOException or UnauthorizedAccessException)
        { await WriteAsync(error, new GameError { Code = "invalid_input", Message = exception.Message }); return 2; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        { await WriteAsync(error, new GameError { Code = "connection_error", Message = "Could not reach the chess server. Check --server and try again." }); return 1; }
    }

    public static Uri ServerUri(string? address)
    {
        if (address is null) throw new CliUsageException("Set --server URL or CHESS_SERVER_URL for the first command.");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new CliUsageException("The server must be an http or https URL without credentials, query, or fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    private static GameAction ActionOf(CliArguments args) => (args.Command, args.Arguments.FirstOrDefault()) switch
    {
        ("resign", _) => GameAction.Resign,
        ("draw", "offer") => GameAction.OfferDraw,
        ("draw", "accept") => GameAction.AcceptDraw,
        ("draw", "decline") => GameAction.DeclineDraw,
        ("claim", "threefold") => GameAction.ClaimThreefold,
        ("claim", "fifty") => GameAction.ClaimFiftyMoves,
        _ => throw new CliUsageException("Use draw offer|accept|decline or claim threefold|fifty.")
    };
    private static Task WriteAsync<T>(TextWriter writer, T value) => writer.WriteLineAsync(JsonSerializer.Serialize(value, GameJson.Options));
}

public sealed class CliContext(ChessClient client, SessionFile file, CliSession session)
{
    public ChessClient Client { get; } = client;
    public CliSession Session { get; private set; } = session;

    public async Task SaveAsync(GameAccess access, CancellationToken cancellationToken)
    {
        await PersistAsync(Session with { Access = access }, cancellationToken);
    }

    private async Task PersistAsync(CliSession next, CancellationToken cancellationToken)
    {
        await file.WriteAsync(next, cancellationToken);
        Session = next;
    }

    public Task<GameAccess> CreateAsync(string? initialFen = null, Guid? requestId = null, CancellationToken cancellationToken = default) =>
        EnterAsync(GameEntryKind.Create, initialFen, requestId, cancellationToken);

    public Task<GameAccess> MatchmakeAsync(Guid? requestId = null, CancellationToken cancellationToken = default) =>
        EnterAsync(GameEntryKind.Matchmaking, null, requestId, cancellationToken);

    private async Task<GameAccess> EnterAsync(GameEntryKind kind, string? initialFen, Guid? requestId, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty) throw new CliUsageException("A nonempty request ID is required.");
        if (initialFen is not null && Fen.Parse(initialFen) is NotationError invalidFen)
            throw new CliUsageException(invalidFen.Message);
        var pending = Session.PendingEntry;
        if (pending is not null && requestId is null && (pending.Kind != kind || pending.InitialFen != initialFen))
            throw new CliUsageException("A previous create or random request has no confirmed response. Retry that command first, or supply a new --request-id to replace it.");
        var entry = new SavedGameEntry { Kind = kind, InitialFen = initialFen, RequestId = requestId ?? pending?.RequestId ?? Guid.NewGuid() };
        await PersistAsync(Session with { PendingEntry = entry }, cancellationToken);
        try
        {
            var access = kind == GameEntryKind.Create
                ? await Client.CreateAsync(initialFen, entry.RequestId, cancellationToken)
                : await Client.MatchmakeAsync(entry.RequestId, cancellationToken);
            await PersistAsync(Session with { Access = access, PendingEntry = null }, cancellationToken);
            return access;
        }
        catch (ChessServerException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404 or 409 or 422)
        {
            await PersistAsync(Session with { PendingEntry = null }, cancellationToken);
            throw;
        }
    }

    public async Task<GameAccess> ResolveAsync(string? match, string? code, CancellationToken cancellationToken)
    {
        if ((match is null) != (code is null)) throw new CliUsageException("Supply --match and --code together.");
        if (match is null) return Session.Access ?? throw new CliUsageException("No current game. Create, join, or use random first; or supply --match and --code.");
        var gameId = CliArguments.ParseGuid(match, "match");
        if (Session.Access is { } saved && saved.GameId == gameId && saved.Code == code) return saved;
        var access = await Client.JoinAsync(code!, cancellationToken);
        if (access.GameId != gameId) throw new CliUsageException("The game code does not belong to the specified match.");
        return access;
    }

    public Task<GameSnapshot> ActionAsync(GameAccess access, GameSnapshot snapshot, GameAction action, Guid? requestId, CancellationToken cancellationToken) =>
        SendCommandAsync(access, snapshot, new GameCommandRequest
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedRevision = snapshot.Revision, Action = action
        }, requestId is null, cancellationToken);

    public Task<GameSnapshot> MoveAsync(GameAccess access, GameSnapshot snapshot, string move, MoveNotation notation, Guid? requestId, CancellationToken cancellationToken) =>
        SendCommandAsync(access, snapshot, new GameCommandRequest
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedRevision = snapshot.Revision,
            Action = GameAction.Move, Move = move, Notation = notation
        }, requestId is null, cancellationToken);

    private async Task<GameSnapshot> SendCommandAsync(GameAccess access, GameSnapshot observed, GameCommandRequest command, bool reusePending, CancellationToken cancellationToken)
    {
        if (Session.LastCommand is { } previous)
        {
            var sameSide = previous.GameId == access.GameId && (previous.Side is null || previous.Side == access.Side);
            var sameCommand = previous.Request with { RequestId = command.RequestId, ExpectedRevision = command.ExpectedRevision } == command;
            if (previous.Request.RequestId == command.RequestId && (!sameSide || !sameCommand))
                throw new CliUsageException("This request ID was already used for a different command.");
            if (previous.Request.RequestId == command.RequestId || reusePending && previous.IsPending && sameSide && sameCommand)
                command = previous.Request;
        }
        var saved = new SavedCommand { GameId = access.GameId, Side = access.Side, Request = command, IsPending = true };
        await PersistAsync(Session with { Access = access, LastCommand = saved }, cancellationToken);
        try
        {
            var receipt = await Client.CommandAsync(access, command, cancellationToken);
            var snapshot = observed.GameId == receipt.GameId && observed.Revision > receipt.Revision ? observed : receipt;
            await PersistAsync(Session with { Access = access with { Snapshot = snapshot }, LastCommand = saved with { IsPending = false } }, cancellationToken);
            return snapshot;
        }
        catch (ChessServerException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404 or 409 or 422)
        {
            await PersistAsync(Session with { LastCommand = saved with { IsPending = false } }, cancellationToken);
            throw;
        }
    }

    public async Task<GameSnapshot?> WaitForOpponentAsync(GameAccess access, int afterPly, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var snapshot = await Client.GetAsync(access, deadline.Token);
            while (true)
            {
                if (snapshot.Status == GameStatus.Finished || snapshot.Moves.Any(move => move.Ply > afterPly && move.Side != access.Side))
                {
                    await SaveAsync(access with { Snapshot = snapshot }, deadline.Token);
                    return snapshot;
                }
                snapshot = await Client.WaitAsync(access, snapshot.Revision, 30, deadline.Token) ?? snapshot;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }
}
