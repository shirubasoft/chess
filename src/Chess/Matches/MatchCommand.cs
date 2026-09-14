namespace Chess;

public union MatchCommand(PlayMove, ClaimDraw, Resign, OfferDraw, AcceptDraw, DeclineDraw);

public sealed record Resign
{
    public required Side Player { get; init; }
}

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

public sealed record OfferDraw
{
    public required Side Player { get; init; }
}

public sealed record AcceptDraw
{
    public required Side Player { get; init; }
}

public sealed record DeclineDraw
{
    public required Side Player { get; init; }
}
