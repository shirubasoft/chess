using System.Runtime.InteropServices;
using System.Windows;

namespace Chess.Windows;

public sealed partial class ChessWindow
{
    private async Task DragInputAsync(string source, string? destination, bool cancel = false, Func<Task>? whileHeld = null)
    {
        verificationGesture++;
        Activate();
        if (verificationGesture == 1) VerifyInitialBoardVisibility();
        var from = squares[source].PointToScreen(new Point(squares[source].ActualWidth / 2, squares[source].ActualHeight / 2));
        var to = destination is { } name ? squares[name].PointToScreen(new Point(squares[name].ActualWidth / 2, squares[name].ActualHeight / 2))
            : board.PointToScreen(new Point(board.ActualWidth + 40, board.ActualHeight / 2));
        MoveVerificationCursor(from); await Task.Delay(80);
        var mouseDowns = verificationMouseDowns;
        MouseEvent(0x0002, 0, 0, 0, 0); await Task.Delay(80);
        try
        {
            TraceVerificationInput("after-press", requested: from);
            if (verificationMouseDowns == mouseDowns || verificationMouseDownSquare != source)
                throw new InvalidOperationException($"The injected mouse press did not reach square {source}. See the input log.");
            for (var step = 1; step <= 12; step++)
            {
                MoveVerificationCursor(new Point(from.X + (to.X - from.X) * step / 12, from.Y + (to.Y - from.Y) * step / 12));
                // Batch final native motion and release before WPF processes either event.
                if (step < 12) await Task.Delay(40);
                if (step == 6 && whileHeld is not null)
                {
                    TraceVerificationInput("before-remote-change");
                    await whileHeld();
                    TraceVerificationInput("after-remote-change");
                }
            }
            if (cancel) { KeyEvent(0x1b, 0, 0, 0); KeyEvent(0x1b, 0, 2, 0); await Task.Delay(80); }
        }
        finally
        {
            MouseEvent(0x0004, 0, 0, 0, 0); await Task.Delay(120);
            TraceVerificationInput("after-release");
        }
        var parked = PointToScreen(new Point(10, 10)); SetCursorPos((int)parked.X, (int)parked.Y);
    }

    private void MoveVerificationCursor(Point target)
    {
        var moved = SetCursorPos((int)target.X, (int)target.Y);
        var error = Marshal.GetLastWin32Error();
        TraceVerificationInput("injected-motion", requested: target);
        if (!moved) throw new InvalidOperationException($"SetCursorPos failed with Win32 error {error}.");
        if (!GetCursorPos(out var actual) || actual.X != (int)target.X || actual.Y != (int)target.Y)
            throw new InvalidOperationException("Windows did not place the cursor at the requested drag coordinate. See the input log.");
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extraInfo);
    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void KeyEvent(byte key, byte scan, uint flags, nuint extraInfo);
}
