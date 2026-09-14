using System.Drawing;
using Chess.Contracts;
using Chess.Notation;
using Terminal.Gui.Input;

namespace Chess.Cli.Tests;

public sealed class BoardTests
{
    [Test]
    [Arguments(PlayerSide.White, 3, 1, "a8")]
    [Arguments(PlayerSide.White, 16, 7, "e2")]
    [Arguments(PlayerSide.Black, 3, 1, "h1")]
    [Arguments(PlayerSide.Black, 13, 7, "e7")]
    [Arguments(PlayerSide.White, 2, 1, null)]
    [Arguments(PlayerSide.White, 27, 1, null)]
    [Arguments(PlayerSide.White, 3, 0, null)]
    [Arguments(PlayerSide.White, 3, 9, null)]
    [Arguments(PlayerSide.Black, -1, -1, null)]
    public async Task HitTestingRespectsOrientationAndExcludesTheFrame(PlayerSide side, int x, int y, string? expected) =>
        await Assert.That(ChessBoardView.SquareAt(new Point(x, y), side)).IsEqualTo(expected);

    [Test]
    public async Task PiecesUseDistinctWhiteAndBlackUnicodeGlyphs()
    {
        using var board = new ChessBoardView();
        await Assert.That(string.Concat("abcdefgh".Select(file => board.PieceAt($"{file}1")))).IsEqualTo("♖♘♗♕♔♗♘♖");
        await Assert.That(string.Concat("abcdefgh".Select(file => board.PieceAt($"{file}8")))).IsEqualTo("♜♞♝♛♚♝♞♜");
        await Assert.That(board.PieceAt("e2")).IsEqualTo('♙');
        await Assert.That(board.PieceAt("e7")).IsEqualTo('♟');
        await Assert.That(board.PieceAt("e4")).IsEqualTo(' ');
    }

    [Test]
    public async Task DragReportsLegalMoveOnlyOnReleaseAndKeepsTheBoardUntilTheServerReplies()
    {
        using var board = new ChessBoardView();
        board.SetGame(Game(), busy: false);
        string[]? moves = null;
        board.MoveRequested += (_, values) => moves = values;
        Mouse(board, MouseFlags.LeftButtonPressed, 16, 7);
        Mouse(board, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, 16, 5);
        await Assert.That(board.IsDragging).IsTrue();
        await Assert.That(moves).IsNull();
        Mouse(board, MouseFlags.LeftButtonReleased, 16, 5);
        await Assert.That(moves).IsEquivalentTo(new[] { "e2e4" });
        await Assert.That(board.IsDragging).IsFalse();
        await Assert.That(board.PieceAt("e2")).IsEqualTo('♙');
    }

    [Test]
    [Arguments(16, 4)]
    [Arguments(1, 5)]
    [Arguments(16, 7)]
    public async Task IllegalOutsideAndUnmovedDropsCancel(int x, int y)
    {
        using var board = new ChessBoardView();
        board.SetGame(Game(), busy: false);
        var requests = 0;
        board.MoveRequested += (_, _) => requests++;
        Mouse(board, MouseFlags.LeftButtonPressed, 16, 7);
        Mouse(board, MouseFlags.LeftButtonReleased, x, y);
        await Assert.That(requests).IsEqualTo(0);
        await Assert.That(board.IsDragging).IsFalse();
    }

    [Test]
    public async Task BusyWaitingFinishedAndOpponentTurnsCannotStartADrag()
    {
        using var board = new ChessBoardView();
        var game = Game();
        foreach (var (access, busy) in new[]
        {
            (game, true),
            (game with { Snapshot = game.Snapshot with { Status = GameStatus.Waiting } }, false),
            (game with { Snapshot = game.Snapshot with { Status = GameStatus.Finished } }, false),
            (game with { Snapshot = game.Snapshot with { SideToMove = PlayerSide.Black } }, false)
        })
        {
            board.SetGame(access, busy);
            Mouse(board, MouseFlags.LeftButtonPressed, 16, 7);
            await Assert.That(board.IsDragging).IsFalse();
        }
    }

    [Test]
    public async Task UpdatedSnapshotAndRightClickCancelTheDrag()
    {
        using var board = new ChessBoardView();
        var game = Game();
        board.SetGame(game, busy: false);
        var requests = 0;
        board.MoveRequested += (_, _) => requests++;
        Mouse(board, MouseFlags.LeftButtonPressed, 16, 7);
        board.SetGame(game with { Snapshot = game.Snapshot with { Revision = 2 } }, busy: false);
        Mouse(board, MouseFlags.LeftButtonReleased, 16, 5);
        Mouse(board, MouseFlags.LeftButtonPressed, 16, 7);
        Mouse(board, MouseFlags.RightButtonPressed, 16, 5);
        Mouse(board, MouseFlags.LeftButtonReleased, 16, 5);
        await Assert.That(requests).IsEqualTo(0);
        await Assert.That(board.IsDragging).IsFalse();
    }

    [Test]
    public async Task PromotionDropOffersEveryLegalPromotion()
    {
        using var board = new ChessBoardView();
        var game = Game();
        board.SetGame(game with { Snapshot = game.Snapshot with
        {
            Fen = "7k/P7/8/8/8/8/8/7K w - - 0 1",
            LegalMoves = ["a7a8q", "a7a8r", "a7a8b", "a7a8n"]
        } }, busy: false);
        string[]? moves = null;
        board.MoveRequested += (_, values) => moves = values;
        Mouse(board, MouseFlags.LeftButtonPressed, 4, 2);
        Mouse(board, MouseFlags.LeftButtonReleased, 4, 1);
        await Assert.That(moves).IsEquivalentTo(new[] { "a7a8q", "a7a8r", "a7a8b", "a7a8n" });
    }

    private static void Mouse(ChessBoardView board, MouseFlags flags, int x, int y) =>
        board.NewMouseEvent(new Mouse { Flags = flags, Position = new Point(x, y), View = board });

    private static GameAccess Game() => new()
    {
        GameId = Guid.NewGuid(), Code = "test-only", Side = PlayerSide.White,
        Snapshot = new GameSnapshot
        {
            GameId = Guid.NewGuid(), Revision = 1, Fen = Fen.Format(Position.Initial), SideToMove = PlayerSide.White,
            Status = GameStatus.Active, White = new() { Side = PlayerSide.White }, Black = new() { Side = PlayerSide.Black },
            LegalMoves = ["e2e3", "e2e4"], Moves = []
        }
    };
}
