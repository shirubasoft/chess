using System.Drawing;
using Chess.Contracts;
using Chess.Notation;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace Chess.Cli;

public sealed class ChessBoardView : View
{
    private static readonly Color Ink = new("#102018");
    private static readonly Color LightSquare = new("#E5E2CF");
    private static readonly Color DarkSquare = new("#809B88");
    private static readonly Color SelectedSquare = new("#DBC57A");
    private static readonly Color LegalSquare = new("#B3CA99");
    private GameAccess? access;
    private bool canMove;
    private string[] ranks = ReadRanks(Fen.Format(Position.Initial));
    private Drag? drag;
    private string? hoveredSquare;

    public ChessBoardView()
    {
        Width = 30;
        Height = 10;
        CanFocus = false;
        MousePositionTracking = true;
    }

    public event Action<GameAccess, string[]>? MoveRequested;
    public event Action<string>? Feedback;
    public bool IsDragging => drag is not null;
    public PlayerSide Perspective => access?.Side ?? PlayerSide.White;

    public void SetGame(GameAccess? game, bool busy)
    {
        CancelDrag();
        access = game;
        ranks = ReadRanks(game?.Snapshot.Fen ?? Fen.Format(Position.Initial));
        canMove = !busy && game is { Snapshot.Status: GameStatus.Active } && game.Side == game.Snapshot.SideToMove;
        SetNeedsDraw();
    }

    public void CancelDrag()
    {
        if (drag is null) return;
        drag = null;
        hoveredSquare = null;
        if (App?.Mouse.IsGrabbed(this) == true) App.Mouse.UngrabMouse();
        SetNeedsDraw();
    }

    public static string? SquareAt(Point point, PlayerSide perspective)
    {
        if (point.X is < 3 or >= 27 || point.Y is < 1 or >= 9) return null;
        var file = (point.X - 3) / 3;
        var rank = 8 - point.Y;
        if (perspective == PlayerSide.Black) { file = 7 - file; rank = 7 - rank; }
        return $"{(char)('a' + file)}{rank + 1}";
    }

    public char PieceAt(string square) => Glyph(ranks[8 - (square[1] - '0')][square[0] - 'a']);

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttributeForRole(VisualRole.Normal);
        AddStr(2, 0, "┌" + FileLabels() + "┐");
        AddStr(2, 9, "└" + FileLabels() + "┘");
        for (var row = 0; row < 8; row++)
        {
            var rank = Perspective == PlayerSide.White ? 8 - row : row + 1;
            SetAttributeForRole(VisualRole.Normal);
            AddStr(0, row + 1, $"{rank} │");
            AddStr(27, row + 1, $"│ {rank}");
            for (var column = 0; column < 8; column++)
            {
                var square = SquareAt(new Point(3 + column * 3, row + 1), Perspective)!;
                var selected = drag?.Source == square;
                var legal = drag is { } active && active.Access.Snapshot.LegalMoves.Any(move => move.StartsWith(active.Source + square, StringComparison.Ordinal));
                SetAttribute(SquareAppearance(selected, legal, (column + row) % 2 == 0));
                var symbol = PieceAt(square);
                if (drag is { } moving && hoveredSquare == square && square != moving.Source) symbol = PieceAt(moving.Source);
                else if (symbol == ' ' && legal) symbol = '·';
                AddStr(3 + column * 3, row + 1, selected ? $"[{symbol}]" : $" {symbol} ");
            }
        }
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        var square = mouse.Position is { } point ? SquareAt(point, Perspective) : null;
        if (mouse.Flags.HasFlag(MouseFlags.RightButtonPressed))
        {
            CancelDrag();
            return true;
        }
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased))
        {
            var dropped = drag;
            CancelDrag();
            if (dropped is null || square == dropped.Source) return true;
            var moves = square is null ? [] : dropped.Access.Snapshot.LegalMoves
                .Where(move => move.StartsWith(dropped.Source + square, StringComparison.Ordinal)).ToArray();
            if (moves.Length > 0) MoveRequested?.Invoke(dropped.Access, moves);
            else Feedback?.Invoke("Move cancelled. Drop on a highlighted square.");
            return true;
        }
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            if (drag is null && canMove && access is { } current && square is not null
                && current.Snapshot.LegalMoves.Any(move => move.StartsWith(square, StringComparison.Ordinal)))
            {
                drag = new Drag { Access = current, Source = square };
                App?.Mouse.GrabMouse(this);
                Feedback?.Invoke("Drop on a highlighted square. Right-click or Esc cancels.");
            }
            hoveredSquare = square;
            SetNeedsDraw();
            return true;
        }
        if (drag is not null && mouse.Flags.HasFlag(MouseFlags.PositionReport))
        {
            hoveredSquare = square;
            SetNeedsDraw();
            return true;
        }
        return base.OnMouseEvent(mouse);
    }

    private string FileLabels() => string.Concat((Perspective == PlayerSide.White ? "abcdefgh" : "hgfedcba").Select(file => $"─{file}─"));

    private Attribute SquareAppearance(bool selected, bool legal, bool light)
    {
        if (App?.Driver?.Force16Colors == true)
        {
            var background = selected ? ColorName16.BrightYellow : legal ? ColorName16.BrightGreen : light ? ColorName16.Gray : ColorName16.DarkGray;
            var foreground = selected || legal || light ? ColorName16.Black : ColorName16.White;
            return new Attribute(foreground, background);
        }
        return new Attribute(Ink, selected ? SelectedSquare : legal ? LegalSquare : light ? LightSquare : DarkSquare);
    }

    private static string[] ReadRanks(string fen) => fen.Split(' ')[0].Split('/')
        .Select(rank => string.Concat(rank.Select(symbol => char.IsAsciiDigit(symbol) ? new string(' ', symbol - '0') : symbol.ToString()))).ToArray();

    private static char Glyph(char piece) => piece switch
    {
        'K' => '♔', 'Q' => '♕', 'R' => '♖', 'B' => '♗', 'N' => '♘', 'P' => '♙',
        'k' => '♚', 'q' => '♛', 'r' => '♜', 'b' => '♝', 'n' => '♞', 'p' => '♟',
        _ => ' '
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) CancelDrag();
        base.Dispose(disposing);
    }

    private sealed record Drag
    {
        public required GameAccess Access { get; init; }
        public required string Source { get; init; }
    }
}
