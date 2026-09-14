namespace Chess;

public static partial class Match
{
    private static MatchCommandResult DecideOffer(OngoingMatch ongoing, OfferDraw offer) => ongoing.DrawOffer switch
    {
        NoDrawOffer => new CommandAccepted(new DrawOffered(ongoing.Revision + 1,
            ongoing.History.Keys.Count - 1, ongoing.History.CurrentKey, offer.Player)),
        PendingDrawOffer => MatchCommandResult.DrawOfferAlreadyPending
    };

    private static MatchCommandResult DecideAcceptance(OngoingMatch ongoing, AcceptDraw accept) =>
        DecideDrawReply(ongoing, accept.Player, () => ongoing.History.Keys.Count < 3
            ? MatchCommandResult.DrawAgreementUnavailable
            : new CommandAccepted(new DrawAgreed(ongoing.Revision + 1,
                ongoing.History.Keys.Count - 1, ongoing.History.CurrentKey, accept.Player)));

    private static MatchCommandResult DecideDecline(OngoingMatch ongoing, DeclineDraw decline) =>
        DecideDrawReply(ongoing, decline.Player, () =>
            new CommandAccepted(new DrawOfferDeclined(ongoing.Revision + 1,
                ongoing.History.Keys.Count - 1, ongoing.History.CurrentKey, decline.Player)));

    private static MatchCommandResult DecideDrawReply(OngoingMatch ongoing, Side player, Func<MatchCommandResult> accepted)
    {
        if (ongoing.DrawOffer is not PendingDrawOffer pending)
        {
            return MatchCommandResult.NoPendingDrawOffer;
        }
        Side opponent = pending.Player switch { White => Side.Black, Black => Side.White };
        return player.Equals(opponent)
            ? accepted()
            : new WrongPlayer { Expected = opponent, Actual = player };
    }

    private static MatchState ApplyOffer(OngoingMatch ongoing, DrawOffered offered)
    {
        RequireAcceptedDrawAction(DecideOffer(ongoing, new OfferDraw { Player = offered.Player }));
        return new OngoingMatch(ongoing.History, offered.Revision, new PendingDrawOffer { Player = offered.Player });
    }

    private static MatchState ApplyDecline(OngoingMatch ongoing, DrawOfferDeclined declined)
    {
        RequireAcceptedDrawAction(DecideDecline(ongoing, new DeclineDraw { Player = declined.Player }));
        return new OngoingMatch(ongoing.History, declined.Revision, DrawOfferState.None);
    }

    private static MatchState ApplyAgreement(OngoingMatch ongoing, DrawAgreed agreed)
    {
        RequireAcceptedDrawAction(DecideAcceptance(ongoing, new AcceptDraw { Player = agreed.Player }));
        return new FinishedMatch(ongoing.History, new MatchDrawn { Reason = DrawReason.Agreement }, agreed.Revision);
    }

    private static void RequireAcceptedDrawAction(MatchCommandResult result)
    {
        if (result is not CommandAccepted)
        {
            throw new InvalidOperationException("The recorded draw action is invalid in this event stream.");
        }
    }
}
