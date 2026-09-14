using static Chess.Tests.MatchFixtures;
using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class MatchTests
{
    [Test]
    public async Task MovesProduceEventsWithoutChangingTheOriginalState()
    {
        var initial = Match.Start();
        var @event = await Accept(initial, new PlayMove { Player = Side.White, Move = Move("e2", "e4") });
        var played = await Assert.That(@event.Value).IsTypeOf<MovePlayed>().And.IsNotNull();
        await Assert.That(played.Ply).IsEqualTo(1);
        await Assert.That(played.Move).IsEqualTo((MoveRequest)Move("e2", "e4"));
        await Assert.That(played.Progress.Value).IsTypeOf<PlayContinues>();
        var next = Match.Apply(initial, @event);
        await Assert.That(next.Value).IsTypeOf<OngoingMatch>();
        await Assert.That(At(History(initial).Current, "e2")).IsEqualTo('P');
        await Assert.That(History(initial).Keys.Count).IsEqualTo(1);
        await Assert.That(At(History(next).Current, "e4")).IsEqualTo('P');
        await Assert.That(History(next).Keys.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FoolsMateFinishesAtomicallyAndReplaysWithTheWinner()
    {
        var state = Match.Start();
        var events = new List<MatchEvent>();
        foreach (var (from, to) in new[] { ("f2", "f3"), ("e7", "e5"), ("g2", "g4"), ("d8", "h4") })
        {
            var next = await Play(state, from, to);
            state = next.State;
            events.Add(next.Event);
        }
        var finished = await Assert.That(state.Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var win = await Assert.That(finished.Result.Value).IsTypeOf<MatchWon>().And.IsNotNull();
        await Assert.That(win.Winner).IsEqualTo((Side)Side.Black);
        await Assert.That(win.Reason).IsEqualTo(WinReason.Checkmate);
        var replayed = await Assert.That(Match.Replay(events).Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        await Assert.That(replayed.Result).IsEqualTo(finished.Result);
        await Assert.That(replayed.History.CurrentKey).IsEqualTo(finished.History.CurrentKey);
        await Assert.That(replayed.History.Keys.SequenceEqual(finished.History.Keys)).IsTrue();
        await Assert.That(replayed.History.Current.HalfmoveClock).IsEqualTo(1);
        await Assert.That(replayed.History.Current.FullmoveNumber).IsEqualTo(3);
        await Assert.That(Match.Decide(state, new PlayMove { Player = Side.White, Move = Move("a2", "a3") }).Value)
            .IsTypeOf<MatchAlreadyFinished>();
        await Assert.That(Match.Decide(state, Claim(state, DrawClaimReason.ThreefoldRepetition)).Value).IsTypeOf<MatchAlreadyFinished>();
        await Assert.That(() => Match.Apply(state, events[0])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StalemateAndDeadMaterialFinishAfterTheAcceptedMove()
    {
        var beforeStalemate = Match.Start(Setup(("f6", 'K'), ("e7", 'Q'), ("h8", 'k')));
        var stalemate = await Play(beforeStalemate, "e7", "f7");
        await AssertDraw(stalemate.State, DrawReason.Stalemate);
        var beforeDead = Match.Start(Setup(("e1", 'K'), ("c4", 'B'), ("e8", 'k'), ("d5", 'n')));
        var dead = await Play(beforeDead, "c4", "d5");
        await AssertDraw(dead.State, DrawReason.DeadPosition);
        await Assert.That(History(dead.State).Current.HalfmoveClock).IsEqualTo(0);
    }

    [Test]
    public async Task TerminalStartingPositionsAreFinished()
    {
        await AssertDraw(Match.Start(Setup(("a1", 'K'), ("h8", 'k'))), DrawReason.DeadPosition);
        await AssertDraw(Match.Start(Setup(("f6", 'K'), ("f7", 'Q'), ("h8", 'k')) with { SideToMove = Side.Black }), DrawReason.Stalemate);
        var mate = Match.Start(Setup(("f6", 'K'), ("g7", 'Q'), ("h8", 'k')) with { SideToMove = Side.Black });
        await Assert.That(mate.Value).IsTypeOf<FinishedMatch>();
        await AssertDraw(Match.Replay([], Setup(("a1", 'K'), ("h8", 'k'))), DrawReason.DeadPosition);
    }

    [Test]
    public async Task WrongPlayerAndIllegalMovesAreTypedRejections()
    {
        var state = Match.Start();
        var wrong = await Assert.That(Match.Decide(state, new PlayMove { Player = Side.Black, Move = Move("e7", "e5") }).Value)
            .IsTypeOf<WrongPlayer>().And.IsNotNull();
        await Assert.That(wrong.Expected).IsEqualTo((Side)Side.White);
        await Assert.That(wrong.Actual).IsEqualTo((Side)Side.Black);
        await Reject<SourceSquareEmpty>(state, Move("e3", "e4"));
        await Reject<WrongSideToMove>(state, Move("e7", "e5"));
        await Reject<FriendlyPieceOnDestination>(state, Move("a1", "b1"));
        await Reject<InvalidMovement>(state, Move("e2", "e5"));
        await Reject<PathBlocked>(state, Move("c1", "h6"));
        await Reject<CastlingUnavailable>(Match.Start(Position.Initial with { WhiteCastlingRights = CastlingRights.None }),
            new Castle { Wing = CastlingWing.KingSide });
        var pinned = Match.Start(Setup(("e1", 'K'), ("e2", 'R'), ("e8", 'r'), ("a8", 'k')));
        await Reject<KingWouldBeInCheck>(pinned, Move("e2", "f2"));
        var checking = Match.Start(Setup(("a1", 'K'), ("e2", 'R'), ("e8", 'k')));
        await Reject<KingCaptureNotAllowed>(checking, Move("e2", "e8"));
        var promotion = Match.Start(Setup(("e1", 'K'), ("a7", 'P'), ("h8", 'k')));
        await Reject<PromotionRequired>(promotion, Move("a7", "a8"));
        await Reject<InvalidPromotion>(state, new Promote { From = Square("e2"), To = Square("e3"), Piece = PromotionPiece.Queen });
        await Assert.That(History(state).Keys.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReplayRejectsRepeatedReorderedAndMismatchedEvents()
    {
        var (state, events) = await RepeatKnights(2);
        await Assert.That(() => Match.Replay([events[1], events[0]])).Throws<InvalidOperationException>();
        await Assert.That(() => Match.Replay([events[0], events[0]])).Throws<InvalidOperationException>();
        await Assert.That(() => Match.Apply(state, events[0])).Throws<InvalidOperationException>();
        var alternative = await Play(Match.Start(), "e2", "e4");
        await Assert.That(() => Match.Apply(alternative.State, events[1])).Throws<InvalidOperationException>();
        await Assert.That(History(Match.Replay(events)).Keys.SequenceEqual(History(state).Keys)).IsTrue();
    }

    [Test]
    public async Task SpecialMovesReplayBoardAndMetadata()
    {
        var castleInitial = Setup(("e1", 'K'), ("h1", 'R'), ("e8", 'k')) with { WhiteCastlingRights = CastlingRights.KingSide };
        var castle = await Accept(Match.Start(castleInitial), new PlayMove { Player = Side.White, Move = new Castle { Wing = CastlingWing.KingSide } });
        var castled = History(Match.Replay([castle], castleInitial)).Current;
        await Assert.That(At(castled, "g1")).IsEqualTo('K');
        await Assert.That(At(castled, "f1")).IsEqualTo('R');
        await Assert.That(castled.WhiteCastlingRights.Value).IsTypeOf<NoCastlingRights>();

        var promotionInitial = Setup(("e1", 'K'), ("a7", 'P'), ("h8", 'k'));
        var promotion = await Accept(Match.Start(promotionInitial), new PlayMove
        {
            Player = Side.White, Move = new Promote { From = Square("a7"), To = Square("a8"), Piece = PromotionPiece.Knight }
        });
        var promoted = Match.Replay([promotion], promotionInitial);
        await Assert.That(At(History(promoted).Current, "a8")).IsEqualTo('N');
        await AssertDraw(promoted, DrawReason.DeadPosition);

        var enPassantInitial = FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        var enPassant = await Play(Match.Start(enPassantInitial), "e5", "d6");
        var captured = History(Match.Replay([enPassant.Event], enPassantInitial)).Current;
        await Assert.That(At(captured, "d5")).IsEqualTo('.');
        await Assert.That(At(captured, "d6")).IsEqualTo('P');
        await Assert.That(captured.EnPassant.Value).IsTypeOf<NoEnPassant>();
        await Assert.That(captured.HalfmoveClock).IsEqualTo(0);
    }

    private static async Task Reject<T>(MatchState state, MoveRequest move) where T : class
    {
        var command = new PlayMove { Player = History(state).Current.SideToMove, Move = move };
        await Assert.That(Match.Decide(state, command).Value).IsTypeOf<T>();
    }
}
