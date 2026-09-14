using System.Globalization;
using Chess.Contracts;

namespace Chess.Cli;

public sealed record CliArguments
{
    public required string Command { get; init; }
    public required string[] Arguments { get; init; }
    public required IReadOnlyDictionary<string, string> Options { get; init; }
    public string? Option(string key) => Options.GetValueOrDefault(key);
    public string RequiredArgument(int index, string description) => Arguments.Length > index
        ? Arguments[index] : throw new CliUsageException($"Missing {description}. Run 'dotnet chess help' for usage.");
    public Guid? RequestId => Option("request-id") is { } value ? ParseGuid(value, "request-id") : null;
    public MoveNotation Notation => Option("notation") switch
    {
        null or "san" => MoveNotation.San,
        "uci" => MoveNotation.Uci,
        _ => throw new CliUsageException("--notation must be san or uci.")
    };
    public int Number(string key, int fallback, int minimum, int maximum) => Option(key) is not { } value ? fallback
        : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number : throw new CliUsageException($"--{key} must be between {minimum} and {maximum}.");
    public static Guid ParseGuid(string value, string option) => Guid.TryParse(value, out var id) && id != Guid.Empty
        ? id : throw new CliUsageException($"--{option} must be a nonempty UUID.");

    public static CliArguments Parse(string[] args)
    {
        var words = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["server", "session", "match", "code", "notation", "timeout", "after-ply", "fen", "request-id"];
        for (var index = 0; index < args.Length; index++)
        {
            var word = args[index];
            if (word is "--help" or "-h") return new() { Command = "help", Arguments = [], Options = options };
            if (!word.StartsWith("--", StringComparison.Ordinal)) { words.Add(word); continue; }
            var split = word.IndexOf('=');
            var key = split < 0 ? word[2..] : word[2..split];
            if (!allowed.Contains(key, StringComparer.Ordinal)) throw new CliUsageException($"Unknown option --{key}.");
            var value = split < 0
                ? ++index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal) ? args[index] : throw new CliUsageException($"--{key} needs a value.")
                : word[(split + 1)..];
            if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"--{key} needs a value.");
            if (!options.TryAdd(key, value)) throw new CliUsageException($"--{key} was supplied more than once.");
        }
        var command = words.FirstOrDefault() ?? "tui";
        var arguments = words.Skip(1).ToArray();
        var expected = command switch { "join" or "resume" or "move" or "draw" or "claim" => 1, _ => 0 };
        if (arguments.Length != expected) throw new CliUsageException($"'{command}' expects {expected} argument(s). Run 'dotnet chess help' for usage.");
        if (command is not ("help" or "tui" or "create" or "join" or "resume" or "random" or "show" or "history" or "move" or "wait" or "resign" or "draw" or "claim"))
            throw new CliUsageException($"Unknown command '{command}'.");
        return new() { Command = command, Arguments = arguments, Options = options };
    }
}

public sealed class CliUsageException(string message) : Exception(message);
