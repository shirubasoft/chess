using Chess.Contracts;

namespace Chess.Crossplay.Tests;

public enum Frontend { Android, Web, Windows, Linux, Cli }
internal enum MoveEntry { Board, San }
internal sealed record TestMove(string Uci, string San, MoveEntry Entry);

internal interface IPlayer : IAsyncDisposable
{
    string ClientName { get; }
    GameAccess? Access { get; }
    Task<GameAccess> CreateAsync();
    Task<GameAccess> JoinAsync(string code);
    Task<GameAccess> ResumeAsync(string code) => JoinAsync(code);
    Task<GameSnapshot> RefreshAsync();
    Task<GameSnapshot> MoveAsync(TestMove move);
    Task<GameSnapshot> ResignAsync();
    Task VerifyOpponentAsync(string opponentName);
}

internal static class PlayerNames
{
    public static string For(Frontend frontend) => frontend switch
    {
        Frontend.Android => "Chess Android", Frontend.Web => "Chess Web",
        Frontend.Windows => "Chess Windows", Frontend.Linux => "Chess Linux", Frontend.Cli => "Chess CLI",
        _ => throw new ArgumentOutOfRangeException(nameof(frontend))
    };
}
