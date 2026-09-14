using System.Net.WebSockets;
using System.Text.Json;
using Chess.Client;
using Chess.Contracts;
using Chess.Web.Board;
using Chess.Web.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using BoardPresentation = Chess.Web.Board.BoardPresentation;
using BoardSquare = Chess.Web.Board.BoardSquare;

namespace Chess.Web;

public partial class App
{
    [Inject] private ChessClient Client { get; set; } = null!;
    [Inject] private BrowserGameStore Store { get; set; } = null!;
    [Inject] private IJSRuntime Javascript { get; set; } = null!;

    private GameAccess? _access;
    private GameSnapshot? _snapshot;
    private BoardSquare[] _squares = BoardPresentation.Squares(Position.Initial, PlayerSide.White).ToArray();
    private string[] _legalMoves = [];
    private HashSet<string> _targets = [];
    private string[] _promotionMoves = [];
    private string? _selected;
    private string? _error;
    private string? _toast;
    private string _joinCode = "";
    private string _move = "";
    private MoveNotation _notation = MoveNotation.San;
    private bool _busy;
    private bool _flipped;
    private bool _showCode;
    private bool _confirmResign;
    private bool _connected;
    private string _connectionText = "Checking for moves";
    private UpdateTransport _transport;
    private CancellationTokenSource? _watchCancellation;
    private Task? _watchTask;
    private readonly CancellationTokenSource _lifetime = new();

    private enum UpdateTransport { Polling, Sse, WebSocket }
    private PlayerSide YourSide => _access?.Side ?? PlayerSide.White;
    private PlayerSide OpponentSide => YourSide == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;
    private PlayerSide Orientation => _flipped ? OpponentSide : YourSide;
    private string YourName => _snapshot is null ? "You" : (YourSide == PlayerSide.White ? _snapshot.White.ClientName : _snapshot.Black.ClientName) ?? "Chess Web";
    private string OpponentName => _snapshot is null ? "An open seat" : (OpponentSide == PlayerSide.White ? _snapshot.White.ClientName : _snapshot.Black.ClientName) ?? "An open seat";
    private bool CanPlay => _snapshot is { Status: GameStatus.Active } && _snapshot.SideToMove == YourSide;
    private string HeadingDescription => _snapshot is null
        ? "A shared game, wherever you play."
        : _snapshot.Status == GameStatus.Finished ? "Every game has another beginning."
        : _snapshot.Status == GameStatus.Waiting ? "Your board is ready. Your opponent can join from any client."
        : $"You are playing {YourSide.ToString().ToLowerInvariant()}. No clock, no rush.";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (await Store.LoadAsync() is { } access)
            {
                _access = access;
                ApplySnapshot(access.Snapshot);
                try { await UpdateAsync(await Client.GetAsync(access, _lifetime.Token)); }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    _error = "Your saved game is shown. The server is unavailable; refresh when you are connected.";
                }
                StartWatching();
            }
        }
        catch (BrowserStorageException)
        {
            _error = "This browser cannot save game access. Keep your side code to resume later.";
        }
    }

    private Task CreateAsync() => OpenAsync(async () => await Client.CreateAsync(
        requestId: await Store.EntryRequestIdAsync(BrowserEntry.PrivateGame), cancellationToken: _lifetime.Token), BrowserEntry.PrivateGame);
    private Task MatchmakeAsync() => OpenAsync(async () => await Client.MatchmakeAsync(
        requestId: await Store.EntryRequestIdAsync(BrowserEntry.Matchmaking), cancellationToken: _lifetime.Token), BrowserEntry.Matchmaking);
    private Task JoinAsync() => OpenAsync(() => Client.JoinAsync(_joinCode.Trim(), _lifetime.Token));

    private async Task OpenAsync(Func<Task<GameAccess>> open, BrowserEntry? entry = null)
    {
        await RunAsync(async () =>
        {
            var access = await open();
            await StopWatchingAsync();
            try { await Store.SaveAsync(access); }
            catch
            {
                StartWatching();
                throw;
            }
            _access = access;
            _snapshot = null;
            _flipped = false;
            _confirmResign = false;
            _showCode = false;
            _move = "";
            _joinCode = "";
            ApplySnapshot(access.Snapshot);
            if (entry is { } completed)
            {
                try { await Store.CompleteEntryAsync(completed); }
                catch (BrowserStorageException)
                {
                    // Access is durable. Retaining the request ID keeps any later retry idempotent.
                    _error = "Your game is saved, but browser storage could not finish updating. Keep your side code.";
                }
            }
            StartWatching();
        });
    }

    private Task PlayNotationAsync() => PlayAsync(_move.Trim(), _notation);

    private async Task PlayAsync(string move, MoveNotation notation)
    {
        if (_access is null || _snapshot is null || !CanPlay) return;
        await RunAsync(async () =>
        {
            await UpdateAsync(await Client.MoveAsync(_access, _snapshot, move, notation, cancellationToken: _lifetime.Token));
            _move = "";
            ClearSelection();
        });
    }

    private async Task CommandAsync(GameAction action)
    {
        if (_access is null || _snapshot is null) return;
        await RunAsync(async () =>
        {
            await UpdateAsync(await Client.CommandAsync(_access, new GameCommandRequest
            {
                RequestId = Guid.NewGuid(), ExpectedRevision = _snapshot.Revision, Action = action
            }, _lifetime.Token));
            _confirmResign = false;
        });
    }

    private Task RefreshAsync() => RunAsync(async () =>
    {
        if (_access is null) return;
        await UpdateAsync(await Client.GetAsync(_access, _lifetime.Token));
        _connected = true;
        _connectionText = "Up to date";
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        _error = null;
        try { await action(); }
        catch (ChessServerException exception)
        {
            _error = exception.Error.Message;
            if (exception.Error.CurrentRevision is not null && _access is { } access)
            {
                try { await UpdateAsync(await Client.GetAsync(access, _lifetime.Token)); }
                catch (Exception refreshException) when (IsRecoverable(refreshException)) { }
            }
        }
        catch (BrowserStorageException)
        {
            _error = "This browser could not save game access. Check site storage and retry.";
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            _error = "Could not reach the game server. Check your connection, then refresh or retry.";
            _connected = false;
        }
        finally { _busy = false; }
    }

    private async Task SelectSquareAsync(BoardSquare square)
    {
        if (!CanPlay || _busy) return;
        if (_selected is { } selected)
        {
            var moves = BoardPresentation.MovesBetween(_legalMoves, selected, square.Name);
            if (moves.Length == 1) { await PlayAsync(moves[0], MoveNotation.Uci); return; }
            if (moves.Length > 1) { _promotionMoves = moves; return; }
        }
        if (square.Side == YourSide && square.Name != _selected)
        {
            _selected = square.Name;
            _targets = BoardPresentation.Destinations(_legalMoves, square.Name).ToHashSet(StringComparer.Ordinal);
            _promotionMoves = [];
        }
        else ClearSelection();
    }

    private void ClearSelection()
    {
        _selected = null;
        _targets.Clear();
        _promotionMoves = [];
    }

    private void ApplySnapshot(GameSnapshot snapshot)
    {
        if (_access is null || snapshot.GameId != _access.GameId || (_snapshot is not null && snapshot.Revision < _snapshot.Revision)) return;
        var changedPosition = _snapshot?.Fen != snapshot.Fen;
        _snapshot = snapshot;
        _access = _access with { Snapshot = snapshot };
        _legalMoves = BoardPresentation.LegalMoves(snapshot);
        _squares = BoardPresentation.Squares(ChessClient.PositionOf(snapshot), Orientation).ToArray();
        if (changedPosition || !CanPlay) ClearSelection();
    }

    private async Task UpdateAsync(GameSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
        if (_access is null) return;
        try { await Store.SaveAsync(_access); }
        catch (BrowserStorageException) { _error = "Your move is saved on the server. This browser could not save your code; copy it before leaving."; }
    }

    private void StartWatching()
    {
        if (_access is null) return;
        _watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _watchTask = WatchAsync(_access, _transport, _watchCancellation.Token);
    }

    private async Task WatchAsync(GameAccess access, UpdateTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (transport == UpdateTransport.Polling)
                {
                    await ReceiveAsync(await Client.GetAsync(access, cancellationToken));
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                else
                {
                    var revision = _snapshot?.Revision ?? -1;
                    var updates = transport == UpdateTransport.Sse
                        ? Client.WatchSseAsync(access, revision, cancellationToken)
                        : Client.WatchWebSocketAsync(access, revision, cancellationToken);
                    await foreach (var snapshot in updates) await ReceiveAsync(snapshot);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                await InvokeAsync(() => { _connected = false; _connectionText = "Reconnecting"; StateHasChanged(); });
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    await ReceiveAsync(await Client.GetAsync(access, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception retryException) when (IsRecoverable(retryException)) { }
            }
        }
        return;

        Task ReceiveAsync(GameSnapshot snapshot) => InvokeAsync(async () =>
        {
            if (cancellationToken.IsCancellationRequested || snapshot.GameId != _access?.GameId) return;
            await UpdateAsync(snapshot);
            _connected = true;
            _connectionText = transport == UpdateTransport.Polling ? "Up to date · polling" : $"Live · {transport}";
            StateHasChanged();
        });
    }

    private async Task StopWatchingAsync()
    {
        if (_watchCancellation is null) return;
        await _watchCancellation.CancelAsync();
        if (_watchTask is not null) await _watchTask;
        _watchCancellation.Dispose();
        _watchCancellation = null;
        _watchTask = null;
    }

    private async Task ChangeTransportAsync(ChangeEventArgs args)
    {
        if (!Enum.TryParse<UpdateTransport>(args.Value?.ToString(), out var transport)) return;
        await StopWatchingAsync();
        _transport = transport;
        _connectionText = "Connecting";
        StartWatching();
    }

    private async Task ForgetAsync()
    {
        await StopWatchingAsync();
        try { await Store.ForgetAsync(); }
        catch (BrowserStorageException) { _error = "The browser could not remove saved access. Clear this site's storage to remove it."; }
        _access = null;
        _snapshot = null;
        _flipped = false;
        _squares = BoardPresentation.Squares(Position.Initial, PlayerSide.White).ToArray();
        ClearSelection();
    }

    private void FlipBoard()
    {
        _flipped = !_flipped;
        _squares = BoardPresentation.Squares(_snapshot is null ? Position.Initial : ChessClient.PositionOf(_snapshot), Orientation).ToArray();
    }

    private async Task CopyAsync(string code)
    {
        try { await Javascript.InvokeVoidAsync("navigator.clipboard.writeText", code); _toast = "Code copied"; }
        catch (JSException) { _toast = "Select the code and copy it manually."; }
        await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token);
        _toast = null;
    }

    private string SquareClass(BoardSquare square)
    {
        var last = _snapshot?.Moves.LastOrDefault()?.Uci;
        var wasLastMove = last is { Length: >= 4 } && (last.StartsWith(square.Name, StringComparison.Ordinal) || last.AsSpan(2, 2).SequenceEqual(square.Name));
        return $"square {(square.Dark ? "dark" : "light")} {(_selected == square.Name ? "selected" : "")} {(wasLastMove ? "last-move" : "")}";
    }

    private bool ShowRank(BoardSquare square) => square.Name[0] == (Orientation == PlayerSide.White ? 'a' : 'h');
    private bool ShowFile(BoardSquare square) => square.Name[1] == (Orientation == PlayerSide.White ? '1' : '8');
    private static string PromotionName(char piece) => piece switch { 'q' => "queen", 'r' => "rook", 'b' => "bishop", 'n' => "knight", _ => throw new ArgumentOutOfRangeException(nameof(piece)) };
    private static string Humanize(string name) => string.Concat(name.Select((letter, index) => index > 0 && char.IsUpper(letter) ? " " + char.ToLowerInvariant(letter) : letter.ToString()));
    private static bool IsRecoverable(Exception exception) => exception is HttpRequestException or ChessServerException or JsonException or WebSocketException or OperationCanceledException;

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        await StopWatchingAsync();
        _lifetime.Dispose();
    }
}
