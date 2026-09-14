using Chess.Contracts;

namespace Chess.Client;

public sealed record BoardSquare
{
    public required string Name { get; init; }
    public required string Symbol { get; init; }
    public required string Description { get; init; }
    public required bool IsLight { get; init; }
}

public static class BoardPresentation
{
    private static readonly BoardFile[] Files = [BoardFile.A, BoardFile.B, BoardFile.C, BoardFile.D, BoardFile.E, BoardFile.F, BoardFile.G, BoardFile.H];
    private static readonly BoardRank[] Ranks = [BoardRank.One, BoardRank.Two, BoardRank.Three, BoardRank.Four, BoardRank.Five, BoardRank.Six, BoardRank.Seven, BoardRank.Eight];

    public static IEnumerable<BoardSquare> Squares(GameSnapshot? snapshot, PlayerSide perspective = PlayerSide.White)
    {
        var position = snapshot is null ? Position.Initial : ChessClient.PositionOf(snapshot);
        for (var row = 0; row < 8; row++)
        for (var column = 0; column < 8; column++)
        {
            var file = perspective == PlayerSide.White ? column : 7 - column;
            var rank = perspective == PlayerSide.White ? 7 - row : row;
            var name = $"{(char)('a' + file)}{rank + 1}";
            var coordinate = new Coordinate { File = Files[file], Rank = Ranks[rank] };
            var (symbol, description) = position.Board[coordinate] switch
            {
                Occupied occupied => PieceText(occupied.Piece),
                Empty => ("", "Empty"),
                null => throw new InvalidOperationException("The board contains an uninitialized square.")
            };
            yield return new BoardSquare { Name = name, Symbol = symbol, Description = $"{name}, {description}", IsLight = (file + rank) % 2 != 0 };
        }
    }

    private static (string Symbol, string Description) PieceText(OwnedPiece owned)
    {
        var white = owned.Side is White;
        var (symbol, name) = owned.Piece switch
        {
            Pawn => (white ? "♙" : "♟", "pawn"), Knight => (white ? "♘" : "♞", "knight"),
            Bishop => (white ? "♗" : "♝", "bishop"), Rook => (white ? "♖" : "♜", "rook"),
            Queen => (white ? "♕" : "♛", "queen"), King => (white ? "♔" : "♚", "king")
        };
        return (symbol, $"{(white ? "White" : "Black")} {name}");
    }
}
