using Chess.Client;
using Chess.Contracts;
using Chess.Native.Testing;

namespace Chess.Linux;

internal static class Program
{
    private static int Main(string[] args)
    {
        var application = Gtk.Application.New("org.shirubasoft.chess", Gio.ApplicationFlags.FlagsNone);
        application.OnActivate += (_, _) => new ChessWindow(application, args).Show();
        var result = application.RunWithSynchronizationContext(null);
        return Environment.ExitCode == 0 ? result : Environment.ExitCode;
    }
}

public sealed partial class ChessWindow
{
    private readonly Gtk.ApplicationWindow window;
    private readonly GameSession session;
    private readonly Gtk.Entry server = Gtk.Entry.New();
    private readonly Gtk.Entry code = Gtk.Entry.New();
    private readonly Gtk.Entry notation = Gtk.Entry.New();
    private readonly Gtk.Label status = Gtk.Label.New("Ready to play");
    private readonly Gtk.Label opponent = Gtk.Label.New("No opponent yet");
    private readonly Gtk.Label access = Gtk.Label.New("");
    private readonly Gtk.Label message = Gtk.Label.New("");
    private readonly Gtk.Label history = Gtk.Label.New("No moves yet.");
    private readonly Gtk.Grid board = Gtk.Grid.New();
    private readonly Gtk.Box promotions = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
    private readonly Dictionary<string, Gtk.Button> squares = [];
    private readonly List<Gtk.Widget> commands = [];
    private readonly Dictionary<string, Gtk.Button> namedCommands = [];

    public ChessWindow(Gtk.Application application, string[] args)
    {
        var settingsPath = Environment.GetEnvironmentVariable("CHESS_SETTINGS_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chess.Linux", "session");
        session = new GameSession("Chess Linux", settingsPath, Environment.GetEnvironmentVariable("CHESS_SERVER") ?? "http://localhost:5080/");
        window = Gtk.ApplicationWindow.New(application);
        window.Title = "Chess";
        window.SetDefaultSize(940, 800);
        window.OnCloseRequest += (_, _) => { session.Dispose(); return false; };
        var content = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        content.SetMarginTop(20); content.SetMarginBottom(20); content.SetMarginStart(20); content.SetMarginEnd(20);
        window.SetChild(content);

        var title = Gtk.Label.New("Chess"); title.AddCssClass("title-1"); title.SetHalign(Gtk.Align.Start); content.Append(title);
        content.Append(Field("Server", server, "server-address")); ((Gtk.Editable)server).SetText(session.Server);
        var joining = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        joining.Append(Action("Create game", "create-game", () => session.CreateAsync(((Gtk.Editable)server).GetText())));
        joining.Append(Action("Find opponent", "random-game", () => session.MatchmakeAsync(((Gtk.Editable)server).GetText())));
        code.PlaceholderText = "Your side's game code"; code.Name = "game-code"; code.Hexpand = true; ((Gtk.Editable)code).SetText(session.SavedCode);
        joining.Append(code); joining.Append(Action("Join / resume", "join-game", () => session.JoinAsync(((Gtk.Editable)server).GetText(), ((Gtk.Editable)code).GetText())));
        content.Append(joining);
        access.Selectable = true; access.Wrap = true; access.SetHalign(Gtk.Align.Start); access.Name = "access-codes"; content.Append(access);

        var play = Gtk.Box.New(Gtk.Orientation.Horizontal, 24); play.Vexpand = true; content.Append(play);
        var left = Gtk.Box.New(Gtk.Orientation.Vertical, 8); left.Hexpand = true; play.Append(left);
        status.Name = "game-status"; status.SetHalign(Gtk.Align.Start); status.AddCssClass("title-3"); left.Append(status);
        opponent.Name = "opponent-client"; opponent.SetHalign(Gtk.Align.Start); left.Append(opponent);
        board.RowHomogeneous = true; board.ColumnHomogeneous = true; board.Hexpand = true; board.Vexpand = true;
        var boardFrame = Gtk.AspectFrame.New(0.5f, 0.5f, 1, false); boardFrame.SetChild(ConfigureBoardDragging()); boardFrame.Vexpand = true; left.Append(boardFrame);
        var promotionLabel = Gtk.Label.New("Promote to"); promotions.Append(promotionLabel);
        foreach (var (label, piece) in new[] { ("Queen", 'q'), ("Rook", 'r'), ("Bishop", 'b'), ("Knight", 'n') })
            promotions.Append(Action(label, "promote-" + piece, () => session.PromoteAsync(piece)));
        left.Append(promotions);

        var right = Gtk.Box.New(Gtk.Orientation.Vertical, 10); right.SetSizeRequest(230, -1); play.Append(right);
        var movesTitle = Gtk.Label.New("Moves"); movesTitle.SetHalign(Gtk.Align.Start); movesTitle.AddCssClass("title-3"); right.Append(movesTitle);
        history.Wrap = true; history.SetHalign(Gtk.Align.Start); history.SetValign(Gtk.Align.Start); history.Name = "move-history";
        var scroll = Gtk.ScrolledWindow.New(); scroll.Vexpand = true; scroll.SetChild(history); right.Append(scroll);
        right.Append(Field("Move in SAN or UCI", notation, "move-notation")); notation.PlaceholderText = "e4 or e2e4";
        right.Append(Action("Play move", "play-move", async () => { if (await session.MoveAsync(((Gtk.Editable)notation).GetText())) ((Gtk.Editable)notation).SetText(""); }));
        right.Append(Action("Refresh", "refresh-game", session.RefreshAsync));
        foreach (var (label, id, action) in new[]
        {
            ("Offer draw", "offer-draw", GameAction.OfferDraw), ("Accept draw", "accept-draw", GameAction.AcceptDraw),
            ("Decline draw", "decline-draw", GameAction.DeclineDraw), ("Claim repetition", "claim-repetition", GameAction.ClaimThreefold),
            ("Claim 50 moves", "claim-fifty", GameAction.ClaimFiftyMoves), ("Resign", "resign-game", GameAction.Resign)
        }) right.Append(Action(label, id, () => session.ActAsync(action)));
        message.Wrap = true; message.SetHalign(Gtk.Align.Start); message.Name = "operation-message"; content.Append(message);
        var css = Gtk.CssProvider.New();
        css.LoadFromString(".chess-square { min-width: 38px; min-height: 38px; padding: 0; border-radius: 0; border: 0; font-family: 'DejaVu Sans'; font-size: 32px; color: #191b17; } .chess-light { background: #e8e5d6; } .chess-dark { background: #8c9d83; } .chess-selected { background: #ddbb65; } .chess-destination { box-shadow: inset 0 0 0 4px #315e43; } .chess-square:focus-visible { outline: 3px solid #153e7e; outline-offset: -3px; }");
        Gtk.StyleContext.AddProviderForDisplay(window.GetDisplay(), css, 600);
        session.Changed += Render;
        Render();
        _ = session.PollAsync();
        if (args.Contains("--resume", StringComparer.Ordinal) && session.SavedCode.Length != 0)
            _ = session.JoinAsync(session.Server, session.SavedCode);
        var recording = Array.IndexOf(args, "--record-gameplay");
        var verification = recording >= 0 ? recording : Array.IndexOf(args, "--verify-ui");
        if (verification >= 0 && verification + 1 < args.Length)
        {
            window.OnMap += async (_, _) =>
            {
                var success = await NativeUiVerification.RunAsync(session,
                    (id, text) => ((Gtk.Editable)(id == "game-code" ? code : notation)).SetText(text),
                    id => { if (id.StartsWith("square-", StringComparison.Ordinal)) squares[id[7..]].Activate(); else namedCommands[id].Activate(); },
                    id => id switch
                    {
                        "access-codes" => access.GetText(), "opponent-client" => opponent.GetText(), "move-history" => history.GetText(),
                        _ => squares[id[7..]].TooltipText ?? ""
                    }, args[verification + 1], DragInputAsync, recording >= 0);
                Environment.ExitCode = success ? 0 : 1;
                window.Close();
            };
        }
    }

    public void Show() => window.Present();

    private Gtk.Button Action(string label, string id, Func<Task> action)
    {
        var button = Gtk.Button.NewWithLabel(label); button.Name = id;
        button.OnClicked += async (_, _) => await action();
        commands.Add(button);
        namedCommands.Add(id, button);
        return button;
    }

    private static Gtk.Widget Field(string label, Gtk.Entry entry, string id)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var caption = Gtk.Label.New(label); box.Append(caption);
        entry.Name = id; entry.Hexpand = true; box.Append(entry);
        return box;
    }

    private void Render()
    {
        if (boardDrag is { } drag && !session.IsCurrentDrag(drag)) CancelBoardDrag();
        status.SetText(session.Status); opponent.SetText($"Opponent: {session.Opponent}");
        message.SetText(session.Message); history.SetText(session.History);
        access.SetText(session.Access is { } a
            ? $"You play {a.Side} · Your code: {a.Code}" + (a.OpponentCode is { } invite ? $"\nOpponent code: {invite}" : "")
                + $"\nGame {a.GameId} · Revision {session.Snapshot?.Revision}"
            : "Your code restores your side on any client.");
        foreach (var command in commands) command.Sensitive = !session.IsBusy;
        foreach (var (id, action) in new[] { ("play-move", GameAction.Move), ("offer-draw", GameAction.OfferDraw),
            ("accept-draw", GameAction.AcceptDraw), ("decline-draw", GameAction.DeclineDraw), ("claim-repetition", GameAction.ClaimThreefold),
            ("claim-fifty", GameAction.ClaimFiftyMoves), ("resign-game", GameAction.Resign) }) namedCommands[id].Sensitive = session.CanAct(action);
        promotions.Visible = session.PromotionMove is not null;
        var rendered = BoardPresentation.Squares(session.Snapshot, session.Access?.Side ?? PlayerSide.White).ToArray();
        if (squares.Count == 0 || squares.Keys.First() != rendered[0].Name)
        {
            foreach (var button in squares.Values) board.Remove(button);
            squares.Clear();
            for (var index = 0; index < rendered.Length; index++)
            {
                var square = rendered[index]; var button = Gtk.Button.New();
                button.Name = "square-" + square.Name; button.AddCssClass("chess-square");
                button.OnClicked += async (_, _) => await session.SelectSquareAsync(square.Name);
                board.Attach(button, index % 8, index / 8, 1, 1); squares.Add(square.Name, button);
            }
        }
        foreach (var square in rendered)
        {
            var button = squares[square.Name]; button.Label = square.Symbol.Length == 0 ? " " : square.Symbol;
            button.TooltipText = square.Description; button.Sensitive = session.CanMove;
            foreach (var name in new[] { "chess-light", "chess-dark", "chess-selected", "chess-destination" }) button.RemoveCssClass(name);
            button.AddCssClass(square.IsLight ? "chess-light" : "chess-dark");
            if (session.SelectedSquare == square.Name) button.AddCssClass("chess-selected");
            if (session.IsDestination(square.Name)) button.AddCssClass("chess-destination");
        }
    }
}
