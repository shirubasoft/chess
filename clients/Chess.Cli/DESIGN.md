# Terminal board

The CLI is an Operate interface. Its work is joining a game and entering a move with a keyboard. The design follows terminal conventions and supports a minimum of 80 columns by 24 rows.

The top row names the server. New game, Random, and the masked join-code field share a row. Match status and both client names sit above the board. A waiting game's invitation code appears next to that context. The game-access code remains in the private session file.

The board uses rank/file labels and ASCII piece letters with no dependence on a chess-symbol font. It rotates for Black. A scrolling move-history list occupies the right side. The move field, Play, and Refresh sit under the board; SAN/UCI examples sit immediately below the input. Draw responses and resignation remain together. Claims occupy a separate row. The final line reports operation progress or errors.

Buttons use Terminal.Gui's keyboard, focus, and theme conventions. Their shadows are disabled so adjacent command rows remain legible. Network work disables operation buttons until its result is available. Invalid moves retain the move input and show the server's explanation. The board is restored from the saved session when the app opens. Background polling updates it while open and retries after transient connection errors.
