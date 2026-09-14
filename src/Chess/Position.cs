namespace Chess;

public sealed record Position
{
    public required Board Board { get; init; }

    public required Side SideToMove { get; init; }

    public required CastlingRights WhiteCastlingRights { get; init; }

    public required CastlingRights BlackCastlingRights { get; init; }

    public required EnPassantState EnPassant { get; init; }

    public required int HalfmoveClock
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    public required int FullmoveNumber
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    }
}
