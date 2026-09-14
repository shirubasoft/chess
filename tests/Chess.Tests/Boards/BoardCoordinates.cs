namespace Chess.Tests;

internal static class BoardCoordinates
{
    internal static IEnumerable<Coordinate> All()
    {
        BoardFile[] files = [BoardFile.A, BoardFile.B, BoardFile.C, BoardFile.D, BoardFile.E, BoardFile.F, BoardFile.G, BoardFile.H];
        BoardRank[] ranks = [BoardRank.One, BoardRank.Two, BoardRank.Three, BoardRank.Four, BoardRank.Five, BoardRank.Six, BoardRank.Seven, BoardRank.Eight];

        foreach (var file in files)
        {
            foreach (var rank in ranks)
            {
                yield return new Coordinate { File = file, Rank = rank };
            }
        }
    }
}
