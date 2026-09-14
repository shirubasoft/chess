using Chess.Client;
using Chess.Contracts;

namespace Chess.Native.Tests;

public sealed class BoardPresentationTests
{
    [Test]
    public async Task BoardUsesCorePiecesAndCorrectColorsForBothPerspectives()
    {
        var white = BoardPresentation.Squares(null).ToArray();
        var black = BoardPresentation.Squares(null, PlayerSide.Black).ToArray();
        await Assert.That(white.Length).IsEqualTo(64);
        await Assert.That(white[0].Name).IsEqualTo("a8");
        await Assert.That(white[0].Description).IsEqualTo("a8, Black rook");
        await Assert.That(white[0].IsLight).IsTrue();
        await Assert.That(black[0].Name).IsEqualTo("h1");
        await Assert.That(black[0].Description).IsEqualTo("h1, White rook");
        await Assert.That(white.Select(square => square.Name).Distinct().Count()).IsEqualTo(64);
        await Assert.That(white.Count(square => square.Symbol.Length != 0)).IsEqualTo(32);
        await Assert.That(white.Single(square => square.Name == "a1").IsLight).IsFalse();
    }

    [Test]
    public async Task BoardRendersPromotedPieceFromServerFen()
    {
        var snapshot = new GameSnapshot
        {
            GameId = Guid.NewGuid(), Revision = 3, Fen = "N6k/8/8/8/8/8/8/K7 b - - 0 1",
            SideToMove = PlayerSide.Black, Status = GameStatus.Finished,
            White = new PlayerView { Side = PlayerSide.White, ClientName = "Chess Android" },
            Black = new PlayerView { Side = PlayerSide.Black, ClientName = "Chess Linux" }, LegalMoves = [], Moves = []
        };
        var square = BoardPresentation.Squares(snapshot).Single(square => square.Name == "a8");
        await Assert.That(square.Description).IsEqualTo("a8, White knight");
        await Assert.That(square.Symbol).IsEqualTo("♘");
    }

    [Test]
    public async Task SessionRestoresSavedServerAndOwnCode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chess-session-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllLinesAsync(path, ["https://chess.example/", "side-code"]);
            using var session = new GameSession("Chess Linux", path);
            await Assert.That(session.Server).IsEqualTo("https://chess.example/");
            await Assert.That(session.SavedCode).IsEqualTo("side-code");
            await Assert.That(session.CanMove).IsFalse();
            await Assert.That(session.CanAct(GameAction.Resign)).IsFalse();
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task InvalidServerAddressProducesRecoverableMessageBeforeNetworking()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chess-session-{Guid.NewGuid():N}");
        using var session = new GameSession("Chess Windows", path);
        await session.CreateAsync("file:///etc/passwd");
        await Assert.That(session.Access).IsNull();
        await Assert.That(session.IsBusy).IsFalse();
        await Assert.That(session.Message).Contains("HTTP or HTTPS");
    }
}
