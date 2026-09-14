# Match commands and event replay

`Match.Start()` creates the standard game. Pass an initial `Position` to start
from an edited setup. The initial snapshot counts toward repetition and is
adjudicated immediately.

`MatchState` is either `OngoingMatch` or `FinishedMatch`. Each carries immutable
position history. A finished state carries a `MatchResult`, either `MatchWon`
with winner and reason, or `MatchDrawn` with a draw reason.

`Match.Decide(state, command)` is a pure decision. `PlayMove` validates the named
player and individual move request. `ClaimDraw` checks a threefold or fifty-move
claim for the current position or a declared intended move. `Resign` is available
to either player on either turn. Accepted commands return `CommandAccepted`
containing a `MatchEvent`. Rejections have distinct
cases, including wrong player, unavailable claim, the existing move rejection
types, and an already finished match. Rejections leave the state and history
unchanged.

```csharp
var state = Match.Start();
var request = new PlayMove
{
    Player = Side.White,
    Move = new MovePiece
    {
        From = new Coordinate { File = BoardFile.E, Rank = BoardRank.Two },
        To = new Coordinate { File = BoardFile.E, Rank = BoardRank.Four }
    }
};
if (Match.Decide(state, request) is CommandAccepted accepted)
{
    // Persist accepted.Event, then apply it to the local state.
    state = Match.Apply(state, accepted.Event);
}
```

A `MovePlayed` event contains the move and the adjudicated progress, either play
continues or a win/draw. Applying it advances history and finishes the match in
one operation when needed. A `DrawClaimed` event finishes the match without
playing the intended move or appending a position. Event cases have internal
constructors so domain callers obtain them through accepted decisions.

`Match.Replay(events, initial)` folds the event sequence from the same initial
snapshot. Persist that snapshot alongside the accepted events when using an
edited setup. Replay applies the recorded finish decision rather than searching
for a new outcome after each move. Events carry their ply and previous repetition
key, which reject repeated, reordered, or mismatched predecessors. Replay also
validates the recorded move and rejects any event after a finish. Use the
[JSON options](json.md) to persist and restore the initial position and events.

The finish decision uses [position outcomes](position-outcomes.md) and
[repetition history](repetition.md). A recognized dead position finishes
immediately; an ongoing position permits continued play. Threefold repetition and
100 halfmoves require a claim. Fivefold repetition and 150 halfmoves finish
automatically. Checkmate takes precedence over the 150-halfmove rule, as specified
by [FIDE Article 9.6.2](https://handbook.fide.com/chapter/e012023).

`Resign` records a `PlayerResigned` event with the actor and adjudicated result.
It normally awards the opponent a win with reason `Resignation`. In the
[known cases of insufficient mating material](position-outcomes.md), it produces
`ResignationWithoutMatingMaterial` as a draw, following
[FIDE Article 5.1.2](https://handbook.fide.com/chapter/E012023). Applying or replaying
the event finishes the match without adding a position or changing counters.

Applications implementing clocks, arbiter penalties, or draw offers need commands
and events for those procedures. In particular, a rejected intended-move claim
leaves the domain state
unchanged; over-the-board penalties and the obligation to play the declared move
are procedures for the application to enforce.
