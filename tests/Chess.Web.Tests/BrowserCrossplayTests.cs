using System.Collections.Concurrent;
using System.Text.Json;
using Chess.Client;
using Chess.Contracts;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.Tests;

[Category("Browser")]
[NotInParallel]
public sealed class BrowserCrossplayTests
{
    [Test]
    [Arguments("Polling")]
    [Arguments("Sse")]
    [Arguments("WebSocket")]
    public async Task BrowserPlaysAndResumesAgainstSharedClientThroughEachTransport(string transport)
    {
        var settings = Settings();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        await page.GotoAsync(settings.WebUrl);
        await page.GetByTestId("create-game").ClickAsync();
        var invite = await page.GetByTestId("opponent-code").InputValueAsync();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var opponent = new ChessClient(http, "Crossplay reference");
        var black = await opponent.JoinAsync(invite);
        await Expect(page.GetByTestId("opponent-name")).ToHaveTextAsync("Crossplay reference");
        await Expect(page.GetByTestId("your-name")).ToHaveTextAsync("Chess Web");

        await page.Locator(".connection-details summary").ClickAsync();
        await page.GetByTestId("update-mode").SelectOptionAsync(transport);
        await page.GetByTestId("square-e2").ClickAsync();
        await page.GetByTestId("square-e4").ClickAsync();
        var afterWhite = await WaitForMoveAsync(opponent, black, "e2e4");
        var afterBlack = await opponent.MoveAsync(black, afterWhite, "e5");
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-revision", afterBlack.Revision.ToString());
        if (transport != "Polling")
            await Expect(page.GetByTestId("connection-status")).ToContainTextAsync("Live");

        await page.GetByTestId("move-input").FillAsync("Nf3");
        await page.GetByTestId("play-move").ClickAsync();
        var afterKnight = await WaitForMoveAsync(opponent, black, "g1f3");
        await page.ReloadAsync();
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-game-id", black.GameId.ToString());
        await Expect(page.GetByTestId("square-f3")).ToHaveAttributeAsync("data-piece", "knight");
        await Expect(page.GetByTestId("move-history")).ToContainTextAsync("Nf3");

        if (transport == "Polling")
        {
            Directory.CreateDirectory(settings.ScreenshotDirectory);
            await page.ScreenshotAsync(new() { Path = Path.Combine(settings.ScreenshotDirectory, "web-desktop.png"), FullPage = true });
            await page.SetViewportSizeAsync(390, 844);
            await page.ScreenshotAsync(new() { Path = Path.Combine(settings.ScreenshotDirectory, "web-mobile.png"), FullPage = true });
            await Assert.That(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth")).IsTrue();
        }

        var drawOffer = await opponent.CommandAsync(black, new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = afterKnight.Revision, Action = GameAction.OfferDraw
        });
        await Expect(page.GetByTestId("draw-offer")).ToBeVisibleAsync();
        await page.GetByTestId("accept-draw").ClickAsync();
        await Expect(page.GetByTestId("game-result")).ToContainTextAsync("Game drawn");
        var finished = await opponent.GetAsync(black);
        await Assert.That(finished.Status).IsEqualTo(GameStatus.Finished);
        await Assert.That(finished.Revision > drawOffer.Revision).IsTrue();
        await Assert.That(errors.IsEmpty).IsTrue();
    }

    [Test]
    public async Task BrowserJoinsExistingSideAndChoosesUnderpromotionOnMobile()
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Crossplay reference");
        var white = await reference.CreateAsync("7k/P7/8/8/8/8/8/7K w - - 0 1");
        var black = await reference.JoinAsync(white.OpponentCode!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true });
        await page.GotoAsync(settings.WebUrl);
        await page.GetByTestId("join-code").FillAsync(white.Code);
        await page.GetByTestId("join-game").ClickAsync();
        await Expect(page.GetByTestId("your-name")).ToHaveTextAsync("Chess Web");
        await page.GetByTestId("square-a7").ClickAsync();
        await page.GetByTestId("square-a8").ClickAsync();
        await Expect(page.GetByTestId("promotion")).ToBeVisibleAsync();
        await page.GetByTestId("promote-r").ClickAsync();
        await Expect(page.GetByTestId("square-a8")).ToHaveAttributeAsync("data-piece", "rook");
        var snapshot = await WaitForMoveAsync(reference, black, "a7a8r");
        await Assert.That(snapshot.White.ClientName).IsEqualTo("Chess Web");
        await page.GetByTestId("resign").ClickAsync();
        await page.GetByTestId("confirm-resign").ClickAsync();
        await Expect(page.GetByTestId("game-result")).ToBeVisibleAsync();
        await Assert.That((await reference.GetAsync(black)).Status).IsEqualTo(GameStatus.Finished);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostEntryResponseKeepsPreviousGameAndRecoversCommittedSeatAfterReload(bool matchmaking)
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Recovery reference");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(settings.WebUrl);
        await page.GetByTestId("create-game").ClickAsync();
        var previousBlack = await reference.JoinAsync(await page.GetByTestId("opponent-code").InputValueAsync());
        await Expect(page.GetByTestId("opponent-name")).ToHaveTextAsync("Recovery reference");
        var previousCode = await page.GetByTestId("your-code").InputValueAsync();

        var entryRequests = new ConcurrentQueue<Guid>();
        GameAccess? committed = null;
        await page.RouteAsync(matchmaking ? "**/api/matchmaking" : "**/api/games", async route =>
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            entryRequests.Enqueue(request.RootElement.GetProperty("requestId").GetGuid());
            if (committed is null)
            {
                // The server commits the real request; only the browser's response is dropped.
                var response = await route.FetchAsync();
                if (!response.Ok) throw new InvalidOperationException(await response.TextAsync());
                committed = JsonSerializer.Deserialize<GameAccess>(await response.TextAsync(), GameJson.Options)!;
                await response.DisposeAsync();
                await route.AbortAsync("connectionreset");
            }
            else await route.ContinueAsync();
        });

        var button = matchmaking ? "find-opponent" : "create-game";
        await page.GetByTestId(button).ClickAsync();
        await Expect(page.GetByTestId("error")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-game-id", previousBlack.GameId.ToString());
        await Assert.That(await page.GetByTestId("your-code").InputValueAsync()).IsEqualTo(previousCode);
        var saved = await page.EvaluateAsync<string>("localStorage.getItem('chess.web.game.v1')");
        await Assert.That(JsonSerializer.Deserialize<GameAccess>(saved, GameJson.Options)!.GameId).IsEqualTo(previousBlack.GameId);
        await Assert.That(committed).IsNotNull();
        var recovered = committed!;

        var nextBlack = matchmaking
            ? await reference.MatchmakeAsync()
            : await reference.JoinAsync(recovered.OpponentCode!);
        await Assert.That(nextBlack.GameId).IsEqualTo(recovered.GameId);
        var previous = await reference.GetAsync(previousBlack);
        await reference.CommandAsync(previousBlack, new GameCommandRequest
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = previous.Revision, Action = GameAction.Resign
        });

        await page.ReloadAsync();
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-game-id", previousBlack.GameId.ToString());
        await page.GetByTestId(button).ClickAsync();
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-game-id", recovered.GameId.ToString());
        await Expect(page.GetByTestId("opponent-name")).ToHaveTextAsync("Recovery reference");
        await Assert.That(await page.GetByTestId("your-code").InputValueAsync()).IsEqualTo(recovered.Code);
        var attempts = entryRequests.ToArray();
        await Assert.That(attempts.Length).IsEqualTo(2);
        await Assert.That(attempts[1]).IsEqualTo(attempts[0]);
        var pendingKey = matchmaking ? "chess.web.pending.matchmaking.v1" : "chess.web.pending.create.v1";
        await Assert.That(await page.EvaluateAsync<string?>("key => localStorage.getItem(key)", pendingKey)).IsNull();

        await page.GetByTestId("square-e2").ClickAsync();
        await page.GetByTestId("square-e4").ClickAsync();
        await WaitForMoveAsync(reference, nextBlack, "e2e4");
        await page.GetByTestId("resign").ClickAsync();
        await page.GetByTestId("confirm-resign").ClickAsync();
        await Expect(page.GetByTestId("game-result")).ToBeVisibleAsync();
        await Assert.That((await reference.GetAsync(nextBlack)).Status).IsEqualTo(GameStatus.Finished);
    }

    private static async Task<GameSnapshot> WaitForMoveAsync(ChessClient client, GameAccess access, string uci)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var snapshot = await client.GetAsync(access, timeout.Token);
            if (snapshot.Moves.LastOrDefault()?.Uci == uci) return snapshot;
            await Task.Delay(100, timeout.Token);
        }
    }

    private static BrowserSettings Settings()
    {
        var web = Environment.GetEnvironmentVariable("CHESS_WEB_URL");
        var server = Environment.GetEnvironmentVariable("CHESS_SERVER_URL");
        if (string.IsNullOrWhiteSpace(web) || string.IsNullOrWhiteSpace(server))
            Skip.Test("Browser integration requires CHESS_WEB_URL and CHESS_SERVER_URL pointing at the running Aspire application.");
        var screenshots = Environment.GetEnvironmentVariable("CHESS_SCREENSHOT_DIRECTORY") ?? "artifacts/chess-web";
        return new BrowserSettings(web!, server!, Path.GetFullPath(screenshots));
    }

    private sealed record BrowserSettings(string WebUrl, string ServerUrl, string ScreenshotDirectory);
}
