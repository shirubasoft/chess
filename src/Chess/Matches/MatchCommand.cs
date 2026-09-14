namespace Chess;

public union MatchCommand(PlayMove, ClaimDraw);

public sealed record PlayMove
{
    public required Side Player { get; init; }

    public required MoveRequest Move { get; init; }
}

public sealed record ClaimDraw
{
    public required Side Player { get; init; }

    public required DrawClaimReason Reason { get; init; }

    public required DrawClaimTiming Timing { get; init; }
}

public enum DrawClaimReason
{
    ThreefoldRepetition,
    FiftyMoveRule
}

public union DrawClaimTiming(CurrentPositionClaim, IntendedMoveClaim)
{
    public static CurrentPositionClaim CurrentPosition { get; } = new();
}

public sealed class CurrentPositionClaim
{
    internal CurrentPositionClaim()
    {
    }
}

public sealed record IntendedMoveClaim
{
    public required MoveRequest Move { get; init; }
}
