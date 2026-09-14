using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Chess.Contracts;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chess.Crossplay.Tests;

internal sealed class BrowserPlayer : IPlayer
{
    private readonly IBrowserContext context;
    private readonly IPage page;
    private readonly HttpClient auditHttp;
    private readonly ConcurrentQueue<string> browserErrors = new();

    private BrowserPlayer(IBrowserContext context, IPage page, string server)
    {
        this.context = context;
        this.page = page;
        auditHttp = new HttpClient { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(20) };
        page.PageError += (_, error) => browserErrors.Enqueue(error);
    }

    public string ClientName => "Chess Web";
    public GameAccess? Access { get; private set; }

    public static async Task<BrowserPlayer> OpenAsync(IBrowser browser, string web, string server)
    {
        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 1000 } });
        try
        {
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(15_000);
            var player = new BrowserPlayer(context, page, server);
            await page.GotoAsync(web);
            await Expect(page.GetByTestId("create-game")).ToBeVisibleAsync();
            return player;
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async Task<GameAccess> CreateAsync()
    {
        await page.GetByTestId("create-game").ClickAsync();
        return await ReadAccessAsync();
    }

    public async Task<GameAccess> JoinAsync(string code)
    {
        await page.GetByTestId("join-code").FillAsync(code);
        await page.GetByTestId("join-game").ClickAsync();
        var access = await ReadAccessAsync();
        await Assert.That(access.Code).IsEqualTo(code);
        return access;
    }

    public async Task<GameAccess> MatchmakeAsync()
    {
        await page.GetByTestId("find-opponent").ClickAsync();
        return await ReadAccessAsync();
    }

    public async Task<GameSnapshot> RefreshAsync()
    {
        if (Access is null) throw new InvalidOperationException("The browser has no current game.");
        await OpenDetailsAsync();
        await page.GetByTestId("refresh-game").ClickAsync();
        var snapshot = await AuditSnapshotAsync(Access.GameId, Access.Code);
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-revision", snapshot.Revision.ToString());
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-fen", snapshot.Fen);
        Access = Access with { Snapshot = snapshot };
        AssertNoBrowserErrors();
        return snapshot;
    }

    public async Task<GameSnapshot> MoveAsync(TestMove move)
    {
        await RefreshAsync();
        var requests = page.RunAndWaitForResponseAsync(async () =>
        {
            if (move.Entry == MoveEntry.Board)
            {
                await page.GetByTestId($"square-{move.Uci[..2]}").ClickAsync();
                await page.GetByTestId($"square-{move.Uci.Substring(2, 2)}").ClickAsync();
            }
            else
            {
                await page.Locator("#notation").SelectOptionAsync("San");
                await page.GetByTestId("move-input").FillAsync(move.San);
                await page.GetByTestId("play-move").ClickAsync();
            }
        }, response => response.Request.Method == "POST" && response.Url.EndsWith("/commands", StringComparison.Ordinal));
        var response = await requests;
        if (!response.Ok) throw new InvalidOperationException($"Browser command failed: {await response.TextAsync()}");
        var snapshot = await RefreshAsync();
        await Assert.That(snapshot.Moves.LastOrDefault()?.Uci).IsEqualTo(move.Uci);
        await Expect(page.GetByTestId("move-history")).ToContainTextAsync(move.San);
        return snapshot;
    }

    public async Task<GameSnapshot> ResignAsync()
    {
        await RefreshAsync();
        await page.GetByTestId("resign").ClickAsync();
        await page.GetByTestId("confirm-resign").ClickAsync();
        await Expect(page.GetByTestId("game-result")).ToBeVisibleAsync();
        return await RefreshAsync();
    }

    public async Task VerifyOpponentAsync(string opponentName)
    {
        await RefreshAsync();
        await Expect(page.GetByTestId("opponent-name")).ToHaveTextAsync(opponentName);
        await Expect(page.GetByTestId("your-name")).ToHaveTextAsync(ClientName);
    }

    private async Task<GameAccess> ReadAccessAsync()
    {
        await Expect(page.GetByTestId("board")).ToHaveAttributeAsync("data-game-id", new Regex("^[0-9a-f-]{36}$"));
        var gameId = Guid.Parse((await page.GetByTestId("board").GetAttributeAsync("data-game-id"))!);
        var code = await page.GetByTestId("your-code").InputValueAsync();
        var sideLabel = await page.Locator(".player-strip.you div > span").InnerTextAsync();
        var side = sideLabel.StartsWith("White", StringComparison.Ordinal) ? PlayerSide.White
            : sideLabel.StartsWith("Black", StringComparison.Ordinal) ? PlayerSide.Black
            : throw new InvalidOperationException($"The browser did not identify its side: {sideLabel}");
        var invite = page.GetByTestId("opponent-code");
        Access = new GameAccess
        {
            GameId = gameId, Code = code, Side = side,
            Snapshot = await AuditSnapshotAsync(gameId, code),
            OpponentCode = await invite.CountAsync() == 1 ? await invite.InputValueAsync() : null
        };
        AssertNoBrowserErrors();
        return Access;
    }

    private async Task OpenDetailsAsync()
    {
        if (!await page.GetByTestId("refresh-game").IsVisibleAsync())
            await page.Locator(".connection-details summary").ClickAsync();
    }

    // Read-only server observation checks the position that the browser displays.
    // Game creation, joining, moves, and resignation all go through the rendered UI.
    private async Task<GameSnapshot> AuditSnapshotAsync(Guid gameId, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/games/{gameId}");
        request.Headers.Add("X-Game-Code", code);
        using var response = await auditHttp.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameSnapshot>(GameJson.Options)
            ?? throw new InvalidOperationException("The server returned an empty snapshot.");
    }

    private void AssertNoBrowserErrors()
    {
        if (!browserErrors.IsEmpty)
            throw new InvalidOperationException($"Browser runtime errors: {string.Join(Environment.NewLine, browserErrors)}");
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        auditHttp.Dispose();
    }
}
