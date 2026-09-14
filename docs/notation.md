# Chess notation

`Chess.Notation` references the core `Chess` library and owns text conversion.
`Fen.Parse(text)` returns `NotationResult<Position>` and
`Uci.Parse(position, text)` returns `NotationResult<MoveRequest>`. Match
`Parsed<T>` or `NotationError` to handle input. `OrThrow()` returns the parsed
value or throws `NotationException`, which carries the structured error.

## FEN positions

`Fen.Format(position)` writes all six standard FEN fields. Parsing checks rank
widths, symbols, counters, king presence, pawn promotion ranks, castling setup,
and en passant setup. It rejects a position with the side that just moved still
in check. These checks protect the core's valid-position precondition; they do
not prove historical reachability from the initial position.

The en passant field preserves the raw target after a double pawn push even
when a capture is unavailable. Repetition identity applies its own legal-capture
normalization. Counters use the domain's nonnegative halfmove and positive
fullmove `Int32` ranges.

## UCI moves

UCI moves use source and destination squares with a lowercase promotion suffix,
such as `e2e4` or `a7a8n`. Standard castling uses the king's move, such as
`e1g1`; the parser produces the domain's `Castle` request. Parsing verifies
legality in the supplied position. `Uci.Format(position, move)` encodes a domain
request; callers supply a legal move when exporting play. Null moves and
Chess960 castling are outside standard board play.

## SAN moves

`San.Parse(position, text)` resolves SAN against the legal move set.
`San.Format(position, move)` produces canonical SAN, including the minimum
source disambiguation, captures, promotions, and check or mate suffix. Pinned
pieces that cannot legally reach the destination do not require disambiguation.
Import accepts omitted check suffixes and zeroes in castling; supplied suffixes
must describe the actual result. Ambiguous moves have a distinct error kind.

## PGN games

`Pgn.Parse(text)` reads one game. `Pgn.ReadGames(text)` lazily yields game results
from a string and stops after the first error. Errors identify the offending
token's character offset. A parsed game contains tags, the initial position,
the recorded result, and an immutable move tree with comments, numeric
annotations, and recursive variations. Every variation starts before the move
it replaces and undergoes the same legality checks as the mainline. Nesting is
limited to 64 levels.

PGN import supports brace and semicolon comments, annotation suffixes, tag
escapes, move numbers, percent escape lines, and SetUp/FEN games. A result marker
is required and must agree with the Result tag when present. Move numbers must
match the position. A FEN tag requires SetUp 1. Variant tags select standard
chess only.

The PGN result is recorded metadata. Move legality is checked with `MoveRules`;
applications use `Match` for adjudication and claims. A recorded win or draw does
not identify which command caused it, so import preserves the result without
inventing resignation or agreement events.

The notation contracts follow the [PGN/FEN specification](https://www.saremba.de/chessgml/standards/pgn/pgn-complete.htm)
and [Stockfish's UCI documentation](https://github.com/official-stockfish/Stockfish/wiki/UCI-&-Commands).

Run `dotnet test --configuration Release` to execute the notation and domain
tests. GitHub Actions runs this command for pushes and pull requests.
