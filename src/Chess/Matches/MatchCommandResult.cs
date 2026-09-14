namespace Chess;

public union MatchCommandResult(
    CommandAccepted,
    MatchAlreadyFinished,
    WrongPlayer,
    DrawClaimUnavailable,
    SourceSquareEmpty,
    WrongSideToMove,
    FriendlyPieceOnDestination,
    InvalidMovement,
    PathBlocked,
    KingCaptureNotAllowed,
    KingWouldBeInCheck,
    PromotionRequired,
    InvalidPromotion,
    CastlingUnavailable)
{
    public static MatchAlreadyFinished AlreadyFinished { get; } = new();

    public static DrawClaimUnavailable DrawClaimUnavailable { get; } = new();
}

public sealed class CommandAccepted
{
    internal CommandAccepted(MatchEvent @event) => Event = @event;

    public MatchEvent Event { get; }
}

public sealed class MatchAlreadyFinished
{
    internal MatchAlreadyFinished()
    {
    }
}

public sealed record WrongPlayer
{
    public required Side Expected { get; init; }

    public required Side Actual { get; init; }
}

public sealed class DrawClaimUnavailable
{
    internal DrawClaimUnavailable()
    {
    }
}
