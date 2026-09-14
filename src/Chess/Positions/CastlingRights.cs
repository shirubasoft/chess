namespace Chess;

public union CastlingRights(NoCastlingRights, KingSideCastlingRights, QueenSideCastlingRights, BothCastlingRights)
{
    public static NoCastlingRights None { get; } = new();

    public static KingSideCastlingRights KingSide { get; } = new();

    public static QueenSideCastlingRights QueenSide { get; } = new();

    public static BothCastlingRights Both { get; } = new();
}

public sealed class NoCastlingRights
{
    internal NoCastlingRights()
    {
    }
}

public sealed class KingSideCastlingRights
{
    internal KingSideCastlingRights()
    {
    }
}

public sealed class QueenSideCastlingRights
{
    internal QueenSideCastlingRights()
    {
    }
}

public sealed class BothCastlingRights
{
    internal BothCastlingRights()
    {
    }
}
