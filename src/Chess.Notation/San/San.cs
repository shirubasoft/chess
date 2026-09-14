using System.Text.RegularExpressions;

namespace Chess.Notation;

public static partial class San
{
    public static NotationResult<MoveRequest> Parse(Position position, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var input = text.Replace('0', 'O');
        var stem = input.TrimEnd('+', '#');
        var legal = MoveRules.GetLegalMoves(position).ToArray();
        IEnumerable<MoveRequest> candidates;
        if (stem is "O-O" or "O-O-O")
        {
            candidates = legal.Where(move => move is Castle castle && (castle.Wing is KingSide) == (stem == "O-O"));
        }
        else
        {
            var match = MovePattern().Match(input);
            if (!match.Success) return Error(NotationErrorKind.Syntax, "Expected standard algebraic notation.");
            var piece = match.Groups["piece"].Value;
            var file = match.Groups["file"].Value;
            var rank = match.Groups["rank"].Value;
            var captures = match.Groups["capture"].Success;
            if (piece.Length == 0 && (rank.Length != 0 || captures != (file.Length != 0)))
                return Error(NotationErrorKind.Syntax, "Pawn captures require their source file; pawn pushes use only their destination.");
            var target = match.Groups["target"].Value;
            var promotion = match.Groups["promotion"].Value;
            candidates = legal.Where(move => Matches(move));

            bool Matches(MoveRequest move)
            {
                if (move is Castle) return false;
                var uci = Uci.Format(position, move);
                var source = NotationSyntax.Square(uci.AsSpan(0, 2));
                if (position.Board[source] is not Occupied occupied) return false;
                return NotationSyntax.Symbol(occupied.Piece.Piece) == (piece.Length == 0 ? 'P' : piece[0])
                    && uci.AsSpan(2, 2).SequenceEqual(target)
                    && (file.Length == 0 || uci[0] == file[0])
                    && (rank.Length == 0 || uci[1] == rank[0])
                    && IsCapture(position, move) == captures
                    && (promotion.Length == 0 ? move is not Promote : uci.Length == 5 && char.ToUpperInvariant(uci[4]) == promotion[1]);
            }
        }
        using var iterator = candidates.GetEnumerator();
        if (!iterator.MoveNext()) return Error(NotationErrorKind.IllegalMove, "No legal move matches this SAN.");
        var result = iterator.Current;
        if (iterator.MoveNext()) return Error(NotationErrorKind.AmbiguousMove, "The SAN needs a source file or rank to distinguish legal moves.");
        var canonical = Format(position, result, legal);
        if (canonical.TrimEnd('+', '#') != stem || input != stem && input != canonical)
            return Error(NotationErrorKind.Syntax, $"Expected {canonical} for this move.");
        return new Parsed<MoveRequest> { Value = result };
    }

    public static string Format(Position position, MoveRequest move) => Format(position, move, MoveRules.GetLegalMoves(position).ToArray());

    private static string Format(Position position, MoveRequest move, IReadOnlyCollection<MoveRequest> legal)
    {
        if (MoveRules.Apply(position, move) is not Position next)
            throw NotationSyntax.Error("Cannot format an illegal move as SAN.", kind: NotationErrorKind.IllegalMove);
        string stem;
        if (move is Castle castle) stem = castle.Wing is KingSide ? "O-O" : "O-O-O";
        else
        {
            var uci = Uci.Format(position, move);
            var from = NotationSyntax.Square(uci.AsSpan(0, 2));
            var owned = ((Occupied)position.Board[from].Value!).Piece;
            var capture = IsCapture(position, move);
            var prefix = "";
            if (owned.Piece is not Pawn)
            {
                prefix = NotationSyntax.Symbol(owned.Piece).ToString();
                var alternatives = legal.Where(other => other is not Castle && !other.Equals(move))
                    .Select(other => Uci.Format(position, other))
                    .Where(other => other.AsSpan(2, 2).SequenceEqual(uci.AsSpan(2, 2))
                        && position.Board[NotationSyntax.Square(other.AsSpan(0, 2))] is Occupied occupied
                        && occupied.Piece.Piece.Equals(owned.Piece)).ToArray();
                if (alternatives.Length != 0)
                {
                    prefix += alternatives.All(other => other[0] != uci[0]) ? uci[..1]
                        : alternatives.All(other => other[1] != uci[1]) ? uci.Substring(1, 1) : uci[..2];
                }
            }
            else if (capture) prefix = uci[..1];
            stem = prefix + (capture ? "x" : "") + uci.Substring(2, 2)
                + (move is Promote ? "=" + char.ToUpperInvariant(uci[4]) : "");
        }
        return stem + (MoveRules.IsInCheck(next) ? MoveRules.HasLegalMove(next) ? "+" : "#" : "");
    }

    private static bool IsCapture(Position position, MoveRequest move)
    {
        var uci = Uci.Format(position, move);
        return position.Board[NotationSyntax.Square(uci.AsSpan(2, 2))] is Occupied
            || position.Board[NotationSyntax.Square(uci.AsSpan(0, 2))] is Occupied { Piece.Piece: Pawn } && uci[0] != uci[2];
    }

    private static NotationError Error(NotationErrorKind kind, string message) => new() { Kind = kind, Message = message, Offset = 0 };

    [GeneratedRegex(@"\A(?<piece>[KQRBN])?(?<file>[a-h])?(?<rank>[1-8])?(?<capture>x)?(?<target>[a-h][1-8])(?<promotion>=[QRBN])?(?<check>[+#])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex MovePattern();
}
