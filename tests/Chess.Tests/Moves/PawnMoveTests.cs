using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class PawnMoveTests
{
    [Test]
    [Arguments(true, "e2", "e4", "e3")]
    [Arguments(false, "e7", "e5", "e6")]
    public async Task DoublePushSetsTheEnPassantTarget(bool white, string from, string to, string target)
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), (from, white ? 'P' : 'p')) with
        {
            SideToMove = white ? Side.White : Side.Black
        };

        var next = await Result<Position>(MoveRules.Apply(position, Move(from, to)));

        var enPassant = await Assert.That(next.EnPassant.Value).IsTypeOf<EnPassantTarget>().And.IsNotNull();
        await Assert.That(enPassant.Square).IsEqualTo(Square(target));
        await Assert.That(At(next, to)).IsEqualTo(white ? 'P' : 'p');
        await Assert.That(At(next, from)).IsEqualTo('.');
        await Assert.That(next.HalfmoveClock).IsEqualTo(0);
        await Assert.That(next.FullmoveNumber).IsEqualTo(white ? 12 : 13);
        await Assert.That(At(position, from)).IsEqualTo(white ? 'P' : 'p');
    }

    [Test]
    public async Task PawnsCannotJumpOverPiecesOrCaptureForward()
    {
        var blocked = Setup(("a1", 'K'), ("h8", 'k'), ("e2", 'P'), ("e3", 'n'));
        await Result<PathBlocked>(MoveRules.Apply(blocked, Move("e2", "e4")));
        await Result<InvalidMovement>(MoveRules.Apply(blocked, Move("e2", "e3")));

        var destination = Setup(("a1", 'K'), ("h8", 'k'), ("e2", 'P'), ("e4", 'n'));
        await Result<InvalidMovement>(MoveRules.Apply(destination, Move("e2", "e4")));
    }

    [Test]
    [Arguments(true, "e4", "d5")]
    [Arguments(false, "e5", "d4")]
    public async Task PawnsCaptureDiagonally(bool white, string from, string to)
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), (from, white ? 'P' : 'p'), (to, white ? 'n' : 'N')) with
        {
            SideToMove = white ? Side.White : Side.Black
        };

        var next = await Result<Position>(MoveRules.Apply(position, Move(from, to)));

        await Assert.That(At(next, to)).IsEqualTo(white ? 'P' : 'p');
        await Assert.That(At(next, from)).IsEqualTo('.');
        await Assert.That(next.HalfmoveClock).IsEqualTo(0);
    }

    [Test]
    [Arguments(true, "e5", "d6", "d5")]
    [Arguments(false, "e4", "d3", "d4")]
    public async Task EnPassantRemovesTheAdjacentPawn(bool white, string from, string to, string captured)
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), (from, white ? 'P' : 'p'), (captured, white ? 'p' : 'P')) with
        {
            SideToMove = white ? Side.White : Side.Black,
            EnPassant = new EnPassantTarget { Square = Square(to) }
        };

        var next = await Result<Position>(MoveRules.Apply(position, Move(from, to)));

        await Assert.That(At(next, to)).IsEqualTo(white ? 'P' : 'p');
        await Assert.That(At(next, from)).IsEqualTo('.');
        await Assert.That(At(next, captured)).IsEqualTo('.');
        await Assert.That(next.EnPassant.Value).IsTypeOf<NoEnPassant>().And.IsNotNull();
        await Assert.That(next.HalfmoveClock).IsEqualTo(0);
        await Assert.That(At(position, from)).IsEqualTo(white ? 'P' : 'p');
        await Assert.That(At(position, captured)).IsEqualTo(white ? 'p' : 'P');
    }

    [Test]
    public async Task EnPassantRequiresTheCurrentTargetAndAnOpposingPawnOnTheCorrectRank()
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), ("e5", 'P'), ("d5", 'p'));
        await Result<InvalidMovement>(MoveRules.Apply(position, Move("e5", "d6")));
        await Result<InvalidMovement>(MoveRules.Apply(position with
        {
            EnPassant = new EnPassantTarget { Square = Square("f6") }
        }, Move("e5", "d6")));

        foreach (var adjacent in new[] { 'n', 'P' })
        {
            var wrongPiece = Setup(("a1", 'K'), ("h8", 'k'), ("e5", 'P'), ("d5", adjacent)) with
            {
                EnPassant = new EnPassantTarget { Square = Square("d6") }
            };
            await Result<InvalidMovement>(MoveRules.Apply(wrongPiece, Move("e5", "d6")));
        }

        var wrongRank = Setup(("a1", 'K'), ("h8", 'k'), ("e4", 'P'), ("d4", 'p')) with
        {
            EnPassant = new EnPassantTarget { Square = Square("d5") }
        };
        await Result<InvalidMovement>(MoveRules.Apply(wrongRank, Move("e4", "d5")));
    }

    [Test]
    public async Task EnPassantCannotExposeTheKingByRemovingBothPawnsFromARank()
    {
        var position = Setup(("h5", 'K'), ("h8", 'k'), ("g5", 'P'), ("f5", 'p'), ("a5", 'r')) with
        {
            EnPassant = new EnPassantTarget { Square = Square("f6") }
        };

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("g5", "f6")));

        await Assert.That(At(position, "g5")).IsEqualTo('P');
        await Assert.That(At(position, "f5")).IsEqualTo('p');
    }

    [Test]
    public async Task EnPassantCanRemoveACheckingPawn()
    {
        var position = Setup(("e4", 'K'), ("h8", 'k'), ("e5", 'P'), ("d5", 'p')) with
        {
            EnPassant = new EnPassantTarget { Square = Square("d6") }
        };

        var next = await Result<Position>(MoveRules.Apply(position, Move("e5", "d6")));

        await Assert.That(At(next, "d6")).IsEqualTo('P');
        await Assert.That(At(next, "d5")).IsEqualTo('.');
    }
}
