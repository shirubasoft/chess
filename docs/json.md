# JSON persistence and requests

Use `ChessJson.CreateOptions()` with `System.Text.Json` to serialize positions,
move requests, position outcomes, match commands, command results, match events,
and match results. Reuse the options
instance across calls.

```csharp
var options = ChessJson.CreateOptions();
var json = JsonSerializer.Serialize(events, options);
var restored = JsonSerializer.Deserialize<MatchEvent[]>(json, options)!;
var state = Match.Replay(restored, initial);
```

Persist the initial `Position` alongside events for an edited setup. Rebuild
match state and repetition history with replay. Submit incoming commands to
`Match.Decide`; accepted event streams are trusted persistence data, not a client
command format.

.NET 11's native union serializer writes each active case directly. The options
add a `case` property through contract metadata and use a type classifier to
select that case when reading. For example, a side is `{"case":"White"}` and a
move begins `{"case":"MovePiece","from":...,"to":...}`. The identifier is the
case's CLR name, so renaming a case changes the persistence format.

Stateless cases restore the shared domain instances. This preserves equality for
sides, pieces, and coordinates. Event constructors and the position-key
constructor are available to the serializer through `JsonConstructor`.

A board carries a `squares` array of coordinate/occupied-square key-value pairs.
A position preserves its raw en passant target and move counters. A repetition
key preserves its normalized en passant identity and its 64 placement symbols in
file order (`a1` through `a8`, then `b1` through `b8`, through `h8`). Counters are
absent from the key.

Unknown or missing cases and required payload fields throw `JsonException`.
Chess legality is checked when deciding commands. Replay retains recorded finish
decisions and validates event predecessors and moves.
