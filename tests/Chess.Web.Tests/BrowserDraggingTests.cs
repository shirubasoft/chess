using System.Collections.Concurrent;
using Chess.Client;
using Chess.Contracts;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chess.Web.Tests;

public sealed partial class BrowserCrossplayTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MouseDragUsesThePlayersSideAndBoardOrientation(bool playBlack, bool flipped)
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Drag reference");
        var white = await reference.CreateAsync();
        var black = await reference.JoinAsync(white.OpponentCode!);
        if (playBlack) await reference.MoveAsync(white, black.Snapshot, "e4");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
        await JoinBoardAsync(page, settings.WebUrl, playBlack ? black : white);
        if (flipped) await page.GetByRole(AriaRole.Button, new() { Name = "Flip board" }).ClickAsync();
        await MouseDragAsync(page, playBlack ? "e7" : "e2", playBlack ? "e5" : "e4");
        await WaitForMoveAsync(reference, white, playBlack ? "e7e5" : "e2e4");
        await Expect(page.Locator(".drag-piece")).ToHaveCountAsync(0);
    }

    [Test]
    [Arguments("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1", "g1", "e1g1", "f1", "rook")]
    [Arguments("7k/8/8/3pP3/8/8/8/7K w - d6 0 1", "e5", "d6", "e5d6", "d6", "pawn")]
    public async Task DragExecutesSpecialMoves(string fen, string from, string to, string move, string square, string piece)
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Drag reference");
        var white = await reference.CreateAsync(fen);
        await reference.JoinAsync(white.OpponentCode!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await JoinBoardAsync(page, settings.WebUrl, white);
        await MouseDragAsync(page, from, to);
        await WaitForMoveAsync(reference, white, move);
        await Expect(page.GetByTestId("square-" + square)).ToHaveAttributeAsync("data-piece", piece);
        if (move == "e5d6") await Expect(page.GetByTestId("square-d5")).Not.ToHaveAttributeAsync("data-piece", "pawn");
    }

    [Test]
    public async Task TouchDropWaitsForThePromotionChoice()
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Drag reference");
        var white = await reference.CreateAsync("7k/P7/8/8/8/8/8/7K w - - 0 1");
        await reference.JoinAsync(white.OpponentCode!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true });
        await JoinBoardAsync(page, settings.WebUrl, white);
        var before = await reference.GetAsync(white);
        await TouchDragAsync(page, "a7", "a8");
        await Expect(page.GetByTestId("promotion")).ToBeVisibleAsync();
        await Assert.That((await reference.GetAsync(white)).Revision).IsEqualTo(before.Revision);
        await page.GetByTestId("promote-n").TapAsync();
        await Expect(page.GetByTestId("promotion")).ToHaveCountAsync(0);
        await WaitForMoveAsync(reference, white, "a7a8n");
        await Expect(page.GetByTestId("square-a8")).ToHaveAttributeAsync("data-piece", "knight");
        Directory.CreateDirectory(settings.ScreenshotDirectory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(settings.ScreenshotDirectory, "web-touch-promotion.png"), FullPage = true });
    }

    [Test]
    public async Task CancelledTouchDragLeavesTapMovesUsable()
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Drag reference");
        var white = await reference.CreateAsync();
        await reference.JoinAsync(white.OpponentCode!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true });
        await JoinBoardAsync(page, settings.WebUrl, white);
        var before = await reference.GetAsync(white);
        await TouchDragAsync(page, "e2", "e4", cancel: true);
        await Expect(page.Locator(".drag-piece")).ToHaveCountAsync(0);
        await Assert.That((await reference.GetAsync(white)).Revision).IsEqualTo(before.Revision);
        await page.GetByTestId("square-e2").TapAsync();
        await page.GetByTestId("square-e4").TapAsync();
        await WaitForMoveAsync(reference, white, "e2e4");
    }

    [Test]
    [Arguments("illegal")]
    [Arguments("outside")]
    [Arguments("escape")]
    [Arguments("cancel")]
    [Arguments("blur")]
    [Arguments("flip")]
    [Arguments("revision")]
    public async Task CancelledDragsLeaveTheGameAndClickInputUsable(string cancellation)
    {
        var settings = Settings();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Drag reference");
        var white = await reference.CreateAsync();
        var black = await reference.JoinAsync(white.OpponentCode!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
        await JoinBoardAsync(page, settings.WebUrl, white);
        var before = await reference.GetAsync(white);
        // Empty, blocked and opposing squares cannot start a drag.
        foreach (var source in new[] { "e4", "a1", "e7" })
        {
            await MouseDragAsync(page, source, "e5", expectDrag: false);
            await Expect(page.Locator(".drag-piece")).ToHaveCountAsync(0);
        }
        await BeginMouseDragAsync(page, "e2", "e4");
        switch (cancellation)
        {
            case "illegal":
                var illegal = await SquareCenterAsync(page, "e5");
                await page.Mouse.MoveAsync(illegal.X, illegal.Y);
                break;
            case "outside": await page.Mouse.MoveAsync(5, 5); break;
            case "escape": await page.Keyboard.PressAsync("Escape"); break;
            case "cancel":
                await page.GetByTestId("board").DispatchEventAsync("pointercancel", new { pointerId = 1 });
                break;
            case "blur": await page.EvaluateAsync("window.dispatchEvent(new Event('blur'))"); break;
            case "flip":
                await page.GetByRole(AriaRole.Button, new() { Name = "Flip board" }).FocusAsync();
                await page.Keyboard.PressAsync("Enter");
                break;
            case "revision":
                before = await reference.CommandAsync(black, new GameCommandRequest
                {
                    RequestId = Guid.NewGuid(), ExpectedRevision = before.Revision, Action = GameAction.OfferDraw
                });
                await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-revision", before.Revision.ToString());
                break;
        }
        await page.Mouse.UpAsync();
        await Expect(page.Locator(".drag-piece")).ToHaveCountAsync(0);
        await page.GetByTestId("square-e2").ClickAsync();
        await Expect(page.GetByTestId("square-e2")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assert.That((await reference.GetAsync(white)).Revision).IsEqualTo(before.Revision);
        await page.GetByTestId("square-e4").ClickAsync();
        await WaitForMoveAsync(reference, white, "e2e4");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DragGameplayReachesCheckmateWithMouseAndTouch(bool touch)
    {
        var settings = Settings();
        var recording = Environment.GetEnvironmentVariable("CHESS_DRAG_VIDEO_DIRECTORY");
        var name = touch ? "web-touch" : "web-mouse";
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl) };
        var reference = new ChessClient(http, "Scripted opponent");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = touch ? 390 : 1440, Height = touch ? 844 : 1000 },
            IsMobile = touch, HasTouch = touch,
            RecordVideoDir = recording is null ? null : Path.Combine(recording, name),
            RecordVideoSize = recording is null ? null : new() { Width = touch ? 390 : 1440, Height = touch ? 844 : 1000 }
        });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        await page.GotoAsync(settings.WebUrl);
        await page.GetByTestId("create-game").ClickAsync();
        var black = await reference.JoinAsync(await page.GetByTestId("opponent-code").InputValueAsync());
        await Expect(page.GetByTestId("opponent-name")).ToHaveTextAsync("Scripted opponent");
        Directory.CreateDirectory(settings.ScreenshotDirectory);
        var whiteMoves = new[] { ("e2", "e4"), ("f1", "c4"), ("d1", "h5"), ("h5", "f7") };
        var blackMoves = new[] { "e5", "Nc6", "Nf6" };
        for (var turn = 0; turn < whiteMoves.Length; turn++)
        {
            if (recording is not null) await Task.Delay(1500);
            var (from, to) = whiteMoves[turn];
            var screenshot = turn == 0 ? Path.Combine(settings.ScreenshotDirectory, name + "-drag.png") : null;
            if (touch) await TouchDragAsync(page, from, to, screenshot, recording is not null);
            else await MouseDragAsync(page, from, to, screenshot: screenshot, paced: recording is not null);
            var snapshot = await WaitForMoveAsync(reference, black, from + to);
            await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-revision", snapshot.Revision.ToString());
            if (turn < blackMoves.Length)
            {
                if (recording is not null) await Task.Delay(1000);
                snapshot = await reference.MoveAsync(black, snapshot, blackMoves[turn]);
                await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-revision", snapshot.Revision.ToString());
            }
        }
        await Expect(page.GetByTestId("game-result")).ToContainTextAsync("White wins");
        await Assert.That((await reference.GetAsync(black)).Status).IsEqualTo(GameStatus.Finished);
        await page.ScreenshotAsync(new() { Path = Path.Combine(settings.ScreenshotDirectory, name + "-checkmate.png"), FullPage = true });
        await Assert.That(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth")).IsTrue();
        await Assert.That(errors.IsEmpty).IsTrue();
        if (recording is not null) await Task.Delay(3500);
        await context.CloseAsync();
    }

    private static async Task JoinBoardAsync(IPage page, string url, GameAccess access)
    {
        await page.GotoAsync(url);
        await page.GetByTestId("join-code").FillAsync(access.Code);
        await page.GetByTestId("join-game").ClickAsync();
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-game-id", access.GameId.ToString());
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-drag-ready", "true");
        await Expect(page.GetByTestId("your-name")).ToHaveTextAsync("Chess Web");
    }

    private static async Task<(float X, float Y)> SquareCenterAsync(IPage page, string name)
    {
        var bounds = (await page.GetByTestId("square-" + name).BoundingBoxAsync())!;
        return (bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static async Task BeginMouseDragAsync(IPage page, string from, string to, bool expectDrag = true, bool paced = false)
    {
        await PrepareBoardInputAsync(page);
        var start = await SquareCenterAsync(page, from);
        var end = await SquareCenterAsync(page, to);
        await page.Mouse.MoveAsync(start.X, start.Y);
        await page.Mouse.DownAsync();
        for (var step = 1; step <= 12; step++)
        {
            await page.Mouse.MoveAsync(start.X + (end.X - start.X) * step / 12, start.Y + (end.Y - start.Y) * step / 12);
            if (paced) await Task.Delay(50);
        }
        if (expectDrag) await Expect(page.Locator(".drag-piece")).ToBeVisibleAsync();
    }

    private static async Task MouseDragAsync(IPage page, string from, string to, bool expectDrag = true, string? screenshot = null, bool paced = false)
    {
        await BeginMouseDragAsync(page, from, to, expectDrag, paced);
        if (screenshot is not null) await page.ScreenshotAsync(new() { Path = screenshot });
        await page.Mouse.UpAsync();
    }

    private static async Task TouchDragAsync(IPage page, string from, string to, string? screenshot = null, bool paced = false, bool cancel = false)
    {
        await PrepareBoardInputAsync(page);
        var start = await SquareCenterAsync(page, from);
        var end = await SquareCenterAsync(page, to);
        var cdp = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x = start.X, y = start.Y, id = 1 } } });
            for (var step = 1; step <= 12; step++)
            {
                await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new
                {
                    x = start.X + (end.X - start.X) * step / 12, y = start.Y + (end.Y - start.Y) * step / 12, id = 1
                } } });
                if (paced) await Task.Delay(50);
            }
            await Expect(page.Locator(".drag-piece")).ToBeVisibleAsync();
            if (screenshot is not null) await page.ScreenshotAsync(new() { Path = screenshot });
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = cancel ? "touchCancel" : "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        }
        finally { await cdp.DetachAsync(); }
    }

    private static async Task PrepareBoardInputAsync(IPage page)
    {
        await page.Locator(".board-area").EvaluateAsync("area => area.scrollIntoView({ block: 'start', behavior: 'instant' })");
        // Let the scroll event finish before taking pointer capture.
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }
}
