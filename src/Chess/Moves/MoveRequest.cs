namespace Chess;

public union MoveRequest(MovePiece, Castle, Promote);

public sealed record MovePiece
{
    public required Coordinate From { get; init; }

    public required Coordinate To { get; init; }
}

public sealed record Castle
{
    public required CastlingWing Wing { get; init; }
}

public sealed record Promote
{
    public required Coordinate From { get; init; }

    public required Coordinate To { get; init; }

    public required PromotionPiece Piece { get; init; }
}
