using System.Collections.Immutable;

namespace Chess.Notation;

public sealed record PgnGame
{
    public required ImmutableDictionary<string, string> Tags { get; init; }
    public required Position InitialPosition { get; init; }
    public required PgnLine Mainline { get; init; }
    public required PgnResult Result { get; init; }
}

public sealed record PgnLine
{
    public required ImmutableArray<string> Comments { get; init; }
    public required ImmutableArray<PgnMove> Moves { get; init; }
}

public sealed record PgnMove
{
    public required MoveRequest Move { get; init; }
    public required ImmutableArray<string> Comments { get; init; }
    public required ImmutableArray<int> Annotations { get; init; }
    public required ImmutableArray<PgnLine> Variations { get; init; }
}

public enum PgnResult
{
    WhiteWins,
    BlackWins,
    Draw,
    Unfinished
}
