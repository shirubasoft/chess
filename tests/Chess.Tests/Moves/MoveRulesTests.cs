using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class MoveRulesTests
{
    [Test]
    public async Task EmptySourceWrongSideFriendlyDestinationAndStationaryMovesAreRejected()
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), ("b2", 'P'), ("b3", 'N'), ("g7", 'p'));

        await Assert.That(MoveRules.Apply(position, Move("c2", "c3")).Value).IsSameReferenceAs(MoveResult.SourceSquareEmpty);
        await Assert.That(MoveRules.Apply(position, Move("g7", "g6")).Value).IsSameReferenceAs(MoveResult.WrongSideToMove);
        await Assert.That(MoveRules.Apply(position, Move("b2", "b3")).Value).IsSameReferenceAs(MoveResult.FriendlyPieceOnDestination);
        await Assert.That(MoveRules.Apply(position, Move("b2", "b2")).Value).IsSameReferenceAs(MoveResult.InvalidMovement);
        await Assert.That(At(position, "b2")).IsEqualTo('P');
        await Assert.That(position.HalfmoveClock).IsEqualTo(8);
    }

    [Test]
    [Arguments('N', "d4", "f5")]
    [Arguments('B', "d4", "g7")]
    [Arguments('R', "d4", "d7")]
    [Arguments('Q', "d4", "g4")]
    [Arguments('K', "a1", "b2")]
    [Arguments('P', "d4", "d5")]
    public async Task OrdinaryMovesPreserveTheirSourceSnapshot(char piece, string from, string to)
    {
        var position = piece == 'K'
            ? Setup((from, 'K'), ("h6", 'k'))
            : Setup(("a1", 'K'), ("h6", 'k'), (from, piece));

        var next = await Result<Position>(MoveRules.Apply(position, Move(from, to)));

        await Assert.That(At(next, from)).IsEqualTo('.');
        await Assert.That(At(next, to)).IsEqualTo(piece);
        await Assert.That(At(position, from)).IsEqualTo(piece);
        await Assert.That(At(position, to)).IsEqualTo('.');
        await Assert.That(next.SideToMove.Value).IsSameReferenceAs(Side.Black);
        await Assert.That(next.HalfmoveClock).IsEqualTo(piece == 'P' ? 0 : 9);
        await Assert.That(next.FullmoveNumber).IsEqualTo(12);
    }

    [Test]
    [Arguments('N', "d4", "d5")]
    [Arguments('B', "d4", "d5")]
    [Arguments('R', "d4", "e5")]
    [Arguments('Q', "d4", "f5")]
    [Arguments('K', "a1", "c1")]
    [Arguments('P', "d4", "d3")]
    [Arguments('P', "d4", "d6")]
    [Arguments('P', "d4", "e5")]
    public async Task InvalidMovementShapesAreRejected(char piece, string from, string to)
    {
        var position = piece == 'K'
            ? Setup((from, 'K'), ("h6", 'k'))
            : Setup(("a1", 'K'), ("h6", 'k'), (from, piece));

        await Result<InvalidMovement>(MoveRules.Apply(position, Move(from, to)));
    }

    [Test]
    [Arguments('B', "g7", "e5")]
    [Arguments('R', "d7", "d5")]
    [Arguments('Q', "g4", "e4")]
    public async Task SlidingPiecesCannotJumpOverAnInterveningPiece(char piece, string to, string blocker)
    {
        var position = Setup(("a1", 'K'), ("h6", 'k'), ("d4", piece), (blocker, 'p'));

        await Result<PathBlocked>(MoveRules.Apply(position, Move("d4", to)));
    }

    [Test]
    public async Task KnightsCanJumpOverInterveningPieces()
    {
        var position = Setup(("a1", 'K'), ("h6", 'k'), ("d4", 'N'), ("d5", 'P'), ("e4", 'P'));

        var next = await Result<Position>(MoveRules.Apply(position, Move("d4", "f5")));

        await Assert.That(At(next, "f5")).IsEqualTo('N');
    }

    [Test]
    public async Task CapturesRemoveTheOpponentAndResetTheHalfmoveClock()
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), ("b1", 'R'), ("b5", 'b'));

        var next = await Result<Position>(MoveRules.Apply(position, Move("b1", "b5")));

        await Assert.That(At(next, "b5")).IsEqualTo('R');
        await Assert.That(At(next, "b1")).IsEqualTo('.');
        await Assert.That(At(position, "b5")).IsEqualTo('b');
        await Assert.That(next.HalfmoveClock).IsEqualTo(0);
    }

    [Test]
    public async Task KingsCannotBeCaptured()
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), ("h1", 'R'));

        await Result<KingCaptureNotAllowed>(MoveRules.Apply(position, Move("h1", "h8")));
    }

    [Test]
    public async Task AQuietBlackMoveAdvancesTheFullmoveNumberAndExpiresEnPassant()
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), ("d5", 'n')) with
        {
            SideToMove = Side.Black,
            EnPassant = new EnPassantTarget { Square = Square("e3") }
        };

        var next = await Result<Position>(MoveRules.Apply(position, Move("d5", "f4")));

        await Assert.That(next.SideToMove.Value).IsSameReferenceAs(Side.White);
        await Assert.That(next.FullmoveNumber).IsEqualTo(13);
        await Assert.That(next.HalfmoveClock).IsEqualTo(9);
        await Assert.That(next.EnPassant.Value).IsSameReferenceAs(EnPassantState.None);
    }
}
