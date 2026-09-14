# Native UI verification

`NativeUiVerification.cs` is compiled into each native application and runs only when explicitly requested through its verification launch option. It invokes real native buttons and entry fields, then checks the shared session, rendered view content, and authoritative server state from a second HTTP client.

The scenario creates a game, shares and joins its opponent code, plays a board move, receives an opponent reply through polling, enters SAN, checks history, resigns, joins a separate promotion position by side code, and selects knight promotion through the native promotion controls. Each application writes a JSON report containing the client name, checks, final position, and any failure. Desktop processes also return a nonzero exit status on failure. Android reports PASS/FAIL under the `ChessVerification` logcat tag.

Run the platform-specific commands in each client's README with an isolated settings file against every configured backend. A passing shared session test or a cross-compiled Windows binary does not replace a native runtime report.

`dotnet test --project tests/Chess.Native.Tests` checks board orientation, core-derived pieces and promotion rendering, saved session restoration, and rejection of invalid server addresses without network access.
