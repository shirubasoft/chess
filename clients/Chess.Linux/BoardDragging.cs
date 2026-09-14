using Chess.Client;

namespace Chess.Linux;

public sealed partial class ChessWindow
{
    private BoardDrag? boardDrag;
    private bool dragging;
    private double dragX, dragY;
    private readonly Gtk.Fixed dragLayer = Gtk.Fixed.New();
    private readonly Gtk.Label dragPiece = Gtk.Label.New("");
    private Gtk.GestureDrag dragGesture = null!;

    private Gtk.Widget ConfigureBoardDragging()
    {
        var overlay = Gtk.Overlay.New(); overlay.SetChild(board);
        dragLayer.CanTarget = false; overlay.AddOverlay(dragLayer);
        dragPiece.AddCssClass("chess-square"); dragPiece.Opacity = 0.9; dragPiece.Visible = false;
        dragLayer.Put(dragPiece, 0, 0);
        dragGesture = Gtk.GestureDrag.New(); dragGesture.SetButton(1); dragGesture.SetPropagationPhase(Gtk.PropagationPhase.Capture);
        dragGesture.OnDragBegin += (_, e) =>
        {
            CancelBoardDrag();
            dragX = e.StartX; dragY = e.StartY;
            if (SquareAt(dragX, dragY) is { } source) boardDrag = session.BeginDrag(source);
        };
        dragGesture.OnDragUpdate += (_, e) =>
        {
            if (boardDrag is not { } drag) return;
            if (!session.IsCurrentDrag(drag)) { CancelBoardDrag(); return; }
            if (!dragging && !board.DragCheckThreshold((int)dragX, (int)dragY, (int)(dragX + e.OffsetX), (int)(dragY + e.OffsetY))) return;
            if (!dragging)
            {
                dragging = true;
                dragGesture.SetState(Gtk.EventSequenceState.Claimed);
                dragPiece.SetText(squares[drag.Source].Label ?? "");
                squares[drag.Source].Opacity = 0.4;
                dragPiece.Visible = true;
            }
            dragLayer.Move(dragPiece, dragX + e.OffsetX - 20, dragY + e.OffsetY - 22);
        };
        dragGesture.OnDragEnd += async (_, e) =>
        {
            if (!dragging) { boardDrag = null; return; }
            var drag = boardDrag;
            var destination = SquareAt(dragX + e.OffsetX, dragY + e.OffsetY);
            ResetDragVisual();
            if (drag is not null) await session.DropAsync(drag, destination);
        };
        dragGesture.OnCancel += (_, _) => CancelBoardDrag();
        board.AddController(dragGesture);
        var keys = Gtk.EventControllerKey.New(); keys.SetPropagationPhase(Gtk.PropagationPhase.Capture);
        keys.OnKeyPressed += (_, e) =>
        {
            if (e.Keyval != 0xff1b || boardDrag is null) return false;
            CancelBoardDrag(); dragGesture.Reset(); return true;
        };
        window.AddController(keys);
        window.OnNotify += (_, e) => { if (e.Pspec.GetName() == "is-active" && !window.IsActive) CancelBoardDrag(); };
        window.OnUnmap += (_, _) => CancelBoardDrag();
        return overlay;
    }

    private string? SquareAt(double x, double y)
    {
        if (x < 0 || y < 0 || x >= board.GetWidth() || y >= board.GetHeight()) return null;
        return squares.Keys.ElementAtOrDefault((int)(y * 8 / board.GetHeight()) * 8 + (int)(x * 8 / board.GetWidth()));
    }

    private void ResetDragVisual()
    {
        boardDrag = null; dragging = false; dragPiece.Visible = false;
        foreach (var square in squares.Values) square.Opacity = 1;
    }

    private void CancelBoardDrag()
    {
        var drag = boardDrag;
        ResetDragVisual();
        if (drag is not null) session.CancelDrag(drag);
    }
}
