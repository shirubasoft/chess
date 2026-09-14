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

    internal static Position FromFen(string fen)
    {
        var fields = fen.Split(' ');
        var pieces = new List<(string Square, char Piece)>();
        var ranks = fields[0].Split('/');
        for (var rank = 0; rank < 8; rank++)
        {
            var file = 0;
            foreach (var symbol in ranks[rank])
            {
                if (char.IsDigit(symbol))
                {
                    file += symbol - '0';
                }
                else
                {
                    pieces.Add(($"{(char)('a' + file++)}{8 - rank}", symbol));
                }
            }
        }
        return Setup(pieces.ToArray()) with
        {
            SideToMove = fields[1] == "w" ? Side.White : Side.Black,
            WhiteCastlingRights = Rights('K', 'Q'),
            BlackCastlingRights = Rights('k', 'q'),
            EnPassant = fields[3] == "-" ? EnPassantState.None : new EnPassantTarget { Square = Square(fields[3]) },
            HalfmoveClock = int.Parse(fields[4]),
            FullmoveNumber = int.Parse(fields[5])
        };

        CastlingRights Rights(char kingSide, char queenSide) => (fields[2].Contains(kingSide), fields[2].Contains(queenSide)) switch
        {
            (true, true) => CastlingRights.Both,
            (true, false) => CastlingRights.KingSide,
            (false, true) => CastlingRights.QueenSide,
            (false, false) => CastlingRights.None
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
