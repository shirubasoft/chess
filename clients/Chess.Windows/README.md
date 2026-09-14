# Windows client

The WPF client uses native Windows controls and the shared `Chess.Client` library. Build on a machine with the SDK selected in `global.json`:

```sh
dotnet build clients/Chess.Windows
```

`EnableWindowsTargeting` allows compilation on Linux. Running the app and verifying Windows UI behavior require Windows:

```powershell
dotnet run --project clients/Chess.Windows
```

Create a game or find an opponent, share the opponent code, and retain your own side's code. Join / resume accepts a code from any client. The application saves the server address and own code in the user's local application data directory. Pass `--resume` to restore the saved side at startup. Board selection and SAN/UCI entry use the shared core, and polling refreshes the view without requiring a persistent connection.

For a Windows CI runner with a running server, use an isolated settings path and the UI verification mode:

```powershell
$env:CHESS_SERVER = 'http://localhost:5080/'
$env:CHESS_SETTINGS_PATH = Join-Path $env:RUNNER_TEMP 'chess-windows-session'
dotnet run --project clients/Chess.Windows -- --verify-ui artifacts/native/windows-verification.json
```

The exit code reports success or failure, the JSON report contains the checked interactions and server state, and a PNG next to the report captures the rendered window. WPF controls expose stable `AutomationProperties.AutomationId` values for external UI automation. The shared scenario is in `tests/Chess.Native.Tests`.

The WPF board scales uniformly beside move history and native controls. Segoe UI Symbol renders dark Unicode pieces on cream and sage squares. Selection uses a gold background, and legal destinations have dark green borders. Board colors remain visible when its buttons are disabled, including after the game finishes.

Drag one of your movable pieces to a legal destination, or select the source and destination with clicks. After movement crosses Windows' drag threshold, a translucent piece follows the pointer and its source square fades. Releasing outside the board, Escape or lost input, or a changed game position cancels the drag. Dragging a pawn onto its final rank reveals promotion buttons below the board. SAN and UCI entry remain available.
