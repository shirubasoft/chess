using System.Text.Json;
using Chess.Client;
using Chess.Contracts;

if (args.Length != 3 || args[0] is not ("prepare" or "continue"))
    throw new ArgumentException("Usage: prepare|continue <server-url> <state-path>");

using var http = new HttpClient { BaseAddress = new Uri(args[1]) };
var whiteClient = new ChessClient(http, "Switch validation White");
var blackClient = new ChessClient(http, "Switch validation Black");
SwitchState state;
if (args[0] == "prepare")
{
    var white = await whiteClient.CreateAsync();
    var black = await blackClient.JoinAsync(white.OpponentCode!);
    var command = Move(black.Snapshot, "e4");
    var snapshot = await whiteClient.CommandAsync(white, command);
    state = new(white, black, PlayerSide.White, command, snapshot);
}
else
{
    state = JsonSerializer.Deserialize<SwitchState>(await File.ReadAllTextAsync(args[2]), GameJson.Options)!;
    var current = await whiteClient.GetAsync(state.White);
    Require(Equal(current, state.Snapshot), "Snapshot changed across the backend restart.");
    var previousAccess = state.LastSide == PlayerSide.White ? state.White : state.Black;
    var retry = await whiteClient.CommandAsync(previousAccess, state.LastCommand);
    Require(Equal(retry, state.Snapshot), "A committed request retry lost its receipt across the backend restart.");
    var side = current.SideToMove;
    var access = side == PlayerSide.White ? state.White : state.Black;
    var command = Move(current, current.Moves.Length switch { 1 => "e5", 2 => "Nf3", 3 => "Nc6", _ => throw new InvalidOperationException("Unexpected move count.") });
    var next = await whiteClient.CommandAsync(access, command);
    Require(next.Revision == current.Revision + 1 && next.Moves.Length == current.Moves.Length + 1,
        "The resumed game did not accept exactly one move.");
    var resumedWhite = await whiteClient.JoinAsync(state.White.Code);
    var resumedBlack = await blackClient.JoinAsync(state.Black.Code);
    Require(resumedWhite.GameId == state.White.GameId && resumedWhite.Side == PlayerSide.White, "White's code did not resume the seat.");
    Require(resumedBlack.GameId == state.Black.GameId && resumedBlack.Side == PlayerSide.Black, "Black's code did not resume the seat.");
    Require(Equal(resumedWhite.Snapshot, next) && Equal(resumedBlack.Snapshot, next), "Resumed clients disagree about the game.");
    state = state with { LastSide = side, LastCommand = command, Snapshot = next };
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
await using (var output = new FileStream(args[2], FileMode.Create, FileAccess.Write, FileShare.None))
{
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(args[2], UnixFileMode.UserRead | UnixFileMode.UserWrite);
    await JsonSerializer.SerializeAsync(output, state, GameJson.Options);
}
Console.WriteLine($"Verified durable game revision {state.Snapshot.Revision}: {string.Join(' ', state.Snapshot.Moves.Select(m => m.San))}");

static GameCommandRequest Move(GameSnapshot snapshot, string move) => new()
{
    RequestId = Guid.NewGuid(), ExpectedRevision = snapshot.Revision, Action = GameAction.Move,
    Move = move, Notation = MoveNotation.San
};
static bool Equal(GameSnapshot a, GameSnapshot b) => JsonSerializer.Serialize(a, GameJson.Options) == JsonSerializer.Serialize(b, GameJson.Options);
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record SwitchState(GameAccess White, GameAccess Black, PlayerSide LastSide, GameCommandRequest LastCommand, GameSnapshot Snapshot);
