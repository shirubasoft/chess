using System.Runtime.InteropServices;
using System.Windows;

namespace Chess.Windows;

public sealed partial class ChessWindow
{
    private async Task DragInputAsync(string source, string? destination, bool cancel = false, Func<Task>? whileHeld = null)
    {
        Activate();
        var from = squares[source].PointToScreen(new Point(squares[source].ActualWidth / 2, squares[source].ActualHeight / 2));
        var to = destination is { } name ? squares[name].PointToScreen(new Point(squares[name].ActualWidth / 2, squares[name].ActualHeight / 2))
            : board.PointToScreen(new Point(board.ActualWidth + 40, board.ActualHeight / 2));
        SetCursorPos((int)from.X, (int)from.Y); await Task.Delay(80);
        MouseEvent(0x0002, 0, 0, 0, 0); await Task.Delay(80);
        for (var step = 1; step <= 12; step++)
        {
            SetCursorPos((int)(from.X + (to.X - from.X) * step / 12), (int)(from.Y + (to.Y - from.Y) * step / 12));
            await Task.Delay(40);
            if (step == 6 && whileHeld is not null) await whileHeld();
        }
        if (cancel) { KeyEvent(0x1b, 0, 0, 0); KeyEvent(0x1b, 0, 2, 0); await Task.Delay(80); }
        MouseEvent(0x0004, 0, 0, 0, 0); await Task.Delay(120);
        var parked = PointToScreen(new Point(10, 10)); SetCursorPos((int)parked.X, (int)parked.Y);
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extraInfo);
    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void KeyEvent(byte key, byte scan, uint flags, nuint extraInfo);
}
