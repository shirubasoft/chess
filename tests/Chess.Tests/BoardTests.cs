namespace Chess.Tests;

public sealed class BoardTests
{
    private static readonly Coordinate A1 = new() { File = new A(), Rank = new One() };
    private static readonly Coordinate H8 = new() { File = new H(), Rank = new Eight() };
    private static readonly OwnedPiece WhiteRook = new() { Side = new White(), Piece = new Rook() };
    private static readonly OwnedPiece BlackQueen = new() { Side = new Black(), Piece = new Queen() };

    [Test]
    public async Task NewBoardHasEmptyContentsAtEverySquare()
    {
        var board = new Board();

        foreach (var coordinate in BoardCoordinates.All())
        {
            await Assert.That(board[coordinate].Value).IsTypeOf<Empty>().And.IsNotNull();
        }
    }

    [Test]
    public async Task PlacingOnAnEmptySquareReturnsANewBoardAndPreservesTheOriginal()
    {
        var original = new Board();

        var updated = await Assert.That(original.Place(A1, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();

        await Assert.That(updated).IsNotSameReferenceAs(original);
        await Assert.That(original[A1].Value).IsTypeOf<Empty>().And.IsNotNull();
        var occupied = await Assert.That(updated[A1].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(occupied.Piece).IsEqualTo(WhiteRook);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PlacingOnAnOccupiedSquareReportsTheExistingPiece(bool samePiece)
    {
        var board = await Assert.That(new Board().Place(A1, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();
        var attemptedPiece = samePiece ? WhiteRook : BlackQueen;

        var conflict = await Assert.That(board.Place(A1, attemptedPiece).Value).IsTypeOf<PlacementConflict>().And.IsNotNull();

        await Assert.That(conflict.Coordinate).IsEqualTo(A1);
        await Assert.That(conflict.ExistingPiece).IsEqualTo(WhiteRook);
        var occupied = await Assert.That(board[A1].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(occupied.Piece).IsEqualTo(WhiteRook);
    }

    [Test]
    public async Task BoardsBranchedFromTheSameSnapshotRemainIndependent()
    {
        var original = new Board();

        var first = await Assert.That(original.Place(A1, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();
        var second = await Assert.That(original.Place(H8, BlackQueen).Value).IsTypeOf<Board>().And.IsNotNull();

        await Assert.That(first[A1].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(first[H8].Value).IsTypeOf<Empty>().And.IsNotNull();
        await Assert.That(second[A1].Value).IsTypeOf<Empty>().And.IsNotNull();
        await Assert.That(second[H8].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(original[A1].Value).IsTypeOf<Empty>().And.IsNotNull();
        await Assert.That(original[H8].Value).IsTypeOf<Empty>().And.IsNotNull();
    }

    [Test]
    public async Task All64SquaresCanBeOccupiedWhileEveryEarlierSnapshotIsPreserved()
    {
        var coordinates = BoardCoordinates.All().ToArray();
        var snapshots = new List<Board> { new() };

        foreach (var coordinate in coordinates)
        {
            var next = await Assert.That(snapshots[^1].Place(coordinate, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();
            snapshots.Add(next);
        }

        for (var placedCount = 0; placedCount < snapshots.Count; placedCount++)
        {
            for (var squareIndex = 0; squareIndex < coordinates.Length; squareIndex++)
            {
                var isOccupied = snapshots[placedCount][coordinates[squareIndex]] is Occupied;
                await Assert.That(isOccupied).IsEqualTo(squareIndex < placedCount)
                    .Because($"snapshot {placedCount} must retain the contents of square {squareIndex}");
            }
        }
    }

    [Test]
    public async Task RemovingAPiecePreservesOtherSquaresAndTheOriginalBoard()
    {
        var first = await Assert.That(new Board().Place(A1, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();
        var original = await Assert.That(first.Place(H8, BlackQueen).Value).IsTypeOf<Board>().And.IsNotNull();

        var updated = original.Remove(A1);

        await Assert.That(updated).IsNotSameReferenceAs(original);
        await Assert.That(updated[A1].Value).IsTypeOf<Empty>().And.IsNotNull();
        var remaining = await Assert.That(updated[H8].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(remaining.Piece).IsEqualTo(BlackQueen);
        var removed = await Assert.That(original[A1].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(removed.Piece).IsEqualTo(WhiteRook);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RemovingAnEmptySquareReturnsTheSameBoard(bool hasOtherPieces)
    {
        var board = new Board();
        if (hasOtherPieces)
        {
            board = await Assert.That(board.Place(A1, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();
        }

        var updated = board.Remove(H8);

        await Assert.That(updated).IsSameReferenceAs(board);
    }

    [Test]
    public async Task RemovedSquaresCanBeReusedWithoutChangingEarlierBoards()
    {
        var original = await Assert.That(new Board().Place(A1, WhiteRook).Value).IsTypeOf<Board>().And.IsNotNull();
        var cleared = original.Remove(A1);

        var repeatedRemoval = cleared.Remove(A1);
        var replaced = await Assert.That(cleared.Place(A1, BlackQueen).Value).IsTypeOf<Board>().And.IsNotNull();

        await Assert.That(repeatedRemoval).IsSameReferenceAs(cleared);
        await Assert.That(cleared[A1].Value).IsTypeOf<Empty>().And.IsNotNull();
        var previousPiece = await Assert.That(original[A1].Value).IsTypeOf<Occupied>().And.IsNotNull();
        var currentPiece = await Assert.That(replaced[A1].Value).IsTypeOf<Occupied>().And.IsNotNull();
        await Assert.That(previousPiece.Piece).IsEqualTo(WhiteRook);
        await Assert.That(currentPiece.Piece).IsEqualTo(BlackQueen);
    }
}
