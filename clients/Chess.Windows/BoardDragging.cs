using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Chess.Client;

namespace Chess.Windows;

public sealed partial class ChessWindow
{
    private BoardDrag? boardDrag;
    private Point dragStart;
    private bool dragging;
    private DragAdorner? dragPreview;
    private AdornerLayer? dragLayer;

    private void ConfigureBoardDragging()
    {
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            CancelBoardDrag();
            var point = e.GetPosition(board);
            if (SquareAt(point) is not { } source) return;
            boardDrag = session.BeginDrag(source);
            dragStart = point;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (boardDrag is not { } drag) return;
            if (e.LeftButton != MouseButtonState.Pressed || !session.IsCurrentDrag(drag)) { CancelBoardDrag(); return; }
            var position = e.GetPosition(board);
            if (!dragging && Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!dragging)
            {
                dragging = true;
                board.CaptureMouse();
                dragLayer = AdornerLayer.GetAdornerLayer(board);
                dragPreview = new DragAdorner(board, squares[drag.Source].Content?.ToString() ?? "");
                dragLayer?.Add(dragPreview);
                squares[drag.Source].Opacity = 0.4;
            }
            dragPreview!.Position = position;
            dragPreview.InvalidateVisual();
            e.Handled = true;
        };
        PreviewMouseLeftButtonUp += async (_, e) =>
        {
            if (!dragging) { boardDrag = null; return; }
            var drag = boardDrag;
            var destination = SquareAt(e.GetPosition(board));
            ResetDragVisual();
            e.Handled = true;
            if (drag is not null) await session.DropAsync(drag, destination);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && boardDrag is not null) { CancelBoardDrag(); e.Handled = true; } };
        LostMouseCapture += (_, _) => { if (dragging && Mouse.Captured != board) CancelBoardDrag(); };
        Deactivated += (_, _) => CancelBoardDrag();
        Closed += (_, _) => CancelBoardDrag();
    }

    private string? SquareAt(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= board.ActualWidth || point.Y >= board.ActualHeight) return null;
        var index = (int)(point.Y * 8 / board.ActualHeight) * 8 + (int)(point.X * 8 / board.ActualWidth);
        return squares.Keys.ElementAtOrDefault(index);
    }

    private void ResetDragVisual()
    {
        boardDrag = null;
        dragging = false;
        if (dragPreview is not null) dragLayer?.Remove(dragPreview);
        dragPreview = null; dragLayer = null;
        foreach (var square in squares.Values) square.Opacity = 1;
        if (Mouse.Captured == board) board.ReleaseMouseCapture();
    }

    private void CancelBoardDrag()
    {
        var drag = boardDrag;
        ResetDragVisual();
        if (drag is not null) session.CancelDrag(drag);
    }

    private sealed class DragAdorner : Adorner
    {
        private readonly string symbol;
        public DragAdorner(UIElement owner, string symbol) : base(owner) { this.symbol = symbol; IsHitTestVisible = false; }
        public Point Position { get; set; }
        protected override void OnRender(DrawingContext context)
        {
            var text = new FormattedText(symbol, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Symbol"), 43, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.PushOpacity(0.9);
            context.DrawText(text, new Point(Position.X - text.Width / 2, Position.Y - text.Height / 2));
            context.Pop();
        }
    }

    private void CancelStaleDrag()
    {
        if (boardDrag is { } drag && !session.IsCurrentDrag(drag)) CancelBoardDrag();
    }
}
