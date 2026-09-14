using System.Globalization;
using Chess.Notation;

namespace Chess.Validation;

internal static class UciDriver
{
    internal static void Run()
    {
        var position = Chess.Position.Initial;
        while (Console.ReadLine() is { } line)
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            try
            {
                switch (tokens[0])
                {
                    case "uci":
                        Console.WriteLine("id name Chess.Validation\nid author shirubasoft\nuciok");
                        break;
                    case "isready": Console.WriteLine("readyok"); break;
                    case "ucinewgame": position = Chess.Position.Initial; break;
                    case "position": position = ReadPosition(tokens); break;
                    case "go" when tokens is [_, "perft", var value]:
                        var divide = Perft.Divide(position, int.Parse(value, CultureInfo.InvariantCulture));
                        foreach (var pair in divide.Moves) Console.WriteLine($"{pair.Key}: {pair.Value}");
                        Console.WriteLine($"Nodes searched: {divide.Nodes}");
                        break;
                    case "quit": return;
                    case "setoption": break;
                    default: throw new ArgumentException("Only position and perft commands are supported.");
                }
            }
            catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException or OverflowException)
            {
                Console.Error.WriteLine(error.Message);
                Environment.ExitCode = 1;
                return;
            }
        }
    }

    private static Position ReadPosition(string[] tokens)
    {
        Position next;
        int cursor;
        if (tokens.Length > 1 && tokens[1] == "startpos")
        {
            next = Position.Initial;
            cursor = 2;
        }
        else if (tokens.Length >= 8 && tokens[1] == "fen")
        {
            next = Fen.Parse(string.Join(' ', tokens[2..8])).OrThrow();
            cursor = 8;
        }
        else throw new ArgumentException("Expected startpos or six FEN fields.");
        if (cursor == tokens.Length) return next;
        if (tokens[cursor++] != "moves") throw new ArgumentException("Expected moves after the position.");
        while (cursor < tokens.Length)
            next = (Position)MoveRules.Apply(next, Uci.Parse(next, tokens[cursor++]).OrThrow()).Value!;
        return next;
    }
}
