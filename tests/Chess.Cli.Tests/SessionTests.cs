namespace Chess.Cli.Tests;

public sealed class SessionTests
{
    [Test]
    public async Task AtomicSessionWritesKeepPrivatePermissionsAndLeaveNoTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chess-session-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "access.json");
        try
        {
            var file = new SessionFile(path);
            await file.WriteAsync(new() { Server = "https://example.invalid/" }, CancellationToken.None);
            await file.WriteAsync(new() { Server = "https://another.invalid/" }, CancellationToken.None);
            await Assert.That((await file.ReadAsync(CancellationToken.None))!.Server).IsEqualTo("https://another.invalid/");
            await Assert.That(Directory.GetFiles(directory).Length).IsEqualTo(1);
            if (!OperatingSystem.IsWindows())
                await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
