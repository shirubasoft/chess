using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class KingSafetyTests
{
    [Test]
    public async Task APinnedPieceMustContinueShieldingItsKing()
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("e2", 'R'), ("e8", 'r'));

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("e2", "f2")));
        var next = await Result<Position>(MoveRules.Apply(position, Move("e2", "e3")));

        await Assert.That(At(next, "e3")).IsEqualTo('R');
        await Assert.That(At(position, "e2")).IsEqualTo('R');
    }

    [Test]
    public async Task AMoveMustResolveAnExistingCheck()
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("e8", 'r'), ("a2", 'P'));

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("a2", "a3")));
    }

    [Test]
    [Arguments("d8", 'r', "d1")]
    [Arguments("f4", 'n', "e2")]
    [Arguments("d3", 'p', "e2")]
    [Arguments("b5", 'b', "e2")]
    [Arguments("d8", 'q', "d1")]
    public async Task KingsCannotMoveOntoAttackedSquares(string attackerSquare, char attacker, string to)
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), (attackerSquare, attacker));

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("e1", to)));
    }

    [Test]
    public async Task KingsCannotBecomeAdjacent()
    {
        var position = Setup(("e1", 'K'), ("e3", 'k'));

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("e1", "e2")));
    }

    [Test]
    public async Task PinnedEnemyPiecesStillAttackSquaresForKingSafety()
    {
        var position = Setup(("c5", 'K'), ("e1", 'R'), ("e8", 'k'), ("e7", 'n'));

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("c5", "d5")));
    }

    [Test]
    public async Task KingSafetyUsesTheBoardAfterRemovingTheCapturedPieceAndVacatingTheSource()
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("d1", 'r'), ("h1", 'r'));

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("e1", "d1")));
    }

    [Test]
    public async Task ACaptureCanResolveCheck()
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("e2", 'r'));

        var next = await Result<Position>(MoveRules.Apply(position, Move("e1", "e2")));

        await Assert.That(At(next, "e2")).IsEqualTo('K');
        await Assert.That(next.HalfmoveClock).IsEqualTo(0);
    }
}
