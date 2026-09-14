using System.Text.Json.Serialization;

namespace Chess;

public union MatchEvent(MovePlayed, DrawClaimed, PlayerResigned, DrawOffered, DrawOfferDeclined, DrawAgreed);

public union MatchProgress(PlayContinues, MatchWon, MatchDrawn)
{
    public static PlayContinues Continue { get; } = new();
}

public sealed class PlayContinues
{
    internal PlayContinues()
    {
    }
}

public sealed class MovePlayed
{
    [JsonConstructor]
    internal MovePlayed(int revision, int ply, PositionKey previousKey, MoveRequest move, MatchProgress progress)
    {
        Revision = revision;
        Ply = ply;
        PreviousKey = previousKey;
        Move = move;
        Progress = progress;
    }

    public int Revision { get; }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public MoveRequest Move { get; }

    public MatchProgress Progress { get; }
}

public sealed class DrawClaimed
{
    [JsonConstructor]
    internal DrawClaimed(int revision, int ply, PositionKey previousKey, DrawClaimReason reason, DrawClaimTiming timing)
    {
        Revision = revision;
        Ply = ply;
        PreviousKey = previousKey;
        Reason = reason;
        Timing = timing;
    }

    public int Revision { get; }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public DrawClaimReason Reason { get; }

    public DrawClaimTiming Timing { get; }
}

public sealed class PlayerResigned
{
    [JsonConstructor]
    internal PlayerResigned(int revision, int ply, PositionKey previousKey, Side player, MatchResult result)
    {
        Revision = revision;
        Ply = ply;
        PreviousKey = previousKey;
        Player = player;
        Result = result;
    }

    public int Revision { get; }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public Side Player { get; }

    public MatchResult Result { get; }
}

public sealed class DrawOffered
{
    [JsonConstructor]
    internal DrawOffered(int revision, int ply, PositionKey previousKey, Side player)
    {
        Revision = revision;
        Ply = ply;
        PreviousKey = previousKey;
        Player = player;
    }

    public int Revision { get; }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public Side Player { get; }
}

public sealed class DrawOfferDeclined
{
    [JsonConstructor]
    internal DrawOfferDeclined(int revision, int ply, PositionKey previousKey, Side player)
    {
        Revision = revision;
        Ply = ply;
        PreviousKey = previousKey;
        Player = player;
    }

    public int Revision { get; }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public Side Player { get; }
}

public sealed class DrawAgreed
{
    [JsonConstructor]
    internal DrawAgreed(int revision, int ply, PositionKey previousKey, Side player)
    {
        Revision = revision;
        Ply = ply;
        PreviousKey = previousKey;
        Player = player;
    }

    public int Revision { get; }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public Side Player { get; }
}
