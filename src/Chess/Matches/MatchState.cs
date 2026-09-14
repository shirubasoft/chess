namespace Chess;

public union MatchState(OngoingMatch, FinishedMatch)
{
    public int Revision => this switch
    {
        OngoingMatch ongoing => ongoing.Revision,
        FinishedMatch finished => finished.Revision
    };
}

public sealed class OngoingMatch
{
    internal OngoingMatch(PositionHistory history, int revision, DrawOfferState drawOffer)
    {
        History = history;
        Revision = revision;
        DrawOffer = drawOffer;
    }

    public int Revision { get; }

    public DrawOfferState DrawOffer { get; }

    public PositionHistory History { get; }
}

public sealed class FinishedMatch
{
    internal FinishedMatch(PositionHistory history, MatchResult result, int revision)
    {
        History = history;
        Result = result;
        Revision = revision;
    }

    public PositionHistory History { get; }

    public MatchResult Result { get; }

    public int Revision { get; }
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
    ResignationWithoutMatingMaterial,
    Agreement
}
