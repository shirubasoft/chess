using Chess.Client;
using Chess.Contracts;

namespace Chess.Cli.Tests;

public sealed class AutomationTests
{
    [Test]
    public async Task HelpRunsAsAnExecutableWithoutServerOrTui()
    {
        using var cli = new CliProcess();
        var result = await cli.RunAsync("help");
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).Contains("dotnet chess move");
        await Assert.That(result.Error).IsEmpty();
    }

    [Test]
    [Arguments("--bogus")]
    [Arguments("move")]
    [Arguments("unknown")]
    public async Task UsageErrorsHaveNoStdout(string argument)
    {
        using var cli = new CliProcess();
        var result = await cli.RunAsync(argument);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error).Contains("invalid_input");
    }

    [Test]
    public async Task TuiRejectsRedirectedInputWithoutWritingTerminalEscapeCodes()
    {
        using var cli = new CliProcess();
        var result = await cli.RunAsync("tui");
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error).Contains("needs a terminal");
    }

    [Test, RequiresChessServer]
    public async Task ActualCliProcessesCrossplayWithSharedClientAndResumeWithoutConnection()
    {
        using var cli = new CliProcess();
        var created = await cli.RunAsync("create");
        await Assert.That(created.ExitCode).IsEqualTo(0);
        var access = created.Read<GameAccess>();
        using var http = new HttpClient { BaseAddress = new Uri(cli.Server) };
        var opponent = new ChessClient(http, "Crossplay test client");
        var black = await opponent.JoinAsync(access.OpponentCode!);

        var whiteMove = await cli.RunAsync("move", "e4");
        await Assert.That(whiteMove.ExitCode).IsEqualTo(0);
        var afterWhite = whiteMove.Read<GameSnapshot>();
        await Assert.That(afterWhite.White.ClientName).IsEqualTo("Chess CLI");
        await Assert.That(afterWhite.Black.ClientName).IsEqualTo("Crossplay test client");
        await Assert.That(afterWhite.Moves.Single().Uci).IsEqualTo("e2e4");

        var afterBlack = await opponent.MoveAsync(black, afterWhite, "e7e5", MoveNotation.Uci);
        var waiting = await cli.RunAsync("wait", "--timeout", "3");
        await Assert.That(waiting.ExitCode).IsEqualTo(0);
        await Assert.That(waiting.Read<GameSnapshot>().Revision).IsEqualTo(afterBlack.Revision);

        using var resumed = new CliProcess { Server = cli.Server };
        var rejoin = await resumed.RunAsync("resume", access.Code);
        await Assert.That(rejoin.Read<GameAccess>().GameId).IsEqualTo(access.GameId);
        var next = await resumed.RunAsync("move", "g1f3", "--notation", "uci");
        await Assert.That(next.ExitCode).IsEqualTo(0);
        await Assert.That((await opponent.GetAsync(black)).Moves.Last().San).IsEqualTo("Nf3");
        var history = await resumed.RunAsync("history");
        await Assert.That(history.Read<PlayedMove[]>().Length).IsEqualTo(3);

        var shown = await resumed.RunAsync("show", "--match", access.GameId.ToString(), "--code", access.Code);
        await Assert.That(shown.ExitCode).IsEqualTo(0);
        await Assert.That(shown.Read<GameSnapshot>().Moves.Length).IsEqualTo(3);
    }

    [Test, RequiresChessServer]
    public async Task RetryReusesTheOriginalRevisionAndWrongTurnWritesOnlyStderr()
    {
        using var cli = new CliProcess();
        using var peer = new CliProcess { Server = cli.Server };
        var access = (await cli.RunAsync("create")).Read<GameAccess>();
        await peer.RunAsync("join", access.OpponentCode!);
        var requestId = Guid.NewGuid().ToString();
        var first = await cli.RunAsync("move", "e4", "--request-id", requestId);
        await Assert.That(first.ExitCode).IsEqualTo(0);
        var retry = await cli.RunAsync("move", "e4", "--request-id", requestId);
        await Assert.That(retry.ExitCode).IsEqualTo(0);
        await Assert.That(retry.Read<GameSnapshot>().Moves.Length).IsEqualTo(1);
        var rejected = await cli.RunAsync("move", "d4");
        await Assert.That(rejected.ExitCode).IsEqualTo(1);
        await Assert.That(rejected.Output).IsEmpty();
        await Assert.That(rejected.Error).IsNotEmpty();
        var show = await cli.RunAsync("show");
        await Assert.That(show.Read<GameSnapshot>().Moves.Length).IsEqualTo(1);
    }

    [Test, RequiresChessServer]
    public async Task WaitIgnoresDrawOffersAndTimesOutUntilAnOpponentMove()
    {
        using var white = new CliProcess();
        using var black = new CliProcess { Server = white.Server };
        var access = (await white.RunAsync("create")).Read<GameAccess>();
        await black.RunAsync("join", access.OpponentCode!);
        await white.RunAsync("move", "e4");
        await black.RunAsync("draw", "offer");
        var timeout = await white.RunAsync("wait", "--timeout", "1");
        await Assert.That(timeout.ExitCode).IsEqualTo(124);
        await Assert.That(timeout.Output).Contains("timeout");
        await black.RunAsync("move", "e5");
        var received = await white.RunAsync("wait", "--timeout", "2");
        await Assert.That(received.ExitCode).IsEqualTo(0);
        await Assert.That(received.Read<GameSnapshot>().Moves.Last().San).IsEqualTo("e5");
    }

    [Test, RequiresChessServer, RequiresLinuxTerminal]
    public async Task CtrlCCancelsAnExecutableOpponentWait()
    {
        using var cli = new CliProcess();
        await cli.RunAsync("create");
        using var waiting = cli.Start("wait", "--timeout", "30");
        var resultTask = CliProcess.ReadAsync(waiting);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var signal = new System.Diagnostics.ProcessStartInfo("/usr/bin/kill") { UseShellExecute = false };
        signal.ArgumentList.Add("-INT");
        signal.ArgumentList.Add(waiting.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var sender = System.Diagnostics.Process.Start(signal)!;
        await sender.WaitForExitAsync();
        var result = await resultTask;
        await Assert.That(result.ExitCode).IsEqualTo(130);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error).Contains("cancelled");
    }

    [Test, RequiresChessServer]
    public async Task DrawAgreementAndPromotionRunThroughTheExecutable()
    {
        using var white = new CliProcess();
        using var black = new CliProcess { Server = white.Server };
        var access = (await white.RunAsync("create")).Read<GameAccess>();
        await black.RunAsync("join", access.OpponentCode!);
        await white.RunAsync("move", "e4");
        await black.RunAsync("move", "e5");
        await white.RunAsync("draw", "offer");
        var accepted = await black.RunAsync("draw", "accept");
        await Assert.That(accepted.ExitCode).IsEqualTo(0);
        await Assert.That(accepted.Read<GameSnapshot>().Status).IsEqualTo(GameStatus.Finished);

        access = (await white.RunAsync("create", "--fen", "7k/P7/8/8/8/8/8/7K w - - 0 1")).Read<GameAccess>();
        await black.RunAsync("join", access.OpponentCode!);
        var promoted = await white.RunAsync("move", "a8=Q+");
        await Assert.That(promoted.ExitCode).IsEqualTo(0);
        await Assert.That(promoted.Read<GameSnapshot>().Moves.Single().Uci).IsEqualTo("a7a8q");
    }

    [Test, RequiresChessServer, NotInParallel("cli-matchmaking")]
    public async Task RandomMatchmakingAndResignationWorkAcrossDisconnectedCliProcesses()
    {
        using var first = new CliProcess();
        using var second = new CliProcess { Server = first.Server };
        var a = (await first.RunAsync("random")).Read<GameAccess>();
        var b = (await second.RunAsync("random")).Read<GameAccess>();
        await Assert.That(a.GameId).IsEqualTo(b.GameId);
        await Assert.That(a.Side).IsNotEqualTo(b.Side);
        var resigned = await second.RunAsync("resign");
        await Assert.That(resigned.ExitCode).IsEqualTo(0);
        var final = (await first.RunAsync("wait", "--timeout", "2")).Read<GameSnapshot>();
        await Assert.That(final.Status).IsEqualTo(GameStatus.Finished);
    }
}
