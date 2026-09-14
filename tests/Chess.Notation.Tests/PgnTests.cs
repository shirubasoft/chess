namespace Chess.Notation.Tests;

public sealed class PgnTests
{
    [Test]
    public async Task ImportsTagsAnnotationsCommentsAndNestedVariations()
    {
        const string pgn = """
            [Event "A \"quoted\" event"]
            [Site "A\\B"]
            [Result "*"]

            {Opening} 1.e4! {King pawn} (1.d4 d5 (1...Nf6)) e5 $2
            2.Nf3 ; Developing
            Nc6 *
            """;
        var game = Pgn.Parse(pgn).OrThrow();
        await Assert.That(game.Tags["Event"]).IsEqualTo("A \"quoted\" event");
        await Assert.That(game.Tags["Site"]).IsEqualTo("A\\B");
        await Assert.That(game.Mainline.Comments[0]).IsEqualTo("Opening");
        await Assert.That(game.Mainline.Moves.Length).IsEqualTo(4);
        var first = game.Mainline.Moves[0];
        await Assert.That(first.Annotations[0]).IsEqualTo(1);
        await Assert.That(first.Comments[0]).IsEqualTo("King pawn");
        await Assert.That(game.Mainline.Moves[1].Annotations[0]).IsEqualTo(2);
        await Assert.That(game.Mainline.Moves[2].Comments[0]).IsEqualTo(" Developing");
        var variation = first.Variations[0];
        await Assert.That(Uci.Format(Position.Initial, variation.Moves[0].Move)).IsEqualTo("d2d4");
        await Assert.That(Uci.Format(Position.Initial, variation.Moves[1].Variations[0].Moves[0].Move)).IsEqualTo("g8f6");
        await Assert.That(game.Result).IsEqualTo(PgnResult.Unfinished);
    }

    [Test]
    public async Task StreamsGamesAndUsesSetupForBlackToMove()
    {
        const string pgn = """
            % Exporter metadata
            [SetUp "1"]
            [FEN "4k3/8/8/8/8/8/p7/4K3 b - - 0 42"]
            [Result "0-1"]
            42...a1=Q+ 0-1

            1.f3 e5 2.g4 Qh4# 0-1
            """;
        var games = Pgn.ReadGames(pgn).Select(result => result.OrThrow()).ToArray();
        await Assert.That(games.Length).IsEqualTo(2);
        await Assert.That(games[0].InitialPosition.FullmoveNumber).IsEqualTo(42);
        await Assert.That(games[0].Mainline.Moves[0].Move.Value).IsTypeOf<Promote>();
        var position = games[1].InitialPosition;
        foreach (var item in games[1].Mainline.Moves)
            position = (Position)MoveRules.Apply(position, item.Move).Value!;
        await Assert.That(PositionRules.GetOutcome(position).Value).IsTypeOf<Checkmate>();
        await Assert.That(Pgn.Parse(pgn).Value).IsTypeOf<NotationError>();
    }

    [Test]
    public async Task ErrorIdentifiesTheOffendingMoveOffset()
    {
        const string pgn = "1. e4 e5 2. Bh6 *";
        var error = await Assert.That(Pgn.Parse(pgn).Value).IsTypeOf<NotationError>().And.IsNotNull();
        await Assert.That(error.Kind).IsEqualTo(NotationErrorKind.IllegalMove);
        await Assert.That(error.Offset).IsEqualTo(pgn.IndexOf("Bh6", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("")]
    [Arguments("1. e4")]
    [Arguments("1. e4 (1. d4 *")]
    [Arguments("1. e4 ) *")]
    [Arguments("(1. e4) *")]
    [Arguments("1. e4 () *")]
    [Arguments("1. e4 (1. e5) *")]
    [Arguments("1. e4 {unfinished")]
    [Arguments("$1 1. e4 *")]
    [Arguments("1. e4 $256 *")]
    [Arguments("[Result \"1-0\"] 1. e4 0-1")]
    [Arguments("[Event \"first\"] [Event \"second\"] *")]
    [Arguments("[Event \"unterminated] *")]
    [Arguments("[Event \"bad\\t escape\"] *")]
    [Arguments("[SetUp \"1\"] *")]
    [Arguments("[FEN \"4k3/8/8/8/8/8/8/4K3 w - - 0 1\"] *")]
    [Arguments("[Variant \"Chess960\"] *")]
    [Arguments("2. e4 *")]
    [Arguments("1... e4 *")]
    public async Task RejectsMalformedOrInconsistentGames(string pgn)
    {
        await Assert.That(Pgn.Parse(pgn).Value).IsTypeOf<NotationError>();
    }

    [Test]
    public async Task PartialEnumerationDoesNotReadTheFollowingBrokenGame()
    {
        using var games = Pgn.ReadGames("1. e4 * 1. impossible *").GetEnumerator();
        await Assert.That(games.MoveNext()).IsTrue();
        await Assert.That(games.Current.Value).IsTypeOf<Parsed<PgnGame>>();
        await Assert.That(games.MoveNext()).IsTrue();
        await Assert.That(games.Current.Value).IsTypeOf<NotationError>();
        await Assert.That(games.MoveNext()).IsFalse();
    }

    [Test]
    public async Task EnumerationCanBeRepeatedAndCommentsCanSurroundTagsAndResult()
    {
        var games = Pgn.ReadGames("{Before tags} [Event \"Test\"] 1. e4 * {After result}");
        foreach (var pass in new[] { games, games })
        {
            var game = pass.Single().OrThrow();
            await Assert.That(game.Mainline.Comments[0]).IsEqualTo("Before tags");
            await Assert.That(game.Mainline.Moves[0].Comments[0]).IsEqualTo("After result");
        }
    }
}
