using Chess.Client;
using Chess.Contracts;
using Chess.Notation;

namespace Chess.Web.Board;

public sealed record BoardSquare(string Name, bool Dark, string? Piece, PlayerSide? Side)
{
    public string Description => Piece is null ? $"{Name}, empty" : $"{Name}, {Side} {Piece}";
}

public static class BoardPresentation
{
    private static readonly BoardFile[] Files = [BoardFile.A, BoardFile.B, BoardFile.C, BoardFile.D, BoardFile.E, BoardFile.F, BoardFile.G, BoardFile.H];
    private static readonly BoardRank[] Ranks = [BoardRank.One, BoardRank.Two, BoardRank.Three, BoardRank.Four, BoardRank.Five, BoardRank.Six, BoardRank.Seven, BoardRank.Eight];

    public static IEnumerable<BoardSquare> Squares(Position position, PlayerSide orientation)
    {
        for (var row = 0; row < 8; row++)
        for (var column = 0; column < 8; column++)
        {
            var file = orientation == PlayerSide.White ? column : 7 - column;
            var rank = orientation == PlayerSide.White ? 7 - row : row;
            var content = position.Board[new Coordinate { File = Files[file], Rank = Ranks[rank] }];
            var occupied = content is Occupied square ? square.Piece : null;
            yield return new BoardSquare($"{(char)('a' + file)}{rank + 1}", (file + rank) % 2 == 0,
                occupied is null ? null : Name(occupied.Piece),
                occupied is null ? null : occupied.Side is White ? PlayerSide.White : PlayerSide.Black);
        }
    }

    public static string[] LegalMoves(GameSnapshot snapshot)
    {
        if (snapshot.Status != GameStatus.Active) return [];
        var position = ChessClient.PositionOf(snapshot);
        var allowed = snapshot.LegalMoves.ToHashSet(StringComparer.Ordinal);
        return MoveRules.GetLegalMoves(position).Select(move => Uci.Format(position, move))
            .Where(allowed.Contains).ToArray();
    }

    public static IEnumerable<string> Destinations(IEnumerable<string> moves, string? source) =>
        source is null ? [] : moves.Where(move => move.StartsWith(source, StringComparison.Ordinal))
            .Select(move => move.Substring(2, 2)).Distinct(StringComparer.Ordinal);

    public static string[] MovesBetween(IEnumerable<string> moves, string source, string target) =>
        moves.Where(move => move.StartsWith(source + target, StringComparison.Ordinal)).ToArray();

    public static string Status(GameSnapshot snapshot, PlayerSide side) => snapshot.Status switch
    {
        GameStatus.Waiting => "Waiting for an opponent",
        GameStatus.Active when snapshot.SideToMove == side => "Your move",
        GameStatus.Active => $"{snapshot.SideToMove} to move",
        GameStatus.Finished when snapshot.Result?.Winner is { } winner => $"{winner} wins",
        GameStatus.Finished => "Game drawn",
        _ => "Game unavailable"
    };

    private static string Name(Piece piece) => piece switch
    {
        Pawn => "pawn", Knight => "knight", Bishop => "bishop", Rook => "rook", Queen => "queen", King => "king",
        _ => throw new ArgumentException("The board contains an unknown piece.", nameof(piece))
    };
}
