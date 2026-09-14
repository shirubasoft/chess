using System.Text.Json;
using Chess;
using Chess.Notation;
using Chess.Validation;

if (args is ["--uci"])
{
    UciDriver.Run();
    return;
}
if (args.Length != 0) throw new ArgumentException("Use --uci for perft tools, or JSON lines on stdin.");

var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
while (Console.ReadLine() is { } line)
{
    try
    {
        using var document = JsonDocument.Parse(line);
        var request = document.RootElement;
        var operation = request.GetProperty("operation").GetString();
        object response = operation switch
        {
            "position" => Inspector.Position(request),
            "history" => Inspector.History(request),
            "move" => Inspector.Move(request),
            "pgn" => Inspector.Pgn(request),
            "perft" => Perft.Divide(Fen.Parse(request.GetProperty("fen").GetString()!).OrThrow(), request.GetProperty("depth").GetInt32()),
            _ => throw new ArgumentException("Unknown validation operation.")
        };
        Console.WriteLine(JsonSerializer.Serialize(response, options));
    }
    catch (NotationException error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = error.Error }, options));
    }
    catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = new { kind = error.GetType().Name, message = error.Message } }, options));
    }
}
