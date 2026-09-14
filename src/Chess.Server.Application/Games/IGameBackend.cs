using Chess.Contracts;

namespace Chess.Server.Application;

public interface IGameBackend
{
    Task<GameSnapshot> CommandAsync(GameId gameId, string code, GameCommandRequest command, CancellationToken cancellationToken);
}

public sealed class PostgresGameBackend(PostgresGameStore store) : IGameBackend
{
    public Task<GameSnapshot> CommandAsync(GameId gameId, string code, GameCommandRequest command, CancellationToken cancellationToken) =>
        store.CommandAsync(gameId, code, command, cancellationToken);
}
