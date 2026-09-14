namespace Chess;

public union CastlingWing(KingSide, QueenSide)
{
    public static KingSide KingSide { get; } = new();

    public static QueenSide QueenSide { get; } = new();
}

public sealed class KingSide
{
    internal KingSide()
    {
    }
}

public sealed class QueenSide
{
    internal QueenSide()
    {
    }
}
