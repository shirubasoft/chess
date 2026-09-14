namespace Chess;

public sealed record Position
{
    public static Position Initial { get; } = CreateInitial();

    public required Board Board { get; init; }

    public required Side SideToMove { get; init; }

    public required CastlingRights WhiteCastlingRights { get; init; }

    public required CastlingRights BlackCastlingRights { get; init; }

    public required EnPassantState EnPassant { get; init; }

    public required int HalfmoveClock
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    public required int FullmoveNumber
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    }

    private static Position CreateInitial()
    {
        Piece[] backRank = [Piece.Rook, Piece.Knight, Piece.Bishop, Piece.Queen, Piece.King, Piece.Bishop, Piece.Knight, Piece.Rook];
        var board = new Board();
        for (var file = 0; file < 8; file++)
        {
            Add(file, 0, Side.White, backRank[file]);
            Add(file, 1, Side.White, Piece.Pawn);
            Add(file, 6, Side.Black, Piece.Pawn);
            Add(file, 7, Side.Black, backRank[file]);
        }

        return new Position
        {
            Board = board,
            SideToMove = Side.White,
            WhiteCastlingRights = CastlingRights.Both,
            BlackCastlingRights = CastlingRights.Both,
            EnPassant = EnPassantState.None,
            HalfmoveClock = 0,
            FullmoveNumber = 1
        };

        void Add(int file, int rank, Side side, Piece piece)
        {
            board = board.Place(BoardGeometry.At(file, rank), new OwnedPiece { Side = side, Piece = piece }) switch
            {
                Board placed => placed,
                PlacementConflict => throw new InvalidOperationException("Initial squares must be distinct.")
            };
        }
    }
}
