using System.Diagnostics;
using System.Text.RegularExpressions;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Cli.Tests;

public sealed class TerminalTests
{
    [Test, RequiresLinuxTerminal]
    [Arguments(80, 24)]
    [Arguments(100, 28)]
    public async Task RealPseudoTerminalRendersBoardAndExitsOnEscape(int columns, int rows)
    {
        using var cli = new CliProcess();
        var launch = CliProcess.StartInfo("tui", "--server", "http://127.0.0.1:1/", "--session", cli.Session);
        var shellCommand = $"stty cols {columns} rows {rows}; exec dotnet " + string.Join(' ', launch.ArgumentList.Select(ShellQuote));
        var start = new ProcessStartInfo("/usr/bin/script")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "-q", "-e", "-c", shellCommand, "/dev/null" }) start.ArgumentList.Add(argument);
        start.Environment["TERM"] = "xterm-256color";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await process.StandardInput.WriteAsync("\u001b");
            await process.StandardInput.FlushAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(deadline.Token);
            await Assert.That(process.ExitCode).IsEqualTo(0);
            var rendered = Regex.Replace(await output, "\u001b\\[[0-9;]*m", "");
            await Assert.That(rendered).Contains("Chess CLI");
            await Assert.That(rendered).Contains("♜");
            await Assert.That(rendered).Contains("♙");
            await Assert.That(rendered).Contains("┌");
            await Assert.That(rendered).Contains("Drag a piece");
            await Assert.That(rendered).Contains("Claim fifty moves");
            await Assert.That(rendered).Contains("Move history");
            await Assert.That(await errors).IsEmpty();
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Test, RequiresLinuxTerminal, RequiresChessServer]
    [Arguments(false)]
    [Arguments(true)]
    public async Task KeyboardOrMouseMoveCrossplaysAndReceivesTheOpponentsMove(bool drag)
    {
        using var cli = new CliProcess();
        var access = (await cli.RunAsync("create")).Read<GameAccess>();
        using var http = new HttpClient { BaseAddress = new Uri(cli.Server) };
        var other = new ChessClient(http, "TUI crossplay opponent");
        var opponent = await other.JoinAsync(access.OpponentCode!);
        var launch = CliProcess.StartInfo("tui", "--session", cli.Session);
        var command = "stty cols 80 rows 24; exec dotnet " + string.Join(' ', launch.ArgumentList.Select(ShellQuote));
        var start = new ProcessStartInfo("/usr/bin/script")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "-q", "-e", "-c", command, "/dev/null" }) start.ArgumentList.Add(argument);
        start.Environment["TERM"] = "xterm-256color";
        using var process = Process.Start(start)!;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = ReadTerminalAsync(process.StandardOutput, ready);
        var errors = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await ready.Task.WaitAsync(deadline.Token);
            await process.StandardInput.WriteAsync(drag
                ? "\u001b[<0;18;15M\u001b[<32;18;14M\u001b[<32;18;13M\u001b[<0;18;13m"
                : "\t\t\t\t\te4\r");
            await process.StandardInput.FlushAsync();
            GameSnapshot snapshot;
            do
            {
                await Task.Delay(100, deadline.Token);
                snapshot = await other.GetAsync(opponent, deadline.Token);
            } while (snapshot.Moves.Length == 0);
            await Assert.That(snapshot.Moves.Single().San).IsEqualTo("e4");
            await Assert.That(snapshot.White.ClientName).IsEqualTo("Chess CLI");
            await other.MoveAsync(opponent, snapshot, "e5", cancellationToken: deadline.Token);
            CliSession? session;
            do
            {
                await Task.Delay(100, deadline.Token);
                session = await new SessionFile(cli.Session).ReadAsync(deadline.Token);
            } while (session?.Access?.Snapshot.Moves.Length != 2);
            await Assert.That(session.Access.Snapshot.Black.ClientName).IsEqualTo("TUI crossplay opponent");
            await process.StandardInput.WriteAsync("\u001b");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync(deadline.Token);
            await Assert.That(process.ExitCode).IsEqualTo(0);
            await Assert.That(await output).Contains("e5");
            await Assert.That(await errors).IsEmpty();
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static async Task<string> ReadTerminalAsync(StreamReader reader, TaskCompletionSource ready)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            text.Append(buffer, 0, count);
            if (!ready.Task.IsCompleted && text.ToString().Contains("Drag a piece", StringComparison.Ordinal)) ready.TrySetResult();
        }
        return text.ToString();
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}

public sealed class RequiresLinuxTerminalAttribute() : SkipAttribute("This PTY test requires Linux and util-linux script.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script"));
}
