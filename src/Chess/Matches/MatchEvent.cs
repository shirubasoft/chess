using System.Text.Json.Serialization;

namespace Chess;

public union MatchEvent(MovePlayed, DrawClaimed, PlayerResigned);

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
    internal MovePlayed(int ply, PositionKey previousKey, MoveRequest move, MatchProgress progress)
    {
        Ply = ply;
        PreviousKey = previousKey;
        Move = move;
        Progress = progress;
    }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public MoveRequest Move { get; }

    public MatchProgress Progress { get; }
}

public sealed class DrawClaimed
{
    [JsonConstructor]
    internal DrawClaimed(int ply, PositionKey previousKey, DrawClaimReason reason, DrawClaimTiming timing)
    {
        Ply = ply;
        PreviousKey = previousKey;
        Reason = reason;
        Timing = timing;
    }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public DrawClaimReason Reason { get; }

    public DrawClaimTiming Timing { get; }
}

public sealed class PlayerResigned
{
    [JsonConstructor]
    internal PlayerResigned(int ply, PositionKey previousKey, Side player, MatchResult result)
    {
        Ply = ply;
        PreviousKey = previousKey;
        Player = player;
        Result = result;
    }

    public int Ply { get; }

    public PositionKey PreviousKey { get; }

    public Side Player { get; }

    public MatchResult Result { get; }
}
