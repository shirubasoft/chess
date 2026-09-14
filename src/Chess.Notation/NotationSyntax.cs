namespace Chess.Notation;

internal static class NotationSyntax
{
    private static readonly BoardFile[] Files = [BoardFile.A, BoardFile.B, BoardFile.C, BoardFile.D, BoardFile.E, BoardFile.F, BoardFile.G, BoardFile.H];
    private static readonly BoardRank[] Ranks = [BoardRank.One, BoardRank.Two, BoardRank.Three, BoardRank.Four, BoardRank.Five, BoardRank.Six, BoardRank.Seven, BoardRank.Eight];

    internal static Coordinate At(int file, int rank) => new() { File = Files[file], Rank = Ranks[rank] };

    internal static string Square(Coordinate square) => $"{(char)('a' + Array.IndexOf(Files, square.File))}{Array.IndexOf(Ranks, square.Rank) + 1}";

    internal static Coordinate Square(ReadOnlySpan<char> text, int offset = 0)
    {
        if (text.Length != 2 || text[0] is < 'a' or > 'h' || text[1] is < '1' or > '8')
        {
            throw Error("Expected a square from a1 to h8.", offset);
        }
        return At(text[0] - 'a', text[1] - '1');
    }

    internal static char Symbol(Piece piece) => piece switch
    {
        Pawn => 'P', Knight => 'N', Bishop => 'B', Rook => 'R', Queen => 'Q', King => 'K'
    };

    internal static Piece Piece(char symbol) => symbol switch
    {
        'P' => Chess.Piece.Pawn, 'N' => Chess.Piece.Knight, 'B' => Chess.Piece.Bishop,
        'R' => Chess.Piece.Rook, 'Q' => Chess.Piece.Queen, 'K' => Chess.Piece.King,
        _ => throw Error("Unknown piece symbol.")
    };

    internal static NotationException Error(string message, int offset = 0, NotationErrorKind kind = NotationErrorKind.Syntax) =>
        new(new NotationError { Kind = kind, Message = message, Offset = offset });
}
