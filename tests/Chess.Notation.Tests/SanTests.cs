namespace Chess.Notation.Tests;

public sealed class SanTests
{
    [Test]
    [Arguments(FenTests.Initial)]
    [Arguments(FenTests.Kiwipete)]
    [Arguments("7k/8/8/8/8/8/8/KN3N2 w - - 0 1")]
    [Arguments("7k/8/8/8/8/R7/8/R6K w - - 0 1")]
    [Arguments("7k/8/8/1N6/8/1N3N2/8/K7 w - - 0 1")]
    public async Task EveryLegalMoveHasUnambiguousRoundTrip(string fen)
    {
        var position = Fen.Parse(fen).OrThrow();
        foreach (var move in MoveRules.GetLegalMoves(position))
            await Assert.That(San.Parse(position, San.Format(position, move)).OrThrow()).IsEqualTo(move);
    }

    [Test]
    [Arguments("7k/8/8/8/8/8/8/KN3N2 w - - 0 1", "b1d2", "Nbd2")]
    [Arguments("7k/8/8/8/8/R7/8/R6K w - - 0 1", "a1a2", "R1a2")]
    [Arguments("7k/8/8/1N6/8/1N3N2/8/K7 w - - 0 1", "b3d4", "Nb3d4")]
    [Arguments("k3r3/8/8/8/8/8/4N1N1/4K3 w - - 0 1", "g2f4", "Nf4")]
    [Arguments("7k/8/5KQ1/8/8/8/8/8 w - - 0 1", "g6g7", "Qg7#")]
    [Arguments("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "a1a8", "Ra8+")]
    [Arguments("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8n", "a8=N")]
    [Arguments("1r2k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7b8q", "axb8=Q+")]
    [Arguments("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "e5d6", "exd6")]
    [Arguments("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
    public async Task CanonicalSanReflectsLegalAlternativesAndMoveEffects(string fen, string uci, string san)
    {
        var position = Fen.Parse(fen).OrThrow();
        var move = Uci.Parse(position, uci).OrThrow();
        await Assert.That(San.Format(position, move)).IsEqualTo(san);
        await Assert.That(San.Parse(position, san).OrThrow()).IsEqualTo(move);
    }

    [Test]
    public async Task AmbiguityIsReportedSeparatelyFromIllegality()
    {
        var position = Fen.Parse("7k/8/8/8/8/8/8/KN3N2 w - - 0 1").OrThrow();
        var error = await Assert.That(San.Parse(position, "Nd2").Value).IsTypeOf<NotationError>().And.IsNotNull();
        await Assert.That(error.Kind).IsEqualTo(NotationErrorKind.AmbiguousMove);
    }

    [Test]
    [Arguments("e4+")]
    [Arguments("e4#")]
    [Arguments("e4++")]
    [Arguments("ee4")]
    [Arguments("e2e4")]
    [Arguments("Ng1f3")]
    [Arguments("Nxf3")]
    [Arguments("e5")]
    [Arguments("O-O")]
    [Arguments("e4!")]
    [Arguments(" e4")]
    public async Task RejectsIncorrectMoveNotation(string san)
    {
        await Assert.That(San.Parse(Position.Initial, san).Value).IsTypeOf<NotationError>();
    }
}
