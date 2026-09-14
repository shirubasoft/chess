# Linux client

The GTK4 client uses native controls and `Chess.Client` for session handling and core board interpretation. Install the .NET SDK selected by `global.json` and GTK4, then run from the repository root:

```sh
dotnet run --project clients/Chess.Linux
```

Set the server address, create a game or choose Find opponent, and share the opponent code. Your own code and server address are saved under the user's local application data directory. Join / resume restores a side using its code. `--resume` restores the saved side at startup. During a game, select a piece and destination or enter SAN/UCI notation. The view refreshes every three seconds and has a manual Refresh button.

`CHESS_SERVER` supplies the initial server address. `CHESS_SETTINGS_PATH` selects a separate settings file for testing. The saved address takes precedence over `CHESS_SERVER`.

Build with `dotnet build clients/Chess.Linux`. For display-free CI, install GTK4 and Xvfb and run the real UI verification against a running server:

```sh
CHESS_SERVER=http://localhost:5080/ CHESS_SETTINGS_PATH=/tmp/chess-linux-ci-session \
  xvfb-run -a dotnet run --project clients/Chess.Linux -- \
  --verify-ui artifacts/native/linux-verification.json
```

The process exits with a failing code if verification fails. GTK widget names are stable automation IDs such as `create-game`, `move-notation`, and `square-e2`. The shared verification source and scenario are in `tests/Chess.Native.Tests`.

Gir.Core 0.8.1 binds GTK4. UI code uses GTK 4.0-era controls. Run the UI verification against the target distribution's GTK4 runtime when packaging for that distribution.

Drag one of your movable pieces to a legal destination, or select the source and destination with clicks. The moving piece follows the pointer while its source and legal destinations remain marked. Releasing outside the board, Escape or lost input, or a changed game position cancels the drag. Dragging a pawn onto its final rank opens the promotion choices. SAN and UCI entry remain available.
