namespace Chess;

public union SquareContent(Empty, Occupied)
{
    public static Empty Empty { get; } = new();
}

public sealed class Empty
{
    internal Empty()
    {
    }
}

public sealed record Occupied
{
    public required OwnedPiece Piece { get; init; }
}
