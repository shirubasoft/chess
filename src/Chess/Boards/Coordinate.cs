namespace Chess;

public readonly record struct Coordinate
{
    public required BoardFile File { get; init; }

    public required BoardRank Rank { get; init; }
}
