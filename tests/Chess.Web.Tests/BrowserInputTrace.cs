using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace Chess.Web.Tests;

internal sealed class BrowserInputTrace(ICDPSession session, string artifact) : IAsyncDisposable
{
    private const int MaximumEvents = 20_000;
    private readonly ConcurrentQueue<JsonElement> events = new();
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool saved;
    private bool overflow;

    public static async Task<BrowserInputTrace> StartAsync(IBrowser browser, string artifact)
    {
        var session = await browser.NewBrowserCDPSessionAsync();
        var trace = new BrowserInputTrace(session, artifact);
        session.Event("Tracing.dataCollected").OnEvent += (_, data) =>
        {
            foreach (var item in data!.Value.GetProperty("value").EnumerateArray())
            {
                if (trace.events.Count < MaximumEvents) trace.events.Enqueue(item.Clone());
                else trace.overflow = true;
            }
        };
        session.Event("Tracing.tracingComplete").OnEvent += (_, _) => trace.completed.TrySetResult();
        await session.SendAsync("Tracing.start", new() { ["categories"] = "input" });
        return trace;
    }

    public bool Contains(string name) => events.Any(item => item.GetProperty("name").GetString() == name);

    public async Task SaveAsync()
    {
        if (saved) return;
        await session.SendAsync("Tracing.end");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        saved = true;
        Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
        await File.WriteAllTextAsync(artifact + ".input.json", JsonSerializer.Serialize(new { traceEvents = events, overflow }));
        if (overflow) throw new InvalidOperationException("The browser input trace exceeded its event limit.");
    }

    public async ValueTask DisposeAsync()
    {
        try { await SaveAsync(); }
        finally { await session.DetachAsync(); }
    }
}
