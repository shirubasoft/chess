namespace Chess.Tests;

public sealed class PositionTests
{
    [Test]
    public async Task ChangingMetadataPreservesTheOriginalPosition()
    {
        var original = CreatePosition();
        var target = new Coordinate { File = BoardFile.E, Rank = BoardRank.Three };

        var updated = original with
        {
            SideToMove = Side.Black,
            WhiteCastlingRights = CastlingRights.KingSide,
            BlackCastlingRights = CastlingRights.QueenSide,
            EnPassant = new EnPassantTarget { Square = target },
            HalfmoveClock = 5,
            FullmoveNumber = 12
        };

        await Assert.That(updated.Board).IsSameReferenceAs(original.Board);
        await Assert.That(original.SideToMove.Value).IsTypeOf<White>().And.IsNotNull();
        await Assert.That(original.WhiteCastlingRights.Value).IsTypeOf<BothCastlingRights>().And.IsNotNull();
        await Assert.That(original.BlackCastlingRights.Value).IsTypeOf<NoCastlingRights>().And.IsNotNull();
        await Assert.That(original.EnPassant.Value).IsTypeOf<NoEnPassant>().And.IsNotNull();
        await Assert.That(original.HalfmoveClock).IsEqualTo(0);
        await Assert.That(original.FullmoveNumber).IsEqualTo(1);
        await Assert.That(updated.SideToMove.Value).IsTypeOf<Black>().And.IsNotNull();
        await Assert.That(updated.WhiteCastlingRights.Value).IsTypeOf<KingSideCastlingRights>().And.IsNotNull();
        await Assert.That(updated.BlackCastlingRights.Value).IsTypeOf<QueenSideCastlingRights>().And.IsNotNull();
        var enPassant = await Assert.That(updated.EnPassant.Value).IsTypeOf<EnPassantTarget>().And.IsNotNull();
        await Assert.That(enPassant.Square).IsEqualTo(target);
        await Assert.That(updated.HalfmoveClock).IsEqualTo(5);
        await Assert.That(updated.FullmoveNumber).IsEqualTo(12);
    }

    [Test]
    public async Task ChangingTheBoardPreservesTheOriginalPosition()
    {
        var original = CreatePosition();
        var square = new Coordinate { File = BoardFile.E, Rank = BoardRank.Two };
        var piece = new OwnedPiece { Side = Side.White, Piece = Piece.Pawn };
        var board = await Assert.That(original.Board.Place(square, piece).Value).IsTypeOf<Board>().And.IsNotNull();

        var updated = original with { Board = board };

        await Assert.That(original.Board[square].Value).IsTypeOf<Empty>().And.IsNotNull();
        var occupied = await Assert.That(updated.Board[square].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(occupied.Piece).IsEqualTo(piece);
        await Assert.That(updated.SideToMove).IsEqualTo(original.SideToMove);
        await Assert.That(updated.WhiteCastlingRights).IsEqualTo(original.WhiteCastlingRights);
        await Assert.That(updated.BlackCastlingRights).IsEqualTo(original.BlackCastlingRights);
        await Assert.That(updated.EnPassant).IsEqualTo(original.EnPassant);
        await Assert.That(updated.HalfmoveClock).IsEqualTo(original.HalfmoveClock);
        await Assert.That(updated.FullmoveNumber).IsEqualTo(original.FullmoveNumber);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task NegativeHalfmoveClocksAreRejectedDuringConstructionAndUpdates(int value)
    {
        var original = CreatePosition();

        await Assert.That(() => CreatePosition(halfmoveClock: value)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => original with { HalfmoveClock = value }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task NonpositiveFullmoveNumbersAreRejectedDuringConstructionAndUpdates(int value)
    {
        var original = CreatePosition();

        await Assert.That(() => CreatePosition(fullmoveNumber: value)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => original with { FullmoveNumber = value }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(100, 51)]
    public async Task ValidCounterValuesArePreserved(int halfmoveClock, int fullmoveNumber)
    {
        var constructed = CreatePosition(halfmoveClock, fullmoveNumber);
        var updated = CreatePosition() with { HalfmoveClock = halfmoveClock, FullmoveNumber = fullmoveNumber };

        await Assert.That(constructed.HalfmoveClock).IsEqualTo(halfmoveClock);
        await Assert.That(constructed.FullmoveNumber).IsEqualTo(fullmoveNumber);
        await Assert.That(updated.HalfmoveClock).IsEqualTo(halfmoveClock);
        await Assert.That(updated.FullmoveNumber).IsEqualTo(fullmoveNumber);
    }

    private static Position CreatePosition(int halfmoveClock = 0, int fullmoveNumber = 1) => new()
    {
        Board = new Board(),
        SideToMove = Side.White,
        WhiteCastlingRights = CastlingRights.Both,
        BlackCastlingRights = CastlingRights.None,
        EnPassant = EnPassantState.None,
        HalfmoveClock = halfmoveClock,
        FullmoveNumber = fullmoveNumber
    };
}
