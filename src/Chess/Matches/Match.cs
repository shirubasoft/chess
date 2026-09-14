namespace Chess;

public static partial class Match
{
    public static MatchState Start(Position? initial = null)
    {
        var history = PositionHistory.Start(initial ?? Position.Initial);
        return State(history, Adjudicate(history), 0, DrawOfferState.None);
    }

    public static MatchCommandResult Decide(MatchState state, MatchCommand command)
    {
        return state switch
        {
            FinishedMatch => MatchCommandResult.AlreadyFinished,
            OngoingMatch ongoing => Decide(ongoing, command)
        };
    }

    public static MatchState Apply(MatchState state, MatchEvent @event)
    {
        if (state is not OngoingMatch ongoing)
        {
            throw new InvalidOperationException("A finished match cannot accept more events.");
        }
        RequirePredecessor(ongoing, @event);
        var history = ongoing.History;
        return @event switch
        {
            MovePlayed played => ApplyMove(ongoing, played),
            DrawClaimed claimed => ApplyClaim(history, claimed),
            PlayerResigned resigned => new FinishedMatch(history, resigned.Result, resigned.Revision),
            DrawOffered offered => ApplyOffer(ongoing, offered),
            DrawOfferDeclined declined => ApplyDecline(ongoing, declined),
            DrawAgreed agreed => ApplyAgreement(ongoing, agreed)
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

    private static MatchCommandResult Decide(OngoingMatch ongoing, MatchCommand command)
    {
        var history = ongoing.History;
        var player = command switch
        {
            PlayMove play => play.Player, ClaimDraw claim => claim.Player, Resign resign => resign.Player,
            OfferDraw offer => offer.Player, AcceptDraw accept => accept.Player, DeclineDraw decline => decline.Player
        };
        if (command is PlayMove or ClaimDraw && !player.Equals(history.Current.SideToMove))
        {
            return new WrongPlayer { Expected = history.Current.SideToMove, Actual = player };
        }
        return command switch
        {
            PlayMove play => ResolveMove(MoveRules.Apply(history.Current, play.Move), next =>
                new CommandAccepted(new MovePlayed(ongoing.Revision + 1, history.Keys.Count, history.CurrentKey, play.Move, Adjudicate(history.Record(next))))),
            ClaimDraw claim => DecideClaim(ongoing, claim),
            Resign resign => DecideResignation(ongoing, resign),
            OfferDraw offer => DecideOffer(ongoing, offer),
            AcceptDraw accept => DecideAcceptance(ongoing, accept),
            DeclineDraw decline => DecideDecline(ongoing, decline)
        };
    }

    private static MatchCommandResult DecideResignation(OngoingMatch ongoing, Resign resign)
    {
        var history = ongoing.History;
        Side opponent = resign.Player switch { White => Side.Black, Black => Side.White };
        MatchResult result = MatingMaterial.IsKnownInsufficient(history.Current.Board, opponent)
            ? new MatchDrawn { Reason = DrawReason.ResignationWithoutMatingMaterial }
            : new MatchWon { Winner = opponent, Reason = WinReason.Resignation };
        return new CommandAccepted(new PlayerResigned(ongoing.Revision + 1, history.Keys.Count - 1, history.CurrentKey, resign.Player, result));
    }

    private static MatchCommandResult DecideClaim(OngoingMatch ongoing, ClaimDraw claim) => claim.Timing switch
    {
        CurrentPositionClaim => Claim(ongoing, claim, ongoing.History.Current, ongoing.History.CurrentOccurrences),
        IntendedMoveClaim intended => ResolveMove(MoveRules.Apply(ongoing.History.Current, intended.Move), next =>
            Claim(ongoing, claim, next, ongoing.History.Occurrences(PositionKey.Create(next)) + 1))
    };

    private static MatchCommandResult Claim(OngoingMatch ongoing, ClaimDraw claim, Position position, int occurrences)
    {
        var available = claim.Reason switch
        {
            DrawClaimReason.ThreefoldRepetition => occurrences >= 3,
            DrawClaimReason.FiftyMoveRule => position.HalfmoveClock >= 100,
            _ => false
        };
        var history = ongoing.History;
        return available
            ? new CommandAccepted(new DrawClaimed(ongoing.Revision + 1, history.Keys.Count - 1, history.CurrentKey, claim.Reason, claim.Timing))
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
            OngoingPosition => HistoryOutcome(history)
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

    private static MatchState State(PositionHistory history, MatchProgress progress, int revision, DrawOfferState offer) => progress switch
    {
        PlayContinues => new OngoingMatch(history, revision, offer),
        MatchWon won => new FinishedMatch(history, won, revision),
        MatchDrawn drawn => new FinishedMatch(history, drawn, revision)
    };

    private static MatchState ApplyMove(OngoingMatch ongoing, MovePlayed played)
    {
        var history = ongoing.History;
        var position = MoveRules.Apply(history.Current, played.Move) switch
        {
            Position accepted => accepted,
            _ => throw new InvalidOperationException("The recorded move is illegal in this event stream.")
        };
        var offer = ongoing.DrawOffer is PendingDrawOffer pending && !pending.Player.Equals(history.Current.SideToMove)
            ? DrawOfferState.None : ongoing.DrawOffer;
        return State(history.Record(position), played.Progress, played.Revision, offer);
    }

    private static MatchState ApplyClaim(PositionHistory history, DrawClaimed claimed)
    {
        var reason = claimed.Reason switch
        {
            DrawClaimReason.ThreefoldRepetition => DrawReason.ThreefoldRepetition,
            DrawClaimReason.FiftyMoveRule => DrawReason.FiftyMoveRule,
            _ => throw new InvalidOperationException("Unknown recorded draw claim.")
        };
        return new FinishedMatch(history, new MatchDrawn { Reason = reason }, claimed.Revision);
    }

    private static void RequirePredecessor(OngoingMatch ongoing, MatchEvent @event)
    {
        var (revision, ply, key) = @event switch
        {
            MovePlayed played => (played.Revision, played.Ply - 1, played.PreviousKey),
            DrawClaimed claimed => (claimed.Revision, claimed.Ply, claimed.PreviousKey),
            PlayerResigned resigned => (resigned.Revision, resigned.Ply, resigned.PreviousKey),
            DrawOffered offered => (offered.Revision, offered.Ply, offered.PreviousKey),
            DrawOfferDeclined declined => (declined.Revision, declined.Ply, declined.PreviousKey),
            DrawAgreed agreed => (agreed.Revision, agreed.Ply, agreed.PreviousKey)
        };
        if (revision != ongoing.Revision + 1 || ongoing.History.Keys.Count - 1 != ply || ongoing.History.CurrentKey != key)
        {
            throw new InvalidOperationException("The event does not follow this match state.");
        }
    }

    private static MatchCommandResult ResolveMove(MoveResult result, Func<Position, MatchCommandResult> accepted) => result switch
    {
        Position next => accepted(next),
        SourceSquareEmpty rejection => rejection,
        WrongSideToMove rejection => rejection,
        FriendlyPieceOnDestination rejection => rejection,
        InvalidMovement rejection => rejection,
        PathBlocked rejection => rejection,
        KingCaptureNotAllowed rejection => rejection,
        KingWouldBeInCheck rejection => rejection,
        PromotionRequired rejection => rejection,
        InvalidPromotion rejection => rejection,
        CastlingUnavailable rejection => rejection
    };
}
