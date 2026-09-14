namespace Chess.Tests;

public sealed class CoordinateTests
{
    [Test]
    public async Task IndependentlyConstructedCoordinatesAddressTheSame64Squares()
    {
        var squares = new Dictionary<Coordinate, int>();
        foreach (var coordinate in BoardCoordinates.All())
        {
            squares.Add(coordinate, squares.Count);
        }

        await Assert.That(squares).Count().IsEqualTo(64);

        var expectedIndex = 0;
        foreach (var coordinate in BoardCoordinates.All())
        {
            await Assert.That(squares.TryGetValue(coordinate, out var actualIndex)).IsTrue();
            await Assert.That(actualIndex).IsEqualTo(expectedIndex);
            expectedIndex++;
        }
    }
}
