using Chess.Contracts;
using Chess.Notation;
using Chess.Web.Board;

namespace Chess.Web.Tests;

public sealed class BoardPresentationTests
{
    [Test]
    public async Task BothPerspectivesPreserveCoordinatesPieceIdentityAndSquareColors()
    {
        var white = BoardPresentation.Squares(Position.Initial, PlayerSide.White).ToArray();
        var black = BoardPresentation.Squares(Position.Initial, PlayerSide.Black).ToArray();
        await Assert.That(white.Length).IsEqualTo(64);
        await Assert.That(white[0].Name).IsEqualTo("a8");
        await Assert.That(black[0].Name).IsEqualTo("h1");
        await Assert.That(white.SequenceEqual(black.Reverse())).IsTrue();
        var a1 = white.Single(square => square.Name == "a1");
        await Assert.That(a1.Dark).IsTrue();
        await Assert.That(a1.Description).IsEqualTo("a1, White rook");
        await Assert.That(white.Single(square => square.Name == "e4").Description).IsEqualTo("e4, empty");
    }

    [Test]
    public async Task MovePreviewRejectsAnIllegalMoveEvenIfServerAdvertisesIt()
    {
        var snapshot = Snapshot(Fen.Format(Position.Initial), ["e2e4", "e2e5", "a1a8"]);
        var moves = BoardPresentation.LegalMoves(snapshot);
        await Assert.That(moves.SequenceEqual(["e2e4"])).IsTrue();
        await Assert.That(BoardPresentation.Destinations(moves, "e2").Single()).IsEqualTo("e4");
    }

    [Test]
    public async Task MovePreviewHonorsServerAvailabilityEvenIfCoreHasOtherLegalMoves()
    {
        var moves = BoardPresentation.LegalMoves(Snapshot(Fen.Format(Position.Initial), []));
        await Assert.That(moves.Length).IsEqualTo(0);
    }

    [Test]
    public async Task PromotionHasOneTargetAndAllFourPieceChoices()
    {
        var snapshot = Snapshot("7k/P7/8/8/8/8/8/7K w - - 0 1", ["a7a8q", "a7a8r", "a7a8b", "a7a8n"]);
        var moves = BoardPresentation.LegalMoves(snapshot);
        await Assert.That(BoardPresentation.Destinations(moves, "a7").Single()).IsEqualTo("a8");
        await Assert.That(BoardPresentation.MovesBetween(moves, "a7", "a8").Order().SequenceEqual(snapshot.LegalMoves.Order())).IsTrue();
    }

    [Test]
    public async Task CastlingIsSelectedByKingDestination()
    {
        var snapshot = Snapshot("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", ["e1g1", "e1c1"]);
        var moves = BoardPresentation.LegalMoves(snapshot);
        await Assert.That(BoardPresentation.MovesBetween(moves, "e1", "g1").Single()).IsEqualTo("e1g1");
        await Assert.That(BoardPresentation.MovesBetween(moves, "e1", "c1").Single()).IsEqualTo("e1c1");
    }

    [Test]
    public async Task EnPassantPreviewDoesNotExposeItsOwnKing()
    {
        var snapshot = Snapshot("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", ["e5d6", "e5e6"]);
        await Assert.That(BoardPresentation.LegalMoves(snapshot).SequenceEqual(["e5e6"])).IsTrue();
    }

    [Test]
    [Arguments(GameStatus.Waiting)]
    [Arguments(GameStatus.Finished)]
    public async Task InactiveGameOffersNoMovePreview(GameStatus status)
    {
        var snapshot = Snapshot(Fen.Format(Position.Initial), ["e2e4"]) with { Status = status };
        await Assert.That(BoardPresentation.LegalMoves(snapshot).Length).IsEqualTo(0);
    }

    internal static GameSnapshot Snapshot(string fen, string[] moves) => new()
    {
        GameId = Guid.NewGuid(), Revision = 0, Fen = fen, SideToMove = PlayerSide.White,
        Status = GameStatus.Active, White = new() { Side = PlayerSide.White, ClientName = "Chess Web" },
        Black = new() { Side = PlayerSide.Black, ClientName = "Chess CLI" }, LegalMoves = moves, Moves = []
    };
}
