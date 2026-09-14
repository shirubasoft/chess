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

The notation contracts follow the [PGN/FEN specification](https://www.saremba.de/chessgml/standards/pgn/pgn-complete.htm)
and [Stockfish's UCI documentation](https://github.com/official-stockfish/Stockfish/wiki/UCI-&-Commands).

Run `dotnet test --configuration Release` to execute the notation and domain
tests. GitHub Actions runs this command for pushes and pull requests.
