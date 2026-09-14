using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

internal static class MatchFixtures
{
    internal static PositionHistory History(MatchState state) => state switch
    {
        OngoingMatch ongoing => ongoing.History,
        FinishedMatch finished => finished.History
    };

    internal static async Task<MatchEvent> Accept(MatchState state, MatchCommand command)
    {
        var accepted = await Assert.That(Match.Decide(state, command).Value).IsTypeOf<CommandAccepted>().And.IsNotNull();
        return accepted.Event;
    }

    internal static async Task<(MatchState State, MatchEvent Event)> Play(MatchState state, string from, string to)
    {
        var @event = await Accept(state, new PlayMove { Player = History(state).Current.SideToMove, Move = Move(from, to) });
        return (Match.Apply(state, @event), @event);
    }

    internal static ClaimDraw Claim(MatchState state, DrawClaimReason reason, MoveRequest? intended = null) => new()
    {
        Player = History(state).Current.SideToMove,
        Reason = reason,
        Timing = intended is { } move ? new IntendedMoveClaim { Move = move } : DrawClaimTiming.CurrentPosition
    };

    internal static async Task<(MatchState State, List<MatchEvent> Events)> RepeatKnights(int plies)
    {
        var state = Match.Start();
        var events = new List<MatchEvent>();
        (string From, string To)[] cycle = [("g1", "f3"), ("g8", "f6"), ("f3", "g1"), ("f6", "g8")];
        for (var ply = 0; ply < plies; ply++)
        {
            var (from, to) = cycle[ply % 4];
            var next = await Play(state, from, to);
            state = next.State;
            events.Add(next.Event);
        }
        return (state, events);
    }

    internal static async Task AssertDraw(MatchState state, DrawReason reason)
    {
        var finished = await Assert.That(state.Value).IsTypeOf<FinishedMatch>().And.IsNotNull();
        var draw = await Assert.That(finished.Result.Value).IsTypeOf<MatchDrawn>().And.IsNotNull();
        await Assert.That(draw.Reason).IsEqualTo(reason);
    }
}
