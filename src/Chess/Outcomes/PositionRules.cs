namespace Chess;

public static class PositionRules
{
    public static PositionOutcome GetOutcome(
        Position position, DeadPositionSearch? search = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MoveRules.HasLegalMove(position))
        {
            return MoveRules.IsInCheck(position)
                ? new Checkmate { Winner = position.SideToMove switch { White => Side.Black, Black => Side.White } }
                : PositionOutcome.Stalemate;
        }

        search ??= DeadPositionSearch.Default;
        var remaining = search.MaximumPositions;
        return Search(position, search.MaximumDepth, ref remaining, cancellationToken) switch
        {
            Analysis.Dead => PositionOutcome.DeadPosition,
            Analysis.MateReachable => PositionOutcome.MatingContinuationExists,
            Analysis.Undetermined => PositionOutcome.Undetermined,
            _ => throw new InvalidOperationException("Unknown analysis result.")
        };
    }

    private static Analysis Search(Position position, int depth, ref int remaining, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (remaining == 0)
        {
            return Analysis.Undetermined;
        }
        remaining--;

        if (!MoveRules.HasLegalMove(position))
        {
            return MoveRules.IsInCheck(position) ? Analysis.MateReachable : Analysis.Dead;
        }
        if (HasDeadMaterial(position.Board))
        {
            return Analysis.Dead;
        }
        if (depth == 0)
        {
            return Analysis.Undetermined;
        }

        var allDead = true;
        var searchable = position with { HalfmoveClock = 0, FullmoveNumber = 1 };
        foreach (var move in MoveRules.GetLegalMoves(searchable))
        {
            if (remaining == 0)
            {
                return Analysis.Undetermined;
            }
            var next = MoveRules.Apply(searchable, move) switch
            {
                Position accepted => accepted,
                _ => throw new InvalidOperationException("A generated legal move must be applicable with reset counters.")
            };
            var result = Search(next, depth - 1, ref remaining, cancellationToken);
            if (result == Analysis.MateReachable)
            {
                return result;
            }
            allDead &= result == Analysis.Dead;
        }

        return allDead ? Analysis.Dead : Analysis.Undetermined;
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

    private enum Analysis
    {
        Dead,
        MateReachable,
        Undetermined
    }
}
