namespace Chess;

internal static class MatingMaterial
{
    internal static bool IsKnownInsufficient(Board board, Side side)
    {
        var material = Pieces(board).ToArray();
        var own = material.Where(item => item.Piece.Side.Equals(side)).ToArray();
        if (own.Length == 0)
        {
            return true;
        }
        if (own.Any(item => item.Piece.Piece is Pawn or Rook or Queen))
        {
            return false;
        }
        if (own.Any(item => item.Piece.Piece is Knight))
        {
            return own.Length == 1
                && material.Where(item => !item.Piece.Side.Equals(side)).All(item => item.Piece.Piece is Queen);
        }

        var bishopColor = Color(own[0].Square);
        return material.All(item => item.Piece.Piece switch
        {
            Pawn or Knight => false,
            Bishop => Color(item.Square) == bishopColor,
            _ => true
        });
    }

    private static int Color(Coordinate square) => (BoardGeometry.File(square) + BoardGeometry.Rank(square)) % 2;

    private static IEnumerable<(Coordinate Square, OwnedPiece Piece)> Pieces(Board board)
    {
        foreach (var square in BoardGeometry.All())
        {
            if (board[square] is Occupied { Piece.Piece: not King } occupied)
            {
                yield return (square, occupied.Piece);
            }
        }
    }
}
