namespace Chess.Tests;

internal static class MoveFixtures
{
    internal static Coordinate Square(string name) => BoardCoordinates.All().ElementAt((name[0] - 'a') * 8 + name[1] - '1');

    internal static MovePiece Move(string from, string to) => new() { From = Square(from), To = Square(to) };

    internal static OwnedPiece CreatePiece(char symbol) => new()
    {
        Side = char.IsUpper(symbol) ? Side.White : Side.Black,
        Piece = char.ToLowerInvariant(symbol) switch
        {
            'p' => Piece.Pawn,
            'n' => Piece.Knight,
            'b' => Piece.Bishop,
            'r' => Piece.Rook,
            'q' => Piece.Queen,
            'k' => Piece.King,
            _ => throw new ArgumentException("Unknown fixture piece.", nameof(symbol))
        }
    };

    internal static Position Setup(params (string Square, char Piece)[] pieces)
    {
        var board = new Board();
        foreach (var (square, piece) in pieces)
        {
            board = board.Place(Square(square), CreatePiece(piece)) switch
            {
                Board next => next,
                PlacementConflict => throw new ArgumentException("Duplicate fixture square.", nameof(pieces))
            };
        }

        return new Position
        {
            Board = board,
            SideToMove = Side.White,
            WhiteCastlingRights = CastlingRights.None,
            BlackCastlingRights = CastlingRights.None,
            EnPassant = EnPassantState.None,
            HalfmoveClock = 8,
            FullmoveNumber = 12
        };
    }

    internal static char At(Position position, string square) => position.Board[Square(square)] switch
    {
        Empty => '.',
        Occupied occupied => Symbol(occupied.Piece)
    };

    internal static async Task<T> Result<T>(MoveResult result) where T : class =>
        await Assert.That(result.Value).IsTypeOf<T>().And.IsNotNull();

    private static char Symbol(OwnedPiece piece)
    {
        var symbol = piece.Piece switch
        {
            Pawn => 'p',
            Knight => 'n',
            Bishop => 'b',
            Rook => 'r',
            Queen => 'q',
            King => 'k'
        };
        return piece.Side switch { White => char.ToUpperInvariant(symbol), Black => symbol };
    }
}
