using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chess.Contracts;

public enum PlayerSide { White, Black }
public enum GameStatus { Waiting, Active, Finished }
public enum GameAction { Move, Resign, OfferDraw, AcceptDraw, DeclineDraw, ClaimThreefold, ClaimFiftyMoves }
public enum MoveNotation { Uci, San }

public sealed record CreateGameRequest
{
    public required Guid RequestId { get; init; }
    public required string ClientName { get; init; }
    public string? InitialFen { get; init; }
}

public sealed record JoinGameRequest
{
    public required string Code { get; init; }
    public required string ClientName { get; init; }
}

public sealed record MatchmakingRequest
{
    public required Guid RequestId { get; init; }
    public required string ClientName { get; init; }
}

public sealed record GameCommandRequest
{
    public required Guid RequestId { get; init; }
    public required long ExpectedRevision { get; init; }
    public required GameAction Action { get; init; }
    public string? Move { get; init; }
    public MoveNotation Notation { get; init; } = MoveNotation.San;
}

public sealed record PlayerView
{
    public required PlayerSide Side { get; init; }
    public string? ClientName { get; init; }
}

public sealed record PlayedMove
{
    public required int Ply { get; init; }
    public required PlayerSide Side { get; init; }
    public required string Uci { get; init; }
    public required string San { get; init; }
}

public sealed record GameResultView
{
    public PlayerSide? Winner { get; init; }
    public required string Reason { get; init; }
}

public sealed record GameSnapshot
{
    public required Guid GameId { get; init; }
    public required long Revision { get; init; }
    public required string Fen { get; init; }
    public required PlayerSide SideToMove { get; init; }
    public required GameStatus Status { get; init; }
    public required PlayerView White { get; init; }
    public required PlayerView Black { get; init; }
    public required string[] LegalMoves { get; init; }
    public required PlayedMove[] Moves { get; init; }
    public PlayerSide? DrawOfferedBy { get; init; }
    public GameResultView? Result { get; init; }
}

public sealed record GameAccess
{
    public required Guid GameId { get; init; }
    public required string Code { get; init; }
    public required PlayerSide Side { get; init; }
    public required GameSnapshot Snapshot { get; init; }
    public string? OpponentCode { get; init; }
}

public sealed record GameError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public long? CurrentRevision { get; init; }
}

public sealed record WatchRequest
{
    public required string Code { get; init; }
    public long AfterRevision { get; init; } = -1;
}

public static class GameJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
