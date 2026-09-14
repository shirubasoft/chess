using System.Text.Json.Serialization;

namespace Chess;

public union MatchCommandResult(
    CommandAccepted,
    MatchAlreadyFinished,
    WrongPlayer,
    DrawClaimUnavailable,
    DrawOfferAlreadyPending,
    NoPendingDrawOffer,
    DrawAgreementUnavailable,
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

    public static DrawOfferAlreadyPending DrawOfferAlreadyPending { get; } = new();

    public static NoPendingDrawOffer NoPendingDrawOffer { get; } = new();

    public static DrawAgreementUnavailable DrawAgreementUnavailable { get; } = new();
}

public sealed class CommandAccepted
{
    [JsonConstructor]
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

public sealed class DrawOfferAlreadyPending
{
    internal DrawOfferAlreadyPending()
    {
    }
}

public sealed class NoPendingDrawOffer
{
    internal NoPendingDrawOffer()
    {
    }
}

public sealed class DrawAgreementUnavailable
{
    internal DrawAgreementUnavailable()
    {
    }
}
