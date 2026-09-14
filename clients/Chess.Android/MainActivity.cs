using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Chess.Client;
using Chess.Contracts;
using Chess.Native.Testing;

namespace Chess.Android;

[Activity(Name = "org.shirubasoft.chess.MainActivity", Label = "Chess", MainLauncher = true, Exported = true, ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize)]
public sealed partial class MainActivity : Activity
{
    private GameSession session = null!;
    private EditText server = null!;
    private EditText code = null!;
    private EditText notation = null!;
    private TextView status = null!;
    private TextView opponent = null!;
    private TextView access = null!;
    private TextView message = null!;
    private TextView history = null!;
    private LinearLayout board = null!;
    private LinearLayout promotions = null!;
    private LinearLayout lobby = null!;
    private Button connectionToggle = null!;
    private Guid? renderedGame;
    private readonly Dictionary<string, Button> squares = [];
    private readonly List<Button> commands = [];
    private readonly Dictionary<string, Button> namedCommands = [];
    private CancellationTokenSource? polling;
    private bool verificationStarted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        session = new GameSession("Chess Android", System.IO.Path.Combine(FilesDir!.AbsolutePath, (Intent?.GetBooleanExtra("verify_ui", false) == true || Intent?.GetBooleanExtra("record_gameplay", false) == true) ? "verification-session" : "session"),
            Intent?.GetStringExtra("server") ?? "http://10.0.2.2:5080/");
        var scroll = new ScrollView(this); var content = Vertical(); content.SetPadding(Dp(16), Dp(20), Dp(16), Dp(20));
        scroll.AddView(content); SetContentView(scroll);
        var heading = Horizontal(); var title = Label("Chess", 26); title.SetTypeface(null, TypefaceStyle.Bold); heading.AddView(title, Weight());
        connectionToggle = new Button(this) { Text = "Game & connection", TextSize = 13 }; connectionToggle.SetAllCaps(false); Identify(connectionToggle, "game_details");
        connectionToggle.Click += (_, _) => lobby.Visibility = lobby.Visibility == ViewStates.Gone ? ViewStates.Visible : ViewStates.Gone;
        heading.AddView(connectionToggle); content.AddView(heading);
        lobby = Vertical(); content.AddView(lobby);
        server = Input("Server address", "server_address", session.Server); server.InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextVariationUri;
        lobby.AddView(server);
        var joining = Horizontal(); joining.AddView(Action("Create game", "create_game", () => session.CreateAsync(server.Text ?? "")), Weight());
        joining.AddView(Action("Find opponent", "random_game", () => session.MatchmakeAsync(server.Text ?? "")), Weight()); lobby.AddView(joining);
        code = Input("Your side's game code", "game_code", session.SavedCode); lobby.AddView(code);
        lobby.AddView(Action("Join / resume", "join_game", () => session.JoinAsync(server.Text ?? "", code.Text ?? "")));
        access = Label(""); access.SetTextIsSelectable(true); Identify(access, "access_codes"); lobby.AddView(access);
        status = Label("Ready to play", 21); status.SetTypeface(null, TypefaceStyle.Bold); Identify(status, "game_status"); content.AddView(status);
        opponent = Label("No opponent yet"); Identify(opponent, "opponent_client"); content.AddView(opponent);
        board = new SquareBoardLayout(this) { Orientation = Orientation.Vertical }; content.AddView(board, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        promotions = Horizontal();
        foreach (var (label, piece) in new[] { ("Queen", 'q'), ("Rook", 'r'), ("Bishop", 'b'), ("Knight", 'n') })
            promotions.AddView(Action(label, "promote_" + piece, () => session.PromoteAsync(piece)), Weight());
        content.AddView(promotions);
        notation = Input("SAN or UCI move: e4 or e2e4", "move_notation", ""); content.AddView(notation);
        var moving = Horizontal(); moving.AddView(Action("Play move", "play_move", async () => { if (await session.MoveAsync(notation.Text ?? "")) notation.Text = ""; }), Weight());
        moving.AddView(Action("Refresh", "refresh_game", session.RefreshAsync), Weight()); content.AddView(moving);
        message = Label(""); Identify(message, "operation_message"); message.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite; content.AddView(message);
        content.AddView(Label("Moves", 20)); history = Label("No moves yet."); Identify(history, "move_history"); history.SetTextIsSelectable(true); content.AddView(history);
        foreach (var group in new[]
        {
            new[] { ("Offer draw", "offer_draw", GameAction.OfferDraw), ("Accept draw", "accept_draw", GameAction.AcceptDraw), ("Decline draw", "decline_draw", GameAction.DeclineDraw) },
            new[] { ("Claim repetition", "claim_repetition", GameAction.ClaimThreefold), ("Claim 50 moves", "claim_fifty", GameAction.ClaimFiftyMoves), ("Resign", "resign_game", GameAction.Resign) }
        })
        {
            var actions = Horizontal();
            foreach (var (label, id, action) in group) actions.AddView(Action(label, id, () => session.ActAsync(action)), Weight());
            content.AddView(actions);
        }
        session.Changed += Changed;
        Render();
    }

    protected override void OnStart()
    {
        base.OnStart(); polling = new CancellationTokenSource(); _ = session.PollAsync(polling.Token);
        if (session.Access is not null) _ = session.RefreshAsync();
        if (!verificationStarted && (Intent?.GetBooleanExtra("verify_ui", false) == true || Intent?.GetBooleanExtra("record_gameplay", false) == true))
        {
            verificationStarted = true;
            _ = VerifyAsync();
        }
    }

    private async Task VerifyAsync()
    {
        var path = System.IO.Path.Combine(FilesDir!.AbsolutePath, "verification.json");
        var success = await NativeUiVerification.RunAsync(session, (id, text) => { if (id == "game-code") code.Text = text; else notation.Text = text; },
            id =>
            {
                if (id is "join-game" or "create-game" && lobby.Visibility == ViewStates.Gone) connectionToggle.PerformClick();
                if (id.StartsWith("square-", StringComparison.Ordinal)) squares[id[7..]].PerformClick(); else namedCommands[id.Replace('-', '_')].PerformClick();
            },
            id => id switch
            {
                "access-codes" => access.Text ?? "", "opponent-client" => opponent.Text ?? "", "move-history" => history.Text ?? "",
                _ => squares[id[7..]].ContentDescription ?? ""
            }, path, DragInputAsync, Intent?.GetBooleanExtra("record_gameplay", false) == true);
        global::Android.Util.Log.Info("ChessVerification", success ? "PASS" : "FAIL");
    }

    protected override void OnStop() { CancelBoardDrag(); touchSource = null; polling?.Cancel(); polling?.Dispose(); polling = null; base.OnStop(); }
    protected override void OnDestroy() { session.Changed -= Changed; session.Dispose(); base.OnDestroy(); }
    private void Changed() => RunOnUiThread(Render);

    private Button Action(string label, string id, Func<Task> action)
    {
        var button = new Button(this) { Text = label, TextSize = 13 }; button.SetAllCaps(false); Identify(button, id);
        button.Click += async (_, _) => await action(); commands.Add(button); namedCommands.Add(id, button); return button;
    }

    private EditText Input(string hint, string id, string value)
    {
        var input = new EditText(this) { Hint = hint, Text = value, TextSize = 15 }; input.SetSingleLine(true); Identify(input, id);
        input.ContentDescription = hint; return input;
    }

    private TextView Label(string text, int size = 15)
    {
        var label = new TextView(this) { Text = text, TextSize = size }; label.SetTextColor(Color.Rgb(25, 27, 23)); label.SetPadding(0, Dp(6), 0, Dp(6)); return label;
    }

    private LinearLayout Vertical() => new(this) { Orientation = Orientation.Vertical };
    private LinearLayout Horizontal() => new(this) { Orientation = Orientation.Horizontal };
    private static LinearLayout.LayoutParams Weight() => new(0, ViewGroup.LayoutParams.WrapContent, 1);
    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
    private void Identify(View view, string id)
    {
        view.Tag = id;
        view.Id = Resources!.GetIdentifier(id, "id", PackageName);
    }

    private void Render()
    {
        if (IsDestroyed) return;
        if (boardDrag is { } drag && !session.IsCurrentDrag(drag)) CancelBoardDrag();
        status.Text = session.Status; opponent.Text = $"Opponent: {session.Opponent}"; history.Text = session.History; message.Text = session.Message;
        access.Text = session.Access is { } a ? $"You play {a.Side}\nYour code: {a.Code}"
            + (a.OpponentCode is { } invite ? $"\nOpponent code: {invite}" : "") + $"\nRevision {session.Snapshot?.Revision}"
            : "Keep your side's code to resume on any client.";
        foreach (var command in commands) command.Enabled = !session.IsBusy;
        foreach (var (id, action) in ActionAvailability()) namedCommands[id].Enabled = session.CanAct(action);
        if (session.Access is { } accessState && renderedGame != accessState.GameId)
        {
            renderedGame = accessState.GameId;
            lobby.Visibility = ViewStates.Gone;
        }
        connectionToggle.Visibility = session.Access is null ? ViewStates.Gone : ViewStates.Visible;
        promotions.Visibility = session.PromotionMove is null ? ViewStates.Gone : ViewStates.Visible;
        var rendered = BoardPresentation.Squares(session.Snapshot, session.Access?.Side ?? PlayerSide.White).ToArray();
        if (squares.Count == 0 || squares.Keys.First() != rendered[0].Name)
        {
            squares.Clear(); board.RemoveAllViews();
            for (var row = 0; row < 8; row++)
            {
                var line = Horizontal(); board.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
                for (var column = 0; column < 8; column++)
                {
                    var square = rendered[row * 8 + column]; var button = new Button(this) { TextSize = 28 };
                    button.SetPadding(0, 0, 0, 0); button.SetMinimumWidth(0); button.SetMinimumHeight(0); button.SetAllCaps(false);
                    Identify(button, "square_" + square.Name); button.Click += async (_, _) => await session.SelectSquareAsync(square.Name);
                    ConfigureSquareDragging(button, square.Name);
                    line.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1)); squares.Add(square.Name, button);
                }
            }
        }
        foreach (var square in rendered)
        {
            var button = squares[square.Name]; button.Text = square.Symbol; button.ContentDescription = square.Description; button.Enabled = session.CanMove;
            button.SetTextColor(Color.Rgb(25, 27, 23));
            var background = new GradientDrawable(); background.SetColor(session.SelectedSquare == square.Name ? Color.Rgb(221, 187, 101)
                : square.IsLight ? Color.Rgb(232, 229, 214) : Color.Rgb(140, 157, 131));
            if (session.IsDestination(square.Name)) background.SetStroke(Dp(3), Color.Rgb(49, 94, 67));
            button.Background = background;
        }
    }

    private static IEnumerable<(string, GameAction)> ActionAvailability()
    {
        yield return ("play_move", GameAction.Move); yield return ("offer_draw", GameAction.OfferDraw);
        yield return ("accept_draw", GameAction.AcceptDraw); yield return ("decline_draw", GameAction.DeclineDraw);
        yield return ("claim_repetition", GameAction.ClaimThreefold); yield return ("claim_fifty", GameAction.ClaimFiftyMoves);
        yield return ("resign_game", GameAction.Resign);
    }
}

internal sealed class SquareBoardLayout(Context context) : LinearLayout(context)
{
    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        base.OnMeasure(widthMeasureSpec, MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly));
    }
}
