namespace Chess;

public union BoardRank(One, Two, Three, Four, Five, Six, Seven, Eight)
{
    public static One One { get; } = new();

    public static Two Two { get; } = new();

    public static Three Three { get; } = new();

    public static Four Four { get; } = new();

    public static Five Five { get; } = new();

    public static Six Six { get; } = new();

    public static Seven Seven { get; } = new();

    public static Eight Eight { get; } = new();
}

public sealed class One
{
    internal One()
    {
    }
}

public sealed class Two
{
    internal Two()
    {
    }
}

public sealed class Three
{
    internal Three()
    {
    }
}

public sealed class Four
{
    internal Four()
    {
    }
}

public sealed class Five
{
    internal Five()
    {
    }
}

public sealed class Six
{
    internal Six()
    {
    }
}

public sealed class Seven
{
    internal Seven()
    {
    }
}

public sealed class Eight
{
    internal Eight()
    {
    }
}
