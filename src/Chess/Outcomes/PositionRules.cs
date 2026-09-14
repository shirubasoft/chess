namespace Chess;

public static class PositionRules
{
    public static PositionOutcome GetOutcome(Position position)
    {
        using var moves = MoveRules.GetLegalMoves(position).GetEnumerator();
        if (!moves.MoveNext())
        {
            return MoveRules.IsInCheck(position)
                ? new Checkmate { Winner = position.SideToMove switch { White => Side.Black, Black => Side.White } }
                : PositionOutcome.Stalemate;
        }

        if (HasDeadMaterial(position.Board)
            || CaptureLeavesBareKings(position.Board, moves.Current) && !moves.MoveNext())
        {
            return PositionOutcome.DeadPosition;
        }

        return PositionOutcome.Ongoing;
    }

    private static bool CaptureLeavesBareKings(Board board, MoveRequest move)
    {
        if (move is not MovePiece capture
            || board[capture.From] is not Occupied { Piece.Piece: King }
            || board[capture.To] is not Occupied)
        {
            return false;
        }

        foreach (var square in BoardGeometry.All())
        {
            if (square != capture.To && board[square] is Occupied { Piece.Piece: not King })
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDeadMaterial(Board board)
    {
        var minors = 0;
        var knights = 0;
        int? bishopColor = null;
        var sameColorBishops = true;
        foreach (var square in BoardGeometry.All())
        {
            if (board[square] is not Occupied occupied || occupied.Piece.Piece is King)
            {
                continue;
            }
            switch (occupied.Piece.Piece)
            {
                case Knight:
                    minors++;
                    knights++;
                    break;
                case Bishop:
                    minors++;
                    var color = (BoardGeometry.File(square) + BoardGeometry.Rank(square)) % 2;
                    bishopColor ??= color;
                    sameColorBishops &= color == bishopColor;
                    break;
                default:
                    return false;
            }
        }

        return minors <= 1 || knights == 0 && sameColorBishops;
    }
}
