using Microsoft.Playwright;

namespace Chess.Crossplay.Tests;

internal sealed class CrossplayFixture : IAsyncDisposable
{
    private readonly List<IPlayer> players = [];
    private readonly string directory = Path.Combine(Path.GetTempPath(), "chess-crossplay-" + Guid.NewGuid().ToString("N"));
    private IPlaywright? playwright;
    private IBrowser? browser;
    private int nextPlayerId;

    private CrossplayFixture(string server, string web)
    {
        Server = server;
        Web = web;
        Directory.CreateDirectory(directory);
    }

    public string Server { get; }
    private string Web { get; }

    public static CrossplayFixture Create()
    {
        var server = Environment.GetEnvironmentVariable("CHESS_TEST_SERVER");
        var web = Environment.GetEnvironmentVariable("CHESS_WEB_URL");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(web))
        {
            if (Environment.GetEnvironmentVariable("CHESS_REQUIRE_CROSSPLAY") == "1")
                throw new InvalidOperationException("Required crossplay tests need CHESS_TEST_SERVER and CHESS_WEB_URL from the running Aspire application.");
            Skip.Test("Set CHESS_TEST_SERVER and CHESS_WEB_URL to run the frontend crossplay matrix.");
        }
        return new CrossplayFixture(server!, web!);
    }

    public async Task<IPlayer> PlayerAsync(Frontend kind)
    {
        var settings = Path.Combine(directory, $"{nextPlayerId++}-{kind}.session");
        IPlayer player;
        if (kind == Frontend.Web)
        {
            playwright ??= await Playwright.CreateAsync();
            browser ??= await playwright.Chromium.LaunchAsync(new() { Headless = true });
            player = await BrowserPlayer.OpenAsync(browser, Web, Server);
        }
        else if (kind == Frontend.Cli) player = new CliPlayer(Server, settings);
        else player = new NativeSessionPlayer(kind, Server, settings);
        players.Add(player);
        return player;
    }

    public async Task CloseAsync(IPlayer player)
    {
        if (players.Remove(player)) await player.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var player in players) await player.DisposeAsync();
        if (browser is not null) await browser.DisposeAsync();
        playwright?.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
