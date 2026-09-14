using Chess.Contracts;
using Chess.Notation;
using Chess.Web.Storage;
using Microsoft.JSInterop;

namespace Chess.Web.Tests;

public sealed class BrowserGameStoreTests
{
    [Test]
    public async Task SavedSideAccessSurvivesAStoreRecreation()
    {
        var browser = new MemoryBrowser();
        var snapshot = BoardPresentationTests.Snapshot(Fen.Format(Position.Initial), ["e2e4"]);
        var access = new GameAccess { GameId = snapshot.GameId, Code = "my-side-secret", Side = PlayerSide.Black, Snapshot = snapshot, OpponentCode = "opponent-invite" };
        await new BrowserGameStore(browser).SaveAsync(access);
        var loaded = await new BrowserGameStore(browser).LoadAsync();
        await Assert.That(loaded).IsNotNull();
        await Assert.That(loaded!.GameId).IsEqualTo(access.GameId);
        await Assert.That(loaded.Side).IsEqualTo(PlayerSide.Black);
        await Assert.That(loaded.Code).IsEqualTo(access.Code);
        await Assert.That(loaded.OpponentCode).IsEqualTo(access.OpponentCode);
        await Assert.That(loaded.Snapshot.Fen).IsEqualTo(access.Snapshot.Fen);
    }

    [Test]
    public async Task DamagedLocalStorageIsRemovedSoTheLobbyCanStillLoad()
    {
        var browser = new MemoryBrowser { Value = "{broken data" };
        var loaded = await new BrowserGameStore(browser).LoadAsync();
        await Assert.That(loaded).IsNull();
        await Assert.That(browser.Value).IsNull();
    }

    [Test]
    public async Task ForgettingGameRemovesItsSideCredential()
    {
        var browser = new MemoryBrowser { Value = "saved-game" };
        await new BrowserGameStore(browser).ForgetAsync();
        await Assert.That(browser.Value).IsNull();
    }

    [Test]
    public async Task UnacknowledgedEntryRetainsItsRequestIdAfterBrowserStoreRecreation()
    {
        var browser = new MemoryBrowser();
        var first = await new BrowserGameStore(browser).EntryRequestIdAsync(BrowserEntry.PrivateGame);
        var retry = await new BrowserGameStore(browser).EntryRequestIdAsync(BrowserEntry.PrivateGame);
        await Assert.That(first).IsNotEqualTo(Guid.Empty);
        await Assert.That(retry).IsEqualTo(first);
    }

    [Test]
    public async Task DifferentEntryKindsKeepIndependentRecoveryIds()
    {
        var browser = new MemoryBrowser();
        var store = new BrowserGameStore(browser);
        var create = await store.EntryRequestIdAsync(BrowserEntry.PrivateGame);
        var random = await store.EntryRequestIdAsync(BrowserEntry.Matchmaking);
        await Assert.That(random).IsNotEqualTo(create);
        await Assert.That(await new BrowserGameStore(browser).EntryRequestIdAsync(BrowserEntry.PrivateGame)).IsEqualTo(create);
        await Assert.That(await new BrowserGameStore(browser).EntryRequestIdAsync(BrowserEntry.Matchmaking)).IsEqualTo(random);
    }

    [Test]
    public async Task CompletedEntryAllowsANewRequestWithoutRemovingSavedAccess()
    {
        var browser = new MemoryBrowser { Value = "existing-access" };
        var store = new BrowserGameStore(browser);
        var request = await store.EntryRequestIdAsync(BrowserEntry.Matchmaking);
        await store.CompleteEntryAsync(BrowserEntry.Matchmaking);
        var next = await new BrowserGameStore(browser).EntryRequestIdAsync(BrowserEntry.Matchmaking);
        await Assert.That(next).IsNotEqualTo(request);
        await Assert.That(browser.Value).IsEqualTo("existing-access");
    }

    // This test double dispatches dictionary operations, never JavaScript.
#pragma warning disable BL0016
    private sealed class MemoryBrowser : IJSRuntime
    {
        private readonly Dictionary<string, string?> values = [];
        public string? Value
        {
            get => values.GetValueOrDefault("chess.web.game.v1");
            set => values["chess.web.game.v1"] = value;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            Dispatch<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            Dispatch<TValue>(identifier, args);

        private ValueTask<TValue> Dispatch<TValue>(string identifier, object?[]? args)
        {
            var key = (string)args![0]!;
            if (identifier == "localStorage.setItem") values[key] = (string?)args[1];
            else if (identifier == "localStorage.removeItem") values.Remove(key);
            else if (identifier == "localStorage.getItem") return ValueTask.FromResult((TValue)(object?)values.GetValueOrDefault(key)!);
            else throw new NotSupportedException(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
#pragma warning restore BL0016
}
