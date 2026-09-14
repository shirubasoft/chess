using System.Text.Json;
using Chess.Contracts;
using Chess.Notation;

namespace Chess.Server.Application;

public readonly record struct GameId(Guid Value);

public sealed record GameDocument
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid Id { get; init; }
    public required long Revision { get; init; }
    public required string InitialFen { get; init; }
    public required string WhiteClient { get; init; }
    public string? BlackClient { get; init; }
    public string[] Events { get; init; } = [];
    public PlayedMove[] Moves { get; init; } = [];
}

public static class GameDecisions
{
    private static readonly JsonSerializerOptions DomainJson = ChessJson.CreateOptions();

    public static MatchState Restore(GameDocument game)
    {
        if (game.SchemaVersion != 1) throw new InvalidOperationException($"Unsupported game schema {game.SchemaVersion}.");
        return Match.Replay(game.Events.Select(e => JsonSerializer.Deserialize<MatchEvent>(e, DomainJson)),
            Fen.Parse(game.InitialFen).OrThrow());
    }

    public static GameSnapshot Snapshot(GameDocument game)
    {
        var state = Restore(game);
        var position = PositionOf(state);
        GameResultView? result = state is FinishedMatch finished ? finished.Result switch
        {
            MatchWon won => new GameResultView { Winner = ToView(won.Winner), Reason = won.Reason.ToString() },
            MatchDrawn drawn => new GameResultView { Reason = drawn.Reason.ToString() }
        } : null;
        return new GameSnapshot
        {
            GameId = game.Id, Revision = game.Revision, Fen = Fen.Format(position), SideToMove = ToView(position.SideToMove),
            Status = result is not null ? GameStatus.Finished : game.BlackClient is null ? GameStatus.Waiting : GameStatus.Active,
            White = new PlayerView { Side = PlayerSide.White, ClientName = game.WhiteClient },
            Black = new PlayerView { Side = PlayerSide.Black, ClientName = game.BlackClient },
            LegalMoves = result is null && game.BlackClient is not null
                ? MoveRules.GetLegalMoves(position).Select(m => Uci.Format(position, m)).ToArray() : [],
            Moves = game.Moves,
            DrawOfferedBy = state is OngoingMatch ongoing && ongoing.DrawOffer is PendingDrawOffer offer ? ToView(offer.Player) : null,
            Result = result
        };
    }

    public static GameDocument Apply(GameDocument game, PlayerSide side, GameCommandRequest request)
    {
        if (request.RequestId == Guid.Empty) throw GameFault.Invalid("A nonempty requestId is required.");
        if (request.ExpectedRevision != game.Revision)
            throw new GameFault(409, "revision_conflict", "Refresh the game before submitting this command.", game.Revision);
        if (game.BlackClient is null) throw new GameFault(409, "opponent_missing", "The other side has not joined yet.");
        if (!Enum.IsDefined(request.Action) || !Enum.IsDefined(request.Notation)) throw GameFault.Invalid("Unknown action or notation.");
        var state = Restore(game);
        var position = PositionOf(state);
        Side player = side == PlayerSide.White ? Side.White : Side.Black;
        MoveRequest ParseMove()
        {
            if (string.IsNullOrWhiteSpace(request.Move) || request.Move.Length > 32) throw GameFault.Invalid("Supply a SAN or UCI move of at most 32 characters.");
            var parsed = request.Notation == MoveNotation.Uci ? Uci.Parse(position, request.Move) : San.Parse(position, request.Move);
            return parsed switch
            {
                Parsed<MoveRequest> success => success.Value,
                NotationError error => throw new GameFault(422, "invalid_move", error.Message)
            };
        }
        MatchCommand command = request.Action switch
        {
            GameAction.Move => new PlayMove { Player = player, Move = ParseMove() },
            GameAction.Resign => new Resign { Player = player },
            GameAction.OfferDraw => new OfferDraw { Player = player },
            GameAction.AcceptDraw => new AcceptDraw { Player = player },
            GameAction.DeclineDraw => new DeclineDraw { Player = player },
            GameAction.ClaimThreefold or GameAction.ClaimFiftyMoves => new ClaimDraw
            {
                Player = player,
                Reason = request.Action == GameAction.ClaimThreefold ? DrawClaimReason.ThreefoldRepetition : DrawClaimReason.FiftyMoveRule,
                Timing = request.Move is null ? DrawClaimTiming.CurrentPosition : new IntendedMoveClaim { Move = ParseMove() }
            },
            _ => throw GameFault.Invalid("Unknown action.")
        };
        var decision = Match.Decide(state, command);
        if (decision is not CommandAccepted accepted)
        {
            var rejection = JsonSerializer.SerializeToElement(decision, DomainJson).GetProperty("case").GetString()!;
            throw new GameFault(422, rejection, $"The command was rejected: {rejection}.", game.Revision);
        }
        var moves = game.Moves;
        if (command is PlayMove play)
            moves = [.. moves, new PlayedMove
            {
                Ply = moves.Length + 1, Side = side, Uci = Uci.Format(position, play.Move), San = San.Format(position, play.Move)
            }];
        return game with
        {
            Revision = checked(game.Revision + 1),
            Events = [.. game.Events, JsonSerializer.Serialize(accepted.Event, DomainJson)], Moves = moves
        };
    }

    private static Position PositionOf(MatchState state) => state switch
    {
        OngoingMatch ongoing => ongoing.History.Current,
        FinishedMatch finished => finished.History.Current
    };

    public static PlayerSide ToView(Side side) => side is White ? PlayerSide.White : PlayerSide.Black;
}

public sealed class GameFault(int status, string code, string message, long? revision = null) : Exception(message)
{
    public int Status { get; } = status;
    public GameError Error { get; } = new() { Code = code, Message = message, CurrentRevision = revision };
    public static GameFault Invalid(string message) => new(400, "invalid_request", message);
    public static GameFault Unauthorized() => new(401, "invalid_code", "Supply the game code for your side.");
}
