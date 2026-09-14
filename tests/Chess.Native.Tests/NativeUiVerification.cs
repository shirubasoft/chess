using System.Text.Json;
using System.IO;
using System.Net.Http;
using Chess.Client;
using Chess.Contracts;

namespace Chess.Native.Testing;

public static class NativeUiVerification
{
    public static async Task<bool> RunAsync(GameSession session, Action<string, string> setText,
        Action<string> click, Func<string, string> readText, string reportPath)
    {
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
            click("square-e2");
            await UntilAsync(() => session.SelectedSquare == "e2", () => session.Message);
            click("square-e4");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 1, () => session.Message);
            var afterWhite = await opponent.GetAsync(other);
            Require(afterWhite.Moves[0].Uci == "e2e4", "Clicking board squares must send the same move to the server.");
            Require(readText("square-e4").Contains("White pawn", StringComparison.Ordinal), "The native board renders the pawn on e4.");
            checks.Add("Played e2-e4 using two native square controls and verified it from the opponent client.");
            await opponent.MoveAsync(other, afterWhite, "e5");
            await UntilAsync(() => session.Snapshot?.Moves.Length == 2, () => session.Message);
            checks.Add("Received the opponent's reply through periodic refresh without a persistent connection.");
            setText("move-notation", "Nf3"); click("play-move");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 3, () => session.Message);
            Require(readText("move-history").Contains("Nf3", StringComparison.Ordinal), "The native history must show the entered SAN move.");
            click("resign-game");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Status == GameStatus.Finished, () => session.Message);
            Require((await opponent.GetAsync(other)).Result?.Winner == PlayerSide.Black, "Resigning from the native view awards the game to the opponent.");
            checks.Add("Submitted SAN, rendered move history, and resigned through native controls.");
            var promotion = await opponent.CreateAsync("7k/P7/8/8/8/8/8/K7 w - - 0 1");
            var promotionOpponent = await opponent.JoinAsync(promotion.OpponentCode!);
            setText("game-code", promotion.Code); click("join-game");
            await UntilAsync(() => !session.IsBusy && session.Access?.GameId == promotion.GameId, () => session.Message);
            click("square-a7");
            await UntilAsync(() => session.SelectedSquare == "a7", () => session.Message);
            click("square-a8");
            await UntilAsync(() => session.PromotionMove == "a7a8", () => session.Message);
            click("promote-n");
            await UntilAsync(() => !session.IsBusy && session.Snapshot?.Moves.Length == 1, () => session.Message);
            Require((await opponent.GetAsync(promotionOpponent)).Moves[0].Uci == "a7a8n", "The native promotion choice must reach the opponent as knight promotion.");
            Require(readText("square-a8").Contains("White knight", StringComparison.Ordinal), "The native board must render the promoted knight.");
            checks.Add("Joined another game by side code and underpromoted through the native promotion controls.");
        }
        catch (Exception exception) { failure = exception; }
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
