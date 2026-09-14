using Chess.Contracts;

namespace Chess.Crossplay.Tests;

[Category("Crossplay")]
[NotInParallel]
public sealed class CrossplayTests
{
    [Test]
    [Arguments(Frontend.Android, Frontend.Web)]
    [Arguments(Frontend.Web, Frontend.Android)]
    [Arguments(Frontend.Android, Frontend.Windows)]
    [Arguments(Frontend.Windows, Frontend.Android)]
    [Arguments(Frontend.Android, Frontend.Linux)]
    [Arguments(Frontend.Linux, Frontend.Android)]
    [Arguments(Frontend.Android, Frontend.Cli)]
    [Arguments(Frontend.Cli, Frontend.Android)]
    [Arguments(Frontend.Web, Frontend.Windows)]
    [Arguments(Frontend.Windows, Frontend.Web)]
    [Arguments(Frontend.Web, Frontend.Linux)]
    [Arguments(Frontend.Linux, Frontend.Web)]
    [Arguments(Frontend.Web, Frontend.Cli)]
    [Arguments(Frontend.Cli, Frontend.Web)]
    [Arguments(Frontend.Windows, Frontend.Linux)]
    [Arguments(Frontend.Linux, Frontend.Windows)]
    [Arguments(Frontend.Windows, Frontend.Cli)]
    [Arguments(Frontend.Cli, Frontend.Windows)]
    [Arguments(Frontend.Linux, Frontend.Cli)]
    [Arguments(Frontend.Cli, Frontend.Linux)]
    public async Task ClientsPlayBothSidesAndResumeWithoutPersistentConnections(Frontend whiteKind, Frontend blackKind)
    {
        await using var fixture = CrossplayFixture.Create();
        var white = await fixture.PlayerAsync(whiteKind);
        var black = await fixture.PlayerAsync(blackKind);
        var whiteAccess = await white.CreateAsync();
        await Assert.That(whiteAccess.Side).IsEqualTo(PlayerSide.White);
        await Assert.That(whiteAccess.OpponentCode).IsNotNull();
        var blackAccess = await black.JoinAsync(whiteAccess.OpponentCode!);
        await Assert.That(blackAccess.Side).IsEqualTo(PlayerSide.Black);
        await Assert.That(blackAccess.GameId).IsEqualTo(whiteAccess.GameId);
        await Assert.That(blackAccess.Code != whiteAccess.Code).IsTrue();

        var whiteName = PlayerNames.For(whiteKind);
        var blackName = PlayerNames.For(blackKind);
        await VerifyNamesAsync(white, black, whiteName, blackName);
        await white.MoveAsync(new("e2e4", "e4", MoveEntry.Board));
        var firstReply = await black.MoveAsync(new("e7e5", "e5", MoveEntry.San));
        await Assert.That(firstReply.Moves.Select(move => move.Uci).SequenceEqual(["e2e4", "e7e5"])).IsTrue();
        await Assert.That((await white.RefreshAsync()).Fen).IsEqualTo(firstReply.Fen);

        await fixture.CloseAsync(white);
        await fixture.CloseAsync(black);
        white = await fixture.PlayerAsync(whiteKind);
        black = await fixture.PlayerAsync(blackKind);
        var resumedWhite = await white.ResumeAsync(whiteAccess.Code);
        var resumedBlack = await black.ResumeAsync(blackAccess.Code);
        await Assert.That(resumedWhite.GameId).IsEqualTo(whiteAccess.GameId);
        await Assert.That(resumedBlack.GameId).IsEqualTo(whiteAccess.GameId);
        await Assert.That(resumedWhite.Side).IsEqualTo(PlayerSide.White);
        await Assert.That(resumedBlack.Side).IsEqualTo(PlayerSide.Black);
        await Assert.That(resumedBlack.Snapshot.Fen).IsEqualTo(firstReply.Fen);
        await VerifyNamesAsync(white, black, whiteName, blackName);

        await white.MoveAsync(new("g1f3", "Nf3", MoveEntry.San));
        var afterResume = await black.MoveAsync(new("b8c6", "Nc6", MoveEntry.Board));
        await Assert.That(afterResume.Moves.Select(move => move.Uci).SequenceEqual(["e2e4", "e7e5", "g1f3", "b8c6"])).IsTrue();
        await Assert.That((await white.RefreshAsync()).Fen).IsEqualTo(afterResume.Fen);

        await white.ResignAsync();
        var finished = await black.RefreshAsync();
        await Assert.That(finished.Status).IsEqualTo(GameStatus.Finished);
        await Assert.That(finished.Result?.Winner).IsEqualTo(PlayerSide.Black);
        await Assert.That(finished.Result?.Reason).IsEqualTo("Resignation");
        await VerifyNamesAsync(white, black, whiteName, blackName);
    }

    [Test]
    public async Task BrowserAndCliFindEachOtherAndCliWaitsForTheBrowserMove()
    {
        await using var fixture = CrossplayFixture.Create();
        var web = (BrowserPlayer)await fixture.PlayerAsync(Frontend.Web);
        var cli = (CliPlayer)await fixture.PlayerAsync(Frontend.Cli);
        var white = await web.MatchmakeAsync();
        await Assert.That(white.Snapshot.Status).IsEqualTo(GameStatus.Waiting);
        var black = await cli.MatchmakeAsync();
        await Assert.That(black.GameId).IsEqualTo(white.GameId);
        await Assert.That(black.Side).IsEqualTo(PlayerSide.Black);
        await VerifyNamesAsync(web, cli, "Chess Web", "Chess CLI");

        var waiting = cli.WaitForOpponentAsync(afterPly: 0);
        await web.MoveAsync(new("e2e4", "e4", MoveEntry.Board));
        var observed = await waiting;
        await Assert.That(observed.Moves.LastOrDefault()?.Uci).IsEqualTo("e2e4");
        var reply = await cli.MoveAsync(new("e7e5", "e5", MoveEntry.San));
        await Assert.That((await web.RefreshAsync()).Fen).IsEqualTo(reply.Fen);
        await web.ResignAsync();
        await Assert.That((await cli.RefreshAsync()).Status).IsEqualTo(GameStatus.Finished);
    }

    private static async Task VerifyNamesAsync(IPlayer white, IPlayer black, string whiteName, string blackName)
    {
        await white.VerifyOpponentAsync(blackName);
        await black.VerifyOpponentAsync(whiteName);
        foreach (var player in new[] { white, black })
        {
            var snapshot = await player.RefreshAsync();
            await Assert.That(snapshot.White.ClientName).IsEqualTo(whiteName);
            await Assert.That(snapshot.Black.ClientName).IsEqualTo(blackName);
        }
    }
}
