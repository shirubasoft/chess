# External chess validation

GitHub Actions runs the domain and notation tests, then executes
`tests/validation/validate.py` against the compiled `Chess.Validation` program.
The external job fails on any disagreement, parser error, timeout, missing
reference, or process failure. It uploads `artifacts/validation-report.json`
with the seed, reference versions, corpus checksum, executed checks, and
reproduction data for failures.

## Run locally

On Linux x86-64, from the repository root:

```sh
python3 -m venv artifacts/validation-venv
artifacts/validation-venv/bin/python -m pip install -r tests/validation/requirements.txt
bash tests/validation/fetch-references.sh
dotnet build --configuration Release
dotnet test --no-build --configuration Release
artifacts/validation-venv/bin/python tests/validation/validate.py \
  --stockfish artifacts/stockfish/stockfish/stockfish-ubuntu-x86-64 \
  --corpus artifacts/standard.epd
```

Use `--seed`, `--games`, and `--plies` to reproduce or expand generated games.
The default seed is 20260914, with 12 games of up to 80 plies. A generated game
stops at an automatic terminal outcome. Each inspected position compares the
complete legal move set, every successor FEN, SAN, check status, and supported
position outcomes against python-chess. Separate history checks compare
repetition counts, current and intended draw claims, automatic outcomes, and
serialized event replay. JSON position round trips must preserve all FEN fields
and repetition identity.

## Reference sources

The fetch script pins and checks SHA-256 digests for:

- [Stockfish 17.1](https://github.com/official-stockfish/Stockfish/releases/tag/sf_17.1),
  using the generic Linux x86-64 binary as an independent perft reference.
- [Ethereal's standard perft corpus](https://github.com/AndyGrant/Ethereal/blob/0e47e9b67f345c75eb965d9fb3e2493b6a11d09a/src/perft/standard.epd),
  using the exact linked revision. Every position is checked at depths 1 and 2;
  the starting position and Kiwipete also run at depth 3. The runner compares
  counts per move with Stockfish and checks a published total whenever the
  source supplies that depth. The report identifies which checks have a
  published total. The corpus contains constructed positions and does not
  promise historical reachability.

`requirements.txt` pins the python-chess distribution and its chess package.
This implementation supplies move, notation, and history expectations.
`sample.pgn` is the factual Fischer vs. Spassky game record in section 2.3 of the
[PGN specification](https://www.saremba.de/chessgml/standards/pgn/pgn-complete.htm).
The runner compares this record and generated PGNs against python-chess import.

## Rules and support boundary

`tests/validation/fide-scenarios.json` owns the external golden positions and
their [FIDE article references](https://handbook.fide.com/chapter/E012023).
It exercises pinned attacks, castling constraints, en passant king safety,
promotion choices, checkmate, stalemate, and recognized dead positions. The
runner adds history scenarios around threefold/fivefold repetition and the
50/75-move thresholds, including checkmate precedence. Core TUnit tests exercise
command rejection, draw agreement, resignation, and event ordering.

The forced capture to bare kings has a FIDE-derived expectation because
python-chess's material-only detector does not recognize it. This exception is
attached to that specific fixture. General dead-position reachability remains
outside the [documented detector scope](position-outcomes.md). A passing report
establishes agreement on the exercised cases, not complete FIDE certification.

## Executable adapters

`tools/Chess.Validation` depends on `Chess.Notation` and the core library. Its
default interface accepts one JSON request per stdin line and returns one JSON
response per stdout line. The runner uses operations for position snapshots,
move parsing, match history, PGN import, and perft. All chess decisions call the
production library.

`Chess.Validation --uci` exposes a minimal perft interface for external tools.
It accepts `uci`, `isready`, `ucinewgame`, `position startpos`, six-field
`position fen`, optional UCI move sequences, `go perft N`, and `quit`. It prints
Stockfish-style divide lines and a total. Perft uses legal moves directly and
continues through draw conditions that do not remove legal moves. Errors
terminate this mode with a nonzero exit code so a stale position cannot produce
a misleading successful result.
