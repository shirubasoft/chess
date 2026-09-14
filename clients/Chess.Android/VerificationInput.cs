using Android.OS;
using Android.Views;

namespace Chess.Android;

public sealed partial class MainActivity
{
    private async Task DragInputAsync(string source, string? destination, bool cancel = false, Func<Task>? whileHeld = null)
    {
        var decor = Window!.DecorView;
        var origin = new int[2]; decor.GetLocationOnScreen(origin);
        var from = new int[2]; squares[source].GetLocationOnScreen(from);
        float fromX = from[0] - origin[0] + squares[source].Width / 2f, fromY = from[1] - origin[1] + squares[source].Height / 2f;
        var to = new int[2];
        float toX, toY;
        if (destination is { } name)
        {
            squares[name].GetLocationOnScreen(to);
            toX = to[0] - origin[0] + squares[name].Width / 2f; toY = to[1] - origin[1] + squares[name].Height / 2f;
        }
        else { board.GetLocationOnScreen(to); toX = to[0] - origin[0] + board.Width / 2f; toY = to[1] - origin[1] - 25; }
        var downAt = SystemClock.UptimeMillis();
        void Send(MotionEventActions action, float x, float y)
        {
            using var input = MotionEvent.Obtain(downAt, SystemClock.UptimeMillis(), action, x, y, 0)!;
            input.SetSource(InputSourceType.Touchscreen);
            decor.DispatchTouchEvent(input);
        }
        Send(MotionEventActions.Down, fromX, fromY); await Task.Delay(80);
        for (var step = 1; step <= 12; step++)
        {
            Send(MotionEventActions.Move, fromX + (toX - fromX) * step / 12, fromY + (toY - fromY) * step / 12);
            await Task.Delay(40);
            if (step == 6 && whileHeld is not null) await whileHeld();
        }
        Send(cancel ? MotionEventActions.Cancel : MotionEventActions.Up, toX, toY);
        await Task.Delay(120);
    }
}
