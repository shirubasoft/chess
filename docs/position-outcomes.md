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

Resignation checks the opponent's ability to mate using a side-specific material
check. It recognizes a bare king; a lone knight when the resigning side has only
a king and queens; and bishops confined to one square color when all bishops on
the board share that color and there are no pawns or knights. Enemy pieces can
help form a mating net, so a lone minor piece is not always insufficient. For
example, a lone knight can mate a king with a rook through cooperative play.
The same limitation on complex positions applies: this material check does not
prove every case where one side can never mate.
