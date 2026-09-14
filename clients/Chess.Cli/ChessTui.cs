using System.Text;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Chess.Client;
using Chess.Contracts;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Chess.Cli;

public static class ChessTui
{
    public static async Task<int> RunAsync(CliContext context, CancellationToken cancellationToken)
    {
        string? initialError = null;
        if (context.Session.Access is { } access)
        {
            try { await context.SaveAsync(access with { Snapshot = await context.Client.GetAsync(access, cancellationToken) }, cancellationToken); }
            catch (Exception exception) when (exception is ChessServerException or HttpRequestException) { initialError = exception.Message; }
        }
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            try
            {
                using var app = Application.Create().Init();
                using var window = new ChessWindow(context, app, cancellationToken);
                using var registration = cancellationToken.Register(() => app.Invoke(() => app.RequestStop()));
                if (initialError is not null) window.SetStatus(initialError);
                app.Run(window);
                completion.SetResult(cancellationToken.IsCancellationRequested ? 130 : 0);
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { Name = "Chess terminal UI" };
        uiThread.Start();
        return await completion.Task;
    }
}

public sealed partial class ChessWindow : Window
{
    private readonly CliContext context;
    private readonly IApplication application;
    private readonly CancellationTokenSource lifetime;
    private readonly Label game = new() { X = 0, Y = 3, Width = Dim.Fill(), Height = 1 };
    private readonly Label players = new() { X = 0, Y = 4, Width = Dim.Fill(), Height = 1 };
    private readonly Label invitation = new() { X = 0, Y = 5, Width = Dim.Fill(), Height = 1 };
    private readonly Label board = new() { X = 0, Y = 6, Width = 32, Height = 11 };
    private readonly ListView history = new() { X = 34, Y = 7, Width = Dim.Fill(), Height = 10 };
    private readonly TextField move = new() { X = 6, Y = 17, Width = 22 };
    private readonly TextField joinCode = new() { X = 34, Y = 1, Width = 25, Secret = true };
    private readonly Label status = new() { X = 0, Y = 21, Width = Dim.Fill(), Height = 2 };
    private readonly List<Button> operations = [];
    private bool busy;

    public ChessWindow(CliContext context, IApplication application, CancellationToken cancellationToken)
    {
        this.context = context;
        this.application = application;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Title = "Chess CLI | Esc to leave";
        Add(new Label { X = 0, Y = 0, Text = context.Session.Server, Width = Dim.Fill(), Height = 1 });
        AddButton("_New game", 0, 1, async token => await SaveAsync(await context.CreateAsync(cancellationToken: token), token));
        AddButton("_Random", 12, 1, async token => await SaveAsync(await context.MatchmakeAsync(cancellationToken: token), token));
        Add(new Label { Text = "Code", X = 29, Y = 1 }, joinCode);
        AddButton("_Join", 60, 1, async token =>
        {
            if (string.IsNullOrWhiteSpace(joinCode.Text)) throw new CliUsageException("Enter a side's game code to join or resume.");
            await SaveAsync(await context.Client.JoinAsync(joinCode.Text.Trim(), token), token);
            joinCode.Text = "";
        });
        Add(game, players, invitation, board, new Label { Text = "Move history", X = 34, Y = 6 }, history);
        Add(new Label { Text = "Move", X = 0, Y = 17 }, move);
        AddButton("_Play", 29, 17, PlayAsync);
        move.Accepted += (_, _) => Start(PlayAsync);
        AddButton("_Refresh", 39, 17, RefreshAsync);
        Add(new Label { Text = "SAN: Nf3, O-O, a8=Q  |  UCI: g1f3, a7a8q", X = 0, Y = 18, Width = Dim.Fill() });
        AddButton("_Offer draw", 0, 19, token => ActionAsync(GameAction.OfferDraw, token));
        AddButton("_Accept", 15, 19, token => ActionAsync(GameAction.AcceptDraw, token));
        AddButton("_Decline", 26, 19, token => ActionAsync(GameAction.DeclineDraw, token));
        AddButton("Re_sign", 38, 19, async token =>
        {
            if (MessageBox.Query(application, "Resign game", "Your opponent will win. Resign this game?", "Keep playing", "Resign") == 1)
                await ActionAsync(GameAction.Resign, token);
        });
        AddButton("Claim _threefold", 0, 20, token => ActionAsync(GameAction.ClaimThreefold, token));
        AddButton("Claim _fifty moves", 21, 20, token => ActionAsync(GameAction.ClaimFiftyMoves, token));
        Add(status);
        Draw();
        SetStatus("New, Join, or Random to play. Tab moves focus; Esc leaves.");
        _ = PollAsync();
    }

    public void SetStatus(string message) => status.Text = message;

    private void AddButton(string text, int x, int y, Func<CancellationToken, Task> operation)
    {
        var button = new Button { Text = text, X = x, Y = y, ShadowStyle = ShadowStyles.None };
        button.Accepted += (_, _) => Start(operation);
        operations.Add(button);
        Add(button);
    }

    private async void Start(Func<CancellationToken, Task> operation)
    {
        if (busy) return;
        busy = true;
        foreach (var button in operations) button.Enabled = false;
        SetStatus("Connecting...");
        try { await operation(lifetime.Token); SetStatus("Saved. You can leave and resume with this session or your side's code."); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ChessServerException or HttpRequestException or CliUsageException or IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
        { SetStatus(exception is OperationCanceledException ? "The server did not respond. Use Refresh to try again." : exception.Message); }
        finally
        {
            busy = false;
            foreach (var button in operations) button.Enabled = true;
            Draw();
        }
    }

    private async Task SaveAsync(GameAccess access, CancellationToken token)
    {
        await context.SaveAsync(access, token);
        Draw();
    }
    private GameAccess Current => context.Session.Access ?? throw new CliUsageException("Create or join a game first.");
    private async Task RefreshAsync(CancellationToken token) => await SaveAsync(Current with { Snapshot = await context.Client.GetAsync(Current, token) }, token);
    private async Task PlayAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(move.Text)) throw new CliUsageException("Enter a move, such as e4 or g1f3.");
        var access = Current;
        var text = move.Text.Trim();
        var notation = UciPattern().IsMatch(text) ? MoveNotation.Uci : MoveNotation.San;
        var snapshot = await context.MoveAsync(access, access.Snapshot, text, notation, null, token);
        await SaveAsync(access with { Snapshot = snapshot }, token);
        move.Text = "";
    }
    private async Task ActionAsync(GameAction action, CancellationToken token)
    {
        var access = Current;
        await SaveAsync(access with { Snapshot = await context.ActionAsync(access, access.Snapshot, action, null, token) }, token);
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (busy || context.Session.Access is not { } access || access.Snapshot.Status == GameStatus.Finished) continue;
                try
                {
                    var snapshot = await context.Client.GetAsync(access, lifetime.Token);
                    application.Invoke(() =>
                    {
                        if (busy || context.Session.Access is not { } current || current.GameId != access.GameId || current.Snapshot.Revision >= snapshot.Revision) return;
                        Start(token => SaveAsync(current with { Snapshot = snapshot }, token));
                    });
                }
                catch (Exception exception) when (exception is HttpRequestException or ChessServerException or JsonException or OperationCanceledException)
                {
                    if (!lifetime.IsCancellationRequested) application.Invoke(() => SetStatus("Connection interrupted. Retrying; your saved game code still works."));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private void Draw()
    {
        if (context.Session.Access is not { } access)
        {
            board.Text = RenderBoard(Chess.Position.Initial, PlayerSide.White);
            game.Text = "No current game";
            players.Text = "White: waiting  |  Black: waiting";
            invitation.Text = "Share the opponent code after creating a game.";
            history.SetSource(new ObservableCollection<string> { "Your moves will appear here." });
            return;
        }
        var snapshot = access.Snapshot;
        game.Text = $"Game {access.GameId.ToString()[..8]} | You: {access.Side} | {StatusText(snapshot)}";
        players.Text = $"White: {snapshot.White.ClientName ?? "waiting"} | Black: {snapshot.Black.ClientName ?? "waiting"}";
        invitation.Text = access.OpponentCode is { } code && snapshot.Status == GameStatus.Waiting
            ? $"Share opponent code: {code}" : snapshot.DrawOfferedBy is { } side ? $"{side} offered a draw." : "Your side's code is stored in your private session file.";
        board.Text = RenderBoard(ChessClient.PositionOf(snapshot), access.Side);
        history.SetSource(new ObservableCollection<string>(snapshot.Moves.Select(entry => $"{entry.Ply,3}. {entry.Side,-5} {entry.San}")));
    }

    public static string StatusText(GameSnapshot snapshot) => snapshot.Result is { } result
        ? result.Winner is { } winner ? $"{winner} wins: {result.Reason}" : $"Draw: {result.Reason}"
        : snapshot.Status == GameStatus.Waiting ? "Waiting for an opponent" : $"{snapshot.SideToMove} to move";

    public static string RenderBoard(Chess.Position position, PlayerSide perspective)
    {
        var fen = Chess.Notation.Fen.Format(position).Split(' ')[0];
        var ranks = fen.Split('/').Select(rank => string.Concat(rank.Select(symbol => char.IsAsciiDigit(symbol) ? new string('.', symbol - '0') : symbol.ToString()))).ToArray();
        var text = new StringBuilder("   a  b  c  d  e  f  g  h\n");
        if (perspective == PlayerSide.Black) text = new StringBuilder("   h  g  f  e  d  c  b  a\n");
        foreach (var rankIndex in Enumerable.Range(0, 8))
        {
            var row = perspective == PlayerSide.White ? rankIndex : 7 - rankIndex;
            text.Append(8 - row).Append("  ");
            var pieces = perspective == PlayerSide.White ? ranks[row] : new string(ranks[row].Reverse().ToArray());
            text.AppendJoin("  ", pieces.ToCharArray()).Append("  ").Append(8 - row).AppendLine();
        }
        text.AppendLine("White: A-Z | Black: a-z");
        return text.ToString();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    [GeneratedRegex("^[a-h][1-8][a-h][1-8][qrbn]?$")]
    private static partial Regex UciPattern();
}
