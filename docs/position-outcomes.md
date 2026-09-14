# Position outcomes and dead-position scope

`PositionRules.GetOutcome` distinguishes checkmate, stalemate, proven dead
positions, positions with a discovered mating continuation, and undetermined
positions. Checkmate includes the winning side. Invalid king counts return
`InvalidPosition` before outcome detection. Edited setups must otherwise obey the
chess rules; the model does not prove that a board is reachable from the start.

[FIDE Article 5.2.2](https://handbook.fide.com/chapter/e012023) defines deadness by
whether either side could ever mate through legal play, including cooperation.
Failure to force a win does not prove deadness. Two knights against a bare king,
for example, can produce mate with cooperation.

The detector proves bare kings, a single bishop or knight against a bare king,
and positions containing only kings and bishops on one square color. The bishop
rule includes promoted bishops. Opposite-color bishops and additional knights
require analysis of legal continuations.

The continuation search can prove dead positions with other material. For
example, black king a8, white king c6 and white pawn a7, with Black to move, permits
only Kxa7 and then leaves bare kings. Every branch must reach a proven dead
position or stalemate before the search can declare the root dead. Finding any
checkmate proves that a mating continuation exists.

`DeadPositionSearch` bounds depth and positions examined. Its default searches up
to two plies and 256 positions. A depth of zero retains terminal and proven
material checks. The position budget counts the root and each visited successor;
it is a work bound, not a time limit. Depth is capped at 64 to bound recursion.
Cancellation throws `OperationCanceledException`.

Exhausting either limit yields `UndeterminedPosition`. This result permits play
but does not assert that the position is alive. Locked pawn structures and long
forced sequences may remain undetermined. This is a sound, incomplete detector,
not complete adjudication of Article 5.2.2. Consumers that need full adjudication
must resolve undetermined positions separately. Move counters and repetition
claims do not bound the mating-continuation search.
