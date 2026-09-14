using System.Text.Json;
using System.IO;
using System.Net.Http;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Native.Testing;

public delegate Task NativeDragInput(string source, string? destination, bool cancel = false, Func<Task>? whileHeld = null);

public static class NativeUiVerification
{
    public static async Task<bool> RunAsync(GameSession session, Action<string, string> setText,
        Action<string> click, Func<string, string> readText, string reportPath, NativeDragInput drag, bool recordGameplay = false)
    {
        if (recordGameplay) return await RunGameplayAsync(session, click, readText, reportPath, drag);
        var startedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.Delete(reportPath);
        var checks = new List<string>();
        Exception? failure = null;
        try
        {
            click("create-game");
            await UntilAsync(() => !session.IsBusy && session.Access is not null, () => session.Message);
            var access = session.Access!;
            Require(access.OpponentCode is not null, "Creating a game exposes a separate opponent code.");
            Require(readText("access-codes").Contains(access.Code, StringComparison.Ordinal), "The native view displays the own-side code.");
            checks.Add("Created a game through the native button and displayed both side codes.");
            using var http = new HttpClient { BaseAddress = new Uri(session.Server) };
            var opponent = new ChessClient(http, "Chess native verification opponent");
            var other = await opponent.JoinAsync(access.OpponentCode!);
            click("refresh-game");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Status == GameStatus.Active, () => session.Message);
            Require(readText("opponent-client").Contains(opponent.ClientName, StringComparison.Ordinal), "The native view displays the opponent client name.");
            var revision = session.Snapshot!.Revision;
            foreach (var (source, target, cancel) in new[] { ("e7", "e5", false), ("e2", "e5", false), ("e2", (string?)null, false), ("e2", "e4", true) })
            {
                await drag(source, target, cancel);
                Require((await opponent.GetAsync(other)).Revision == revision, "Rejected or canceled gestures must not submit a move.");
            }
            checks.Add("Rejected enemy, illegal, outside and canceled native input gestures without server changes.");
            await drag("e2", "e4", whileHeld: async () =>
            {
                await opponent.CommandAsync(other, new GameCommandRequest { RequestId = Guid.NewGuid(), ExpectedRevision = revision, Action = GameAction.OfferDraw });
                await session.RefreshAsync();
            });
            Require((await opponent.GetAsync(other)).Moves.Length == 0, "A changed revision must cancel a held gesture.");
            checks.Add("Canceled a held native gesture after a remote command changed the revision.");
            await drag("e2", "e4");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 1, () => session.Message);
            var afterWhite = await opponent.GetAsync(other);
            Require(afterWhite.Moves[0].Uci == "e2e4", "Dragging a piece must send the same move to the server.");
            Require(readText("square-e4").Contains("White pawn", StringComparison.Ordinal), "The native board renders the pawn on e4.");
            checks.Add("Dragged e2-e4 using routed native pointer/touch events and verified it from the opponent client.");
            await opponent.MoveAsync(other, afterWhite, "e5");
            await UntilAsync(() => session.Snapshot?.Moves.Length == 2, () => session.Message);
            checks.Add("Received the opponent's reply through periodic refresh without a persistent connection.");
            setText("move-notation", "Nf3"); click("play-move");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 3, () => session.Message);
            Require(readText("move-history").Contains("Nf3", StringComparison.Ordinal), "The native history must show the entered SAN move.");
            var afterKnight = await opponent.GetAsync(other);
            await opponent.MoveAsync(other, afterKnight, "Nc6");
            await UntilAsync(() => session.Snapshot?.Moves.Length == 4, () => session.Message);
            click("square-f1");
            await UntilAsync(() => session.SelectedSquare == "f1", () => session.Message);
            click("square-c4");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 5, () => session.Message);
            click("resign-game");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Status == GameStatus.Finished, () => session.Message);
            Require((await opponent.GetAsync(other)).Result?.Winner == PlayerSide.Black, "Resigning from the native view awards the game to the opponent.");
            checks.Add("Submitted SAN, preserved square clicks, rendered move history, and resigned through native controls.");
            var blackGame = await opponent.CreateAsync();
            setText("game-code", blackGame.OpponentCode!); click("join-game");
            await UntilAsync(() => !session.IsBusy && session.Access?.GameId == blackGame.GameId, () => session.Message);
            await opponent.MoveAsync(blackGame, await opponent.GetAsync(blackGame), "e4");
            await UntilAsync(() => session.CanMove, () => session.Message);
            await drag("e7", "e5");
            await UntilAsync(() => session.Snapshot?.Moves.Length == 2, () => session.Message);
            Require((await opponent.GetAsync(blackGame)).Moves[1].Uci == "e7e5", "Black orientation must resolve the correct source and target.");
            checks.Add("Dragged a Black piece with the board viewed from Black's side.");
            var promotion = await opponent.CreateAsync("7k/P7/8/8/8/8/8/K7 w - - 0 1");
            var promotionOpponent = await opponent.JoinAsync(promotion.OpponentCode!);
            setText("game-code", promotion.Code); click("join-game");
            await UntilAsync(() => !session.IsBusy && session.Access?.GameId == promotion.GameId, () => session.Message);
            await drag("a7", "a8");
            await UntilAsync(() => session.PromotionMove == "a7a8", () => session.Message);
            click("promote-n");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 1, () => session.Message);
            Require((await opponent.GetAsync(promotionOpponent)).Moves[0].Uci == "a7a8n", "The native promotion choice must reach the opponent as knight promotion.");
            Require(readText("square-a8").Contains("White knight", StringComparison.Ordinal), "The native board must render the promoted knight.");
            checks.Add("Joined another game by side code and underpromoted through the native promotion controls.");
        }
        catch (Exception exception) { failure = exception; }
        return await WriteReportAsync(session, reportPath, startedAt, checks, failure);
    }

    private static async Task<bool> RunGameplayAsync(GameSession session, Action<string> click,
        Func<string, string> readText, string reportPath, NativeDragInput drag)
    {
        var startedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.Delete(reportPath);
        var checks = new List<string>();
        Exception? failure = null;
        try
        {
            await Task.Delay(1800);
            click("create-game");
            await UntilAsync(() => !session.IsBusy && session.Access is not null, () => session.Message);
            await Task.Delay(1800);
            using var http = new HttpClient { BaseAddress = new Uri(session.Server) };
            var opponent = new ChessClient(http, "Scripted opponent");
            var other = await opponent.JoinAsync(session.Access!.OpponentCode!);
            click("refresh-game");
            await UntilAsync(() => session.CanMove, () => session.Message);
            Require(readText("opponent-client").Contains("Scripted opponent", StringComparison.Ordinal), "The opponent must be identified as scripted.");
            checks.Add("Created and joined a real game, visibly identifying the scripted opponent.");
            var moveCount = 0;
            foreach (var (source, target, reply) in new[] { ("e2", "e4", "e5"), ("f1", "c4", "Nc6"), ("d1", "h5", "Nf6"), ("h5", "f7", (string?)null) })
            {
                await Task.Delay(1800);
                await drag(source, target);
                moveCount++;
                await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == moveCount, () => session.Message);
                Require((await opponent.GetAsync(other)).Moves[^1].Uci == source + target, "The dragged move must reach the other client.");
                checks.Add("Dragged " + source + "-" + target + " through native input and verified the server move.");
                await Task.Delay(1800);
                if (reply is not null)
                {
                    await opponent.MoveAsync(other, await opponent.GetAsync(other), reply);
                    moveCount++;
                    await UntilAsync(() => session.Snapshot?.Moves.Length == moveCount, () => session.Message);
                }
            }
            Require(session.Snapshot?.Status == GameStatus.Finished && session.Snapshot.Result?.Winner == PlayerSide.White, "The game must conclude with White's checkmate.");
            await Task.Delay(3500);
        }
        catch (Exception exception) { failure = exception; }
        return await WriteReportAsync(session, reportPath, startedAt, checks, failure);
    }

    private static async Task<bool> WriteReportAsync(GameSession session, string reportPath, DateTimeOffset startedAt, List<string> checks, Exception? failure)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        var result = new
        {
            success = failure is null, startedAt, client = session.ClientName, server = session.Server,
            gameId = session.Access?.GameId, revision = session.Snapshot?.Revision, fen = session.Snapshot?.Fen,
            checks, error = failure?.ToString()
        };
        var temporary = reportPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, reportPath, true);
        return failure is null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task UntilAsync(Func<bool> condition, Func<string> status)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            if (deadline.IsCancellationRequested) throw new TimeoutException("The native UI did not reach the expected state: " + status());
            await Task.Delay(50);
        }
    }
}
