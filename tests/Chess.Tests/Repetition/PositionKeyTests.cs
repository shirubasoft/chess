using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class PositionKeyTests
{
    [Test]
    public async Task IndependentlyConstructedBoardsHaveStructuralKeysIgnoringCounters()
    {
        var first = Setup(("e1", 'K'), ("e8", 'k'), ("c4", 'B'));
        var second = Setup(("c4", 'B'), ("e8", 'k'), ("e1", 'K')) with { HalfmoveClock = 99, FullmoveNumber = 400 };
        var key = PositionKey.Create(first);
        var equal = PositionKey.Create(second);
        await Assert.That(key).IsEqualTo(equal);
        await Assert.That(key.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(new HashSet<PositionKey> { key, equal }.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PlacementPieceKindColorAndSideToMoveAffectIdentity()
    {
        var position = Setup(("e1", 'K'), ("e8", 'k'), ("c4", 'B'));
        var key = PositionKey.Create(position);
        await Assert.That(key).IsNotEqualTo(PositionKey.Create(Setup(("e1", 'K'), ("e8", 'k'), ("d5", 'B'))));
        await Assert.That(key).IsNotEqualTo(PositionKey.Create(Setup(("e1", 'K'), ("e8", 'k'), ("c4", 'N'))));
        await Assert.That(key).IsNotEqualTo(PositionKey.Create(Setup(("e1", 'K'), ("e8", 'k'), ("c4", 'b'))));
        await Assert.That(key).IsNotEqualTo(PositionKey.Create(position with { SideToMove = Side.Black }));
    }

    [Test]
    public async Task EveryCastlingRightAffectsIdentityEvenWhenCastlingIsBlocked()
    {
        CastlingRights[] rights = [CastlingRights.None, CastlingRights.KingSide, CastlingRights.QueenSide, CastlingRights.Both];
        var keys = new HashSet<PositionKey>();
        foreach (var white in rights)
        {
            foreach (var black in rights)
            {
                keys.Add(PositionKey.Create(Position.Initial with { WhiteCastlingRights = white, BlackCastlingRights = black }));
            }
        }
        await Assert.That(keys.Count).IsEqualTo(16);
    }

    [Test]
    [Arguments("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]
    [Arguments("4k3/8/8/8/3Pp3/8/8/4K3 b - d3 0 1")]
    [Arguments("4k3/8/8/pP6/8/8/8/4K3 w - a6 0 1")]
    [Arguments("4k3/8/8/6Pp/8/8/8/4K3 w - h6 0 1")]
    public async Task LegalEnPassantChangesIdentityForEitherSideAndAtBoardEdges(string fen)
    {
        var position = FromFen(fen);
        var key = PositionKey.Create(position);
        await Assert.That(key).IsNotEqualTo(PositionKey.Create(position with { EnPassant = EnPassantState.None }));
        await Assert.That(key.EnPassant.Value).IsTypeOf<EnPassantTarget>();
        await Assert.That(key).IsEqualTo(PositionKey.Create(position with { HalfmoveClock = 80, FullmoveNumber = 41 }));
    }

    [Test]
    [Arguments("4k3/8/8/3p4/8/8/8/4K3 w - d6 0 1")]
    [Arguments("4k3/8/8/4P3/8/8/8/4K3 w - d6 0 1")]
    [Arguments("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]
    [Arguments("7k/8/8/r4pPK/8/8/8/8 w - f6 0 1")]
    [Arguments("4k3/8/8/3pR3/8/8/8/4K3 w - d6 0 1")]
    [Arguments("4k3/8/3n4/3pP3/8/8/8/4K3 w - d6 0 1")]
    [Arguments("4k3/8/8/3pP3/8/8/8/4K3 w - d3 0 1")]
    [Arguments("4k3/8/8/3pP3/8/8/8/r3K3 w - d6 0 1")]
    public async Task UnavailableOrIllegalEnPassantDoesNotChangeIdentity(string fen)
    {
        var position = FromFen(fen);
        await Assert.That(PositionKey.Create(position)).IsEqualTo(PositionKey.Create(position with { EnPassant = EnPassantState.None }));
    }

    [Test]
    public async Task OneLegalCapturerIsEnoughWhenTheOtherPawnIsPinned()
    {
        var position = FromFen("k3r3/8/8/2PpP3/8/8/8/4K3 w - d6 0 1");
        await Assert.That(PositionKey.Create(position).EnPassant.Value).IsTypeOf<EnPassantTarget>();
    }
}
