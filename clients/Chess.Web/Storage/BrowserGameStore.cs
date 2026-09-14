using System.Text.Json;
using Chess.Contracts;
using Microsoft.JSInterop;

namespace Chess.Web.Storage;

public sealed class BrowserGameStore(IJSRuntime javascript)
{
    private const string Key = "chess.web.game.v1";

    public async ValueTask<GameAccess?> LoadAsync()
    {
        try
        {
            var saved = await javascript.InvokeAsync<string?>("localStorage.getItem", Key);
            if (saved is null) return null;
            return JsonSerializer.Deserialize<GameAccess>(saved, GameJson.Options);
        }
        catch (JsonException)
        {
            await ForgetAsync();
            return null;
        }
        catch (JSException exception)
        {
            throw new BrowserStorageException(exception);
        }
    }

    public async ValueTask SaveAsync(GameAccess access)
    {
        try { await javascript.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(access, GameJson.Options)); }
        catch (JSException exception) { throw new BrowserStorageException(exception); }
    }

    public async ValueTask<Guid> EntryRequestIdAsync(BrowserEntry entry)
    {
        try
        {
            var key = EntryKey(entry);
            var saved = await javascript.InvokeAsync<string?>("localStorage.getItem", key);
            if (Guid.TryParse(saved, out var requestId) && requestId != Guid.Empty) return requestId;
            requestId = Guid.NewGuid();
            await javascript.InvokeVoidAsync("localStorage.setItem", key, requestId.ToString());
            return requestId;
        }
        catch (JSException exception) { throw new BrowserStorageException(exception); }
    }

    public async ValueTask CompleteEntryAsync(BrowserEntry entry)
    {
        try { await javascript.InvokeVoidAsync("localStorage.removeItem", EntryKey(entry)); }
        catch (JSException exception) { throw new BrowserStorageException(exception); }
    }

    private static string EntryKey(BrowserEntry entry) => entry switch
    {
        BrowserEntry.PrivateGame => "chess.web.pending.create.v1",
        BrowserEntry.Matchmaking => "chess.web.pending.matchmaking.v1",
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };

    public async ValueTask ForgetAsync()
    {
        try { await javascript.InvokeVoidAsync("localStorage.removeItem", Key); }
        catch (JSException exception) { throw new BrowserStorageException(exception); }
    }
}

public enum BrowserEntry { PrivateGame, Matchmaking }

public sealed class BrowserStorageException(Exception innerException)
    : Exception("Browser storage is unavailable.", innerException);
