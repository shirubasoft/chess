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
    [Arguments("8/7k/8/8/8/8/K7/R7 w - - 0 1")]
    [Arguments("8/7k/8/8/8/8/K7/Q7 w - - 0 1")]
    public async Task MaterialOutsideTheProvenCasesPermitsPlay(string fen)
    {
        await Assert.That(PositionRules.GetOutcome(FromFen(fen)).Value).IsTypeOf<OngoingPosition>();
    }

    [Test]
    public async Task StartingPositionAndMateInOneAreOngoingUntilAMoveEndsTheGame()
    {
        await Assert.That(PositionRules.GetOutcome(Position.Initial).Value).IsTypeOf<OngoingPosition>();
        var position = Setup(("f6", 'K'), ("g6", 'Q'), ("h8", 'k'));
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<OngoingPosition>();
        var mate = await Result<Position>(MoveRules.Apply(position, Move("g6", "g7")));
        await Assert.That(PositionRules.GetOutcome(mate).Value).IsTypeOf<Checkmate>();
    }

    [Test]
    [Arguments("k7/P7/2K5/8/8/8/8/8 b - - 0 1", "a8", "a7")]
    [Arguments("8/8/8/8/8/2k5/p7/K7 w - - 0 1", "a1", "a2")]
    [Arguments("7k/7P/5K2/8/8/8/8/8 b - - 0 1", "h8", "h7")]
    public async Task AForcedCaptureOfTheLastPieceIsDead(string fen, string from, string to)
    {
        var position = FromFen(fen);
        await Assert.That(MoveRules.GetLegalMoves(position).Single()).IsEqualTo((MoveRequest)Move(from, to));
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<DeadPosition>();
        var next = await Result<Position>(MoveRules.Apply(position, Move(from, to)));
        await Assert.That(PositionRules.GetOutcome(next).Value).IsTypeOf<DeadPosition>();
    }

    [Test]
    public async Task AnAlternativeToCapturingTheLastPiecePermitsPlay()
    {
        var position = Setup(("c5", 'K'), ("a7", 'P'), ("a8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(MoveRules.GetLegalMoves(position).First()).IsEqualTo((MoveRequest)Move("a8", "a7"));
        await Result<Position>(MoveRules.Apply(position, Move("a8", "b7")));
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<OngoingPosition>();
    }

    [Test]
    public async Task AForcedCaptureWithOtherMaterialRemainingPermitsPlay()
    {
        var position = Setup(("c6", 'K'), ("a7", 'P'), ("h1", 'R'), ("a8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(MoveRules.GetLegalMoves(position).Single()).IsEqualTo((MoveRequest)Move("a8", "a7"));
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<OngoingPosition>();
    }

    [Test]
    public async Task AProtectedLastPieceCannotBeCapturedToProveDeadness()
    {
        var position = Setup(("b6", 'K'), ("a7", 'P'), ("a8", 'k')) with { SideToMove = Side.Black };
        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, Move("a8", "a7")));
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<Stalemate>();
    }

    [Test]
    public async Task TwoKnightsCanCheckmateWithCooperation()
    {
        var position = Setup(("g6", 'K'), ("f6", 'N'), ("f7", 'N'), ("h8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<Checkmate>();
    }
}
