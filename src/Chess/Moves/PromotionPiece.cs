namespace Chess;

public union PromotionPiece(Queen, Rook, Bishop, Knight)
{
    public static Queen Queen => Piece.Queen;

    public static Rook Rook => Piece.Rook;

    public static Bishop Bishop => Piece.Bishop;

    public static Knight Knight => Piece.Knight;
}
