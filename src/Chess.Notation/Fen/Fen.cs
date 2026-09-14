using System.Globalization;
using System.Text;

namespace Chess.Notation;

public static class Fen
{
    public static NotationResult<Position> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return new Parsed<Position> { Value = ParsePosition(text) };
        }
        catch (NotationException error)
        {
            return error.Error;
        }
    }

    public static string Format(Position position)
    {
        var text = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                if (position.Board[NotationSyntax.At(file, rank)] is not Occupied occupied)
                {
                    empty++;
                    continue;
                }
                if (empty > 0)
                {
                    text.Append(empty);
                    empty = 0;
                }
                var symbol = NotationSyntax.Symbol(occupied.Piece.Piece);
                text.Append(occupied.Piece.Side is White ? symbol : char.ToLowerInvariant(symbol));
            }
            if (empty > 0) text.Append(empty);
            if (rank > 0) text.Append('/');
        }
        text.Append(position.SideToMove is White ? " w " : " b ");
        var rightsStart = text.Length;
        AppendRights(position.WhiteCastlingRights, 'K', 'Q');
        AppendRights(position.BlackCastlingRights, 'k', 'q');
        if (text.Length == rightsStart) text.Append('-');
        text.Append(' ').Append(position.EnPassant is EnPassantTarget target ? NotationSyntax.Square(target.Square) : "-");
        text.Append(' ').Append(position.HalfmoveClock.ToString(CultureInfo.InvariantCulture));
        text.Append(' ').Append(position.FullmoveNumber.ToString(CultureInfo.InvariantCulture));
        return text.ToString();

        void AppendRights(CastlingRights rights, char king, char queen)
        {
            if (rights is KingSideCastlingRights or BothCastlingRights) text.Append(king);
            if (rights is QueenSideCastlingRights or BothCastlingRights) text.Append(queen);
        }
    }

    private static Position ParsePosition(string text)
    {
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 6) throw NotationSyntax.Error("FEN requires six fields.");
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8) throw NotationSyntax.Error("FEN requires eight ranks.");
        var board = new Board();
        var whiteKings = 0;
        var blackKings = 0;
        var offset = 0;
        for (var row = 0; row < 8; row++)
        {
            var file = 0;
            var previousDigit = false;
            foreach (var symbol in ranks[row])
            {
                if (symbol is >= '1' and <= '8')
                {
                    if (previousDigit) throw NotationSyntax.Error("Empty squares must use a single digit.", offset);
                    file += symbol - '0';
                    previousDigit = true;
                }
                else
                {
                    if (!"PNBRQKpnbrqk".Contains(symbol) || file >= 8)
                        throw NotationSyntax.Error("Invalid piece placement.", offset);
                    if (symbol == 'K') whiteKings++;
                    if (symbol == 'k') blackKings++;
                    if (symbol is 'P' or 'p' && row is 0 or 7)
                        throw NotationSyntax.Error("A pawn cannot occupy a promotion rank.", offset, NotationErrorKind.InvalidPosition);
                    var piece = new OwnedPiece
                    {
                        Side = char.IsUpper(symbol) ? Side.White : Side.Black,
                        Piece = NotationSyntax.Piece(char.ToUpperInvariant(symbol))
                    };
                    board = board.Place(NotationSyntax.At(file++, 7 - row), piece) switch
                    {
                        Board placed => placed,
                        PlacementConflict => throw new InvalidOperationException("FEN ranks must have distinct squares.")
                    };
                    previousDigit = false;
                }
                if (file > 8) throw NotationSyntax.Error("A rank exceeds eight squares.", offset);
                offset++;
            }
            if (file != 8) throw NotationSyntax.Error("Every rank must describe eight squares.", offset);
            offset++;
        }
        if (whiteKings != 1 || blackKings != 1)
            throw NotationSyntax.Error("A position requires one king of each side.", kind: NotationErrorKind.InvalidPosition);
        Side turn = fields[1] switch
        {
            "w" => Side.White, "b" => Side.Black,
            _ => throw NotationSyntax.Error("Active color must be w or b.")
        };
        var castling = fields[2];
        if (castling != "-" && (castling.Length == 0 || castling.Any(c => !"KQkq".Contains(c)) || castling.Distinct().Count() != castling.Length))
            throw NotationSyntax.Error("Castling rights must contain distinct KQkq symbols or a dash.");
        EnPassantState enPassant = EnPassantState.None;
        if (fields[3] != "-")
        {
            var target = NotationSyntax.Square(fields[3]);
            BoardRank expectedRank = turn is White ? BoardRank.Six : BoardRank.Three;
            if (!target.Rank.Equals(expectedRank))
                throw NotationSyntax.Error("En passant rank does not match the active color.", kind: NotationErrorKind.InvalidPosition);
            enPassant = new EnPassantTarget { Square = target };
        }
        if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var halfmove))
            throw NotationSyntax.Error("Halfmove clock must be a nonnegative Int32.");
        if (!int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var fullmove) || fullmove < 1)
            throw NotationSyntax.Error("Fullmove number must be a positive Int32.");
        var position = new Position
        {
            Board = board, SideToMove = turn, WhiteCastlingRights = Rights('K', 'Q'),
            BlackCastlingRights = Rights('k', 'q'), EnPassant = enPassant,
            HalfmoveClock = halfmove, FullmoveNumber = fullmove
        };
        ValidateSetup(position);
        return position;

        CastlingRights Rights(char king, char queen) => (castling.Contains(king), castling.Contains(queen)) switch
        {
            (true, true) => CastlingRights.Both, (true, false) => CastlingRights.KingSide,
            (false, true) => CastlingRights.QueenSide, (false, false) => CastlingRights.None
        };
    }

    private static void ValidateSetup(Position position)
    {
        Side previous = position.SideToMove is White ? Side.Black : Side.White;
        if (MoveRules.IsInCheck(position, previous))
            throw NotationSyntax.Error("The side that just moved cannot be in check.", kind: NotationErrorKind.InvalidPosition);
        ValidateRights(position.WhiteCastlingRights, Side.White, 0);
        ValidateRights(position.BlackCastlingRights, Side.Black, 7);
        if (position.EnPassant is EnPassantTarget target)
        {
            var rank = position.SideToMove is White ? 4 : 3;
            var file = NotationSyntax.Square(target.Square)[0] - 'a';
            if (position.Board[target.Square] is Occupied
                || position.Board[NotationSyntax.At(file, rank)] is not Occupied { Piece: { Piece: Pawn } } pawn
                || !pawn.Piece.Side.Equals(previous)
                || position.Board[NotationSyntax.At(file, position.SideToMove is White ? 6 : 1)] is Occupied)
                throw NotationSyntax.Error("En passant requires the preceding double pawn push.", kind: NotationErrorKind.InvalidPosition);
        }

        void ValidateRights(CastlingRights rights, Side side, int rank)
        {
            if (rights is NoCastlingRights) return;
            if (!Has(4, Piece.King)
                || rights is KingSideCastlingRights or BothCastlingRights && !Has(7, Piece.Rook)
                || rights is QueenSideCastlingRights or BothCastlingRights && !Has(0, Piece.Rook))
                throw NotationSyntax.Error("Castling rights require the original king and rook squares.", kind: NotationErrorKind.InvalidPosition);
            bool Has(int file, Piece piece) => position.Board[NotationSyntax.At(file, rank)] is Occupied occupied
                && occupied.Piece.Side.Equals(side) && occupied.Piece.Piece.Equals(piece);
        }
    }
}
