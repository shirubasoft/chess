namespace Chess.Tests;

internal static class BoardCoordinates
{
    internal static IEnumerable<Coordinate> All()
    {
        BoardFile[] files = [new A(), new B(), new C(), new D(), new E(), new F(), new G(), new H()];
        BoardRank[] ranks = [new One(), new Two(), new Three(), new Four(), new Five(), new Six(), new Seven(), new Eight()];

        foreach (var file in files)
        {
            foreach (var rank in ranks)
            {
                yield return new Coordinate { File = file, Rank = rank };
            }
        }
    }
}
