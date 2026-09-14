using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Native.Tests;

public sealed class GameSessionRecoveryTests
{
    [Test, RequiresRecoveryServer, Category("polling-regression")]
    public async Task PendingRefreshDoesNotDiscardTheSelectedMoveOrOverwriteItsResult()
    {
        using var settings = new TemporarySettings();
        await using var proxy = new ResponseDroppingProxy(Server);
        using var session = new GameSession("Chess polling regression", settings.Path, proxy.Address.AbsoluteUri);
        await session.CreateAsync(proxy.Address.AbsoluteUri);
        using var http = new HttpClient { BaseAddress = Server };
        var opponent = new ChessClient(http, "Polling regression opponent");
        var other = await opponent.JoinAsync(session.Access!.OpponentCode!);
        await session.RefreshAsync();
        await session.SelectSquareAsync("e2");
        proxy.PauseNextRead();
        var refresh = session.RefreshAsync();
        await proxy.ReadCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var enabledDuringRead = session.CanMove;
        var readsDuringPause = proxy.ReadRequestCount;
        await session.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
        try { await session.SelectSquareAsync("e4").WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { proxy.ReleaseRead.TrySetResult(); }
        await refresh;

        await Assert.That((await opponent.GetAsync(other)).Moves.Length).IsEqualTo(1);
        await Assert.That(enabledDuringRead).IsTrue();
        await Assert.That(proxy.ReadRequestCount).IsEqualTo(readsDuringPause);
        await Assert.That(session.Snapshot!.Moves.Single().Uci).IsEqualTo("e2e4");
        await Assert.That(session.Message).IsEqualTo("Played e2e4.");
    }

    [Test, RequiresRecoveryServer, Category("polling-regression")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PendingRefreshCannotReplaceTheSnapshotAfterJoiningAnotherGame(bool failedRead)
    {
        using var settings = new TemporarySettings();
        await using var proxy = new ResponseDroppingProxy(Server);
        using var session = new GameSession("Chess polling regression", settings.Path, proxy.Address.AbsoluteUri);
        await session.CreateAsync(proxy.Address.AbsoluteUri);
        using var http = new HttpClient { BaseAddress = Server };
        var otherGame = await new ChessClient(http, "Another game").CreateAsync();
        proxy.PauseNextRead(failedRead);
        var refresh = session.RefreshAsync();
        await proxy.ReadCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await session.JoinAsync(proxy.Address.AbsoluteUri, otherGame.Code).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { proxy.ReleaseRead.TrySetResult(); }
        await refresh;

        await Assert.That(session.Access!.GameId).IsEqualTo(otherGame.GameId);
        await Assert.That(session.Snapshot!.GameId).IsEqualTo(otherGame.GameId);
        await Assert.That(session.Message).IsEqualTo("Game restored. Your side is saved on this device.");
    }

    [Test, RequiresRecoveryServer, NotInParallel("game-session-matchmaking")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostEntryResponseRestoresTheSameSideAfterTheSessionRestarts(bool matchmaking)
    {
        using var settings = new TemporarySettings();
        var server = Server;
        await using var proxy = new ResponseDroppingProxy(server, matchmaking ? "/api/matchmaking" : "/api/games");
        const string clientName = "Chess native recovery test";
        string previousCode;
        using (var first = new GameSession(clientName, settings.Path, server.AbsoluteUri))
        {
            await first.CreateAsync(server.AbsoluteUri).WaitAsync(TimeSpan.FromSeconds(25));
            await Assert.That(first.Access).IsNotNull();
            previousCode = first.SavedCode;
            var previousId = first.Access!.GameId;
            await OpenAsync(first, proxy.Address.AbsoluteUri, matchmaking).WaitAsync(TimeSpan.FromSeconds(25));
            var committed = await proxy.DroppedAccess.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(committed.GameId).IsNotEqualTo(previousId);
            await Assert.That(first.Access.GameId).IsEqualTo(previousId);
            await Assert.That(first.Server).IsEqualTo(server.AbsoluteUri);
            await Assert.That(first.SavedCode).IsEqualTo(previousCode);
        }

        using var restored = new GameSession(clientName, settings.Path);
        await Assert.That(restored.Server).IsEqualTo(server.AbsoluteUri);
        await Assert.That(restored.SavedCode).IsEqualTo(previousCode);
        await OpenAsync(restored, proxy.Address.AbsoluteUri, matchmaking).WaitAsync(TimeSpan.FromSeconds(25));
        var original = await proxy.DroppedAccess.Task;
        await Assert.That(restored.Access).IsNotNull();
        await Assert.That(restored.Access!.GameId).IsEqualTo(original.GameId);
        await Assert.That(restored.Access.Side).IsEqualTo(original.Side);
        await Assert.That(restored.SavedCode).IsEqualTo(original.Code);
        await Assert.That(restored.Server).IsEqualTo(proxy.Address.AbsoluteUri);
        var requests = proxy.EntryRequestIds.ToArray();
        await Assert.That(requests.Length).IsEqualTo(2);
        await Assert.That(requests[1]).IsEqualTo(requests[0]);

        using (var savedAgain = new GameSession(clientName, settings.Path))
        {
            await Assert.That(savedAgain.SavedCode).IsEqualTo(original.Code);
            await Assert.That(savedAgain.Server).IsEqualTo(proxy.Address.AbsoluteUri);
        }
        if (!OperatingSystem.IsWindows())
            await Assert.That(File.GetUnixFileMode(settings.Path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Assert.That(Directory.GetFiles(settings.Directory).Length).IsEqualTo(1);

        if (matchmaking && original.Snapshot.Status == GameStatus.Waiting)
        {
            using var http = new HttpClient { BaseAddress = server };
            var other = new ChessClient(http, "Native recovery cleanup opponent");
            var opponent = await other.MatchmakeAsync();
            await Assert.That(opponent.GameId).IsEqualTo(original.GameId);
            await other.CommandAsync(opponent, new()
            {
                RequestId = Guid.NewGuid(), ExpectedRevision = opponent.Snapshot.Revision, Action = GameAction.Resign
            });
        }
    }

    [Test, RequiresRecoveryServer]
    [Arguments("")]
    [Arguments("not-a-valid-game-code")]
    public async Task FailedJoinToAnotherServerPreservesThePreviousSavedConnection(string invalidCode)
    {
        using var settings = new TemporarySettings();
        var server = Server;
        await using var otherAddress = new ResponseDroppingProxy(server);
        using var session = new GameSession("Chess native recovery test", settings.Path, server.AbsoluteUri);
        await session.CreateAsync(server.AbsoluteUri).WaitAsync(TimeSpan.FromSeconds(25));
        await Assert.That(session.Access).IsNotNull();
        var previous = session.Access!;
        var before = await File.ReadAllBytesAsync(settings.Path);
        await session.JoinAsync(otherAddress.Address.AbsoluteUri, invalidCode).WaitAsync(TimeSpan.FromSeconds(25));
        await Assert.That(session.Server).IsEqualTo(server.AbsoluteUri);
        await Assert.That(session.SavedCode).IsEqualTo(previous.Code);
        await Assert.That(session.Access!.GameId).IsEqualTo(previous.GameId);
        await Assert.That(Convert.ToBase64String(await File.ReadAllBytesAsync(settings.Path))).IsEqualTo(Convert.ToBase64String(before));

        using var restarted = new GameSession("Chess native recovery test", settings.Path);
        await restarted.JoinAsync(restarted.Server, restarted.SavedCode).WaitAsync(TimeSpan.FromSeconds(25));
        await Assert.That(restarted.Access!.GameId).IsEqualTo(previous.GameId);
        await Assert.That(restarted.Access.Side).IsEqualTo(previous.Side);
    }

    private static Uri Server => new(Environment.GetEnvironmentVariable("CHESS_TEST_SERVER")!);
    private static Task OpenAsync(GameSession session, string server, bool matchmaking) =>
        matchmaking ? session.MatchmakeAsync(server) : session.CreateAsync(server);

    private sealed class TemporarySettings : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"chess-native-recovery-{Guid.NewGuid():N}");
        public string Path => System.IO.Path.Combine(Directory, "settings");
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
    }

    private sealed class ResponseDroppingProxy : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly HttpClient upstream;
        private readonly string? dropPath;
        private readonly CancellationTokenSource lifetime = new();
        private readonly ConcurrentBag<Task> requests = [];
        private readonly Task accepting;
        private int dropped;
        private int pauseRead;
        private int readRequestCount;
        private bool failPausedRead;

        public ResponseDroppingProxy(Uri server, string? dropPath = null)
        {
            this.dropPath = dropPath;
            upstream = new HttpClient { BaseAddress = server };
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Address = new Uri($"http://127.0.0.1:{port}/");
            listener.Prefixes.Add(Address.AbsoluteUri);
            listener.Start();
            accepting = AcceptAsync();
        }

        public Uri Address { get; }
        public ConcurrentQueue<Guid> EntryRequestIds { get; } = [];
        public TaskCompletionSource<GameAccess> DroppedAccess { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadCaptured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PauseNextRead(bool fail = false)
        {
            failPausedRead = fail;
            Interlocked.Exchange(ref pauseRead, 1);
        }
        public int ReadRequestCount => Volatile.Read(ref readRequestCount);

        private async Task AcceptAsync()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                    requests.Add(ForwardAsync(await listener.GetContextAsync().WaitAsync(lifetime.Token)));
            }
            catch (Exception exception) when (lifetime.IsCancellationRequested && exception is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
        }

        private async Task ForwardAsync(HttpListenerContext context)
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod), context.Request.RawUrl!.TrimStart('/'));
                if (request.Method == HttpMethod.Get) Interlocked.Increment(ref readRequestCount);
                byte[] body;
                using (var buffer = new MemoryStream())
                {
                    await context.Request.InputStream.CopyToAsync(buffer, lifetime.Token);
                    body = buffer.ToArray();
                }
                if (context.Request.HasEntityBody)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
                }
                if (context.Request.Headers["X-Game-Code"] is { } code) request.Headers.Add("X-Game-Code", code);
                var isEntry = context.Request.Url!.AbsolutePath == dropPath && context.Request.HttpMethod == "POST";
                if (isEntry)
                {
                    using var document = JsonDocument.Parse(body);
                    EntryRequestIds.Enqueue(document.RootElement.GetProperty("requestId").GetGuid());
                }
                using var response = await upstream.SendAsync(request, lifetime.Token);
                var result = await response.Content.ReadAsByteArrayAsync(lifetime.Token);
                if (request.Method == HttpMethod.Get && Interlocked.Exchange(ref pauseRead, 0) == 1)
                {
                    ReadCaptured.TrySetResult();
                    await ReleaseRead.Task.WaitAsync(lifetime.Token);
                    if (failPausedRead)
                    {
                        response.StatusCode = HttpStatusCode.ServiceUnavailable;
                        result = JsonSerializer.SerializeToUtf8Bytes(new GameError
                        {
                            Code = "delayed_refresh_error", Message = "The previous game's refresh failed."
                        }, GameJson.Options);
                    }
                }
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
                context.Response.ContentLength64 = result.Length;
                if (isEntry && response.IsSuccessStatusCode && Interlocked.Exchange(ref dropped, 1) == 0)
                {
                    DroppedAccess.TrySetResult(JsonSerializer.Deserialize<GameAccess>(result, GameJson.Options)!);
                    await context.Response.OutputStream.WriteAsync(result.AsMemory(0, 1), lifetime.Token);
                    await context.Response.OutputStream.FlushAsync(lifetime.Token);
                    context.Response.Abort();
                    return;
                }
                await context.Response.OutputStream.WriteAsync(result, lifetime.Token);
                context.Response.Close();
            }
            catch
            {
                context.Response.Abort();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            listener.Close();
            await accepting;
            try { await Task.WhenAll(requests); }
            catch (Exception exception) when (exception is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
            upstream.Dispose();
            lifetime.Dispose();
        }
    }
}

public sealed class RequiresRecoveryServerAttribute() : SkipAttribute("Set CHESS_TEST_SERVER to run native session recovery against a real game server.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHESS_TEST_SERVER")));
}
