using System.Collections.Immutable;

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
