namespace Chess.Notation.Tests;

public sealed class FenTests
{
    internal const string Initial = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    internal const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";

    [Test]
    [Arguments(Initial)]
    [Arguments(Kiwipete)]
    [Arguments("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1")]
    [Arguments("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 b - - 150 2147483647")]
    public async Task PreservesPlacementAndEveryFenField(string fen)
    {
        await Assert.That(Fen.Format(Fen.Parse(fen).OrThrow())).IsEqualTo(fen);
    }

    [Test]
    public async Task StartingPositionMatchesTheDomain()
    {
        await Assert.That(Fen.Format(Position.Initial)).IsEqualTo(Initial);
        var parsed = Fen.Parse(Initial).OrThrow();
        await Assert.That(PositionKey.Create(parsed)).IsEqualTo(PositionKey.Create(Position.Initial));
        await Assert.That(MoveRules.GetLegalMoves(parsed).Count()).IsEqualTo(20);
    }

    [Test]
    [Arguments("")]
    [Arguments("8/8/8/8/8/8/8/8 w - - 0")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - - 0 1 trailing")]
    [Arguments("4k3/8/8/8/8/8/8/4K4 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K2 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K21 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4X3 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 x - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w KK - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w A - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - i6 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - - -1 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - - 0 0")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - - 2147483648 1")]
    public async Task RejectsMalformedNotation(string fen)
    {
        await Assert.That(Fen.Parse(fen).Value).IsTypeOf<NotationError>();
    }

    [Test]
    [Arguments("8/8/8/8/8/8/8/4K3 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/3KK3 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/P3K3 w - - 0 1")]
    [Arguments("8/8/8/8/8/8/4k3/4K3 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/4R3/4K3 w - - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w K - 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - e3 0 1")]
    [Arguments("4k3/8/8/8/8/8/8/4K3 w - e6 0 1")]
    public async Task RejectsUnsafePositionSetupsAtTheBoundary(string fen)
    {
        var error = await Assert.That(Fen.Parse(fen).Value).IsTypeOf<NotationError>().And.IsNotNull();
        await Assert.That(error.Kind).IsEqualTo(NotationErrorKind.InvalidPosition);
    }

    [Test]
    public async Task PreservesRawEnPassantEvenWhenNoCaptureIsLegal()
    {
        const string fen = "4k3/8/8/1K1pP1r1/8/8/8/8 w - d6 0 2";
        var position = Fen.Parse(fen).OrThrow();
        await Assert.That(Fen.Format(position)).IsEqualTo(fen);
        await Assert.That(Uci.Parse(position, "e5d6").Value).IsTypeOf<NotationError>();
        await Assert.That(PositionKey.Create(position)).IsEqualTo(PositionKey.Create(position with { EnPassant = EnPassantState.None }));
    }
}
