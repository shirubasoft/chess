namespace Chess;

public static class Match
{
    public static MatchState Start(Position? initial = null)
    {
        var history = PositionHistory.Start(initial ?? Position.Initial);
        return State(history, Adjudicate(history));
    }

    public static MatchCommandResult Decide(MatchState state, MatchCommand command)
    {
        return state switch
        {
            FinishedMatch => MatchCommandResult.AlreadyFinished,
            OngoingMatch ongoing => Decide(ongoing.History, command)
        };
    }

    public static MatchState Apply(MatchState state, MatchEvent @event)
    {
        if (state is not OngoingMatch ongoing)
        {
            throw new InvalidOperationException("A finished match cannot accept more events.");
        }
        var history = ongoing.History;
        return @event switch
        {
            MovePlayed played => ApplyMove(history, played),
            DrawClaimed claimed => ApplyClaim(history, claimed)
        };
    }

    public static MatchState Replay(IEnumerable<MatchEvent> events, Position? initial = null)
    {
        var state = Start(initial);
        foreach (var @event in events)
        {
            state = Apply(state, @event);
        }
        return state;
    }

    private static MatchCommandResult Decide(PositionHistory history, MatchCommand command)
    {
        var player = command switch { PlayMove play => play.Player, ClaimDraw claim => claim.Player };
        if (!player.Equals(history.Current.SideToMove))
        {
            return new WrongPlayer { Expected = history.Current.SideToMove, Actual = player };
        }
        return command switch
        {
            PlayMove play => ResolveMove(MoveRules.Apply(history.Current, play.Move), next =>
                new CommandAccepted(new MovePlayed(history.Keys.Count, history.CurrentKey, play.Move, Adjudicate(history.Record(next))))),
            ClaimDraw claim => DecideClaim(history, claim)
        };
    }

    private static MatchCommandResult DecideClaim(PositionHistory history, ClaimDraw claim) => claim.Timing switch
    {
        CurrentPositionClaim => Claim(history, claim, history.Current, history.CurrentOccurrences),
        IntendedMoveClaim intended => ResolveMove(MoveRules.Apply(history.Current, intended.Move), next =>
            Claim(history, claim, next, history.Occurrences(PositionKey.Create(next)) + 1))
    };

    private static MatchCommandResult Claim(PositionHistory history, ClaimDraw claim, Position position, int occurrences)
    {
        var available = claim.Reason switch
        {
            DrawClaimReason.ThreefoldRepetition => occurrences >= 3,
            DrawClaimReason.FiftyMoveRule => position.HalfmoveClock >= 100,
            _ => false
        };
        return available
            ? new CommandAccepted(new DrawClaimed(history.Keys.Count - 1, history.CurrentKey, claim.Reason, claim.Timing))
            : MatchCommandResult.DrawClaimUnavailable;
    }

    private static MatchProgress Adjudicate(PositionHistory history)
    {
        var outcome = PositionRules.GetOutcome(history.Current);
        return outcome switch
        {
            Checkmate mate => new MatchWon { Winner = mate.Winner, Reason = WinReason.Checkmate },
            Stalemate => new MatchDrawn { Reason = DrawReason.Stalemate },
            DeadPosition => new MatchDrawn { Reason = DrawReason.DeadPosition },
            MatingContinuationExists => HistoryOutcome(history),
            UndeterminedPosition => HistoryOutcome(history),
            InvalidPosition => throw new ArgumentException("Match positions require one king per side.", nameof(history))
        };
    }

    private static MatchProgress HistoryOutcome(PositionHistory history)
    {
        if (history.IsFivefoldRepetition)
        {
            return new MatchDrawn { Reason = DrawReason.FivefoldRepetition };
        }
        if (history.Current.HalfmoveClock >= 150)
        {
            return new MatchDrawn { Reason = DrawReason.SeventyFiveMoveRule };
        }
        return MatchProgress.Continue;
    }

    private static MatchState State(PositionHistory history, MatchProgress progress) => progress switch
    {
        PlayContinues => new OngoingMatch(history),
        MatchWon won => new FinishedMatch(history, won),
        MatchDrawn drawn => new FinishedMatch(history, drawn)
    };

    private static MatchState ApplyMove(PositionHistory history, MovePlayed played)
    {
        RequirePredecessor(history, played.Ply - 1, played.PreviousKey);
        var position = MoveRules.Apply(history.Current, played.Move) switch
        {
            Position accepted => accepted,
            _ => throw new InvalidOperationException("The recorded move is illegal in this event stream.")
        };
        return State(history.Record(position), played.Progress);
    }

    private static MatchState ApplyClaim(PositionHistory history, DrawClaimed claimed)
    {
        RequirePredecessor(history, claimed.Ply, claimed.PreviousKey);
        var reason = claimed.Reason switch
        {
            DrawClaimReason.ThreefoldRepetition => DrawReason.ThreefoldRepetition,
            DrawClaimReason.FiftyMoveRule => DrawReason.FiftyMoveRule,
            _ => throw new InvalidOperationException("Unknown recorded draw claim.")
        };
        return new FinishedMatch(history, new MatchDrawn { Reason = reason });
    }

    private static void RequirePredecessor(PositionHistory history, int ply, PositionKey key)
    {
        if (history.Keys.Count - 1 != ply || history.CurrentKey != key)
        {
            throw new InvalidOperationException("The event does not follow this match position.");
        }
    }

    private static MatchCommandResult ResolveMove(MoveResult result, Func<Position, MatchCommandResult> accepted) => result switch
    {
        Position next => accepted(next),
        InvalidPosition rejection => rejection,
        SourceSquareEmpty rejection => rejection,
        WrongSideToMove rejection => rejection,
        FriendlyPieceOnDestination rejection => rejection,
        InvalidMovement rejection => rejection,
        PathBlocked rejection => rejection,
        KingCaptureNotAllowed rejection => rejection,
        KingWouldBeInCheck rejection => rejection,
        PromotionRequired rejection => rejection,
        InvalidPromotion rejection => rejection,
        CastlingUnavailable rejection => rejection,
        MoveCounterOverflow rejection => rejection
    };
}
