namespace Chess.Notation;

public static class Uci
{
    public static NotationResult<MoveRequest> Parse(Position position, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            if (text.Length is not (4 or 5)) throw NotationSyntax.Error("A UCI move has four characters and an optional promotion suffix.");
            var from = NotationSyntax.Square(text.AsSpan(0, 2));
            var to = NotationSyntax.Square(text.AsSpan(2, 2), 2);
            MoveRequest request;
            if (text.Length == 5)
            {
                PromotionPiece piece = text[4] switch
                {
                    'q' => PromotionPiece.Queen, 'r' => PromotionPiece.Rook,
                    'b' => PromotionPiece.Bishop, 'n' => PromotionPiece.Knight,
                    _ => throw NotationSyntax.Error("Promotion suffix must be q, r, b, or n.", 4)
                };
                request = new Promote { From = from, To = to, Piece = piece };
            }
            else if (position.Board[from] is Occupied { Piece.Piece: King }
                && text[0] == 'e' && text[2] is 'c' or 'g' && text[1] == text[3]
                && text[1] == (position.SideToMove is White ? '1' : '8'))
            {
                request = new Castle { Wing = text[2] == 'g' ? CastlingWing.KingSide : CastlingWing.QueenSide };
            }
            else request = new MovePiece { From = from, To = to };
            return MoveRules.Apply(position, request) is Position
                ? new Parsed<MoveRequest> { Value = request }
                : new NotationError { Kind = NotationErrorKind.IllegalMove, Message = "The UCI move is illegal in this position.", Offset = 0 };
        }
        catch (NotationException error)
        {
            return error.Error;
        }
    }

    public static string Format(Position position, MoveRequest move) => move switch
    {
        MovePiece ordinary => NotationSyntax.Square(ordinary.From) + NotationSyntax.Square(ordinary.To),
        Promote promotion => NotationSyntax.Square(promotion.From) + NotationSyntax.Square(promotion.To) + (promotion.Piece switch
        {
            Queen => "q", Rook => "r", Bishop => "b", Knight => "n"
        }),
        Castle castle => position.SideToMove is White
            ? castle.Wing is KingSide ? "e1g1" : "e1c1"
            : castle.Wing is KingSide ? "e8g8" : "e8c8"
    };
}
