namespace Chess;

public union MatchState(OngoingMatch, FinishedMatch);

public sealed class OngoingMatch
{
    internal OngoingMatch(PositionHistory history) => History = history;

    public PositionHistory History { get; }
}

public sealed class FinishedMatch
{
    internal FinishedMatch(PositionHistory history, MatchResult result)
    {
        History = history;
        Result = result;
    }

    public PositionHistory History { get; }

    public MatchResult Result { get; }
}

public union MatchResult(MatchWon, MatchDrawn);

public sealed record MatchWon
{
    public required Side Winner { get; init; }

    public required WinReason Reason { get; init; }
}

public sealed record MatchDrawn
{
    public required DrawReason Reason { get; init; }
}

public enum WinReason
{
    Checkmate,
    Resignation
}

public enum DrawReason
{
    Stalemate,
    DeadPosition,
    ThreefoldRepetition,
    FivefoldRepetition,
    FiftyMoveRule,
    SeventyFiveMoveRule,
    ResignationWithoutMatingMaterial
}
