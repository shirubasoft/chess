using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Chess;

public sealed class Board
{
    private readonly ImmutableDictionary<Coordinate, Occupied> _squares;

    public Board()
        : this(ImmutableDictionary<Coordinate, Occupied>.Empty)
    {
    }

    private Board(ImmutableDictionary<Coordinate, Occupied> squares)
    {
        _squares = squares;
    }

    [JsonConstructor]
    internal Board(ImmutableArray<KeyValuePair<Coordinate, Occupied>> squares)
        : this(squares.ToImmutableDictionary())
    {
    }

    [JsonInclude]
    internal ImmutableArray<KeyValuePair<Coordinate, Occupied>> Squares => _squares.ToImmutableArray();

    public SquareContent this[Coordinate coordinate] =>
        _squares.TryGetValue(coordinate, out var square)
            ? square
            : SquareContent.Empty;

    public PlacementResult Place(Coordinate coordinate, OwnedPiece piece) =>
        _squares.TryGetValue(coordinate, out var occupied)
            ? new PlacementConflict { Coordinate = coordinate, ExistingPiece = occupied.Piece }
            : new Board(_squares.Add(coordinate, new Occupied { Piece = piece }));

    public Board Remove(Coordinate coordinate) =>
        _squares.ContainsKey(coordinate)
            ? new(_squares.Remove(coordinate))
            : this;
}
