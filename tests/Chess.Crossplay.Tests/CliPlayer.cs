using System.Diagnostics;
using System.Text.Json;
using Chess.Contracts;

namespace Chess.Crossplay.Tests;

internal sealed class CliPlayer(string server, string sessionPath) : IPlayer
{
    public string ClientName => "Chess CLI";
    public GameAccess? Access { get; private set; }

    public async Task<GameAccess> CreateAsync() => Access = await RunAsync<GameAccess>("create");
    public async Task<GameAccess> JoinAsync(string code) => Access = await RunAsync<GameAccess>("join", code);
    public async Task<GameAccess> ResumeAsync(string code) => Access = await RunAsync<GameAccess>("resume", code);
    public async Task<GameAccess> MatchmakeAsync() => Access = await RunAsync<GameAccess>("random");

    public async Task<GameSnapshot> RefreshAsync() => Save(await RunAsync<GameSnapshot>("show"));

    public async Task<GameSnapshot> MoveAsync(TestMove move) => Save(move.Entry == MoveEntry.San
        ? await RunAsync<GameSnapshot>("move", move.San)
        : await RunAsync<GameSnapshot>("move", move.Uci, "--notation", "uci"));

    public async Task<GameSnapshot> ResignAsync() => Save(await RunAsync<GameSnapshot>("resign"));

    public async Task<GameSnapshot> WaitForOpponentAsync(int afterPly) =>
        Save(await RunAsync<GameSnapshot>("wait", "--after-ply", afterPly.ToString(), "--timeout", "20"));

    public async Task VerifyOpponentAsync(string opponentName)
    {
        var snapshot = await RefreshAsync();
        var opponent = Access!.Side == PlayerSide.White ? snapshot.Black : snapshot.White;
        await Assert.That(opponent.ClientName).IsEqualTo(opponentName);
    }

    private GameSnapshot Save(GameSnapshot snapshot)
    {
        Access = (Access ?? throw new InvalidOperationException("The CLI has no current game.")) with { Snapshot = snapshot };
        return snapshot;
    }

    private async Task<T> RunAsync<T>(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(Executable());
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--server");
        start.ArgumentList.Add(server);
        start.ArgumentList.Add("--session");
        start.ArgumentList.Add(sessionPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the CLI.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"CLI {arguments[0]} did not exit within 40 seconds.");
        }
        var text = await output;
        var errors = await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"CLI {arguments[0]} exited {process.ExitCode}: {errors}");
        if (text.Contains('\u001b')) throw new InvalidOperationException("The automation command emitted terminal control sequences.");
        return JsonSerializer.Deserialize<T>(text, GameJson.Options)
            ?? throw new JsonException($"CLI {arguments[0]} returned empty JSON.");
    }

    private static string Executable()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var framework = output.Name;
        var configuration = output.Parent?.Name ?? throw new InvalidOperationException("Cannot identify the test build configuration.");
        var root = output;
        while (!File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
            root = root.Parent ?? throw new InvalidOperationException("Cannot locate the repository root from the test assembly.");
        var path = Path.Combine(root.FullName, "clients", "Chess.Cli", "bin", configuration, framework, "Chess.Cli.dll");
        return File.Exists(path) ? path : throw new FileNotFoundException("Build the crossplay test project to build its CLI executable dependency.", path);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
