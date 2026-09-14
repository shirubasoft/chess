# Terminal board

The CLI is an Operate interface. Its work is joining a game and playing moves with the keyboard or mouse. The design follows terminal conventions and supports a minimum of 80 columns by 24 rows.

The top row names the server. New game, Random, and the masked join-code field share a row. Match status and both client names sit above the board. A waiting game's invitation code appears next to that context. The game-access code remains in the private session file.

The board has alternating cream and sage squares, a box-drawn frame with file labels, and ranks on either side. Unicode chess symbols distinguish outlined White pieces from filled Black pieces. Dark ink keeps both sets readable on the square colors. Each square occupies three terminal columns and one row, keeping the whole board visible at the minimum size. It rotates for Black. A scrolling move-history list occupies the right side. The drag hint, move field, Play, and Refresh sit under the board; SAN/UCI examples sit immediately below the input. Draw responses and resignation remain together. Claims occupy a separate row. The final line reports operation progress or errors.

Dragging marks the source with brackets and a gold background. Legal destinations use a lighter green, with dots on empty squares. The moving piece follows the pointer between squares. A drop uses the displayed position's legal moves; promotions require a piece choice. A cancelled drag releases mouse capture. A server update or an operation in progress clears the drag so its source cannot outlive the position it came from.

Terminals restricted to 16 colors use light and dark gray squares with contrasting ink, yellow selection, and green legal destinations. The frame, source brackets, and destination dots remain visible independently of the palette.

Buttons use Terminal.Gui's keyboard, focus, and theme conventions. Their shadows are disabled so adjacent command rows remain legible. Network work disables operation buttons until its result is available. Invalid moves retain the move input and show the server's explanation. The board is restored from the saved session when the app opens. Background polling updates it while open and retries after transient connection errors.
