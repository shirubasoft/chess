# Chess CLI

Install the packaged tool and run `dotnet chess`. It uses Terminal.Gui v2 for an interactive terminal board and the shared `Chess.Client` for server requests. Every game shows the client name for each side. The CLI identifies itself as `Chess CLI`.

```sh
dotnet pack clients/Chess.Cli -c Release -o artifacts/packages
dotnet tool install --global Chess.Cli --add-source artifacts/packages
dotnet chess --server http://localhost:5000
```

Use the game server endpoint from the Aspire dashboard. The example port is illustrative. The server URL is saved with the current game; `--server URL` or `CHESS_SERVER_URL` can select another server. Changing servers clears the current game for that invocation.

## Terminal board

Run `dotnet chess` or `dotnet chess tui` in a terminal of at least 80 columns and 24 rows, with a font that supports Unicode chess pieces. The drawn checkerboard faces the current player's side, with outlined White pieces and filled Black pieces. Tab selects fields and buttons. Enter activates the focused control. Escape leaves the terminal UI, keeping the game in the session file.

In a terminal with mouse reporting, hold the left mouse button on a movable piece and drag it to a highlighted destination. Release to play. Right-click, press Escape, or drop outside the board to cancel the drag. A promotion opens a chooser for queen, rook, bishop, or knight. The board accepts moves only on your turn and updates after the server accepts the move.

Create a game and share only the displayed opponent code. Paste a side's code into Join to join or resume that side. Random finds an opponent without exchanging a code. Enter SAN such as `Nf3`, `O-O`, or `a8=Q`, or UCI such as `g1f3` or `a7a8q`. The TUI detects UCI coordinate notation. Draw offers, acceptance, refusal, claims, resignation, and move history are available on the board. It polls while open and restores the latest server state when reopened.

## Commands for automation

Every command runs without interactive prompts and writes one JSON value to stdout. Errors are JSON on stderr. No terminal control sequences appear in command output.

```sh
# The response contains your code and the opponentCode to share.
dotnet chess create --server http://localhost:5000 --session ./white.json

dotnet chess join OPPONENT_CODE --server http://localhost:5000 --session ./black.json

dotnet chess move e4 --session ./white.json
dotnet chess move e7e5 --notation uci --session ./black.json
dotnet chess wait --session ./white.json --timeout 60
dotnet chess history --session ./white.json
```

`resume CODE` restores a side using its game code. `show` returns the snapshot, including FEN, legal moves, result, client names, and revision. `random` enters matchmaking. `resign`, `draw offer`, `draw accept`, `draw decline`, `claim threefold`, and `claim fifty` send the corresponding match commands. `create --fen FEN` starts from a supplied position.

To operate on a game without first selecting it, supply `--match UUID --code CODE`. The addressed game is saved as current before sending a match command, so a failed response can be retried against the same side. A code identifies one side and grants control of it. Keep your own code private. Session files contain these credentials; Unix writes use mode `0600`, replacement is atomic, and symbolic link session files are rejected. On Windows use the default session location under your user profile or a directory private to your account. `CHESS_SESSION_FILE` selects a different session path.

`wait` checks for an opponent move following the snapshot last saved by this session, including a move that arrived before the process started. It ignores joins and draw offers and also returns when the game finishes. For another move boundary, use `--after-ply N`. An explicit game with no saved snapshot starts waiting from its current move boundary. The default deadline is 300 seconds; `--timeout` accepts 1 through 86400 seconds. Ctrl+C cancels a pending command.

The session saves request IDs before create, random, or match commands reach the server. After a lost response or process restart, repeat the same command with the same session file to recover the original game access or command result. A pending move keeps its original expected revision even if the opponent has since replied. An older command receipt preserves the newest board already observed.

Mutating commands also accept `--request-id UUID` for an explicit retry identity. A new explicit ID starts a separate request. A pending create or random request must be retried with its original inputs before another entry request, unless a new ID is supplied. Each session tracks one current game, its latest command, and a pending entry request; use separate session files for independent automation workers. Do not run competing writers against one session file.

| Exit code | Meaning |
| --- | --- |
| 0 | Command succeeded |
| 1 | Server rejected the command or a connection failed |
| 2 | Invalid arguments or inaccessible session |
| 124 | Wait deadline elapsed; stdout contains a timeout event |
| 130 | Command cancelled |

## Validation

```sh
CHESS_TEST_SERVER=http://localhost:5000/ dotnet test --project tests/Chess.Cli.Tests
```

The tests launch actual CLI processes against the selected server for crossplay, SAN/UCI moves, resume, command retry, draw agreement, promotion, matchmaking, resignation, and opponent waits. Without `CHESS_TEST_SERVER`, those server tests are explicitly skipped. Help, error output, session writes, Unicode pieces, board orientation, and drag state tests run independently. Linux PTY tests use `script` from util-linux at 80×24 and 100×28 to check real Terminal.Gui rendering and Escape exit. Live-server PTY tests send keyboard and mouse input for crossplay, Black's rotated board, castling, en passant, underpromotion, and drag cancellation. Response-loss tests discard successful HTTP responses after the real server commits, then restore the session and verify retries recover the same side or move without regressing the board.
