using static Chess.Tests.MatchFixtures;
using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class DrawClaimTests
{
    [Test]
    public async Task ThreefoldRequiresAClaimAndFivefoldFinishesAutomatically()
    {
        var (threefold, events) = await RepeatKnights(8);
        await Assert.That(threefold.Value).IsTypeOf<OngoingMatch>();
        await Assert.That(History(threefold).CurrentOccurrences).IsEqualTo(3);
        var claim = await Accept(threefold, Claim(threefold, DrawClaimReason.ThreefoldRepetition));
        var finished = Match.Apply(threefold, claim);
        await AssertDraw(finished, DrawReason.ThreefoldRepetition);
        await Assert.That(History(finished)).IsSameReferenceAs(History(threefold));
        events.Add(claim);
        await AssertDraw(Match.Replay(events), DrawReason.ThreefoldRepetition);
        await Assert.That(Match.Decide(finished, Claim(finished, DrawClaimReason.ThreefoldRepetition)).Value).IsTypeOf<MatchAlreadyFinished>();

        var (fivefold, repeatedEvents) = await RepeatKnights(16);
        await AssertDraw(fivefold, DrawReason.FivefoldRepetition);
        await Assert.That(History(fivefold).CurrentOccurrences).IsEqualTo(5);
        await AssertDraw(Match.Replay(repeatedEvents), DrawReason.FivefoldRepetition);
        await Assert.That(Match.Decide(fivefold, new PlayMove { Player = Side.White, Move = Move("e2", "e4") }).Value).IsTypeOf<MatchAlreadyFinished>();
    }

    [Test]
    public async Task IntendedRepetitionClaimDoesNotPlayTheDeclaredMove()
    {
        var (state, events) = await RepeatKnights(7);
        var before = History(state);
        await Assert.That(before.IsThreefoldRepetition).IsFalse();
        var @event = await Accept(state, Claim(state, DrawClaimReason.ThreefoldRepetition, Move("f6", "g8")));
        var claimed = await Assert.That(@event.Value).IsTypeOf<DrawClaimed>().And.IsNotNull();
        await Assert.That(claimed.Timing.Value).IsTypeOf<IntendedMoveClaim>();
        await Assert.That(claimed.Ply).IsEqualTo(7);
        var finished = Match.Apply(state, @event);
        await AssertDraw(finished, DrawReason.ThreefoldRepetition);
        await Assert.That(History(finished)).IsSameReferenceAs(before);
        await Assert.That(At(History(finished).Current, "f6")).IsEqualTo('n');
        await Assert.That(History(finished).Current.SideToMove).IsEqualTo((Side)Side.Black);
        events.Add(@event);
        var replayed = Match.Replay(events);
        await AssertDraw(replayed, DrawReason.ThreefoldRepetition);
        await Assert.That(History(replayed).Keys.Count).IsEqualTo(8);
        await Assert.That(History(replayed).CurrentKey).IsEqualTo(before.CurrentKey);
    }

    [Test]
    public async Task UnavailableAndIllegalClaimsAreRejectedWithoutChangingHistory()
    {
        var state = Match.Start();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.ThreefoldRepetition)).Value).IsTypeOf<DrawClaimUnavailable>();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.FiftyMoveRule)).Value).IsTypeOf<DrawClaimUnavailable>();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.ThreefoldRepetition, Move("e2", "e4"))).Value).IsTypeOf<DrawClaimUnavailable>();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.ThreefoldRepetition, Move("e2", "e5"))).Value).IsTypeOf<InvalidMovement>();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.ThreefoldRepetition) with { Player = Side.Black }).Value).IsTypeOf<WrongPlayer>();
        await Assert.That(Match.Decide(state, Claim(state, (DrawClaimReason)42)).Value).IsTypeOf<DrawClaimUnavailable>();
        await Assert.That(History(state).Keys.Count).IsEqualTo(1);
        await Assert.That(History(state).CurrentKey).IsEqualTo(PositionKey.Create(Position.Initial));
    }

    [Test]
    public async Task FiftyMoveClaimSupportsCurrentAndIntendedPositions()
    {
        var initial = Position.Initial with { HalfmoveClock = 99 };
        var state = Match.Start(initial);
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.FiftyMoveRule)).Value).IsTypeOf<DrawClaimUnavailable>();
        var intended = await Accept(state, Claim(state, DrawClaimReason.FiftyMoveRule, Move("g1", "f3")));
        await AssertDraw(Match.Apply(state, intended), DrawReason.FiftyMoveRule);
        await AssertDraw(Match.Replay([intended], initial), DrawReason.FiftyMoveRule);
        await Assert.That(History(Match.Apply(state, intended)).Current.HalfmoveClock).IsEqualTo(99);

        var next = await Play(state, "g1", "f3");
        await Assert.That(next.State.Value).IsTypeOf<OngoingMatch>();
        await Assert.That(History(next.State).Current.HalfmoveClock).IsEqualTo(100);
        var current = await Accept(next.State, Claim(next.State, DrawClaimReason.FiftyMoveRule));
        await AssertDraw(Match.Apply(next.State, current), DrawReason.FiftyMoveRule);
        await AssertDraw(Match.Replay([next.Event, current], initial), DrawReason.FiftyMoveRule);
    }

    [Test]
    public async Task PawnMovesAndCapturesResetTheClaimClock()
    {
        var state = Match.Start(Position.Initial with { HalfmoveClock = 99 });
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.FiftyMoveRule, Move("e2", "e4"))).Value).IsTypeOf<DrawClaimUnavailable>();
        var pawnMove = await Play(state, "e2", "e4");
        await Assert.That(History(pawnMove.State).Current.HalfmoveClock).IsEqualTo(0);
        var capture = Match.Start(Setup(("a1", 'K'), ("a2", 'R'), ("h8", 'k'), ("a7", 'r')) with { HalfmoveClock = 99 });
        await Assert.That(Match.Decide(capture, Claim(capture, DrawClaimReason.FiftyMoveRule, Move("a2", "a7"))).Value).IsTypeOf<DrawClaimUnavailable>();
        var captured = await Play(capture, "a2", "a7");
        await Assert.That(History(captured.State).Current.HalfmoveClock).IsEqualTo(0);
    }

    [Test]
    public async Task SeventyFiveMovesFinishAutomaticallyButCheckmateTakesPrecedence()
    {
        var initial = Position.Initial with { HalfmoveClock = 149 };
        var before = Match.Start(initial);
        await Assert.That(before.Value).IsTypeOf<OngoingMatch>();
        var next = await Play(before, "g1", "f3");
        await AssertDraw(next.State, DrawReason.SeventyFiveMoveRule);
        await AssertDraw(Match.Replay([next.Event], initial), DrawReason.SeventyFiveMoveRule);
        await AssertDraw(Match.Start(Position.Initial with { HalfmoveClock = 150 }), DrawReason.SeventyFiveMoveRule);

        var beforeMate = Match.Start(Setup(("f6", 'K'), ("g6", 'Q'), ("h8", 'k')) with { HalfmoveClock = 149 });
        var mate = await Play(beforeMate, "g6", "g7");
        var finished = await Assert.That(mate.State.Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var won = await Assert.That(finished.Result.Value).IsTypeOf<MatchWon>().And.IsNotNull();
        await Assert.That(won.Winner).IsEqualTo((Side)Side.White);
        await Assert.That(History(mate.State).Current.HalfmoveClock).IsEqualTo(150);
    }
}
