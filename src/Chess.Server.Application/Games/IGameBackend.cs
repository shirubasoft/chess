using Chess.Contracts;

namespace Chess.Server.Application;

public interface IGameBackend
{
    Task<GameSnapshot> CommandAsync(GameId gameId, string code, GameCommandRequest command, CancellationToken cancellationToken);
}
