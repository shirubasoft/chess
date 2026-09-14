using Chess.Contracts;

namespace Chess.Client;

public sealed class BoardDrag
{
    internal BoardDrag(GameSession owner, GameAccess access, GameSnapshot snapshot, string source)
    {
        Owner = owner; Access = access; Snapshot = snapshot; Source = source;
    }

    internal GameSession Owner { get; }
    internal GameAccess Access { get; }
    internal GameSnapshot Snapshot { get; }
    public string Source { get; }
    internal bool IsActive { get; set; } = true;
}

public sealed partial class GameSession
{
    public BoardDrag? BeginDrag(string source)
    {
        if (source.Length != 2 || source[0] is < 'a' or > 'h' || source[1] is < '1' or > '8') return null;
        if (!CanMove || Snapshot is not { } snapshot || Access is not { } access
            || !snapshot.LegalMoves.Any(move => move.StartsWith(source, StringComparison.Ordinal))) return null;
        var drag = new BoardDrag(this, access, snapshot, source);
        SelectedSquare = source;
        PromotionMove = null;
        Changed?.Invoke();
        return drag;
    }

    public bool IsCurrentDrag(BoardDrag drag) => CanMove && MatchesDrag(drag);

    private bool MatchesDrag(BoardDrag drag) => drag.IsActive && ReferenceEquals(drag.Owner, this)
        && ReferenceEquals(drag.Access, Access) && Snapshot is { Status: GameStatus.Active } snapshot
        && snapshot.GameId == drag.Snapshot.GameId && snapshot.Revision == drag.Snapshot.Revision
        && snapshot.SideToMove == drag.Access.Side;

    public void CancelDrag(BoardDrag drag)
    {
        if (!ReferenceEquals(drag.Owner, this)) return;
        drag.IsActive = false;
        if (!ReferenceEquals(drag.Access, Access) || SelectedSquare != drag.Source) return;
        SelectedSquare = null;
        PromotionMove = null;
        Changed?.Invoke();
    }

    public Task<bool> DropAsync(BoardDrag drag, string? destination)
    {
        var prefix = drag.Source + destination;
        if (!IsCurrentDrag(drag) || destination is not { Length: 2 }
            || !drag.Snapshot.LegalMoves.Any(move => move.StartsWith(prefix, StringComparison.Ordinal)))
        {
            CancelDrag(drag);
            return Task.FromResult(false);
        }
        return RunAsync(async () =>
        {
            if (!MatchesDrag(drag)) throw new ArgumentException("The position changed. Select your piece again.");
            drag.IsActive = false;
            var candidates = drag.Snapshot.LegalMoves.Where(move => move.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (candidates.Length > 1)
            {
                SelectedSquare = drag.Source;
                PromotionMove = prefix;
                Message = "Choose the promotion piece.";
                return;
            }
            SelectedSquare = null;
            PromotionMove = null;
            Update(await client.MoveAsync(drag.Access, drag.Snapshot, candidates[0], MoveNotation.Uci, cancellationToken: lifetime.Token));
            Message = $"Played {candidates[0]}.";
        });
    }
}
