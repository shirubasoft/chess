using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Chess.Notation;

public static class Pgn
{
    public static NotationResult<PgnGame> Parse(string text)
    {
        using var games = ReadGames(text).GetEnumerator();
        if (!games.MoveNext()) return Error("Expected a PGN game.", 0);
        var first = games.Current;
        if (first is NotationError) return first;
        return games.MoveNext() ? Error("Expected one game; use ReadGames for a collection.", 0) : first;
    }

    public static IEnumerable<NotationResult<PgnGame>> ReadGames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Read();

        IEnumerable<NotationResult<PgnGame>> Read()
        {
            foreach (var result in new Reader(text).ReadGames()) yield return result;
        }
    }

    private static NotationError Error(string message, int offset) => new()
    {
        Kind = NotationErrorKind.InvalidGame, Message = message, Offset = offset
    };

    private sealed class Reader(string text)
    {
        private readonly Lexer _lexer = new(text);
        private Token _current;
        private NotationError? _nextGameError;

        internal IEnumerable<NotationResult<PgnGame>> ReadGames()
        {
            var started = false;
            while (true)
            {
                NotationResult<PgnGame> result;
                try
                {
                    if (!started)
                    {
                        Advance();
                        started = true;
                    }
                    if (_nextGameError is { } error) throw new NotationException(error);
                    if (_current.Kind == TokenKind.End) yield break;
                    result = new Parsed<PgnGame> { Value = ReadGame() };
                }
                catch (NotationException exception)
                {
                    result = exception.Error;
                }
                yield return result;
                if (result is NotationError) yield break;
            }
        }

        private PgnGame ReadGame()
        {
            var leadingComments = ImmutableArray.CreateBuilder<string>();
            while (_current.Kind == TokenKind.Comment)
            {
                leadingComments.Add(_current.Text);
                Advance();
            }
            var tags = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            while (_current.Kind == TokenKind.Tag)
            {
                if (!tags.TryAdd(_current.Text, _current.Value!)) Fail("Duplicate PGN tag.");
                Advance();
            }
            if (tags.TryGetValue("Variant", out var variant) && variant is not ("Standard" or "Chess"))
                throw NotationSyntax.Error("Only standard chess PGN is supported.", _current.Offset, NotationErrorKind.UnsupportedVariant);
            var initial = Position.Initial;
            if (tags.TryGetValue("SetUp", out var setup) && setup is not ("0" or "1")) Fail("SetUp must be 0 or 1.");
            if (tags.TryGetValue("FEN", out var fen))
            {
                if (setup != "1") Fail("A FEN tag requires SetUp 1.");
                initial = Fen.Parse(fen).OrThrow();
            }
            else if (setup == "1") Fail("SetUp 1 requires a FEN tag.");
            var line = ReadLine(initial, 0, out var result);
            line = line with { Comments = leadingComments.ToImmutable().AddRange(line.Comments) };
            if (tags.TryGetValue("Result", out var recorded) && recorded != ResultToken(result!.Value))
                Fail("The Result tag and movetext result disagree.");
            return new PgnGame { Tags = tags.ToImmutable(), InitialPosition = initial, Mainline = line, Result = result!.Value };
        }

        private PgnLine ReadLine(Position initial, int depth, out PgnResult? result)
        {
            if (depth > 64) Fail("PGN variation nesting exceeds 64 levels.");
            var moves = ImmutableArray.CreateBuilder<PgnMove>();
            var comments = ImmutableArray.CreateBuilder<string>();
            var position = initial;
            var beforeLast = initial;
            result = null;
            while (true)
            {
                switch (_current.Kind)
                {
                    case TokenKind.Comment:
                        if (moves.Count == 0) comments.Add(_current.Text);
                        else moves[^1] = moves[^1] with { Comments = moves[^1].Comments.Add(_current.Text) };
                        Advance();
                        break;
                    case TokenKind.Number:
                        ValidateNumber(position);
                        Advance();
                        break;
                    case TokenKind.Annotation:
                        if (moves.Count == 0) Fail("An annotation must follow a move.");
                        moves[^1] = moves[^1] with { Annotations = moves[^1].Annotations.Add(int.Parse(_current.Text, CultureInfo.InvariantCulture)) };
                        Advance();
                        break;
                    case TokenKind.OpenVariation:
                        if (moves.Count == 0) Fail("A variation must replace a preceding move.");
                        Advance();
                        var variation = ReadLine(beforeLast, depth + 1, out _);
                        moves[^1] = moves[^1] with { Variations = moves[^1].Variations.Add(variation) };
                        break;
                    case TokenKind.CloseVariation:
                        if (depth == 0 || moves.Count == 0) Fail("Unexpected or empty variation.");
                        Advance();
                        return new PgnLine { Comments = comments.ToImmutable(), Moves = moves.ToImmutable() };
                    case TokenKind.Result:
                        if (depth != 0) Fail("A game result cannot terminate a variation.");
                        result = _current.Text switch
                        {
                            "1-0" => PgnResult.WhiteWins, "0-1" => PgnResult.BlackWins,
                            "1/2-1/2" => PgnResult.Draw, "*" => PgnResult.Unfinished,
                            _ => throw new InvalidOperationException("Unexpected result token.")
                        };
                        try
                        {
                            Advance();
                            while (_current.Kind == TokenKind.Comment)
                            {
                                if (moves.Count == 0) comments.Add(_current.Text);
                                else moves[^1] = moves[^1] with { Comments = moves[^1].Comments.Add(_current.Text) };
                                Advance();
                            }
                        }
                        catch (NotationException trailingError)
                        {
                            _nextGameError = trailingError.Error;
                        }
                        return new PgnLine { Comments = comments.ToImmutable(), Moves = moves.ToImmutable() };
                    case TokenKind.Symbol:
                        var parsed = San.Parse(position, _current.Text);
                        if (parsed is NotationError error) throw new NotationException(error with { Offset = _current.Offset });
                        var move = parsed.OrThrow();
                        beforeLast = position;
                        position = MoveRules.Apply(position, move) switch
                        {
                            Position next => next,
                            _ => throw new InvalidOperationException("A parsed SAN move must be legal.")
                        };
                        moves.Add(new PgnMove { Move = move, Comments = [], Annotations = [], Variations = [] });
                        Advance();
                        break;
                    default:
                        Fail(depth == 0 ? "A game must end with a result marker." : "Unclosed variation.");
                        break;
                }
            }
        }

        private void ValidateNumber(Position position)
        {
            var dots = _current.Text.IndexOf('.');
            if (!int.TryParse(_current.Text.AsSpan(0, dots), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number != position.FullmoveNumber || _current.Text.Length - dots != (position.SideToMove is White ? 1 : 3))
                Fail("Move number does not match the position and active color.");
        }

        private void Advance() => _current = _lexer.Next();
        private void Fail(string message) => throw new NotationException(Error(message, _current.Offset));
    }

    private static string ResultToken(PgnResult result) => result switch
    {
        PgnResult.WhiteWins => "1-0", PgnResult.BlackWins => "0-1", PgnResult.Draw => "1/2-1/2", PgnResult.Unfinished => "*",
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };

    private enum TokenKind { End, Tag, Symbol, Number, Comment, Annotation, OpenVariation, CloseVariation, Result }
    private readonly record struct Token(TokenKind Kind, string Text, int Offset, string? Value = null);

    private sealed class Lexer(string text)
    {
        private int _offset = text.StartsWith('\uFEFF') ? 1 : 0;

        internal Token Next()
        {
            SkipWhitespace();
            var start = _offset;
            if (start == text.Length) return new Token(TokenKind.End, "", start);
            var current = text[_offset++];
            switch (current)
            {
                case '[': return Tag(start);
                case '(' : return new Token(TokenKind.OpenVariation, "(", start);
                case ')' : return new Token(TokenKind.CloseVariation, ")", start);
                case '{':
                    var close = text.IndexOf('}', _offset);
                    if (close < 0) throw NotationSyntax.Error("Unclosed PGN comment.", start);
                    var comment = text[_offset..close];
                    _offset = close + 1;
                    return new Token(TokenKind.Comment, comment, start);
                case ';':
                    while (_offset < text.Length && text[_offset] is not ('\r' or '\n')) _offset++;
                    return new Token(TokenKind.Comment, text[(start + 1).._offset], start);
                case '$':
                    while (_offset < text.Length && char.IsAsciiDigit(text[_offset])) _offset++;
                    if (!int.TryParse(text.AsSpan(start + 1, _offset - start - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var nag) || nag > 255)
                        throw NotationSyntax.Error("Numeric annotation must be between 0 and 255.", start);
                    return new Token(TokenKind.Annotation, nag.ToString(CultureInfo.InvariantCulture), start);
                case '!' or '?':
                    while (_offset < text.Length && text[_offset] is '!' or '?') _offset++;
                    var annotation = text[start.._offset] switch
                    {
                        "!" => "1", "?" => "2", "!!" => "3", "??" => "4", "!?" => "5", "?!" => "6",
                        _ => throw NotationSyntax.Error("Unknown annotation suffix.", start)
                    };
                    return new Token(TokenKind.Annotation, annotation, start);
                case '}' or ']' or '"': throw NotationSyntax.Error("Unexpected PGN delimiter.", start);
            }
            if (char.IsAsciiDigit(current))
            {
                var cursor = _offset;
                while (cursor < text.Length && char.IsAsciiDigit(text[cursor])) cursor++;
                if (cursor < text.Length && text[cursor] == '.')
                {
                    while (cursor < text.Length && text[cursor] == '.') cursor++;
                    _offset = cursor;
                    return new Token(TokenKind.Number, text[start..cursor], start);
                }
            }
            while (_offset < text.Length && !char.IsWhiteSpace(text[_offset]) && !"[]{}();$!?\"".Contains(text[_offset])) _offset++;
            var symbol = text[start.._offset];
            return new Token(symbol is "1-0" or "0-1" or "1/2-1/2" or "*" ? TokenKind.Result : TokenKind.Symbol, symbol, start);
        }

        private Token Tag(int start)
        {
            SkipWhitespace();
            var nameStart = _offset;
            while (_offset < text.Length && (char.IsAsciiLetterOrDigit(text[_offset]) || text[_offset] == '_')) _offset++;
            var name = text[nameStart.._offset];
            if (name.Length == 0 || !char.IsAsciiLetterOrDigit(name[0]) || _offset >= text.Length || !char.IsWhiteSpace(text[_offset]))
                throw NotationSyntax.Error("Expected a PGN tag name followed by whitespace.", start);
            SkipWhitespace();
            Require('"', start);
            var value = new StringBuilder();
            var closed = false;
            while (_offset < text.Length)
            {
                var character = text[_offset++];
                if (character == '"') { closed = true; break; }
                if (character is '\r' or '\n') throw NotationSyntax.Error("A tag string cannot span lines.", start);
                if (character == '\\')
                {
                    if (_offset >= text.Length || text[_offset] is not ('\\' or '"')) throw NotationSyntax.Error("Invalid tag escape.", _offset - 1);
                    character = text[_offset++];
                }
                value.Append(character);
            }
            if (!closed) throw NotationSyntax.Error("Unclosed PGN tag string.", start);
            SkipWhitespace();
            Require(']', start);
            return new Token(TokenKind.Tag, name, start, value.ToString());
        }

        private void Require(char expected, int start)
        {
            if (_offset >= text.Length || text[_offset++] != expected) throw NotationSyntax.Error($"Expected '{expected}'.", start);
        }

        private void SkipWhitespace()
        {
            while (_offset < text.Length)
            {
                if (char.IsWhiteSpace(text[_offset])) { _offset++; continue; }
                if (text[_offset] == '%' && (_offset == 0 || text[_offset - 1] is '\r' or '\n'))
                {
                    while (_offset < text.Length && text[_offset] is not ('\r' or '\n')) _offset++;
                    continue;
                }
                break;
            }
        }
    }
}
