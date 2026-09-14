namespace Chess;

public static class MoveRules
{
    public static bool IsInCheck(Position position) => IsInCheck(position, position.SideToMove);

    public static bool IsInCheck(Position position, Side side) => KingIsAttacked(position.Board, IsWhite(side));

    public static IEnumerable<MoveRequest> GetLegalMoves(Position position)
    {
        // Counters limit representable moves, not which moves are legal on the board.
        var searchable = position with { HalfmoveClock = 0, FullmoveNumber = 1 };
        foreach (var request in Candidates(searchable))
        {
            if (Apply(searchable, request) is Position)
            {
                yield return request;
            }
        }
    }

    public static bool HasLegalMove(Position position) => GetLegalMoves(position).Any();

    public static MoveResult Apply(Position position, MoveRequest request) => request switch
    {
        MovePiece move => ApplyPieceMove(position, move.From, move.To, null),
        Promote promote => ApplyPieceMove(position, promote.From, promote.To, promote.Piece switch
        {
            Queen queen => (Piece)queen,
            Rook rook => rook,
            Bishop bishop => bishop,
            Knight knight => knight
        }),
        Castle castle => ApplyCastling(position, castle.Wing)
    };

    private static IEnumerable<MoveRequest> Candidates(Position position)
    {
        var white = IsWhite(position.SideToMove);
        foreach (var from in BoardGeometry.All())
        {
            if (position.Board[from] is not Occupied source || IsWhite(source.Piece.Side) != white)
            {
                continue;
            }

            foreach (var to in BoardGeometry.All())
            {
                var dx = BoardGeometry.File(to) - BoardGeometry.File(from);
                var dy = BoardGeometry.Rank(to) - BoardGeometry.Rank(from);
                var possible = source.Piece.Piece switch
                {
                    Pawn => Math.Abs(dx) <= 1 && (dy == (white ? 1 : -1) || dx == 0 && dy == (white ? 2 : -2)),
                    Knight => Math.Abs(dx) * Math.Abs(dy) == 2,
                    Bishop => Math.Abs(dx) == Math.Abs(dy),
                    Rook => dx == 0 || dy == 0,
                    Queen => dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy),
                    King => Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1
                };
                if (!possible || from == to)
                {
                    continue;
                }

                if (source.Piece.Piece is Pawn && BoardGeometry.Rank(to) == (white ? 7 : 0))
                {
                    yield return new Promote { From = from, To = to, Piece = PromotionPiece.Queen };
                    yield return new Promote { From = from, To = to, Piece = PromotionPiece.Rook };
                    yield return new Promote { From = from, To = to, Piece = PromotionPiece.Bishop };
                    yield return new Promote { From = from, To = to, Piece = PromotionPiece.Knight };
                }
                else
                {
                    yield return new MovePiece { From = from, To = to };
                }
            }
        }

        yield return new Castle { Wing = CastlingWing.KingSide };
        yield return new Castle { Wing = CastlingWing.QueenSide };
    }

    private static MoveResult ApplyPieceMove(Position position, Coordinate from, Coordinate to, Piece? promotion)
    {
        if (position.Board[from] is not Occupied source)
        {
            return MoveResult.SourceSquareEmpty;
        }

        var moving = source.Piece;
        var white = IsWhite(moving.Side);
        if (white != IsWhite(position.SideToMove))
        {
            return MoveResult.WrongSideToMove;
        }

        if (from == to)
        {
            return MoveResult.InvalidMovement;
        }

        OwnedPiece? captured = null;
        var capturedSquare = to;
        if (position.Board[to] is Occupied destination)
        {
            if (IsWhite(destination.Piece.Side) == white)
            {
                return MoveResult.FriendlyPieceOnDestination;
            }

            if (destination.Piece.Piece is King)
            {
                return MoveResult.KingCaptureNotAllowed;
            }

            captured = destination.Piece;
        }

        var fileDelta = BoardGeometry.File(to) - BoardGeometry.File(from);
        var rankDelta = BoardGeometry.Rank(to) - BoardGeometry.Rank(from);
        var pawn = moving.Piece is Pawn;
        var promotes = pawn && BoardGeometry.Rank(to) == (white ? 7 : 0);
        if (promotion.HasValue && !promotes)
        {
            return MoveResult.InvalidPromotion;
        }

        EnPassantState enPassant = EnPassantState.None;
        if (pawn)
        {
            var direction = white ? 1 : -1;
            if (fileDelta == 0 && captured is null
                && (rankDelta == direction
                    || rankDelta == 2 * direction && BoardGeometry.Rank(from) == (white ? 1 : 6)))
            {
                if (rankDelta == 2 * direction)
                {
                    var middle = BoardGeometry.At(BoardGeometry.File(from), BoardGeometry.Rank(from) + direction);
                    if (position.Board[middle] is Occupied)
                    {
                        return MoveResult.PathBlocked;
                    }

                    enPassant = new EnPassantTarget { Square = middle };
                }
            }
            else if (Math.Abs(fileDelta) == 1 && rankDelta == direction)
            {
                if (captured is null)
                {
                    if (BoardGeometry.Rank(from) != (white ? 4 : 3)
                        || position.EnPassant is not EnPassantTarget target || target.Square != to)
                    {
                        return MoveResult.InvalidMovement;
                    }

                    capturedSquare = BoardGeometry.At(BoardGeometry.File(to), BoardGeometry.Rank(from));
                    if (position.Board[capturedSquare] is not Occupied adjacent
                        || adjacent.Piece.Piece is not Pawn || IsWhite(adjacent.Piece.Side) == white)
                    {
                        return MoveResult.InvalidMovement;
                    }

                    captured = adjacent.Piece;
                }
            }
            else
            {
                return MoveResult.InvalidMovement;
            }
        }
        else
        {
            var validShape = moving.Piece switch
            {
                Knight => Math.Abs(fileDelta) * Math.Abs(rankDelta) == 2,
                Bishop => Math.Abs(fileDelta) == Math.Abs(rankDelta),
                Rook => fileDelta == 0 || rankDelta == 0,
                Queen => fileDelta == 0 || rankDelta == 0 || Math.Abs(fileDelta) == Math.Abs(rankDelta),
                King => Math.Max(Math.Abs(fileDelta), Math.Abs(rankDelta)) == 1,
                Pawn => false
            };
            if (!validShape)
            {
                return MoveResult.InvalidMovement;
            }

            if (moving.Piece is Bishop or Rook or Queen && !PathIsClear(position.Board, from, to))
            {
                return MoveResult.PathBlocked;
            }
        }

        if (promotes && !promotion.HasValue)
        {
            return MoveResult.PromotionRequired;
        }

        var board = position.Board.Remove(from).Remove(capturedSquare);
        board = Place(board, to, moving with { Piece = promotion ?? moving.Piece });
        return Complete(position, board, moving, from, captured, capturedSquare, enPassant);
    }

    private static MoveResult ApplyCastling(Position position, CastlingWing wing)
    {
        var white = IsWhite(position.SideToMove);
        var kingSide = wing switch { KingSide => true, QueenSide => false };
        var rights = white ? position.WhiteCastlingRights : position.BlackCastlingRights;
        var permitted = rights switch
        {
            NoCastlingRights => false,
            KingSideCastlingRights => kingSide,
            QueenSideCastlingRights => !kingSide,
            BothCastlingRights => true
        };
        var rank = white ? 0 : 7;
        var kingFrom = BoardGeometry.At(4, rank);
        var rookFrom = BoardGeometry.At(kingSide ? 7 : 0, rank);
        if (!permitted
            || position.Board[kingFrom] is not Occupied king || king.Piece.Piece is not King
            || IsWhite(king.Piece.Side) != white
            || position.Board[rookFrom] is not Occupied rook || rook.Piece.Piece is not Rook
            || IsWhite(rook.Piece.Side) != white)
        {
            return MoveResult.CastlingUnavailable;
        }

        if (!PathIsClear(position.Board, kingFrom, rookFrom))
        {
            return MoveResult.PathBlocked;
        }

        var transit = BoardGeometry.At(kingSide ? 5 : 3, rank);
        var kingTo = BoardGeometry.At(kingSide ? 6 : 2, rank);
        if (IsAttacked(position.Board, kingFrom, !white)
            || IsAttacked(Place(position.Board.Remove(kingFrom), transit, king.Piece), transit, !white))
        {
            return MoveResult.KingWouldBeInCheck;
        }

        var board = position.Board.Remove(kingFrom).Remove(rookFrom);
        board = Place(Place(board, kingTo, king.Piece), transit, rook.Piece);
        return Complete(position, board, king.Piece, kingFrom, null, kingTo, EnPassantState.None);
    }

    private static MoveResult Complete(
        Position position, Board board, OwnedPiece moving, Coordinate from,
        OwnedPiece? captured, Coordinate capturedSquare, EnPassantState enPassant)
    {
        var white = IsWhite(moving.Side);
        if (KingIsAttacked(board, white))
        {
            return MoveResult.KingWouldBeInCheck;
        }

        var resetClock = moving.Piece is Pawn || captured is not null;
        if ((!resetClock && position.HalfmoveClock == int.MaxValue)
            || (!white && position.FullmoveNumber == int.MaxValue))
        {
            return MoveResult.MoveCounterOverflow;
        }

        var whiteRights = position.WhiteCastlingRights;
        var blackRights = position.BlackCastlingRights;
        if (moving.Piece is King)
        {
            if (white)
            {
                whiteRights = CastlingRights.None;
            }
            else
            {
                blackRights = CastlingRights.None;
            }
        }
        else if (moving.Piece is Rook)
        {
            if (white)
            {
                whiteRights = RemoveRookRight(whiteRights, from, true);
            }
            else
            {
                blackRights = RemoveRookRight(blackRights, from, false);
            }
        }

        if (captured is not null && captured.Piece is Rook)
        {
            if (IsWhite(captured.Side))
            {
                whiteRights = RemoveRookRight(whiteRights, capturedSquare, true);
            }
            else
            {
                blackRights = RemoveRookRight(blackRights, capturedSquare, false);
            }
        }

        return position with
        {
            Board = board,
            SideToMove = white ? Side.Black : Side.White,
            WhiteCastlingRights = whiteRights,
            BlackCastlingRights = blackRights,
            EnPassant = enPassant,
            HalfmoveClock = resetClock ? 0 : position.HalfmoveClock + 1,
            FullmoveNumber = white ? position.FullmoveNumber : position.FullmoveNumber + 1
        };
    }

    private static CastlingRights RemoveRookRight(CastlingRights rights, Coordinate square, bool white)
    {
        if (BoardGeometry.Rank(square) != (white ? 0 : 7))
        {
            return rights;
        }

        return BoardGeometry.File(square) switch
        {
            0 => rights switch
            {
                NoCastlingRights => rights,
                KingSideCastlingRights => rights,
                QueenSideCastlingRights => CastlingRights.None,
                BothCastlingRights => CastlingRights.KingSide
            },
            7 => rights switch
            {
                NoCastlingRights => rights,
                KingSideCastlingRights => CastlingRights.None,
                QueenSideCastlingRights => rights,
                BothCastlingRights => CastlingRights.QueenSide
            },
            _ => rights
        };
    }

    private static bool KingIsAttacked(Board board, bool white)
    {
        foreach (var square in BoardGeometry.All())
        {
            if (board[square] is Occupied occupied && occupied.Piece.Piece is King
                && IsWhite(occupied.Piece.Side) == white)
            {
                return IsAttacked(board, square, !white);
            }
        }

        return false;
    }

    private static bool IsAttacked(Board board, Coordinate target, bool byWhite)
    {
        foreach (var from in BoardGeometry.All())
        {
            if (board[from] is not Occupied occupied || IsWhite(occupied.Piece.Side) != byWhite || from == target)
            {
                continue;
            }

            var dx = BoardGeometry.File(target) - BoardGeometry.File(from);
            var dy = BoardGeometry.Rank(target) - BoardGeometry.Rank(from);
            var attacks = occupied.Piece.Piece switch
            {
                Pawn => Math.Abs(dx) == 1 && dy == (byWhite ? 1 : -1),
                Knight => Math.Abs(dx) * Math.Abs(dy) == 2,
                Bishop => Math.Abs(dx) == Math.Abs(dy) && PathIsClear(board, from, target),
                Rook => (dx == 0 || dy == 0) && PathIsClear(board, from, target),
                Queen => (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && PathIsClear(board, from, target),
                King => Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1
            };
            if (attacks)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathIsClear(Board board, Coordinate from, Coordinate to)
    {
        var fileStep = Math.Sign(BoardGeometry.File(to) - BoardGeometry.File(from));
        var rankStep = Math.Sign(BoardGeometry.Rank(to) - BoardGeometry.Rank(from));
        var file = BoardGeometry.File(from) + fileStep;
        var rank = BoardGeometry.Rank(from) + rankStep;
        while (file != BoardGeometry.File(to) || rank != BoardGeometry.Rank(to))
        {
            if (board[BoardGeometry.At(file, rank)] is Occupied)
            {
                return false;
            }

            file += fileStep;
            rank += rankStep;
        }

        return true;
    }

    private static bool IsWhite(Side side) => side switch { White => true, Black => false };

    private static Board Place(Board board, Coordinate square, OwnedPiece piece) => board.Place(square, piece) switch
    {
        Board updated => updated,
        PlacementConflict => throw new InvalidOperationException("The destination must be empty after resolving the move.")
    };
}
