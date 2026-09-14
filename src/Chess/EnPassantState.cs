namespace Chess;

public union EnPassantState(NoEnPassant, EnPassantTarget);

public sealed record NoEnPassant;

public sealed record EnPassantTarget
{
    public required Coordinate Square { get; init; }
}
