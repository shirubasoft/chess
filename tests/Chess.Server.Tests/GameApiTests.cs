using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Server.Tests;

[NotInParallel]
public sealed class GameApiTests
{
    private static HttpClient Http() => new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("CHESS_TEST_SERVER")
            ?? throw new InvalidOperationException("Set CHESS_TEST_SERVER to the server endpoint from Aspire.")),
        Timeout = TimeSpan.FromSeconds(70)
    };

    [Test]
    public async Task PrivateGameCanResumeFromAnotherClientWithoutAConnection()
    {
        using var http = Http();
        var white = new ChessClient(http, "Chess Web");
        var black = new ChessClient(http, "Chess Android");
        var access = await white.CreateAsync();
        await Assert.That(access.Snapshot.Status).IsEqualTo(GameStatus.Waiting);
        await Assert.That(access.Code == access.OpponentCode).IsFalse();
        var opponent = await black.JoinAsync(access.OpponentCode!);
        await Assert.That(opponent.Side).IsEqualTo(PlayerSide.Black);
        var snapshot = await white.GetAsync(access);
        snapshot = await white.MoveAsync(access, snapshot, "e4");
        snapshot = await black.MoveAsync(opponent, snapshot, "e7e5", MoveNotation.Uci);
        using var reconnectedHttp = Http();
        var resumed = new ChessClient(reconnectedHttp, "Chess CLI");
        var restored = await resumed.JoinAsync(access.Code);
        await Assert.That(restored.GameId).IsEqualTo(access.GameId);
        await Assert.That(restored.Snapshot.White.ClientName).IsEqualTo("Chess CLI");
        await Assert.That(restored.Snapshot.Black.ClientName).IsEqualTo("Chess Android");
        await Assert.That(restored.Snapshot.Moves.Length).IsEqualTo(2);
        await resumed.MoveAsync(restored, restored.Snapshot, "Nf3");
        var otherView = await black.GetAsync(opponent);
        await Assert.That(otherView.Moves.Last().Uci).IsEqualTo("g1f3");
        await Assert.That(otherView.White.ClientName).IsEqualTo("Chess CLI");
    }

    [Test]
    public async Task ConcurrentRetriesApplyExactlyOneMoveAndRejectRequestIdReuse()
    {
        using var http = Http();
        var client = new ChessClient(http, "retry-test");
        var white = await client.CreateAsync();
        var black = await client.JoinAsync(white.OpponentCode!);
        var command = new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = black.Snapshot.Revision, Action = GameAction.Move, Move = "e4"
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.CommandAsync(white, command)));
        await Assert.That(results.Select(r => r.Revision).Distinct().Count()).IsEqualTo(1);
        await Assert.That((await client.GetAsync(white)).Moves.Length).IsEqualTo(1);
        await FaultAsync(() => client.CommandAsync(white, command with { Move = "d4" }), HttpStatusCode.Conflict, "request_id_reused");
        await FaultAsync(() => client.CommandAsync(black, command with { RequestId = Guid.NewGuid(), Move = "e5" }), HttpStatusCode.Conflict, "revision_conflict");
        var reply = await client.MoveAsync(black, results[0], "e5");
        await Assert.That(reply.Moves.Length).IsEqualTo(2);
    }

    [Test]
    public async Task DifferentCommandsAtTheSameRevisionCannotBothCommit()
    {
        using var http = Http();
        var client = new ChessClient(http, "concurrency-test");
        var access = await client.CreateAsync();
        var joined = await client.JoinAsync(access.OpponentCode!);
        async Task<bool> TryMove(string move)
        {
            try { await client.MoveAsync(access, joined.Snapshot, move); return true; }
            catch (ChessServerException error) when (error.StatusCode == HttpStatusCode.Conflict) { return false; }
        }
        var outcomes = await Task.WhenAll(TryMove("e4"), TryMove("d4"));
        await Assert.That(outcomes.Count(x => x)).IsEqualTo(1);
        await Assert.That((await client.GetAsync(access)).Moves.Length).IsEqualTo(1);
    }

    [Test]
    public async Task CodesAuthorizeOnlyTheirGameAndSide()
    {
        using var http = Http();
        var client = new ChessClient(http, "authorization-test");
        var access = await client.CreateAsync();
        var opponent = await client.JoinAsync(access.OpponentCode!);
        var unrelated = await client.CreateAsync();
        await FaultAsync(() => client.GetAsync(access with { Code = unrelated.Code }), HttpStatusCode.Unauthorized, "invalid_code");
        await FaultAsync(() => client.MoveAsync(opponent, opponent.Snapshot, "e4"), HttpStatusCode.UnprocessableEntity, "WrongPlayer");
        await Assert.That((await client.GetAsync(access)).Moves.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StreamsReplayCommittedUpdatesAfterCursorAndDeliverNewMoves(bool websocket)
    {
        using var http = Http();
        var client = new ChessClient(http, websocket ? "websocket-test" : "sse-test");
        var white = await client.CreateAsync();
        var black = await client.JoinAsync(white.OpponentCode!);
        var first = await client.MoveAsync(white, black.Snapshot, "e4");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var updates = websocket ? client.WatchWebSocketAsync(white, 0, deadline.Token) : client.WatchSseAsync(white, 0, deadline.Token);
        await using var reader = updates.GetAsyncEnumerator(deadline.Token);
        await Assert.That(await reader.MoveNextAsync()).IsTrue();
        await Assert.That(reader.Current.Revision).IsEqualTo(black.Snapshot.Revision);
        await Assert.That(await reader.MoveNextAsync()).IsTrue();
        await Assert.That(reader.Current.Revision).IsEqualTo(first.Revision);
        var next = reader.MoveNextAsync().AsTask();
        var reply = await client.MoveAsync(black, first, "e5");
        await Assert.That(await next).IsTrue();
        await Assert.That(reader.Current.Fen).IsEqualTo(reply.Fen);
    }

    [Test]
    public async Task LongPollTimesOutAndWakesForAnOpponentMove()
    {
        using var http = Http();
        var client = new ChessClient(http, "wait-test");
        var white = await client.CreateAsync();
        var black = await client.JoinAsync(white.OpponentCode!);
        var snapshot = await client.MoveAsync(white, black.Snapshot, "e4");
        await Assert.That(await client.WaitAsync(white, snapshot.Revision, 1)).IsNull();
        var wait = client.WaitAsync(white, snapshot.Revision, 10);
        var reply = await client.MoveAsync(black, snapshot, "c5");
        await Assert.That((await wait)?.Revision).IsEqualTo(reply.Revision);
    }

    [Test]
    public async Task RandomMatchmakingPairsSeatsAndPersistsRetryReceipts()
    {
        using var http = Http();
        var first = new ChessClient(http, "Chess Linux");
        var second = new ChessClient(http, "Chess Windows");
        var requestId = Guid.NewGuid();
        var white = await first.MatchmakeAsync(requestId);
        var retry = await first.MatchmakeAsync(requestId);
        await Assert.That(retry.Code).IsEqualTo(white.Code);
        var black = await second.MatchmakeAsync();
        await Assert.That(black.GameId).IsEqualTo(white.GameId);
        await Assert.That(black.Side).IsEqualTo(PlayerSide.Black);
        await Assert.That(white.OpponentCode).IsNull();
        var snapshot = await first.GetAsync(white);
        await Assert.That(snapshot.White.ClientName).IsEqualTo("Chess Linux");
        await Assert.That(snapshot.Black.ClientName).IsEqualTo("Chess Windows");
        await first.MoveAsync(white, snapshot, "d4");
    }

    [Test]
    public async Task MatchCommandsAdjudicateAgreementAndRejectFurtherMoves()
    {
        using var http = Http();
        var client = new ChessClient(http, "draw-test");
        var white = await client.CreateAsync();
        var black = await client.JoinAsync(white.OpponentCode!);
        var snapshot = await client.MoveAsync(white, black.Snapshot, "e4");
        snapshot = await client.MoveAsync(black, snapshot, "e5");
        snapshot = await client.CommandAsync(white, new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = snapshot.Revision, Action = GameAction.OfferDraw
        });
        await Assert.That(snapshot.DrawOfferedBy).IsEqualTo(PlayerSide.White);
        snapshot = await client.CommandAsync(black, new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = snapshot.Revision, Action = GameAction.AcceptDraw
        });
        await Assert.That(snapshot.Result?.Reason).IsEqualTo("Agreement");
        await Assert.That(snapshot.Status).IsEqualTo(GameStatus.Finished);
        await FaultAsync(() => client.MoveAsync(white, snapshot, "Nf3"), HttpStatusCode.UnprocessableEntity, "MatchAlreadyFinished");
    }

    [Test]
    public async Task PromotionAndMalformedRequestsUseTheSameRulesAndWireErrors()
    {
        using var http = Http();
        var client = new ChessClient(http, "promotion-test");
        var white = await client.CreateAsync("7k/P7/8/8/8/8/8/7K w - - 0 1");
        var black = await client.JoinAsync(white.OpponentCode!);
        var promoted = await client.MoveAsync(white, black.Snapshot, "a8=N");
        await Assert.That(promoted.Moves[0].Uci).IsEqualTo("a7a8n");
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "api/games")
        {
            Content = new StringContent("{\"clientName\":null}", System.Text.Encoding.UTF8, "application/json")
        };
        using var response = await http.SendAsync(malformed);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<GameError>(GameJson.Options);
        await Assert.That(error?.Code).IsEqualTo("invalid_request");
    }

    private static async Task FaultAsync(Func<Task<GameSnapshot>> action, HttpStatusCode status, string code)
    {
        try { await action(); throw new InvalidOperationException("Expected a server rejection."); }
        catch (ChessServerException error)
        {
            await Assert.That(error.StatusCode).IsEqualTo(status);
            await Assert.That(error.Error.Code).IsEqualTo(code);
        }
    }
}
