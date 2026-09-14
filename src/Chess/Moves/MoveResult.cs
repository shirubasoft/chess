namespace Chess;

public union MoveResult(
    Position,
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
    public static SourceSquareEmpty SourceSquareEmpty { get; } = new();

    public static WrongSideToMove WrongSideToMove { get; } = new();

    public static FriendlyPieceOnDestination FriendlyPieceOnDestination { get; } = new();

    public static InvalidMovement InvalidMovement { get; } = new();

    public static PathBlocked PathBlocked { get; } = new();

    public static KingCaptureNotAllowed KingCaptureNotAllowed { get; } = new();

    public static KingWouldBeInCheck KingWouldBeInCheck { get; } = new();

    public static PromotionRequired PromotionRequired { get; } = new();

    public static InvalidPromotion InvalidPromotion { get; } = new();

    public static CastlingUnavailable CastlingUnavailable { get; } = new();
}

public sealed class SourceSquareEmpty
{
    internal SourceSquareEmpty()
    {
    }
}

public sealed class WrongSideToMove
{
    internal WrongSideToMove()
    {
    }
}

public sealed class FriendlyPieceOnDestination
{
    internal FriendlyPieceOnDestination()
    {
    }
}

public sealed class InvalidMovement
{
    internal InvalidMovement()
    {
    }
}

public sealed class PathBlocked
{
    internal PathBlocked()
    {
    }
}

public sealed class KingCaptureNotAllowed
{
    internal KingCaptureNotAllowed()
    {
    }
}

public sealed class KingWouldBeInCheck
{
    internal KingWouldBeInCheck()
    {
    }
}

public sealed class PromotionRequired
{
    internal PromotionRequired()
    {
    }
}

public sealed class InvalidPromotion
{
    internal InvalidPromotion()
    {
    }
}

public sealed class CastlingUnavailable
{
    internal CastlingUnavailable()
    {
    }
}
