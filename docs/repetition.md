# Repetition identity and history

`PositionKey.Create(position)` builds a structural key from piece placement, side
to move, each side's castling rights, and legally available en passant. It ignores
halfmove and fullmove counters. Independently built boards with the same contents
compare equal and work as dictionary keys. `PiecePlacement` contains one symbol
per square in file-major order, a1 through a8, then b1 through b8, and so on.
Uppercase symbols denote White; dots denote empty squares.

[FIDE Article 9.2.3](https://handbook.fide.com/chapter/e012023) makes castling and
en passant relevant to repetition. Castling rights remain in the key even while
pieces block castling. En passant enters the key only if at least one pawn can
legally capture, including king safety after both pawns leave their original
squares. An unavailable target is equivalent to `EnPassantState.None`.

`PositionHistory.Start` records the initial position once. `Record` appends a
snapshot and returns a new history, sharing immutable storage. It preserves the
chronological key sequence and occurrence counts, including nonconsecutive
repetitions. The caller records accepted positions in order; `Record` is a
snapshot operation and does not validate that one position follows another by a
legal move.

`CurrentOccurrences`, `IsThreefoldRepetition`, and `IsFivefoldRepetition` describe
the current snapshot. `Occurrences(key)` also supports checking an intended move
without appending it. Article 9 distinguishes a claim at three occurrences from
an automatic draw at five; history supplies the facts for match adjudication.

Key equality compares the full canonical contents. `GetHashCode()` is an in-memory
dictionary aid; persist the contents or events rather than the process hash.
