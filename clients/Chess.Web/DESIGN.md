# Chess web design

The game board owns the page. A quiet scorebook layout uses warm paper, dark ink, and muted green squares. Game controls form one compact rail beside the board; joining and matchmaking stay within reach without interrupting a game. On phones the board precedes move entry, history, and connection details.

The direction is an implementation choice under the authorized frontend task. It borrows the compact reading rhythm of tournament score sheets and the simple piece shapes of physical club sets. The green board has visible focus outlines, selected-square borders, last-move tint, and legal-target dots. Piece icons are authored SVG paths rather than font glyphs.

During mouse or touch dragging, an SVG copy follows the pointer while the source piece fades inside its selection border. Empty legal destinations use dots; capture destinations use rings. A matching border marks the legal square under the pointer. These drag markers and the moving copy clear when the gesture ends. Promotion uses a chooser centered over the board, with piece icons, text labels, and a Cancel action.

Use a self-hosted serif face for the wordmark and page headings, the platform UI font for controls and body text, and tabular numerals for revisions and move history. Inline errors stay close to controls. No timer is shown because these are untimed games.
