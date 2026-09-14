namespace Chess;

public sealed record DeadPositionSearch
{
    public static DeadPositionSearch Default { get; } = new();

    public int MaximumDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 64);
            field = value;
        }
    } = 2;

    public int MaximumPositions
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 256;
}
