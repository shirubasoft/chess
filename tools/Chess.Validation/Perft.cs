using Chess.Notation;

namespace Chess.Validation;

internal static class Perft
{
    internal static long Count(Position position, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        if (depth == 0) return 1;
        long nodes = 0;
        foreach (var move in MoveRules.GetLegalMoves(position))
        {
            var next = MoveRules.Apply(position, move) switch
            {
                Position accepted => accepted,
                _ => throw new InvalidOperationException("A generated move was rejected.")
            };
            nodes = checked(nodes + Count(next, depth - 1));
        }
        return nodes;
    }

    internal static DivideResult Divide(Position position, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        var moves = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var move in depth == 0 ? [] : MoveRules.GetLegalMoves(position))
        {
            var next = (Position)MoveRules.Apply(position, move).Value!;
            moves.Add(Uci.Format(position, move), Count(next, depth - 1));
        }
        return new DivideResult { Moves = moves, Nodes = depth == 0 ? 1 : moves.Values.Sum() };
    }
}

internal sealed record DivideResult
{
    public required SortedDictionary<string, long> Moves { get; init; }
    public required long Nodes { get; init; }
}
