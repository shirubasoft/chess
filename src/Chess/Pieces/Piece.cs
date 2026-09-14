namespace Chess;

public union Piece(Pawn, Knight, Bishop, Rook, Queen, King)
{
    public static Pawn Pawn { get; } = new();

    public static Knight Knight { get; } = new();

    public static Bishop Bishop { get; } = new();

    public static Rook Rook { get; } = new();

    public static Queen Queen { get; } = new();

    public static King King { get; } = new();
}

public sealed class Pawn
{
    internal Pawn()
    {
    }
}

public sealed class Knight
{
    internal Knight()
    {
    }
}

public sealed class Bishop
{
    internal Bishop()
    {
    }
}

public sealed class Rook
{
    internal Rook()
    {
    }
}

public sealed class Queen
{
    internal Queen()
    {
    }
}

public sealed class King
{
    internal King()
    {
    }
}
