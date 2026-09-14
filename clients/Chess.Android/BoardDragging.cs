using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Chess.Client;

namespace Chess.Android;

public sealed partial class MainActivity
{
    private BoardDrag? boardDrag;
    private Button? touchSource;
    private int touchPointer;
    private float touchX, touchY;
    private bool dragging, touchCanceled;
    private PieceDragDrawable? dragPiece;
    private readonly int[] boardLocation = new int[2];

    private void ConfigureSquareDragging(Button button, string source)
    {
        button.Touch += async (_, args) =>
        {
            if (args.Event is not { } input) return;
            if (input.ActionMasked == MotionEventActions.Down)
            {
                CancelBoardDrag(); touchSource = null;
                boardDrag = session.BeginDrag(source);
                touchSource = button; touchPointer = input.GetPointerId(0);
                touchX = input.RawX; touchY = input.RawY; touchCanceled = false;
                button.Parent?.RequestDisallowInterceptTouchEvent(true);
                args.Handled = true; return;
            }
            if (touchSource != button) return;
            args.Handled = true;
            if (input.ActionMasked is MotionEventActions.Cancel or MotionEventActions.PointerDown
                || input.GetPointerId(0) != touchPointer)
            {
                CancelBoardDrag();
                if (input.ActionMasked == MotionEventActions.Cancel) { touchSource = null; button.Parent?.RequestDisallowInterceptTouchEvent(false); }
                return;
            }
            if (input.ActionMasked == MotionEventActions.Move && !touchCanceled)
            {
                var slop = ViewConfiguration.Get(this)!.ScaledTouchSlop;
                if (!dragging && Math.Abs(input.RawX - touchX) < slop && Math.Abs(input.RawY - touchY) < slop) return;
                if (boardDrag is not { } drag || !session.IsCurrentDrag(drag)) { CancelBoardDrag(); return; }
                if (!dragging)
                {
                    dragging = true; button.Alpha = 0.4f;
                    dragPiece = new PieceDragDrawable(button.Text ?? "", button.TextSize);
                    board.Overlay!.Add(dragPiece);
                }
                board.GetLocationOnScreen(boardLocation);
                dragPiece!.SetBounds((int)(input.RawX - boardLocation[0] - button.Width / 2f),
                    (int)(input.RawY - boardLocation[1] - button.Height / 2f),
                    (int)(input.RawX - boardLocation[0] + button.Width / 2f),
                    (int)(input.RawY - boardLocation[1] + button.Height / 2f));
                dragPiece.InvalidateSelf();
            }
            if (input.ActionMasked == MotionEventActions.Up)
            {
                var drop = boardDrag;
                var wasDragging = dragging;
                var canceled = touchCanceled;
                var destination = SquareAt(input.RawX, input.RawY);
                ResetDragVisual(); touchSource = null;
                button.Parent?.RequestDisallowInterceptTouchEvent(false);
                if (canceled) return;
                if (wasDragging && drop is not null) await session.DropAsync(drop, destination);
                else button.PerformClick();
            }
        };
    }

    private string? SquareAt(float screenX, float screenY)
    {
        board.GetLocationOnScreen(boardLocation);
        var x = screenX - boardLocation[0]; var y = screenY - boardLocation[1];
        if (x < 0 || y < 0 || x >= board.Width || y >= board.Height) return null;
        return squares.Keys.ElementAtOrDefault((int)(y * 8 / board.Height) * 8 + (int)(x * 8 / board.Width));
    }

    private void ResetDragVisual()
    {
        boardDrag = null; dragging = false;
        if (dragPiece is not null) { board.Overlay?.Remove(dragPiece); dragPiece.Dispose(); dragPiece = null; }
        if (touchSource is { } button) button.Alpha = 1;
    }

    private void CancelBoardDrag()
    {
        var drag = boardDrag;
        ResetDragVisual(); touchCanceled = true;
        // Keep a canceled gesture owned until release so ScrollView cannot turn it into a fling.
        if (drag is not null) session.CancelDrag(drag);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        if (!hasFocus) CancelBoardDrag();
        base.OnWindowFocusChanged(hasFocus);
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Escape && boardDrag is not null) { CancelBoardDrag(); return true; }
        return base.OnKeyDown(keyCode, e);
    }

    private sealed class PieceDragDrawable(string symbol, float size) : Drawable
    {
        private readonly Paint paint = new(PaintFlags.AntiAlias) { Color = Color.Rgb(25, 27, 23), TextSize = size, TextAlign = Paint.Align.Center, Alpha = 230 };
        public override void Draw(Canvas canvas) => canvas.DrawText(symbol, Bounds.ExactCenterX(), Bounds.ExactCenterY() - (paint.Ascent() + paint.Descent()) / 2, paint);
        public override void SetAlpha(int alpha) => paint.Alpha = alpha;
        public override void SetColorFilter(ColorFilter? colorFilter) => paint.SetColorFilter(colorFilter);
#pragma warning disable CS0672
        public override int Opacity => (int)Format.Translucent;
#pragma warning restore CS0672
        protected override void Dispose(bool disposing) { if (disposing) paint.Dispose(); base.Dispose(disposing); }
    }
}
