using System.Runtime.InteropServices;

namespace Chess.Linux;

public sealed partial class ChessWindow
{
    private async Task DragInputAsync(string source, string? destination, bool cancel = false, Func<Task>? whileHeld = null)
    {
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("Native mouse verification requires an isolated X11 display.");
        try
        {
            var xid = GetXid(((Gtk.Native)window).GetSurface()!.Handle.DangerousGetHandle());
            XTranslateCoordinates(display, xid, XDefaultRootWindow(display), 0, 0, out var originX, out var originY, out _);
            squares[source].TranslateCoordinates(window, squares[source].GetWidth() / 2d, squares[source].GetHeight() / 2d, out var fromX, out var fromY);
            double toX, toY;
            if (destination is { } name) squares[name].TranslateCoordinates(window, squares[name].GetWidth() / 2d, squares[name].GetHeight() / 2d, out toX, out toY);
            else board.TranslateCoordinates(window, board.GetWidth() + 40, board.GetHeight() / 2d, out toX, out toY);
            void Move(double x, double y) { XTestFakeMotionEvent(display, -1, originX + (int)x, originY + (int)y, 0); XFlush(display); }
            Move(fromX, fromY); await Task.Delay(80);
            XTestFakeButtonEvent(display, 1, true, 0); XFlush(display); await Task.Delay(80);
            for (var step = 1; step <= 12; step++)
            {
                Move(fromX + (toX - fromX) * step / 12, fromY + (toY - fromY) * step / 12);
                await Task.Delay(40);
                if (step == 6 && whileHeld is not null) await whileHeld();
            }
            if (cancel)
            {
                var key = XKeysymToKeycode(display, 0xff1b);
                XTestFakeKeyEvent(display, key, true, 0); XTestFakeKeyEvent(display, key, false, 0); XFlush(display);
                await Task.Delay(80);
            }
            XTestFakeButtonEvent(display, 1, false, 0); XFlush(display); await Task.Delay(120);
            Move(10, 10);
        }
        finally { XCloseDisplay(display); }
    }

    [DllImport("libgtk-4.so.1", EntryPoint = "gdk_x11_surface_get_xid")] private static extern nuint GetXid(nint surface);
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern int XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, out int targetX, out int targetY, out nuint child);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(nint display, nuint symbol);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(nint display, uint button, bool pressed, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(nint display, uint key, bool pressed, nuint delay);
}
