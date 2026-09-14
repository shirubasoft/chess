namespace Chess;

public sealed record OwnedPiece
{
    public required Side Side { get; init; }

    public required Piece Piece { get; init; }
}
