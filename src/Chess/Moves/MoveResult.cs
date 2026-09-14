namespace Chess;

public union MoveResult(
    Position,
    InvalidPosition,
    SourceSquareEmpty,
    WrongSideToMove,
    FriendlyPieceOnDestination,
    InvalidMovement,
    PathBlocked,
    KingCaptureNotAllowed,
    KingWouldBeInCheck,
    PromotionRequired,
    InvalidPromotion,
    CastlingUnavailable,
    MoveCounterOverflow);

public sealed record InvalidPosition
{
    public required int WhiteKingCount { get; init; }

    public required int BlackKingCount { get; init; }
}

public sealed record SourceSquareEmpty;

public sealed record WrongSideToMove;

public sealed record FriendlyPieceOnDestination;

public sealed record InvalidMovement;

public sealed record PathBlocked;

public sealed record KingCaptureNotAllowed;

public sealed record KingWouldBeInCheck;

public sealed record PromotionRequired;

public sealed record InvalidPromotion;

public sealed record CastlingUnavailable;

public sealed record MoveCounterOverflow;
