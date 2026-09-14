namespace Chess;

public union SquareContent(Empty, Occupied);

public sealed record Empty;

public sealed record Occupied
{
    public required OwnedPiece Piece { get; init; }
}
