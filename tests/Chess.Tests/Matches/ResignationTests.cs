using static Chess.Tests.ChessJsonTests;
using static Chess.Tests.MatchFixtures;
using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class ResignationTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EitherPlayerCanResignRegardlessOfWhoseTurnItIs(bool white)
    {
        var (state, events) = await RepeatKnights(3);
        Side player = white ? Side.White : Side.Black;
        MatchCommand command = new Resign { Player = player };
        await Assert.That(RoundTrip(command)).IsEqualTo(command);
        var @event = await Accept(state, RoundTrip(command));
        var resigned = await Assert.That(@event.Value).IsTypeOf<PlayerResigned>().And.IsNotNull();
        await Assert.That(resigned.Player).IsEqualTo(player);
        await Assert.That(resigned.Ply).IsEqualTo(3);
        var finished = await Assert.That(Match.Apply(state, @event).Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var won = await Assert.That(finished.Result.Value).IsTypeOf<MatchWon>().And.IsNotNull();
        await Assert.That(won.Winner).IsEqualTo(white ? (Side)Side.Black : Side.White);
        await Assert.That(won.Reason).IsEqualTo(WinReason.Resignation);
        await Assert.That(finished.History).IsSameReferenceAs(History(state));
        events.Add(@event);
        var replayed = await Assert.That(Match.Replay(RoundTrip(events)).Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        await Assert.That(replayed.Result).IsEqualTo(finished.Result);
        await Assert.That(replayed.History.Keys.SequenceEqual(finished.History.Keys)).IsTrue();
        await Assert.That(Match.Decide(finished, command).Value).IsTypeOf<MatchAlreadyFinished>();
        await Assert.That(Match.Decide(finished, new PlayMove { Player = Side.Black, Move = Move("f6", "g8") }).Value).IsTypeOf<MatchAlreadyFinished>();
        await Assert.That(Match.Decide(finished, Claim(finished, DrawClaimReason.ThreefoldRepetition)).Value).IsTypeOf<MatchAlreadyFinished>();
        await Assert.That(() => Match.Apply(finished, @event)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("7k/8/8/8/8/8/R7/K7 w - - 7 12")]
    [Arguments("7k/8/5n2/8/8/8/Q7/K7 w - - 7 12")]
    [Arguments("7k/8/5b2/8/8/8/R7/K7 w - - 7 12")]
    [Arguments("7k/8/5b2/8/8/8/Q7/K7 w - - 7 12")]
    [Arguments("7k/8/5b2/8/3b4/8/Q7/K7 w - - 7 12")]
    public async Task ResignationDrawsWhenTheOpponentHasKnownInsufficientMatingMaterial(string fen)
    {
        var initial = FromFen(fen);
        var state = Match.Start(initial);
        await Assert.That(state.Value).IsTypeOf<OngoingMatch>();
        var @event = await Accept(state, new Resign { Player = Side.White });
        await AssertDraw(Match.Apply(state, @event), DrawReason.ResignationWithoutMatingMaterial);
        var replayed = Match.Replay(RoundTrip(new[] { @event }), RoundTrip(initial));
        await AssertDraw(replayed, DrawReason.ResignationWithoutMatingMaterial);
        await Assert.That(History(replayed).Keys.Count).IsEqualTo(1);
        await Assert.That(History(replayed).Current.HalfmoveClock).IsEqualTo(7);
        await Assert.That(History(replayed).Current.FullmoveNumber).IsEqualTo(12);
    }

    [Test]
    [Arguments("7k/8/5n2/8/8/8/R7/K7 w - - 0 1")]
    [Arguments("7k/8/5n2/8/8/8/P7/K7 w - - 0 1")]
    [Arguments("7k/8/5b2/8/8/8/P7/K7 w - - 0 1")]
    [Arguments("7k/8/5b2/8/8/8/N7/K7 w - - 0 1")]
    [Arguments("7k/8/5b2/8/8/8/2B5/K7 w - - 0 1")]
    [Arguments("7k/8/5n2/3n4/8/8/8/K7 w - - 0 1")]
    [Arguments("7k/8/5b2/3n4/8/8/8/K7 w - - 0 1")]
    [Arguments("7k/8/5b2/3b4/8/8/8/K7 w - - 0 1")]
    [Arguments("7k/8/5p2/8/8/8/8/K7 w - - 0 1")]
    public async Task PossibleCooperativeMatesStillWinByResignation(string fen)
    {
        var state = Match.Start(FromFen(fen));
        var @event = await Accept(state, new Resign { Player = Side.White });
        var finished = await Assert.That(Match.Apply(state, @event).Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var won = await Assert.That(finished.Result.Value).IsTypeOf<MatchWon>().And.IsNotNull();
        await Assert.That(won.Winner).IsEqualTo((Side)Side.Black);
        await Assert.That(won.Reason).IsEqualTo(WinReason.Resignation);
    }

    [Test]
    public async Task BlackResigningAgainstABareWhiteKingAlsoDraws()
    {
        var state = Match.Start(Setup(("a1", 'K'), ("h8", 'k'), ("h7", 'r')));
        await AssertDraw(Match.Apply(state, await Accept(state, new Resign { Player = Side.Black })), DrawReason.ResignationWithoutMatingMaterial);
    }

    [Test]
    public async Task ResignationEventsRequireTheirOriginalPosition()
    {
        var (state, events) = await RepeatKnights(2);
        var resign = await Accept(state, new Resign { Player = Side.White });
        await Assert.That(() => Match.Apply(Match.Start(), resign)).Throws<InvalidOperationException>();
        var alternative = await Play(Match.Start(), "e2", "e4");
        var otherPosition = await Play(alternative.State, "e7", "e5");
        await Assert.That(() => Match.Apply(otherPosition.State, resign)).Throws<InvalidOperationException>();
        events.Add(resign);
        events.Add(resign);
        await Assert.That(() => Match.Replay(events)).Throws<InvalidOperationException>();
    }
}
