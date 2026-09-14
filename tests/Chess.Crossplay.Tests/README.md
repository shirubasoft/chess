# Frontend crossplay matrix

This suite covers every unordered pair of Android, Web, Windows, Linux, and CLI in both color assignments. Each private game creates and joins through the clients, verifies their names, makes board and notation moves, closes the clients, resumes each side by code in a fresh client, makes more moves, and finishes by resignation. Every pairing checks that the clients observe the same FEN and move history.

| Frontend | Entry point exercised here |
| --- | --- |
| Web | Real headless Chromium running the Blazor app, with UI clicks, notation entry, code entry, and client-name assertions |
| CLI | Actual `Chess.Cli.dll` executable launched in separate `dotnet` processes, with session files and JSON output |
| Android | Actual shared `GameSession` used by `MainActivity`, with the app's `Chess Android` identity |
| Windows | Actual shared `GameSession` used by the WPF app, with the app's `Chess Windows` identity |
| Linux | Actual shared `GameSession` used by the GTK app, with the app's `Chess Linux` identity |

Native session cases call `CreateAsync`, `JoinAsync`, `SelectSquareAsync`, `MoveAsync`, `RefreshAsync`, and `ActAsync`. They verify the `Opponent` value that the native UI displays and the session's saved code. These are native application-session tests on the test host. They do not launch Android, WPF, or GTK processes, render native controls, or prove platform packaging. Each app's `--verify-ui` scenario covers native rendering and controls on its target platform, including WPF in Windows CI. Android uses its equivalent `verify_ui` intent scenario.

Browser commands all pass through the rendered UI. Read-only HTTP requests inspect the authoritative snapshot and compare it with the board's FEN and revision attributes. The suite never uses the audit connection to create, join, or move. Browser contexts and native session files are fresh after closing; the test carries only each side's code into the resumed clients.

An additional scenario pairs the real browser and CLI through random matchmaking, launches the CLI's `wait` command, plays the browser move, checks the wait result, exchanges a reply, and finishes the game. Run the suite serially against an isolated test database with no other matchmaking callers. Its test class is marked `NotInParallel`; separate test processes must also avoid sharing the random matchmaking queue concurrently.

Build and install the Chromium dependency:

```sh
dotnet build tests/Chess.Crossplay.Tests -c Release
pwsh tests/Chess.Crossplay.Tests/bin/Release/net11.0/playwright.ps1 install chromium
```

Ubuntu CI can use `install --with-deps chromium`. The CLI project reference builds the executable without linking it into the test assembly. The runner locates the CLI output using the test assembly's framework and configuration directories.

Run against the endpoints of one running Aspire deployment:

```sh
CHESS_REQUIRE_CROSSPLAY=1 \
CHESS_TEST_SERVER=http://localhost:5080 \
CHESS_WEB_URL=http://localhost:5090 \
dotnet test --project tests/Chess.Crossplay.Tests -c Release --no-build
```

The web host must proxy to that same server. Run these tests for every server backend in the CI matrix. Outside CI, absent URLs explicitly skip the integration cases. `CHESS_REQUIRE_CROSSPLAY=1` converts missing URLs into failures so an incorrectly configured required job cannot pass by skipping. Temporary session files and Chromium contexts are removed after each scenario. Successful scenarios leave finished games, with no waiting matchmaking ticket.
