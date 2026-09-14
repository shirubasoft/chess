using System.Text.Json;
using Chess.Contracts;

namespace Chess.Cli;

public sealed record CliSession
{
    public required string Server { get; init; }
    public GameAccess? Access { get; init; }
    public SavedCommand? LastCommand { get; init; }
    public SavedGameEntry? PendingEntry { get; init; }
}

public sealed record SavedCommand
{
    public required Guid GameId { get; init; }
    public required GameCommandRequest Request { get; init; }
    public PlayerSide? Side { get; init; }
    public bool IsPending { get; init; }
}

public enum GameEntryKind { Create, Matchmaking }

public sealed record SavedGameEntry
{
    public required Guid RequestId { get; init; }
    public required GameEntryKind Kind { get; init; }
    public string? InitialFen { get; init; }
}

public sealed class SessionFile(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public static string DefaultPath => Environment.GetEnvironmentVariable("CHESS_SESSION_FILE")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "chess", "session.json");

    public async Task<CliSession?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path)) return null;
        RejectLink();
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
                throw new CliUsageException($"Session file permissions allow other users access. Run chmod 600 on '{Path}' before using it.");
        }
        await using var stream = File.OpenRead(Path);
        return await JsonSerializer.DeserializeAsync<CliSession>(stream, GameJson.Options, cancellationToken)
            ?? throw new CliUsageException("The session file is empty. Use --session with a different file or restore the saved game code.");
    }

    public async Task WriteAsync(CliSession session, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RejectLink();
        var temporary = System.IO.Path.Combine(directory, $".chess-{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                await JsonSerializer.SerializeAsync(stream, session, GameJson.Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, Path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void RejectLink()
    {
        if (new FileInfo(Path).LinkTarget is not null) throw new CliUsageException("A session file cannot be a symbolic link.");
    }
}
