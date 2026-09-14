namespace Chess;

public union PositionOutcome(Checkmate, Stalemate, DeadPosition, OngoingPosition)
{
    public static Stalemate Stalemate { get; } = new();

    public static DeadPosition DeadPosition { get; } = new();

    public static OngoingPosition Ongoing { get; } = new();
}

public sealed record Checkmate
{
    public required Side Winner { get; init; }
}

public sealed class Stalemate
{
    internal Stalemate()
    {
    }
}

public sealed class DeadPosition
{
    internal DeadPosition()
    {
    }
}

public sealed class OngoingPosition
{
    internal OngoingPosition()
    {
    }
}
