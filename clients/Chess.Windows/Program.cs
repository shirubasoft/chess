using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chess.Client;
using Chess.Contracts;
using Chess.Native.Testing;

namespace Chess.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var application = new Application();
        application.Run(new ChessWindow(args));
    }
}

public sealed class ChessWindow : Window
{
    private readonly GameSession session;
    private readonly TextBox server = new();
    private readonly TextBox code = new();
    private readonly TextBox notation = new();
    private readonly TextBlock status = new() { FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock opponent = new();
    private readonly TextBox access = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock history = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, LineHeight = 28 };
    private readonly UniformGrid board = new() { Rows = 8, Columns = 8, Width = 512, Height = 512 };
    private readonly WrapPanel promotions = new();
    private readonly Dictionary<string, Button> squares = [];
    private readonly List<Button> commands = [];
    private readonly Dictionary<string, Button> namedCommands = [];
    private readonly SolidColorBrush light = new(Color.FromRgb(232, 229, 214));
    private readonly SolidColorBrush dark = new(Color.FromRgb(140, 157, 131));
    private readonly ControlTemplate squareTemplate = CreateSquareTemplate();

    public ChessWindow(string[] args)
    {
        var settingsPath = Environment.GetEnvironmentVariable("CHESS_SETTINGS_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chess.Windows", "session");
        session = new GameSession("Chess Windows", settingsPath, Environment.GetEnvironmentVariable("CHESS_SERVER") ?? "http://localhost:5080/");
        Title = "Chess"; Width = 980; Height = 860; MinWidth = 780; MinHeight = 650;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Background = new SolidColorBrush(Color.FromRgb(247, 247, 243));
        var root = new DockPanel { Margin = new Thickness(24), LastChildFill = true }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Chess", FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        server.Text = session.Server; top.Children.Add(Field("Server", server, "server-address"));
        var joining = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        for (var i = 0; i < 4; i++) joining.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        joining.Children.Add(Action("Create game", "create-game", () => session.CreateAsync(server.Text)));
        Add(joining, Action("Find opponent", "random-game", () => session.MatchmakeAsync(server.Text)), 1);
        code.Text = session.SavedCode; Identify(code, "game-code", "Your side's game code"); code.Margin = new Thickness(8, 0, 8, 0); code.Padding = new Thickness(8);
        Add(joining, code, 2); Add(joining, Action("Join / resume", "join-game", () => session.JoinAsync(server.Text, code.Text)), 3);
        top.Children.Add(joining); Identify(access, "access-codes", "Game access codes"); top.Children.Add(access);
        message.Margin = new Thickness(0, 16, 0, 0); Identify(message, "operation-message", "Operation status"); DockPanel.SetDock(message, Dock.Bottom); root.Children.Add(message);
        var play = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        play.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        play.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); root.Children.Add(play);
        var left = new DockPanel { Margin = new Thickness(0, 0, 24, 0) }; play.Children.Add(left);
        var summary = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(summary, Dock.Top); left.Children.Add(summary);
        Identify(status, "game-status", "Game status"); summary.Children.Add(status); Identify(opponent, "opponent-client", "Opponent client"); summary.Children.Add(opponent);
        DockPanel.SetDock(promotions, Dock.Bottom); left.Children.Add(promotions);
        promotions.Children.Add(new TextBlock { Text = "Promote to", VerticalAlignment = VerticalAlignment.Center });
        foreach (var (label, piece) in new[] { ("Queen", 'q'), ("Rook", 'r'), ("Bishop", 'b'), ("Knight", 'n') })
            promotions.Children.Add(Action(label, "promote-" + piece, () => session.PromoteAsync(piece)));
        left.Children.Add(new Viewbox { Child = board, Stretch = Stretch.Uniform });
        var right = new DockPanel(); Add(play, right, 1);
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Bottom); right.Children.Add(controls);
        controls.Children.Add(Field("SAN or UCI move", notation, "move-notation"));
        controls.Children.Add(Action("Play move", "play-move", async () => { if (await session.MoveAsync(notation.Text)) notation.Clear(); }));
        controls.Children.Add(Action("Refresh", "refresh-game", session.RefreshAsync));
        foreach (var (label, id, action) in new[]
        {
            ("Offer draw", "offer-draw", GameAction.OfferDraw), ("Accept draw", "accept-draw", GameAction.AcceptDraw),
            ("Decline draw", "decline-draw", GameAction.DeclineDraw), ("Claim repetition", "claim-repetition", GameAction.ClaimThreefold),
            ("Claim 50 moves", "claim-fifty", GameAction.ClaimFiftyMoves), ("Resign", "resign-game", GameAction.Resign)
        }) controls.Children.Add(Action(label, id, () => session.ActAsync(action)));
        var moves = new StackPanel(); moves.Children.Add(new TextBlock { Text = "Moves", FontSize = 20, FontWeight = FontWeights.SemiBold });
        Identify(history, "move-history", "Move history"); moves.Children.Add(history);
        right.Children.Add(new ScrollViewer { Content = moves, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        session.Changed += () => Dispatcher.Invoke(Render);
        Closed += (_, _) => session.Dispose();
        Loaded += async (_, _) =>
        {
            _ = session.PollAsync();
            if (args.Contains("--resume", StringComparer.Ordinal) && session.SavedCode.Length != 0)
                await session.JoinAsync(session.Server, session.SavedCode);
            var verification = Array.IndexOf(args, "--verify-ui");
            if (verification >= 0 && verification + 1 < args.Length)
            {
                var success = await NativeUiVerification.RunAsync(session, (id, text) => { if (id == "game-code") code.Text = text; else notation.Text = text; },
                    id => (id.StartsWith("square-", StringComparison.Ordinal) ? squares[id[7..]] : namedCommands[id]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)),
                    id => id switch
                    {
                        "access-codes" => access.Text, "opponent-client" => opponent.Text, "move-history" => history.Text,
                        _ => AutomationProperties.GetName(squares[id[7..]])
                    }, args[verification + 1]);
                var screenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                screenshot.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
                using (var output = File.Create(Path.ChangeExtension(args[verification + 1], ".png"))) encoder.Save(output);
                Application.Current.Shutdown(success ? 0 : 1);
            }
        };
        Render();
    }

    private Button Action(string label, string id, Func<Task> action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 3, 6, 3), MinHeight = 34 };
        Identify(button, id, label); button.Click += async (_, _) => await action(); commands.Add(button); namedCommands.Add(id, button); return button;
    }

    private static FrameworkElement Field(string label, TextBox entry, string id)
    {
        var panel = new DockPanel(); var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left); panel.Children.Add(caption);
        entry.Padding = new Thickness(8, 6, 8, 6); Identify(entry, id, label); panel.Children.Add(entry); return panel;
    }

    private static void Add(Grid grid, UIElement item, int column) { Grid.SetColumn(item, column); grid.Children.Add(item); }
    private static void Identify(DependencyObject target, string id, string label)
    {
        AutomationProperties.SetAutomationId(target, id); AutomationProperties.SetName(target, label);
    }

    private static ControlTemplate CreateSquareTemplate()
    {
        // The default disabled-button chrome replaces the board colors. Keep the square's
        // background and destination border while leaving Button's focus adorner and input intact.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(SnapsToDevicePixelsProperty, true);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void Render()
    {
        status.Text = session.Status; opponent.Text = $"Opponent: {session.Opponent}"; message.Text = session.Message; history.Text = session.History;
        access.Text = session.Access is { } a ? $"You play {a.Side} · Your code: {a.Code}"
            + (a.OpponentCode is { } invite ? $"\nOpponent code: {invite}" : "") + $"\nGame {a.GameId} · Revision {session.Snapshot?.Revision}"
            : "Your code restores your side on any client.";
        foreach (var command in commands) command.IsEnabled = !session.IsBusy;
        foreach (var (id, action) in new[] { ("play-move", GameAction.Move), ("offer-draw", GameAction.OfferDraw),
            ("accept-draw", GameAction.AcceptDraw), ("decline-draw", GameAction.DeclineDraw), ("claim-repetition", GameAction.ClaimThreefold),
            ("claim-fifty", GameAction.ClaimFiftyMoves), ("resign-game", GameAction.Resign) }) namedCommands[id].IsEnabled = session.CanAct(action);
        promotions.Visibility = session.PromotionMove is null ? Visibility.Collapsed : Visibility.Visible;
        var rendered = BoardPresentation.Squares(session.Snapshot, session.Access?.Side ?? PlayerSide.White).ToArray();
        if (squares.Count == 0 || squares.Keys.First() != rendered[0].Name)
        {
            squares.Clear(); board.Children.Clear();
            foreach (var square in rendered)
            {
                var button = new Button { Template = squareTemplate, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 43, Padding = new Thickness(0), Margin = new Thickness(0) };
                button.Click += async (_, _) => await session.SelectSquareAsync(square.Name);
                squares.Add(square.Name, button); board.Children.Add(button);
            }
        }
        foreach (var square in rendered)
        {
            var button = squares[square.Name]; button.Content = square.Symbol; button.IsEnabled = session.CanMove;
            button.Foreground = Brushes.Black; button.Background = session.SelectedSquare == square.Name ? Brushes.Goldenrod : square.IsLight ? light : dark;
            button.BorderThickness = new Thickness(session.IsDestination(square.Name) ? 4 : 0); button.BorderBrush = Brushes.DarkGreen;
            Identify(button, "square-" + square.Name, square.Description); button.ToolTip = square.Description;
        }
    }
}
