using System.Text.Json;
using Chess.Notation;

namespace Chess.Validation;

internal static class Inspector
{
    private static readonly JsonSerializerOptions Persistence = ChessJson.CreateOptions();

    internal static object Position(JsonElement request)
    {
        var position = ReadPosition(request);
        var moves = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var move in MoveRules.GetLegalMoves(position))
        {
            var next = (Position)MoveRules.Apply(position, move).Value!;
            moves.Add(Uci.Format(position, move), new { fen = Fen.Format(next), san = San.Format(position, move) });
        }
        var restored = JsonSerializer.Deserialize<Position>(JsonSerializer.Serialize(position, Persistence), Persistence)!;
        return new
        {
            fen = Fen.Format(position), inCheck = MoveRules.IsInCheck(position),
            outcome = Outcome(PositionRules.GetOutcome(position)), moves,
            roundtripFen = Fen.Format(restored), roundtripKeyMatches = PositionKey.Create(position) == PositionKey.Create(restored)
        };
    }

    internal static object Move(JsonElement request)
    {
        var position = ReadPosition(request);
        var text = request.GetProperty("move").GetString()!;
        var parsed = request.GetProperty("notation").GetString() == "san" ? San.Parse(position, text) : Uci.Parse(position, text);
        if (parsed is NotationError error) return new { accepted = false, error.Kind, fen = Fen.Format(position) };
        var move = parsed.OrThrow();
        var next = (Position)MoveRules.Apply(position, move).Value!;
        return new { accepted = true, uci = Uci.Format(position, move), san = San.Format(position, move), fen = Fen.Format(next) };
    }

    internal static object History(JsonElement request)
    {
        var initial = request.TryGetProperty("fen", out var fen) ? Fen.Parse(fen.GetString()!).OrThrow() : Chess.Position.Initial;
        var state = Match.Start(initial);
        var events = new List<MatchEvent>();
        foreach (var token in request.GetProperty("moves").EnumerateArray())
        {
            var position = GetHistory(state).Current;
            var command = new PlayMove { Player = position.SideToMove, Move = Uci.Parse(position, token.GetString()!).OrThrow() };
            if (Match.Decide(state, command) is not CommandAccepted accepted) throw new InvalidOperationException("The recorded game continued after a terminal position.");
            events.Add(accepted.Event);
            state = Match.Apply(state, accepted.Event);
        }
        var history = GetHistory(state);
        var serialized = JsonSerializer.Serialize(events, Persistence);
        var replay = Match.Replay(JsonSerializer.Deserialize<MatchEvent[]>(serialized, Persistence)!, initial);
        return new
        {
            fen = Fen.Format(history.Current), occurrences = history.CurrentOccurrences,
            result = Result(state), replayFen = Fen.Format(GetHistory(replay).Current), replayResult = Result(replay),
            revision = state.Revision, replayRevision = replay.Revision,
            threefold = Claims(state, DrawClaimReason.ThreefoldRepetition), fiftyMove = Claims(state, DrawClaimReason.FiftyMoveRule)
        };
    }

    internal static object Pgn(JsonElement request)
    {
        return Chess.Notation.Pgn.ReadGames(request.GetProperty("text").GetString()!).Select(parsed =>
        {
            var game = parsed.OrThrow();
            return new { tags = game.Tags, initialFen = Fen.Format(game.InitialPosition), result = game.Result.ToString(), line = Line(game.Mainline, game.InitialPosition) };
        }).ToArray();
    }

    private static object Line(PgnLine line, Position initial)
    {
        var position = initial;
        var moves = new List<object>();
        foreach (var move in line.Moves)
        {
            var next = (Position)MoveRules.Apply(position, move.Move).Value!;
            moves.Add(new
            {
                uci = Uci.Format(position, move.Move), san = San.Format(position, move.Move), fen = Fen.Format(next),
                comments = move.Comments, annotations = move.Annotations,
                variations = move.Variations.Select(variation => Line(variation, position)).ToArray()
            });
            position = next;
        }
        return new { comments = line.Comments, moves, finalFen = Fen.Format(position) };
    }

    private static object Claims(MatchState state, DrawClaimReason reason)
    {
        var position = GetHistory(state).Current;
        var claim = new ClaimDraw { Player = position.SideToMove, Reason = reason, Timing = DrawClaimTiming.CurrentPosition };
        var current = Match.Decide(state, claim) is CommandAccepted;
        var moves = MoveRules.GetLegalMoves(position)
            .Where(move => Match.Decide(state, claim with { Timing = new IntendedMoveClaim { Move = move } }) is CommandAccepted)
            .Select(move => Uci.Format(position, move)).Order(StringComparer.Ordinal).ToArray();
        return new { current, moves };
    }

    private static Position ReadPosition(JsonElement request)
    {
        var position = request.TryGetProperty("fen", out var fen) ? Fen.Parse(fen.GetString()!).OrThrow() : Chess.Position.Initial;
        if (request.TryGetProperty("moves", out var moves))
            foreach (var move in moves.EnumerateArray()) position = (Position)MoveRules.Apply(position, Uci.Parse(position, move.GetString()!).OrThrow()).Value!;
        return position;
    }

    private static PositionHistory GetHistory(MatchState state) => state switch { OngoingMatch ongoing => ongoing.History, FinishedMatch finished => finished.History };

    private static string Outcome(PositionOutcome outcome) => outcome switch
    {
        Checkmate => "checkmate", Stalemate => "stalemate", DeadPosition => "dead_position", OngoingPosition => "ongoing"
    };

    private static object? Result(MatchState state) => state switch
    {
        OngoingMatch => null,
        FinishedMatch finished => finished.Result switch
        {
            MatchWon won => new { winner = (string?)(won.Winner is White ? "white" : "black"), reason = won.Reason.ToString() },
            MatchDrawn drawn => new { winner = (string?)null, reason = drawn.Reason.ToString() }
        }
    };
}
