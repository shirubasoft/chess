using System.Net.Http;
using System.Text.Json;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Cli.Tests;

public sealed class ResponseLossTests
{
    [Test, RequiresChessServer, NotInParallel("cli-matchmaking")]
    [Arguments(GameEntryKind.Create)]
    [Arguments(GameEntryKind.Matchmaking)]
    public async Task EntryRetryAfterRestartRecoversTheOriginalSideCode(GameEntryKind kind)
    {
        using var process = new CliProcess();
        var file = new SessionFile(process.Session);
        using var lostResponse = new DropSuccessfulPostHandler(kind == GameEntryKind.Create ? "/api/games" : "/api/matchmaking");
        using var faultyHttp = new HttpClient(lostResponse) { BaseAddress = new Uri(process.Server) };
        var first = new CliContext(new ChessClient(faultyHttp, CliApplication.ClientName), file, new() { Server = process.Server });
        await Assert.That(async () => { await EnterAsync(first, kind); }).Throws<HttpRequestException>();
        var committed = JsonSerializer.Deserialize<GameAccess>(lostResponse.DroppedBody!, GameJson.Options)!;
        var saved = (await file.ReadAsync(CancellationToken.None))!;
        await Assert.That(saved.PendingEntry).IsNotNull();
        await Assert.That(saved.Access).IsNull();

        using var http = new HttpClient { BaseAddress = new Uri(process.Server) };
        var client = new ChessClient(http, CliApplication.ClientName);
        var restarted = new CliContext(client, file, saved);
        var recovered = await EnterAsync(restarted, kind);
        await Assert.That(recovered.GameId).IsEqualTo(committed.GameId);
        await Assert.That(recovered.Code).IsEqualTo(committed.Code);
        await Assert.That(recovered.Side).IsEqualTo(committed.Side);
        await Assert.That((await file.ReadAsync(CancellationToken.None))!.PendingEntry).IsNull();
        if (kind == GameEntryKind.Matchmaking && recovered.Snapshot.Status == GameStatus.Waiting)
        {
            var other = await new ChessClient(http, "Response-loss test opponent").MatchmakeAsync();
            await Assert.That(other.GameId).IsEqualTo(recovered.GameId);
            await client.CommandAsync(recovered, new()
            {
                RequestId = Guid.NewGuid(), ExpectedRevision = other.Snapshot.Revision, Action = GameAction.Resign
            });
        }
    }

    [Test, RequiresChessServer]
    public async Task LostMoveResponseCanBeRetriedWithoutAnExplicitIdAfterRestart()
    {
        using var process = new CliProcess();
        var file = new SessionFile(process.Session);
        using var http = new HttpClient { BaseAddress = new Uri(process.Server) };
        var client = new ChessClient(http, CliApplication.ClientName);
        var white = await client.CreateAsync();
        var black = await client.JoinAsync(white.OpponentCode!);
        white = white with { Snapshot = black.Snapshot };
        await file.WriteAsync(new() { Server = process.Server, Access = white }, CancellationToken.None);
        using var drop = new DropSuccessfulPostHandler("/commands");
        using var faultyHttp = new HttpClient(drop) { BaseAddress = new Uri(process.Server) };
        var first = new CliContext(new ChessClient(faultyHttp, CliApplication.ClientName), file, (await file.ReadAsync(CancellationToken.None))!);
        await Assert.That(async () => { await first.MoveAsync(white, white.Snapshot, "e4", MoveNotation.San, null, CancellationToken.None); }).Throws<HttpRequestException>();
        var saved = (await file.ReadAsync(CancellationToken.None))!;
        await Assert.That(saved.LastCommand!.IsPending).IsTrue();
        var afterWhite = await client.GetAsync(white);
        var afterBlack = await client.MoveAsync(black, afterWhite, "e5");

        var restarted = new CliContext(client, file, saved);
        var result = await restarted.MoveAsync(white, afterBlack, "e4", MoveNotation.San, null, CancellationToken.None);
        await Assert.That(result.Revision).IsEqualTo(afterBlack.Revision);
        await Assert.That(result.Moves.Length).IsEqualTo(2);
        var recovered = (await file.ReadAsync(CancellationToken.None))!;
        await Assert.That(recovered.LastCommand!.IsPending).IsFalse();
        await Assert.That(recovered.LastCommand.Request.RequestId).IsEqualTo(saved.LastCommand.Request.RequestId);
        await Assert.That(recovered.Access!.Snapshot.Moves.Length).IsEqualTo(2);
    }

    [Test, RequiresChessServer]
    public async Task ReplayingAnOlderReceiptCannotRegressAnObservedSnapshot()
    {
        using var white = new CliProcess();
        using var black = new CliProcess { Server = white.Server };
        var access = (await white.RunAsync("create")).Read<GameAccess>();
        await black.RunAsync("join", access.OpponentCode!);
        var requestId = Guid.NewGuid().ToString();
        await white.RunAsync("move", "e4", "--request-id", requestId);
        var reply = (await black.RunAsync("move", "e5")).Read<GameSnapshot>();
        await white.RunAsync("show");
        var retry = await white.RunAsync("move", "e4", "--request-id", requestId);
        await Assert.That(retry.ExitCode).IsEqualTo(0);
        await Assert.That(retry.Read<GameSnapshot>().Revision).IsEqualTo(reply.Revision);
        await Assert.That((await new SessionFile(white.Session).ReadAsync(CancellationToken.None))!.Access!.Snapshot.Moves.Length).IsEqualTo(2);
    }

    private static Task<GameAccess> EnterAsync(CliContext context, GameEntryKind kind) =>
        kind == GameEntryKind.Create ? context.CreateAsync() : context.MatchmakeAsync();

    private sealed class DropSuccessfulPostHandler(string path) : DelegatingHandler(new SocketsHttpHandler())
    {
        public string? DroppedBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (DroppedBody is null && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith(path, StringComparison.Ordinal) && response.IsSuccessStatusCode)
            {
                DroppedBody = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                throw new HttpRequestException("The server committed the request, but its response was lost.");
            }
            return response;
        }
    }
}
