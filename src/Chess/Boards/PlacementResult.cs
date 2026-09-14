namespace Chess;

public union PlacementResult(Board, PlacementConflict);

public sealed record PlacementConflict
{
    public required Coordinate Coordinate { get; init; }

    public required OwnedPiece ExistingPiece { get; init; }
}
