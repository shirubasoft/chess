using System.Text.Json;
using static Chess.Tests.MatchFixtures;
using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class ChessJsonTests
{
    private static readonly JsonSerializerOptions Options = ChessJson.CreateOptions();

    [Test]
    public async Task PositionPreservesBoardRightsCountersAndRawEnPassant()
    {
        var played = await Result<Position>(MoveRules.Apply(Position.Initial, Move("e2", "e4")));
        var restored = RoundTrip(played);
        await Assert.That(restored.Board).IsNotSameReferenceAs(played.Board);
        await Assert.That(PositionKey.Create(restored)).IsEqualTo(PositionKey.Create(played));
        await Assert.That(restored.EnPassant).IsEqualTo(played.EnPassant);
        await Assert.That(restored.EnPassant.Value).IsTypeOf<EnPassantTarget>();
        await Assert.That(PositionKey.Create(restored).EnPassant.Value).IsTypeOf<NoEnPassant>();
        await Assert.That(restored.HalfmoveClock).IsEqualTo(0);
        await Assert.That(restored.FullmoveNumber).IsEqualTo(1);
        await Assert.That(restored.SideToMove).IsEqualTo((Side)Side.Black);
        await Assert.That(At(restored, "e4")).IsEqualTo('P');
        await Assert.That(At(restored, "e2")).IsEqualTo('.');
        var initial = RoundTrip(Position.Initial);
        await Assert.That(MoveRules.GetLegalMoves(initial).Count()).IsEqualTo(20);
        await Assert.That(PositionKey.Create(initial)).IsEqualTo(PositionKey.Create(Position.Initial));
    }

    [Test]
    public async Task SerializedCheckmateEventsReplayTheWinnerAndCompleteHistory()
    {
        var state = Match.Start();
        var events = new List<MatchEvent>();
        foreach (var (from, to) in new[] { ("f2", "f3"), ("e7", "e5"), ("g2", "g4"), ("d8", "h4") })
        {
            var next = await Play(state, from, to);
            state = next.State;
            events.Add(next.Event);
        }
        var replayed = await Assert.That(Match.Replay(RoundTrip(events)).Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var win = await Assert.That(replayed.Result.Value).IsTypeOf<MatchWon>().And.IsNotNull();
        await Assert.That(win.Winner).IsEqualTo((Side)Side.Black);
        await Assert.That(win.Reason).IsEqualTo(WinReason.Checkmate);
        await Assert.That(RoundTrip(replayed.Result)).IsEqualTo(replayed.Result);
        await Assert.That(replayed.History.Keys.SequenceEqual(History(state).Keys)).IsTrue();
        await Assert.That(replayed.History.Current.HalfmoveClock).IsEqualTo(1);
        await Assert.That(replayed.History.Current.FullmoveNumber).IsEqualTo(3);
        var concrete = await Assert.That(events[0].Value).IsTypeOf<MovePlayed>().And.IsNotNull();
        await Assert.That(RoundTrip(concrete).PreviousKey).IsEqualTo(concrete.PreviousKey);
        await Assert.That(RoundTrip(concrete).Move).IsEqualTo(concrete.Move);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SerializedClaimsPreserveTimingWithoutPlayingTheIntendedMove(bool intended)
    {
        var (state, events) = await RepeatKnights(intended ? 7 : 8);
        MatchCommand command = Claim(state, DrawClaimReason.ThreefoldRepetition, intended ? (MoveRequest?)Move("f6", "g8") : null);
        await Assert.That(RoundTrip(command)).IsEqualTo(command);
        events.Add(await Accept(state, RoundTrip(command)));
        var replayed = Match.Replay(RoundTrip(events));
        await AssertDraw(replayed, DrawReason.ThreefoldRepetition);
        await Assert.That(History(replayed).Keys.SequenceEqual(History(state).Keys)).IsTrue();
        await Assert.That(History(replayed).Current.SideToMove).IsEqualTo(History(state).Current.SideToMove);
    }

    [Test]
    [Arguments("kingSide")]
    [Arguments("queenSide")]
    public async Task SerializedCastlingPreservesTheWingAndRights(string wing)
    {
        var kingSide = wing == "kingSide";
        var initial = Setup(("e1", 'K'), (kingSide ? "h1" : "a1", 'R'), ("e8", 'k')) with
        {
            WhiteCastlingRights = kingSide ? CastlingRights.KingSide : CastlingRights.QueenSide
        };
        MatchCommand command = new PlayMove
        {
            Player = Side.White,
            Move = new Castle { Wing = kingSide ? CastlingWing.KingSide : CastlingWing.QueenSide }
        };
        var @event = await Accept(Match.Start(RoundTrip(initial)), RoundTrip(command));
        var replayed = History(Match.Replay(RoundTrip(new[] { @event }), RoundTrip(initial))).Current;
        await Assert.That(At(replayed, kingSide ? "g1" : "c1")).IsEqualTo('K');
        await Assert.That(At(replayed, kingSide ? "f1" : "d1")).IsEqualTo('R');
        await Assert.That(replayed.WhiteCastlingRights.Value).IsTypeOf<NoCastlingRights>();
    }

    [Test]
    [Arguments("queen", 'Q')]
    [Arguments("rook", 'R')]
    [Arguments("bishop", 'B')]
    [Arguments("knight", 'N')]
    public async Task PromotionWireValuesReplayTheChosenPiece(string piece, char expected)
    {
        PromotionPiece chosen = piece switch
        {
            "queen" => PromotionPiece.Queen, "rook" => PromotionPiece.Rook,
            "bishop" => PromotionPiece.Bishop, _ => PromotionPiece.Knight
        };
        MoveRequest move = new Promote { From = Square("a7"), To = Square("a8"), Piece = chosen };
        var initial = Setup(("e1", 'K'), ("a7", 'P'), ("h8", 'k'));
        var @event = await Accept(Match.Start(initial), new PlayMove { Player = Side.White, Move = RoundTrip(move) });
        var replayed = Match.Replay(RoundTrip(new[] { @event }), RoundTrip(initial));
        await Assert.That(At(History(replayed).Current, "a8")).IsEqualTo(expected);
        if (expected is 'B' or 'N')
        {
            await AssertDraw(replayed, DrawReason.DeadPosition);
        }
    }

    [Test]
    public async Task EnPassantRoundTripPreservesTheLegalTargetAndCapture()
    {
        var initial = FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 12");
        var key = PositionKey.Create(initial);
        await Assert.That(RoundTrip(key)).IsEqualTo(key);
        await Assert.That(RoundTrip(key).EnPassant.Value).IsTypeOf<EnPassantTarget>();
        var next = await Play(Match.Start(RoundTrip(initial)), "e5", "d6");
        var replayed = History(Match.Replay(RoundTrip(new[] { next.Event }), RoundTrip(initial))).Current;
        await Assert.That(At(replayed, "d5")).IsEqualTo('.');
        await Assert.That(At(replayed, "d6")).IsEqualTo('P');
        await Assert.That(replayed.FullmoveNumber).IsEqualTo(12);
    }

    [Test]
    public async Task CommandsUseNativeUnionContractsAndRetainSingletonIdentity()
    {
        MatchCommand command = new PlayMove { Player = Side.White, Move = Move("e2", "e4") };
        var json = JsonSerializer.Serialize(command, Options);
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("case").GetString()).IsEqualTo("PlayMove");
        await Assert.That(document.RootElement.GetProperty("move").GetProperty("case").GetString()).IsEqualTo("MovePiece");
        await Assert.That(Options.GetTypeInfo(typeof(MatchCommand)).Kind).IsEqualTo(System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Union);
        await Assert.That(RoundTrip(command)).IsEqualTo(command);
        await Assert.That(RoundTrip((Side)Side.White).Value).IsSameReferenceAs(Side.White);
        await Assert.That(RoundTrip((Side)Side.Black).Value).IsSameReferenceAs(Side.Black);
        await Assert.That(RoundTrip((Piece)Piece.Knight).Value).IsSameReferenceAs(Piece.Knight);
    }

    [Test]
    [Arguments("""{"case":"unknown"}""")]
    [Arguments("""{"case":"PlayMove","player":{"case":"White"}}""")]
    [Arguments("""{"case":"PlayMove","player":{"case":"Green"},"move":{}}""")]
    [Arguments("""{"case":"PlayMove","player":{"case":"White"},"move":null}""")]
    public async Task InvalidCommandsAreRejectedAtTheJsonBoundary(string json)
    {
        await Assert.That(() => JsonSerializer.Deserialize<MatchCommand>(json, Options)).Throws<JsonException>();
    }

    [Test]
    public async Task CommandResultsAndPositionOutcomesRoundTrip()
    {
        var state = Match.Start();
        var decision = Match.Decide(state, new PlayMove { Player = Side.White, Move = Move("e2", "e4") });
        var accepted = await Assert.That(RoundTrip(decision).Value).IsTypeOf<CommandAccepted>().And.IsNotNull();
        await Assert.That(At(History(Match.Apply(state, accepted.Event)).Current, "e4")).IsEqualTo('P');
        var rejected = Match.Decide(state, new PlayMove { Player = Side.Black, Move = Move("e7", "e5") });
        await Assert.That(RoundTrip(rejected)).IsEqualTo(rejected);
        var mate = PositionRules.GetOutcome(Setup(("f6", 'K'), ("g7", 'Q'), ("h8", 'k')) with { SideToMove = Side.Black });
        await Assert.That(RoundTrip(mate)).IsEqualTo(mate);
        await Assert.That(RoundTrip(PositionOutcome.DeadPosition)).IsSameReferenceAs(PositionOutcome.DeadPosition);
    }

    [Test]
    public async Task EventsRequireTheirPayloadAndPredecessor()
    {
        await Assert.That(() => JsonSerializer.Deserialize<MatchEvent>("""{"case":"MovePlayed"}""", Options)).Throws<JsonException>();
        var next = await Play(Match.Start(), "e2", "e4");
        var json = JsonSerializer.Serialize(next.Event, Options);
        using var document = JsonDocument.Parse(json);
        var key = document.RootElement.GetProperty("previousKey").GetRawText();
        await Assert.That(() => JsonSerializer.Deserialize<MatchEvent>(json.Replace(key, "null"), Options)).Throws<JsonException>();
        await Assert.That(() => Match.Replay(RoundTrip(new[] { next.Event, next.Event }))).Throws<InvalidOperationException>();
    }

    internal static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
