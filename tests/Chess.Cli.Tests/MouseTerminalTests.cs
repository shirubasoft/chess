using System.Diagnostics;
using System.Text;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Cli.Tests;

public sealed class MouseTerminalTests
{
    [Test, RequiresLinuxTerminal, RequiresChessServer]
    public async Task BlackCanDragOnTheRotatedBoard()
    {
        using var cli = new CliProcess();
        using var http = new HttpClient { BaseAddress = new Uri(cli.Server) };
        var peer = new ChessClient(http, "Mouse test opponent");
        var white = await peer.CreateAsync();
        var black = (await cli.RunAsync("join", white.OpponentCode!)).Read<GameAccess>();
        await peer.MoveAsync(white, await peer.GetAsync(white), "e4");
        using var terminal = new TerminalGame(cli.Session);
        await terminal.Ready;
        await terminal.DragAsync(15, 15, 15, 13);
        var final = await WaitForPlyAsync(peer, white, 2);
        await Assert.That(final.Moves.Last().San).IsEqualTo("e5");
        await Assert.That(black.Side).IsEqualTo(PlayerSide.Black);
        await terminal.CloseAsync();
    }

    [Test, RequiresLinuxTerminal, RequiresChessServer]
    [Arguments("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", 18, 16, 24, 16, "O-O", false)]
    [Arguments("7k/8/8/3pP3/8/8/8/K7 w - d6 0 1", 18, 12, 15, 11, "exd6", false)]
    [Arguments("7k/P7/8/8/8/8/8/7K w - - 0 1", 6, 10, 6, 9, "a8=N", true)]
    public async Task DragSupportsSpecialMovesAndChoosingAnUnderpromotion(string fen, int fromX, int fromY, int toX, int toY, string san, bool promotion)
    {
        using var cli = new CliProcess();
        var white = (await cli.RunAsync("create", "--fen", fen)).Read<GameAccess>();
        using var http = new HttpClient { BaseAddress = new Uri(cli.Server) };
        var peer = new ChessClient(http, "Mouse test opponent");
        var black = await peer.JoinAsync(white.OpponentCode!);
        using var terminal = new TerminalGame(cli.Session);
        await terminal.Ready;
        await terminal.DragAsync(fromX, fromY, toX, toY);
        if (promotion)
        {
            await terminal.WaitForTextAsync("Choose the promoted piece.");
            await terminal.SendAsync("\t\t\t\t\r");
        }
        var final = await WaitForPlyAsync(peer, black, 1);
        await Assert.That(final.Moves.Single().San).IsEqualTo(san);
        await terminal.CloseAsync();
    }

    [Test, RequiresLinuxTerminal, RequiresChessServer]
    public async Task OutsideDropAndEscapeCancelWithoutSendingAMoveOrTrappingTheMouse()
    {
        using var cli = new CliProcess();
        var white = (await cli.RunAsync("create")).Read<GameAccess>();
        using var http = new HttpClient { BaseAddress = new Uri(cli.Server) };
        var peer = new ChessClient(http, "Mouse test opponent");
        var black = await peer.JoinAsync(white.OpponentCode!);
        using var terminal = new TerminalGame(cli.Session);
        await terminal.Ready;
        await terminal.DragAsync(18, 15, 60, 12);
        await terminal.WaitForTextAsync("Move cancelled.");
        await terminal.SendAsync("\u001b[<0;18;15M");
        await Task.Delay(150);
        await terminal.SendAsync("\u001b");
        await Task.Delay(200);
        await terminal.SendAsync("\u001b[<0;18;13m");
        await Assert.That((await peer.GetAsync(black)).Moves).IsEmpty();
        await terminal.DragAsync(18, 15, 18, 13);
        await Assert.That((await WaitForPlyAsync(peer, black, 1)).Moves.Single().San).IsEqualTo("e4");
        await terminal.CloseAsync();
    }

    private static async Task<GameSnapshot> WaitForPlyAsync(ChessClient client, GameAccess access, int ply)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var snapshot = await client.GetAsync(access, deadline.Token);
            if (snapshot.Moves.Length == ply) return snapshot;
            await Task.Delay(50, deadline.Token);
        }
    }

    private sealed class TerminalGame : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder text = new();
        private readonly Task output;
        private readonly Task<string> errors;
        public Task Ready => WaitForTextAsync("Drag a piece");

        public TerminalGame(string session)
        {
            var launch = CliProcess.StartInfo("tui", "--session", session);
            var command = "stty cols 80 rows 24; exec dotnet " + string.Join(' ', launch.ArgumentList.Select(ShellQuote));
            var start = new ProcessStartInfo("/usr/bin/script")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var argument in new[] { "-q", "-e", "-c", command, "/dev/null" }) start.ArgumentList.Add(argument);
            start.Environment["TERM"] = "xterm-256color";
            process = Process.Start(start)!;
            output = ReadAsync();
            errors = process.StandardError.ReadToEndAsync();
        }

        public async Task SendAsync(string keys)
        {
            await process.StandardInput.WriteAsync(keys);
            await process.StandardInput.FlushAsync();
        }

        public async Task DragAsync(int fromX, int fromY, int toX, int toY)
        {
            await SendAsync($"\u001b[<0;{fromX};{fromY}M");
            await Task.Delay(100);
            await SendAsync($"\u001b[<32;{toX};{toY}M");
            await Task.Delay(100);
            await SendAsync($"\u001b[<0;{toX};{toY}m");
        }

        public async Task WaitForTextAsync(string expected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                lock (text) { if (text.ToString().Contains(expected, StringComparison.Ordinal)) return; }
                await Task.Delay(50, deadline.Token);
            }
        }

        public async Task CloseAsync()
        {
            await SendAsync("\u001b");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(deadline.Token);
            await output;
            await Assert.That(process.ExitCode).IsEqualTo(0);
            await Assert.That(await errors).IsEmpty();
        }

        private async Task ReadAsync()
        {
            var buffer = new char[4096];
            int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer)) > 0)
                lock (text) { text.Append(buffer, 0, count); }
        }

        public void Dispose()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
        }

        private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
