using System.Collections.Immutable;

namespace Chess;

public sealed class PositionHistory
{
    private readonly ImmutableDictionary<PositionKey, int> _occurrences;
    private readonly ImmutableList<PositionKey> _keys;

    private PositionHistory(Position current, PositionKey key, ImmutableList<PositionKey> keys,
        ImmutableDictionary<PositionKey, int> occurrences)
    {
        Current = current;
        CurrentKey = key;
        _keys = keys;
        _occurrences = occurrences;
    }

    public Position Current { get; }

    public PositionKey CurrentKey { get; }

    public IReadOnlyList<PositionKey> Keys => _keys;

    public int CurrentOccurrences => Occurrences(CurrentKey);

    public bool IsThreefoldRepetition => CurrentOccurrences >= 3;

    public bool IsFivefoldRepetition => CurrentOccurrences >= 5;

    public static PositionHistory Start(Position position)
    {
        var key = PositionKey.Create(position);
        return new PositionHistory(position, key, [key], ImmutableDictionary<PositionKey, int>.Empty.Add(key, 1));
    }

    public int Occurrences(PositionKey key) => _occurrences.GetValueOrDefault(key);

    public PositionHistory Record(Position position)
    {
        var key = PositionKey.Create(position);
        return new PositionHistory(position, key, _keys.Add(key), _occurrences.SetItem(key, checked(Occurrences(key) + 1)));
    }
}
