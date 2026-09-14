using Chess.Client;
using Chess.Contracts;

namespace Chess.Crossplay.Tests;

internal sealed class NativeSessionPlayer : IPlayer
{
    private readonly string server;
    private readonly GameSession session;

    public NativeSessionPlayer(Frontend frontend, string server, string settingsPath)
    {
        if (frontend is not (Frontend.Android or Frontend.Windows or Frontend.Linux))
            throw new ArgumentOutOfRangeException(nameof(frontend));
        this.server = server;
        session = new GameSession(PlayerNames.For(frontend), settingsPath, server);
    }

    public string ClientName => session.ClientName;
    public GameAccess? Access => session.Access;

    public async Task<GameAccess> CreateAsync()
    {
        await session.CreateAsync(server);
        return RequiredAccess();
    }

    public async Task<GameAccess> JoinAsync(string code)
    {
        await session.JoinAsync(server, code);
        var access = RequiredAccess();
        if (session.SavedCode != code) throw new InvalidOperationException("The native session did not persist its side code.");
        return access;
    }

    public async Task<GameSnapshot> RefreshAsync()
    {
        await session.RefreshAsync();
        if (session.Message != "Up to date.") throw new InvalidOperationException($"Native session refresh failed: {session.Message}");
        return RequiredSnapshot();
    }

    public async Task<GameSnapshot> MoveAsync(TestMove move)
    {
        var previous = await RefreshAsync();
        if (move.Entry == MoveEntry.Board)
        {
            await session.SelectSquareAsync(move.Uci[..2]);
            if (!session.IsDestination(move.Uci.Substring(2, 2)))
                throw new InvalidOperationException($"The native board did not offer {move.Uci}: {session.Message}");
            await session.SelectSquareAsync(move.Uci.Substring(2, 2));
        }
        else await session.MoveAsync(move.San);
        var snapshot = RequiredSnapshot();
        if (snapshot.Revision <= previous.Revision || snapshot.Moves.LastOrDefault()?.Uci != move.Uci)
            throw new InvalidOperationException($"Native session move {move.Uci} failed: {session.Message}");
        return snapshot;
    }

    public async Task<GameSnapshot> ResignAsync()
    {
        await RefreshAsync();
        await session.ActAsync(GameAction.Resign);
        var snapshot = RequiredSnapshot();
        if (snapshot.Status != GameStatus.Finished)
            throw new InvalidOperationException($"Native session resignation failed: {session.Message}");
        return snapshot;
    }

    public async Task VerifyOpponentAsync(string opponentName)
    {
        await RefreshAsync();
        await Assert.That(session.Opponent).IsEqualTo(opponentName);
    }

    private GameAccess RequiredAccess() => session.Access
        ?? throw new InvalidOperationException($"Native session has no game: {session.Message}");

    private GameSnapshot RequiredSnapshot() => session.Snapshot
        ?? throw new InvalidOperationException($"Native session has no snapshot: {session.Message}");

    public ValueTask DisposeAsync()
    {
        session.Dispose();
        return ValueTask.CompletedTask;
    }
}
