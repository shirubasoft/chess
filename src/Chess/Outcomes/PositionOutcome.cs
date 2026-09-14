namespace Chess;

public union PositionOutcome(Checkmate, Stalemate, DeadPosition, MatingContinuationExists, UndeterminedPosition)
{
    public static Stalemate Stalemate { get; } = new();

    public static DeadPosition DeadPosition { get; } = new();

    public static MatingContinuationExists MatingContinuationExists { get; } = new();

    public static UndeterminedPosition Undetermined { get; } = new();
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

public sealed class MatingContinuationExists
{
    internal MatingContinuationExists()
    {
    }
}

public sealed class UndeterminedPosition
{
    internal UndeterminedPosition()
    {
    }
}
