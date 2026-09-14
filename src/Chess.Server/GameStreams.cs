using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Chess.Contracts;
using Chess.Server.Application;
using Microsoft.Extensions.Options;

namespace Chess.Server;

internal static class GameStreams
{
    public static async Task<IResult> WaitAsync(Guid id, HttpContext context, PostgresGameStore store, IOptions<GameServerOptions> options)
    {
        var after = Cursor(context);
        var seconds = IntQuery(context, "timeoutSeconds", 30, 1, 60);
        var code = GameServer.Code(context);
        var elapsed = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds));
        do
        {
            var snapshot = await store.ReadAsync(new GameId(id), code, context.RequestAborted);
            if (snapshot.Revision > after) return Results.Json(snapshot, GameJson.Options);
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(seconds)) return Results.NoContent();
        } while (await timer.WaitForNextTickAsync(context.RequestAborted));
        return Results.NoContent();
    }

    public static async Task SseAsync(Guid id, HttpContext context, PostgresGameStore store, IOptions<GameServerOptions> options)
    {
        var after = Cursor(context);
        if (context.Request.Headers.TryGetValue("Last-Event-ID", out var last))
        {
            if (!long.TryParse(last, out after) || after < -1) throw GameFault.Invalid("Last-Event-ID must be a revision.");
        }
        var code = GameServer.Code(context);
        await store.ReadAsync(new GameId(id), code, context.RequestAborted);
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds));
        var heartbeat = Stopwatch.StartNew();
        do
        {
            foreach (var snapshot in await store.ReadUpdatesAsync(new GameId(id), code, after, context.RequestAborted))
            {
                await context.Response.WriteAsync($"id: {snapshot.Revision}\nevent: snapshot\ndata: {JsonSerializer.Serialize(snapshot, GameJson.Options)}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                after = snapshot.Revision;
            }
            if (heartbeat.Elapsed >= TimeSpan.FromSeconds(15))
            {
                await context.Response.WriteAsync(": heartbeat\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                heartbeat.Restart();
            }
        } while (await timer.WaitForNextTickAsync(context.RequestAborted));
    }

    public static async Task WebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        var options = context.RequestServices.GetRequiredService<IOptions<GameServerOptions>>().Value;
        var store = context.RequestServices.GetRequiredService<PostgresGameStore>();
        var id = new GameId(Guid.Parse((string)context.Request.RouteValues["id"]!));
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            WatchRequest watch;
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                var first = await ReceiveAsync(socket, handshake.Token);
                watch = JsonSerializer.Deserialize<WatchRequest>(first, GameJson.Options) ?? throw GameFault.Unauthorized();
                if (watch.AfterRevision < -1) throw GameFault.Invalid("afterRevision must be -1 or greater.");
                await store.ReadAsync(id, watch.Code, handshake.Token);
            }
            var monitor = MonitorCloseAsync(socket, lifetime);
            try
            {
                var after = watch.AfterRevision;
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.PollIntervalMilliseconds));
                do
                {
                    foreach (var snapshot in await store.ReadUpdatesAsync(id, watch.Code, after, lifetime.Token))
                    {
                        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(snapshot, GameJson.Options), WebSocketMessageType.Text, true, lifetime.Token);
                        after = snapshot.Revision;
                    }
                } while (await timer.WaitForNextTickAsync(lifetime.Token));
            }
            finally
            {
                await lifetime.CancelAsync();
                await monitor;
            }
        }
        catch (Exception error) when (error is GameFault or JsonException or OperationCanceledException or WebSocketException)
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Subscription ended. Reconnect with your game code.", timeout.Token); }
                catch (Exception closing) when (closing is WebSocketException or OperationCanceledException) { }
            }
        }
    }

    private static async Task MonitorCloseAsync(WebSocket socket, CancellationTokenSource lifetime)
    {
        try
        {
            // Moves use the same idempotent command endpoint for every transport.
            await ReceiveAsync(socket, lifetime.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or WebSocketException or GameFault) { }
        finally { await lifetime.CancelAsync(); }
    }

    private static async Task<byte[]> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[1024];
        ValueWebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > 4096)
                throw GameFault.Invalid("Expected a subscription message of at most 4096 bytes.");
            message.Write(buffer, 0, received.Count);
        } while (!received.EndOfMessage);
        return message.ToArray();
    }

    private static long Cursor(HttpContext context)
    {
        var query = context.Request.Query["afterRevision"];
        if (query.Count == 0) return -1;
        if (!long.TryParse(query, out var value) || value < -1) throw GameFault.Invalid("afterRevision must be -1 or greater.");
        return value;
    }

    private static int IntQuery(HttpContext context, string name, int fallback, int minimum, int maximum)
    {
        var query = context.Request.Query[name];
        if (query.Count == 0) return fallback;
        if (!int.TryParse(query, out var value) || value < minimum || value > maximum)
            throw GameFault.Invalid($"{name} must be between {minimum} and {maximum}.");
        return value;
    }
}
