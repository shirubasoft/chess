using Chess.Client;
using Chess.Contracts;

namespace Chess.Native.Tests;

public sealed class BoardDragTests
{
    [Test, RequiresRecoveryServer]
    public async Task OnlyMovableOwnPiecesCanStartAndCanceledOrIllegalDropsNeverMove()
    {
        await using var game = await DragGame.CreateAsync();
        foreach (var source in new[] { "", "e", "e9", "e3", "e7", "a1" })
            await Assert.That(game.Session.BeginDrag(source)).IsNull();
        foreach (var target in new string?[] { null, "e5", "e2", "i4" })
        {
            var drag = game.Session.BeginDrag("e2")!;
            await Assert.That(await game.Session.DropAsync(drag, target)).IsFalse();
            await Assert.That(game.Session.SelectedSquare).IsNull();
        }
        var canceled = game.Session.BeginDrag("e2")!;
        game.Session.CancelDrag(canceled);
        await Assert.That(await game.Session.DropAsync(canceled, "e4")).IsFalse();
        await Assert.That((await game.Peer.GetAsync(game.Other)).Moves.Length).IsEqualTo(0);
        var valid = game.Session.BeginDrag("e2")!;
        await Assert.That(await game.Session.DropAsync(valid, "e4")).IsTrue();
        await Assert.That(await game.Session.DropAsync(valid, "e4")).IsFalse();
        await Assert.That((await game.Peer.GetAsync(game.Other)).Moves.Single().Uci).IsEqualTo("e2e4");
        await Assert.That(game.Session.BeginDrag("d2")).IsNull();
    }

    [Test, RequiresRecoveryServer]
    public async Task RemoteRevisionAndGameReentryInvalidateHeldGestures()
    {
        await using var game = await DragGame.CreateAsync();
        var stale = game.Session.BeginDrag("e2")!;
        await game.Peer.CommandAsync(game.Other, new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = game.Session.Snapshot!.Revision, Action = GameAction.OfferDraw
        });
        await game.Session.RefreshAsync();
        await Assert.That(game.Session.IsCurrentDrag(stale)).IsFalse();
        await Assert.That(await game.Session.DropAsync(stale, "e4")).IsFalse();
        var reentered = game.Session.BeginDrag("e2")!;
        await game.Session.JoinAsync(game.Session.Server, game.Session.Access!.Code);
        await Assert.That(await game.Session.DropAsync(reentered, "e4")).IsFalse();
        var otherGame = await game.Peer.CreateAsync();
        var switched = game.Session.BeginDrag("e2")!;
        await game.Session.JoinAsync(game.Session.Server, otherGame.Code);
        await Assert.That(await game.Session.DropAsync(switched, "e4")).IsFalse();
        await Assert.That((await game.Peer.GetAsync(game.Other)).Moves.Length).IsEqualTo(0);
    }

    [Test, RequiresRecoveryServer]
    [Arguments("7k/P7/8/8/8/8/8/K7 w - - 0 1", PlayerSide.White, "a7", "a8")]
    [Arguments("7k/8/8/8/8/8/p7/7K b - - 0 1", PlayerSide.Black, "a2", "a1")]
    public async Task PromotionDragWaitsForTheChooserOnEitherSide(string fen, PlayerSide side, string source, string destination)
    {
        await using var game = await DragGame.CreateAsync(fen, side);
        var drag = game.Session.BeginDrag(source)!;
        await Assert.That(await game.Session.DropAsync(drag, destination)).IsTrue();
        await Assert.That(game.Session.PromotionMove).IsEqualTo(source + destination);
        await Assert.That((await game.Peer.GetAsync(game.Other)).Moves.Length).IsEqualTo(0);
        await game.Session.PromoteAsync('n');
        await Assert.That((await game.Peer.GetAsync(game.Other)).Moves.Single().Uci).IsEqualTo(source + destination + "n");
    }

    private sealed class DragGame : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "chess-drag-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient http;
        public GameSession Session { get; }
        public ChessClient Peer { get; }
        public GameAccess Other { get; private set; } = null!;
        private DragGame()
        {
            var server = Environment.GetEnvironmentVariable("CHESS_TEST_SERVER")!;
            Directory.CreateDirectory(directory);
            Session = new GameSession("Drag tests", Path.Combine(directory, "session"), server);
            http = new HttpClient { BaseAddress = new Uri(server) };
            Peer = new ChessClient(http, "Drag test opponent");
        }
        public static async Task<DragGame> CreateAsync(string? fen = null, PlayerSide side = PlayerSide.White)
        {
            var game = new DragGame();
            var access = await game.Peer.CreateAsync(fen);
            var black = await game.Peer.JoinAsync(access.OpponentCode!);
            game.Other = side == PlayerSide.White ? black : access;
            await game.Session.JoinAsync(game.Session.Server, side == PlayerSide.White ? access.Code : black.Code);
            return game;
        }
        public ValueTask DisposeAsync()
        {
            Session.Dispose(); http.Dispose(); Directory.Delete(directory, true);
            return ValueTask.CompletedTask;
        }
    }
}
