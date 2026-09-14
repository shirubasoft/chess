using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;

namespace Chess.Windows;

public sealed partial class ChessWindow
{
    private StreamWriter? verificationInputLog;
    private int verificationInputEvents;
    private int verificationGesture;
    private int verificationMouseDowns;
    private string? verificationMouseDownSquare;

    private StreamWriter BeginVerificationInputLog(string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        var writer = File.CreateText(Path.ChangeExtension(reportPath, ".input.log"));
        writer.AutoFlush = true;
        verificationInputLog = writer;
        PreProcessInputEventHandler beforeInput = (_, e) =>
        {
            if (e.StagingItem.Input is MouseButtonEventArgs { ChangedButton: MouseButton.Left } button
                && button.RoutedEvent == Mouse.PreviewMouseUpEvent)
                TraceVerificationInput("preprocess-up", button);
        };
        InputManager.Current.PreProcessInput += beforeInput;
        Closed += (_, _) => InputManager.Current.PreProcessInput -= beforeInput;
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            verificationMouseDowns++;
            verificationMouseDownSquare = SquareAt(e.GetPosition(board));
            TraceVerificationInput("routed-down", e);
        }), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) => TraceVerificationInput("routed-up", e)), true);
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, e) => TraceVerificationInput("routed-move", e)), true);
        AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, e) => TraceVerificationInput("lost-capture", e)), true);
        Activated += (_, _) => TraceVerificationInput("activated");
        Deactivated += (_, _) => TraceVerificationInput("deactivated");
        TraceVerificationInput("verification-start");
        return writer;
    }

    private void TraceVerificationInput(string name, MouseEventArgs? input = null, Point? requested = null)
    {
        if (verificationInputLog is not { } writer || verificationInputEvents++ >= 512) return;
        GetCursorPos(out var cursor);
        var handle = new WindowInteropHelper(this).Handle;
        var position = input?.GetPosition(board);
        var origin = board.PointToScreen(new Point());
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, gesture = verificationGesture, name,
            requestedX = requested?.X, requestedY = requested?.Y, cursorX = cursor.X, cursorY = cursor.Y,
            foreground = GetForegroundWindow() == handle, windowAtCursor = WindowFromPoint(cursor) == handle,
            windowAtRequested = requested is { } target
                ? WindowFromPoint(new CursorPoint { X = (int)target.X, Y = (int)target.Y }) == handle : (bool?)null,
            windowLeft = Left, windowTop = Top, windowWidth = ActualWidth, windowHeight = ActualHeight,
            workArea = SystemParameters.WorkArea.ToString(), boardX = origin.X, boardY = origin.Y,
            screenX = SystemParameters.VirtualScreenLeft, screenY = SystemParameters.VirtualScreenTop,
            screenWidth = SystemParameters.VirtualScreenWidth, screenHeight = SystemParameters.VirtualScreenHeight,
            dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip,
            boardWidth = board.ActualWidth, boardHeight = board.ActualHeight,
            routedSquare = position is { } point ? SquareAt(point) : null,
            leftButton = Mouse.LeftButton.ToString(), handled = input?.Handled,
            captured = InputName(Mouse.Captured), directlyOver = InputName(Mouse.DirectlyOver),
            active = IsActive, session.CanMove, session.IsBusy, revision = session.Snapshot?.Revision,
            selected = session.SelectedSquare, dragSource = boardDrag?.Source, dragging,
            dragCurrent = boardDrag is { } drag && session.IsCurrentDrag(drag)
        }));
    }

    private void VerifyInitialBoardVisibility()
    {
        // Window geometry and WorkArea both use WPF units. Allow native pixel rounding.
        var frame = new Rect(Left, Top, ActualWidth, ActualHeight);
        frame.Inflate(-1, -1);
        if (!SystemParameters.WorkArea.Contains(frame))
            throw new InvalidOperationException("The initial chess window extends beyond the desktop work area. See the input log.");
        var handle = new WindowInteropHelper(this).Handle;
        foreach (var point in new[] { new Point(1, 1), new Point(board.ActualWidth - 1, board.ActualHeight - 1) })
        {
            // PointToScreen and WindowFromPoint both use native screen coordinates.
            var screen = board.PointToScreen(point);
            TraceVerificationInput("initial-board-corner", requested: screen);
            if (WindowFromPoint(new CursorPoint { X = (int)screen.X, Y = (int)screen.Y }) != handle)
                throw new InvalidOperationException("The initial chess board is obscured on the desktop. See the input log.");
        }
    }

    private static string? InputName(IInputElement? input) => input is DependencyObject target
        ? target.GetType().Name + ":" + AutomationProperties.GetAutomationId(target) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(CursorPoint point);
}
