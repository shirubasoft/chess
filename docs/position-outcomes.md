# Position outcomes and dead-position scope

`PositionRules.GetOutcome` returns checkmate, stalemate, a recognized dead
position, or `OngoingPosition`. Checkmate includes the winning side. The ordinary
reply check stops at the first legal move.

[FIDE Article 5.2.2](https://handbook.fide.com/chapter/e012023) defines deadness by
whether either side could ever mate through legal play, including cooperation.
Failure to force a win does not prove deadness. Two knights against a bare king,
for example, can produce mate with cooperation.

The detector recognizes these proven cases:

- Bare kings.
- A single bishop or knight against a bare king.
- Kings and bishops with every bishop on the same square color, including
  promoted bishops.
- A king whose only legal move captures the last non-king piece, leaving bare
  kings. For example, Black's king on a8, White's king on c6, and a white pawn on
  a7, with Black to move, permits only Kxa7.

The forced-capture case checks for a second legal move only when the first move
would leave bare kings. These are direct checks of the current position; outcome
queries do not search future move trees.

`OngoingPosition` means no supported terminal condition was recognized. The
starting position, ordinary play, and unsupported dead configurations return
this case. Locked pawn structures and other dead positions outside the listed
cases require separate adjudication for complete Article 5.2.2 support.
