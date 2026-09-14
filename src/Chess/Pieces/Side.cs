namespace Chess;

public union Side(White, Black)
{
    public static White White { get; } = new();

    public static Black Black { get; } = new();
}

public sealed class White
{
    internal White()
    {
    }
}

public sealed class Black
{
    internal Black()
    {
    }
}
