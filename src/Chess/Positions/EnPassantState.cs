namespace Chess;

public union EnPassantState(NoEnPassant, EnPassantTarget)
{
    public static NoEnPassant None { get; } = new();
}

public sealed class NoEnPassant
{
    internal NoEnPassant()
    {
    }
}

public sealed record EnPassantTarget
{
    public required Coordinate Square { get; init; }
}
