# Match commands and event replay

`Match.Start()` creates the standard game. Pass an initial `Position` to start
from an edited setup. The initial snapshot counts toward repetition and is
adjudicated immediately.

`MatchState` is either `OngoingMatch` or `FinishedMatch`. Each carries immutable
position history and an event revision. An ongoing match also carries
`DrawOfferState`, either `NoDrawOffer` or `PendingDrawOffer` with its offering
player. A finished state carries a `MatchResult`, either `MatchWon` with winner
and reason, or `MatchDrawn` with a draw reason.

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
for a new outcome after each move. Events carry their revision, ply, and previous
repetition key. Revisions start at one and advance on every accepted event,
including offers and declines that leave the board unchanged. `MatchState.Revision`
starts at zero. Together these fields reject repeated, reordered, or mismatched
predecessors. Replay also validates recorded moves and draw-offer responses and
rejects any event after a finish. Use the
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

`OfferDraw` records `DrawOffered` and preserves the position. The opponent can
send `AcceptDraw` to record `DrawAgreed` and finish with reason `Agreement`, or
`DeclineDraw` to record `DrawOfferDeclined`. These commands can be sent on either
turn. Acceptance requires both players to have made a move in this match;
starting-position counters do not satisfy that requirement. This follows
[FIDE Articles 5.2.3 and 9.1.2.1](https://handbook.fide.com/chapter/E012023).

An offer remains pending through the offering player's move. An accepted move by
the opponent declines it automatically. Illegal moves and rejected commands
leave it pending. The offering player cannot accept or decline their own offer,
and either player's attempt to add another offer returns `DrawOfferAlreadyPending`.
A response without an offer returns `NoPendingDrawOffer`; acceptance before both
players have moved returns `DrawAgreementUnavailable`. A terminal move, a valid
claim, or resignation can still finish a match with an offer pending.

Clock expiration and over-the-board adjudication procedures are outside this
model. Rejected draw claims leave state unchanged. Applications applying arbiter
penalties, the obligation to play an intended move, or treating an unsuccessful
claim as a draw offer under Article 9.1.2.3 must handle those procedures; explicit
offers use `OfferDraw`.
