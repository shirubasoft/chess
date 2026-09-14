using System.Diagnostics;
using System.Text.Json;
using Chess.Cli;
using Chess.Contracts;

namespace Chess.Cli.Tests;

internal sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public T Read<T>() => JsonSerializer.Deserialize<T>(Output, GameJson.Options)
        ?? throw new InvalidOperationException($"Missing JSON result: {Error}");
}

internal sealed class CliProcess : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"chess-cli-tests-{Guid.NewGuid():N}");
    public string Session => Path.Combine(directory, "session.json");
    public string Server { get; set; } = Environment.GetEnvironmentVariable("CHESS_TEST_SERVER") ?? "http://127.0.0.1:1/";

    public static ProcessStartInfo StartInfo(params string[] arguments)
    {
        var assembly = typeof(CliApplication).Assembly.Location;
        var testName = typeof(CliProcess).Assembly.GetName().Name!;
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("--runtimeconfig");
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"{testName}.runtimeconfig.json"));
        info.ArgumentList.Add("--depsfile");
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"{testName}.deps.json"));
        info.ArgumentList.Add(assembly);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment.Remove("CHESS_SERVER_URL");
        info.Environment.Remove("CHESS_SESSION_FILE");
        return info;
    }

    public Process Start(params string[] arguments) => Process.Start(StartInfo([..arguments, "--server", Server, "--session", Session]))
        ?? throw new InvalidOperationException("The CLI did not start.");

    public async Task<CommandResult> RunAsync(params string[] arguments)
    {
        using var process = Start(arguments);
        return await ReadAsync(process);
    }

    public static async Task<CommandResult> ReadAsync(Process process)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("CLI process exceeded 45 seconds."); }
        return new(process.ExitCode, await output, await error);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}

public sealed class RequiresChessServerAttribute() : SkipAttribute("Set CHESS_TEST_SERVER to run the CLI against a real game server.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHESS_TEST_SERVER")));
}
