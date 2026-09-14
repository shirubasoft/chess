using static Chess.Tests.ChessJsonTests;
using static Chess.Tests.MatchFixtures;
using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class DrawAgreementTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EitherPlayerCanOfferAndTheOpponentCanAcceptOnEitherTurn(bool whiteOffers)
    {
        var (state, events) = await RepeatKnights(2);
        var history = History(state);
        Side offerer = whiteOffers ? Side.White : Side.Black;
        Side opponent = whiteOffers ? Side.Black : Side.White;
        state = await Act(state, new OfferDraw { Player = offerer }, events);
        var ongoing = await Assert.That(state.Value).IsTypeOf<OngoingMatch>().And.IsNotNull();
        var pending = await Assert.That(ongoing.DrawOffer.Value).IsTypeOf<PendingDrawOffer>().And.IsNotNull();
        await Assert.That(pending.Player).IsEqualTo(offerer);
        await Assert.That(RoundTrip(ongoing.DrawOffer)).IsEqualTo(ongoing.DrawOffer);
        await Assert.That(ongoing.History).IsSameReferenceAs(history);
        await Assert.That(state.Revision).IsEqualTo(3);
        state = await Act(state, new AcceptDraw { Player = opponent }, events);
        await AssertDraw(state, DrawReason.Agreement);
        await Assert.That(History(state)).IsSameReferenceAs(history);
        await Assert.That(state.Revision).IsEqualTo(4);
        var replayed = Match.Replay(RoundTrip(events));
        await AssertDraw(replayed, DrawReason.Agreement);
        await Assert.That(replayed.Revision).IsEqualTo(state.Revision);
        await Assert.That(History(replayed).Keys.SequenceEqual(history.Keys)).IsTrue();
        await Assert.That(History(replayed).Current.HalfmoveClock).IsEqualTo(2);
        await Assert.That(History(replayed).Current.FullmoveNumber).IsEqualTo(2);
        await AssertFinishedRejectsCommands(state);
        await Assert.That(() => Match.Apply(state, events[2])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MissingOffersOwnRepliesAndDuplicateOffersAreTypedRejections()
    {
        var state = Match.Start();
        await Assert.That(Match.Decide(state, new AcceptDraw { Player = Side.White }).Value).IsTypeOf<NoPendingDrawOffer>();
        await Assert.That(Match.Decide(state, new DeclineDraw { Player = Side.Black }).Value).IsTypeOf<NoPendingDrawOffer>();
        var pending = await Act(state, new OfferDraw { Player = Side.White });
        var ownAccept = await Assert.That(Match.Decide(pending, new AcceptDraw { Player = Side.White }).Value)
            .IsTypeOf<WrongPlayer>().And.IsNotNull();
        await Assert.That(ownAccept.Expected).IsEqualTo((Side)Side.Black);
        await Assert.That(ownAccept.Actual).IsEqualTo((Side)Side.White);
        await Assert.That(Match.Decide(pending, new DeclineDraw { Player = Side.White }).Value).IsTypeOf<WrongPlayer>();
        await Assert.That(Match.Decide(pending, new OfferDraw { Player = Side.White }).Value).IsTypeOf<DrawOfferAlreadyPending>();
        await Assert.That(Match.Decide(pending, new OfferDraw { Player = Side.Black }).Value).IsTypeOf<DrawOfferAlreadyPending>();
        await Assert.That(pending.Revision).IsEqualTo(1);
        await AssertNoOffer(state);
        var declined = await Act(pending, new DeclineDraw { Player = Side.Black });
        await AssertNoOffer(declined);
        await Assert.That(declined.Revision).IsEqualTo(2);
        await Assert.That(History(declined)).IsSameReferenceAs(History(state));
        await Assert.That(Match.Decide(declined, new AcceptDraw { Player = Side.Black }).Value).IsTypeOf<NoPendingDrawOffer>();
        var offeredAgain = await Act(declined, new OfferDraw { Player = Side.Black });
        await Assert.That(offeredAgain.Revision).IsEqualTo(3);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task AgreementRequiresAnActualMoveFromBothPlayers(int plies)
    {
        var (state, _) = await RepeatKnights(plies);
        state = await Act(state, new OfferDraw { Player = Side.Black });
        await Assert.That(Match.Decide(state, new AcceptDraw { Player = Side.White }).Value).IsTypeOf<DrawAgreementUnavailable>();
        await Assert.That(History(state).Keys.Count).IsEqualTo(plies + 1);
        await Assert.That(state.Revision).IsEqualTo(plies + 1);
        var pending = await Assert.That(state.Value).IsTypeOf<OngoingMatch>().And.IsNotNull();
        await Assert.That(pending.DrawOffer.Value).IsTypeOf<PendingDrawOffer>();
    }

    [Test]
    public async Task EarlyOfferCanBeAcceptedAfterTheOffererMakesTheSecondPly()
    {
        var (state, events) = await RepeatKnights(1);
        state = await Act(state, new OfferDraw { Player = Side.Black }, events);
        await Assert.That(Match.Decide(state, new AcceptDraw { Player = Side.White }).Value).IsTypeOf<DrawAgreementUnavailable>();
        state = await Act(state, new PlayMove { Player = Side.Black, Move = Move("g8", "f6") }, events);
        state = await Act(state, new AcceptDraw { Player = Side.White }, events);
        await AssertDraw(Match.Replay(RoundTrip(events)), DrawReason.Agreement);
        await Assert.That(History(state).Keys.Count).IsEqualTo(3);
    }

    [Test]
    public async Task EditedSetupCountersDoNotSubstituteForPlayedMoves()
    {
        var initial = Position.Initial with { SideToMove = Side.Black, FullmoveNumber = 50, HalfmoveClock = 20 };
        var state = Match.Start(initial);
        state = await Act(state, new OfferDraw { Player = Side.White });
        await Assert.That(Match.Decide(state, new AcceptDraw { Player = Side.Black }).Value).IsTypeOf<DrawAgreementUnavailable>();
        state = await Act(state, new DeclineDraw { Player = Side.Black });
        state = await Act(state, new PlayMove { Player = Side.Black, Move = Move("g8", "f6") });
        state = await Act(state, new PlayMove { Player = Side.White, Move = Move("g1", "f3") });
        state = await Act(state, new OfferDraw { Player = Side.Black });
        await AssertDraw(await Act(state, new AcceptDraw { Player = Side.White }), DrawReason.Agreement);
    }

    [Test]
    public async Task OnlyAnAcceptedOpponentMoveImplicitlyDeclinesTheOffer()
    {
        var (state, events) = await RepeatKnights(2);
        state = await Act(state, new OfferDraw { Player = Side.White }, events);
        state = await Act(state, new PlayMove { Player = Side.White, Move = Move("f3", "g1") }, events);
        var pending = await Assert.That(state.Value).IsTypeOf<OngoingMatch>().And.IsNotNull();
        await Assert.That(pending.DrawOffer.Value).IsTypeOf<PendingDrawOffer>();
        await Assert.That(Match.Decide(state, new PlayMove { Player = Side.Black, Move = Move("f6", "f4") }).Value).IsTypeOf<InvalidMovement>();
        await Assert.That(Match.Decide(state, new PlayMove { Player = Side.White, Move = Move("e2", "e4") }).Value).IsTypeOf<WrongPlayer>();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.ThreefoldRepetition)).Value).IsTypeOf<DrawClaimUnavailable>();
        await Assert.That(pending.DrawOffer.Value).IsTypeOf<PendingDrawOffer>();
        await Assert.That(state.Revision).IsEqualTo(4);
        state = await Act(state, new PlayMove { Player = Side.Black, Move = Move("f6", "g8") }, events);
        await AssertNoOffer(state);
        await AssertNoOffer(Match.Replay(RoundTrip(events)));
        await Assert.That(History(state).CurrentOccurrences).IsEqualTo(2);
        await Assert.That(History(state).Keys.Count).IsEqualTo(5);
        await Assert.That(state.Revision).IsEqualTo(5);
        await Assert.That(Match.Decide(state, new AcceptDraw { Player = Side.Black }).Value).IsTypeOf<NoPendingDrawOffer>();
    }

    [Test]
    public async Task RepeatedAndReorderedActionsAtTheSamePlyCannotReplay()
    {
        var (state, moves) = await RepeatKnights(2);
        var offered = await Accept(state, new OfferDraw { Player = Side.White });
        var pending = Match.Apply(state, offered);
        var declined = await Accept(pending, new DeclineDraw { Player = Side.Black });
        var cleared = Match.Apply(pending, declined);
        var offeredAgain = await Accept(cleared, new OfferDraw { Player = Side.Black });
        var agreed = await Accept(Match.Apply(cleared, offeredAgain), new AcceptDraw { Player = Side.White });
        MatchEvent[] events = [.. moves, offered, declined, offeredAgain, agreed];
        var replayed = Match.Replay(RoundTrip(events));
        await AssertDraw(replayed, DrawReason.Agreement);
        await Assert.That(replayed.Revision).IsEqualTo(6);
        await Assert.That(History(replayed).Keys.Count).IsEqualTo(3);
        await Assert.That(() => Match.Replay([.. moves, offered, offered])).Throws<InvalidOperationException>();
        await Assert.That(() => Match.Replay([.. moves, declined, offered])).Throws<InvalidOperationException>();
        await Assert.That(() => Match.Replay([.. moves, offered, offeredAgain])).Throws<InvalidOperationException>();
        await Assert.That(() => Match.Apply(Match.Start(), offered)).Throws<InvalidOperationException>();

        var oppositeOffer = await Act(state, new OfferDraw { Player = Side.Black });
        await Assert.That(() => Match.Apply(oppositeOffer, declined)).Throws<InvalidOperationException>();
        var whiteAcceptance = await Accept(oppositeOffer, new AcceptDraw { Player = Side.White });
        await Assert.That(() => Match.Apply(pending, whiteAcceptance)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OffersDoNotOverrideCheckmateResignationOrValidDrawClaims()
    {
        var state = Match.Start();
        var events = new List<MatchEvent>();
        foreach (var (from, to) in new[] { ("f2", "f3"), ("e7", "e5"), ("g2", "g4") })
        {
            state = await Act(state, new PlayMove { Player = History(state).Current.SideToMove, Move = Move(from, to) }, events);
        }
        state = await Act(state, new OfferDraw { Player = Side.White }, events);
        state = await Act(state, new PlayMove { Player = Side.Black, Move = Move("d8", "h4") }, events);
        var mate = await Assert.That(Match.Replay(RoundTrip(events)).Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var win = await Assert.That(mate.Result.Value).IsTypeOf<MatchWon>().And.IsNotNull();
        await Assert.That(win.Reason).IsEqualTo(WinReason.Checkmate);
        await AssertFinishedRejectsCommands(state);

        var resignationEvents = new List<MatchEvent>();
        var offered = await Act(Match.Start(), new OfferDraw { Player = Side.White }, resignationEvents);
        var resigned = await Act(offered, new Resign { Player = Side.Black }, resignationEvents);
        await AssertFinishedRejectsCommands(Match.Replay(RoundTrip(resignationEvents)));
        await Assert.That(resigned.Revision).IsEqualTo(2);

        var (repeated, repetitionEvents) = await RepeatKnights(8);
        repeated = await Act(repeated, new OfferDraw { Player = Side.Black }, repetitionEvents);
        repeated = await Act(repeated, Claim(repeated, DrawClaimReason.ThreefoldRepetition), repetitionEvents);
        await AssertDraw(Match.Replay(RoundTrip(repetitionEvents)), DrawReason.ThreefoldRepetition);
        await AssertFinishedRejectsCommands(repeated);
    }

    [Test]
    public async Task IntendedDrawClaimAfterAnOfferUsesTheEventRevisionWithoutPlayingAMove()
    {
        var (state, events) = await RepeatKnights(7);
        state = await Act(state, new OfferDraw { Player = Side.White }, events);
        state = await Act(state, Claim(state, DrawClaimReason.ThreefoldRepetition, Move("f6", "g8")), events);
        var replayed = Match.Replay(RoundTrip(events));
        await AssertDraw(replayed, DrawReason.ThreefoldRepetition);
        await Assert.That(replayed.Revision).IsEqualTo(9);
        await Assert.That(History(replayed).Keys.Count).IsEqualTo(8);
        await Assert.That(At(History(replayed).Current, "f6")).IsEqualTo('n');
        await Assert.That(History(replayed).Current.SideToMove).IsEqualTo((Side)Side.Black);
    }

    private static async Task<MatchState> Act(MatchState state, MatchCommand command, List<MatchEvent>? events = null)
    {
        await Assert.That(RoundTrip(command)).IsEqualTo(command);
        var @event = await Accept(state, RoundTrip(command));
        events?.Add(@event);
        return Match.Apply(state, RoundTrip(@event));
    }

    private static async Task AssertNoOffer(MatchState state)
    {
        var ongoing = await Assert.That(state.Value).IsTypeOf<OngoingMatch>().And.IsNotNull();
        await Assert.That(ongoing.DrawOffer.Value).IsTypeOf<NoDrawOffer>();
    }

    private static async Task AssertFinishedRejectsCommands(MatchState state)
    {
        MatchCommand[] commands =
        [
            new OfferDraw { Player = Side.White }, new AcceptDraw { Player = Side.Black },
            new DeclineDraw { Player = Side.White }, new Resign { Player = Side.Black },
            new PlayMove { Player = History(state).Current.SideToMove, Move = Move("e2", "e4") },
            Claim(state, DrawClaimReason.ThreefoldRepetition)
        ];
        foreach (var command in commands)
        {
            await Assert.That(Match.Decide(state, command).Value).IsTypeOf<MatchAlreadyFinished>();
        }
    }
}
