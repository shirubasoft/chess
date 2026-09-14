namespace Chess;

internal static class BoardGeometry
{
    private static readonly BoardFile[] Files = [new A(), new B(), new C(), new D(), new E(), new F(), new G(), new H()];
    private static readonly BoardRank[] Ranks = [new One(), new Two(), new Three(), new Four(), new Five(), new Six(), new Seven(), new Eight()];

    internal static Coordinate At(int file, int rank) => new() { File = Files[file], Rank = Ranks[rank] };

    internal static int File(Coordinate square) => square.File switch
    {
        A => 0,
        B => 1,
        C => 2,
        D => 3,
        E => 4,
        F => 5,
        G => 6,
        H => 7
    };

    internal static int Rank(Coordinate square) => square.Rank switch
    {
        One => 0,
        Two => 1,
        Three => 2,
        Four => 3,
        Five => 4,
        Six => 5,
        Seven => 6,
        Eight => 7
    };

    internal static IEnumerable<Coordinate> All()
    {
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                yield return At(file, rank);
            }
        }
    }
}
