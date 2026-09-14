using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class PositionOutcomeTests
{
    [Test]
    public async Task CheckmateReportsTheWinnerAndStalemateReportsADraw()
    {
        var mate = Setup(("f6", 'K'), ("g7", 'Q'), ("h8", 'k')) with { SideToMove = Side.Black };
        var checkmate = await Assert.That(PositionRules.GetOutcome(mate).Value).IsTypeOf<Checkmate>().And.IsNotNull();
        await Assert.That(checkmate.Winner).IsEqualTo((Side)Side.White);
        var stalemate = Setup(("f6", 'K'), ("f7", 'Q'), ("h8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(PositionRules.GetOutcome(stalemate).Value).IsTypeOf<Stalemate>();
        var blackMate = Setup(("f3", 'k'), ("g2", 'q'), ("h1", 'K'));
        var blackWinner = await Assert.That(PositionRules.GetOutcome(blackMate).Value).IsTypeOf<Checkmate>().And.IsNotNull();
        await Assert.That(blackWinner.Winner).IsEqualTo((Side)Side.Black);
    }

    [Test]
    [Arguments("8/7k/8/8/8/8/K7/8 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/B7 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/N7 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/n7 b - - 0 1")]
    [Arguments("8/7k/8/8/3b4/8/Kb6/B1B5 w - - 0 1")]
    public async Task ProvenMaterialIsDeadEvenWithPromotedSameColorBishops(string fen)
    {
        await Assert.That(PositionRules.GetOutcome(FromFen(fen)).Value).IsTypeOf<DeadPosition>();
    }

    [Test]
    [Arguments("8/7k/8/8/8/8/K7/NN6 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/BB6 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/BN6 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/Nn6 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/KP6/8 w - - 0 1")]
    public async Task UnprovedMaterialRemainsUndeterminedAtTheSearchLimit(string fen)
    {
        var result = PositionRules.GetOutcome(FromFen(fen), new DeadPositionSearch { MaximumDepth = 0 });
        await Assert.That(result.Value).IsTypeOf<UndeterminedPosition>();
    }

    [Test]
    public async Task ForcedPawnCaptureProvesADeadPositionBeyondMaterialCounts()
    {
        // Black's only move is Kxa7. The pawn cannot ever promote.
        var position = Setup(("c6", 'K'), ("a7", 'P'), ("a8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(MoveRules.GetLegalMoves(position).Single()).IsEqualTo((MoveRequest)Move("a8", "a7"));
        await Assert.That(PositionRules.GetOutcome(position, new DeadPositionSearch { MaximumDepth = 0 }).Value).IsTypeOf<UndeterminedPosition>();
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<DeadPosition>();
    }

    [Test]
    public async Task AMatingContinuationDisprovesDeadnessWithoutRequiringForcedMate()
    {
        var position = Setup(("f6", 'K'), ("g6", 'Q'), ("h8", 'k'));
        var result = PositionRules.GetOutcome(position, new DeadPositionSearch { MaximumDepth = 1 });
        await Assert.That(result.Value).IsTypeOf<MatingContinuationExists>();
        var twoKnightsMate = Setup(("g6", 'K'), ("f6", 'N'), ("f7", 'N'), ("h8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(PositionRules.GetOutcome(twoKnightsMate).Value).IsTypeOf<Checkmate>();
    }

    [Test]
    public async Task PositionBudgetExhaustionCannotProveADraw()
    {
        var position = Setup(("c6", 'K'), ("a7", 'P'), ("a8", 'k')) with { SideToMove = Side.Black };
        var result = PositionRules.GetOutcome(position, new DeadPositionSearch { MaximumPositions = 1 });
        await Assert.That(result.Value).IsTypeOf<UndeterminedPosition>();
        await Assert.That(PositionRules.GetOutcome(Position.Initial).Value).IsTypeOf<UndeterminedPosition>();
    }

    [Test]
    public async Task InvalidBoardsAreNotReportedAsStalemateOrDead()
    {
        var invalid = await Assert.That(PositionRules.GetOutcome(Setup()).Value).IsTypeOf<InvalidPosition>().And.IsNotNull();
        await Assert.That(invalid.WhiteKingCount).IsEqualTo(0);
        await Assert.That(invalid.BlackKingCount).IsEqualTo(0);
    }

    [Test]
    public async Task SearchIgnoresCountersAndHonorsCancellation()
    {
        var position = Setup(("c6", 'K'), ("a7", 'P'), ("a8", 'k')) with
        {
            SideToMove = Side.Black, HalfmoveClock = int.MaxValue, FullmoveNumber = int.MaxValue
        };
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<DeadPosition>();
        await Assert.That(() => PositionRules.GetOutcome(position, cancellationToken: new CancellationToken(true)))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task SearchLimitsRejectInvalidValues()
    {
        await Assert.That(() => new DeadPositionSearch { MaximumDepth = -1 }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new DeadPositionSearch { MaximumDepth = 65 }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new DeadPositionSearch { MaximumPositions = 0 }).Throws<ArgumentOutOfRangeException>();
    }
}
