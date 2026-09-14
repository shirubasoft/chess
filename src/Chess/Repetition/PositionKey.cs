namespace Chess;

public sealed record PositionKey
{
    private PositionKey(Position position)
    {
        PiecePlacement = string.Create(64, position.Board, (characters, board) =>
        {
            var index = 0;
            foreach (var square in BoardGeometry.All())
            {
                characters[index++] = board[square] switch
                {
                    Empty => '.',
                    Occupied occupied => Symbol(occupied.Piece)
                };
            }
        });
        SideToMove = position.SideToMove;
        WhiteCastlingRights = position.WhiteCastlingRights;
        BlackCastlingRights = position.BlackCastlingRights;
        EnPassant = LegalEnPassant(position);
    }

    public string PiecePlacement { get; }

    public Side SideToMove { get; }

    public CastlingRights WhiteCastlingRights { get; }

    public CastlingRights BlackCastlingRights { get; }

    public EnPassantState EnPassant { get; }

    public static PositionKey Create(Position position) => new(position);

    private static EnPassantState LegalEnPassant(Position position)
    {
        if (position.EnPassant is not EnPassantTarget target || position.Board[target.Square] is Occupied)
        {
            return EnPassantState.None;
        }
        var white = position.SideToMove switch { White => true, Black => false };
        var targetRank = BoardGeometry.Rank(target.Square);
        if (targetRank != (white ? 5 : 2))
        {
            return EnPassantState.None;
        }
        var targetFile = BoardGeometry.File(target.Square);
        var searchable = position with { HalfmoveClock = 0, FullmoveNumber = 1 };
        for (var file = targetFile - 1; file <= targetFile + 1; file += 2)
        {
            if (file is < 0 or > 7)
            {
                continue;
            }
            var from = BoardGeometry.At(file, targetRank + (white ? -1 : 1));
            if (position.Board[from] is Occupied occupied && occupied.Piece.Piece is Pawn
                && MoveRules.Apply(searchable, new MovePiece { From = from, To = target.Square }) is Position)
            {
                return target;
            }
        }
        return EnPassantState.None;
    }

    private static char Symbol(OwnedPiece owned)
    {
        var symbol = owned.Piece switch
        {
            Pawn => 'p',
            Knight => 'n',
            Bishop => 'b',
            Rook => 'r',
            Queen => 'q',
            King => 'k'
        };
        return owned.Side switch { White => char.ToUpperInvariant(symbol), Black => symbol };
    }
}
