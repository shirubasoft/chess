# Native UI verification

`NativeUiVerification.cs` is compiled into each native application and runs only when explicitly requested through its verification launch option. It invokes native buttons and entry fields and sends pointer/touch sequences through each platform's input routing, then checks the shared session, rendered view content, and authoritative server state from a second HTTP client.

The scenario creates a game, shares and joins its opponent code, rejects illegal and canceled gestures, cancels a held gesture when the revision changes, drags a board move, receives an opponent reply through polling, enters SAN, checks history, resigns, verifies Black-oriented dragging, joins a separate promotion position by side code, and drags a pawn before selecting knight promotion through the native promotion controls. Each application writes a JSON report containing the client name, checks, final position, and any failure. Desktop processes also return a nonzero exit status on failure. Android reports PASS/FAIL under the `ChessVerification` logcat tag.

Run the platform-specific commands in each client's README with an isolated settings file against every configured backend. A passing shared session test or a cross-compiled Windows binary does not replace a native runtime report.

`dotnet test --project tests/Chess.Native.Tests` checks presentation and saved settings. With `CHESS_TEST_SERVER` set, it also exercises session recovery and drag-token ownership, legal origins and destinations, cancellation, revision and game changes, and promotion for either side against the real server.
