namespace Chess.Notation;

public union NotationResult<T>(Parsed<T>, NotationError)
{
    public T OrThrow() => this switch
    {
        Parsed<T> parsed => parsed.Value,
        NotationError error => throw new NotationException(error)
    };
}

public sealed record Parsed<T>
{
    public required T Value { get; init; }
}

public sealed record NotationError
{
    public required NotationErrorKind Kind { get; init; }
    public required string Message { get; init; }
    public required int Offset { get; init; }
}

public enum NotationErrorKind
{
    Syntax,
    InvalidPosition,
    IllegalMove,
    AmbiguousMove,
    InvalidGame,
    UnsupportedVariant
}

public sealed class NotationException(NotationError error) : FormatException(error.Message)
{
    public NotationError Error { get; } = error;
}
