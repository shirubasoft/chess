# Legal moves

`Position.Initial` is an immutable standard starting position. `MoveRules.Apply`
validates one request and returns the next position or a typed rejection.

`MoveRules.GetLegalMoves(position)` yields `MoveRequest` values lazily. Promotions
include each promotion choice, and castling uses `Castle` requests. A frontend can
enumerate this sequence locally. `MoveRules.HasLegalMove(position)` stops at the
first legal reply. `MoveRules.IsInCheck(position)` queries the side to move; the
overload accepting a `Side` queries that side.

Queries require exactly one king per side and throw `ArgumentException` otherwise.
Enumeration validates this when iteration starts. The position model also permits
edited board setups; king counts do not establish historical reachability.

Move counters do not affect chess legality. Queries can therefore return a move
that `Apply` rejects with `MoveCounterOverflow` when its resulting counters cannot
fit in an integer. Each query reads the immutable position supplied to it, so
partial and repeated enumeration preserve the same board.
