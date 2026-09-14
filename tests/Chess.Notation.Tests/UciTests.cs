namespace Chess.Notation.Tests;

public sealed class UciTests
{
    [Test]
    [Arguments(FenTests.Initial)]
    [Arguments(FenTests.Kiwipete)]
    [Arguments("4k3/P7/8/8/8/8/8/4K3 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/p7/4K3 b - - 0 1")]
    public async Task EveryLegalMoveRoundTrips(string fen)
    {
        var position = Fen.Parse(fen).OrThrow();
        foreach (var move in MoveRules.GetLegalMoves(position))
        {
            await Assert.That(Uci.Parse(position, Uci.Format(position, move)).OrThrow()).IsEqualTo(move);
        }
    }

    [Test]
    [Arguments("e1g1", "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")]
    [Arguments("e1c1", "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")]
    [Arguments("e8g8", "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1")]
    [Arguments("e8c8", "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1")]
    public async Task CastlingUsesTheDomainCastleRequest(string uci, string fen)
    {
        await Assert.That(Uci.Parse(Fen.Parse(fen).OrThrow(), uci).OrThrow().Value).IsTypeOf<Castle>();
    }

    [Test]
    [Arguments("e2e5")]
    [Arguments("e7e5")]
    [Arguments("e1g1")]
    [Arguments("e2e4q")]
    [Arguments("e2e2")]
    public async Task RejectsIllegalMoves(string move)
    {
        var error = await Assert.That(Uci.Parse(Position.Initial, move).Value).IsTypeOf<NotationError>().And.IsNotNull();
        await Assert.That(error.Kind).IsEqualTo(NotationErrorKind.IllegalMove);
        await Assert.That(Fen.Format(Position.Initial)).IsEqualTo(FenTests.Initial);
    }

    [Test]
    [Arguments("")]
    [Arguments("0000")]
    [Arguments("e2e4 ")]
    [Arguments("E2e4")]
    [Arguments("e9e4")]
    [Arguments("e2e4Q")]
    public async Task RejectsMalformedAndNullMoves(string move)
    {
        var error = await Assert.That(Uci.Parse(Position.Initial, move).Value).IsTypeOf<NotationError>().And.IsNotNull();
        await Assert.That(error.Kind).IsEqualTo(NotationErrorKind.Syntax);
    }
}
