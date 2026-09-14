using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class LegalMoveTests
{
    [Test]
    public async Task InitialPositionHasTheStandardPlacementAndMetadata()
    {
        var initial = Position.Initial;
        const string backRank = "RNBQKBNR";
        for (var file = 0; file < 8; file++)
        {
            await Assert.That(At(initial, $"{(char)('a' + file)}1")).IsEqualTo(backRank[file]);
            await Assert.That(At(initial, $"{(char)('a' + file)}2")).IsEqualTo('P');
            await Assert.That(At(initial, $"{(char)('a' + file)}7")).IsEqualTo('p');
            await Assert.That(At(initial, $"{(char)('a' + file)}8")).IsEqualTo(char.ToLowerInvariant(backRank[file]));
            for (var rank = 3; rank <= 6; rank++)
            {
                await Assert.That(At(initial, $"{(char)('a' + file)}{rank}")).IsEqualTo('.');
            }
        }

        await Assert.That(initial.SideToMove).IsEqualTo((Side)Side.White);
        await Assert.That(initial.WhiteCastlingRights).IsEqualTo((CastlingRights)CastlingRights.Both);
        await Assert.That(initial.BlackCastlingRights).IsEqualTo((CastlingRights)CastlingRights.Both);
        await Assert.That(initial.EnPassant).IsEqualTo((EnPassantState)EnPassantState.None);
        await Assert.That(initial.HalfmoveClock).IsEqualTo(0);
        await Assert.That(initial.FullmoveNumber).IsEqualTo(1);
        await Assert.That(MoveRules.IsInCheck(initial)).IsFalse();
        await Assert.That(MoveRules.HasLegalMove(initial)).IsTrue();
        var moved = await Result<Position>(MoveRules.Apply(initial, Move("e2", "e4")));
        await Assert.That(At(initial, "e2")).IsEqualTo('P');
        await Assert.That(At(moved, "e4")).IsEqualTo('P');
    }

    [Test]
    [Arguments(1, 20)]
    [Arguments(2, 400)]
    [Arguments(3, 8902)]
    public async Task InitialPositionPerft(int depth, int expected)
    {
        await Assert.That(Perft(Position.Initial, depth)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(1, 48)]
    [Arguments(2, 2039)]
    public async Task CastlingAndTacticalPositionPerft(int depth, int expected)
    {
        var position = FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        await Assert.That(Perft(position, depth)).IsEqualTo(expected);
    }

    [Test]
    public async Task CheckQueriesIncludePinnedAttackersAndEitherSide()
    {
        var position = Setup(("d5", 'K'), ("e1", 'R'), ("e8", 'k'), ("e7", 'n'));
        await Assert.That(MoveRules.IsInCheck(position)).IsTrue();
        await Assert.That(MoveRules.IsInCheck(position, Side.Black)).IsFalse();
        await Assert.That(MoveRules.IsInCheck(position with { SideToMove = Side.Black })).IsFalse();
    }

    [Test]
    public async Task PromotionsEnumerateEveryChoiceForPushesAndCaptures()
    {
        var position = Setup(("e1", 'K'), ("h8", 'k'), ("a7", 'P'), ("b8", 'r'));
        var promotions = MoveRules.GetLegalMoves(position).Select(move => move.Value).OfType<Promote>().ToArray();
        await Assert.That(promotions.Length).IsEqualTo(8);
        await Assert.That(promotions.Select(p => p.Piece.Value).Distinct().Count()).IsEqualTo(4);
        foreach (var promotion in promotions)
        {
            await Result<Position>(MoveRules.Apply(position, promotion));
        }
    }

    [Test]
    public async Task CastlingIsEmittedAsAnExplicitRequest()
    {
        var position = Setup(("e1", 'K'), ("a1", 'R'), ("h1", 'R'), ("e8", 'k')) with
        {
            WhiteCastlingRights = CastlingRights.Both
        };
        var moves = MoveRules.GetLegalMoves(position).ToArray();
        await Assert.That(moves.Count(move => move is Castle)).IsEqualTo(2);
        await Assert.That(moves.Any(move => move is MovePiece p && p.From == Square("e1") && p.To == Square("g1"))).IsFalse();
    }

    [Test]
    public async Task EnPassantMustNotExposeTheKing()
    {
        var position = Setup(("h5", 'K'), ("h8", 'k'), ("g5", 'P'), ("f5", 'p'), ("a5", 'r')) with
        {
            EnPassant = new EnPassantTarget { Square = Square("f6") }
        };
        await Assert.That(MoveRules.GetLegalMoves(position).Contains((MoveRequest)Move("g5", "f6"))).IsFalse();
        var unpinned = position with { Board = position.Board.Remove(Square("a5")) };
        await Assert.That(MoveRules.GetLegalMoves(unpinned).Contains((MoveRequest)Move("g5", "f6"))).IsTrue();
    }

    [Test]
    [Arguments("g7", true)]
    [Arguments("f7", false)]
    public async Task NoReplyDistinguishesMateFromStalemate(string queen, bool check)
    {
        var position = Setup(("f6", 'K'), (queen, 'Q'), ("h8", 'k')) with { SideToMove = Side.Black };
        await Assert.That(MoveRules.IsInCheck(position)).IsEqualTo(check);
        await Assert.That(MoveRules.HasLegalMove(position)).IsFalse();
        await Assert.That(MoveRules.GetLegalMoves(position).Count()).IsEqualTo(0);
    }

    [Test]
    public async Task EnumerationCanBePartiallyConsumedAndRepeated()
    {
        var moves = MoveRules.GetLegalMoves(Position.Initial);
        using (var iterator = moves.GetEnumerator())
        {
            await Assert.That(iterator.MoveNext()).IsTrue();
            await Result<Position>(MoveRules.Apply(Position.Initial, iterator.Current));
        }
        await Assert.That(moves.Count()).IsEqualTo(20);
        await Assert.That(moves.Distinct().Count()).IsEqualTo(20);
    }

    [Test]
    public async Task CounterOverflowDoesNotChangeChessLegality()
    {
        var position = Position.Initial with { HalfmoveClock = int.MaxValue, FullmoveNumber = int.MaxValue, SideToMove = Side.Black };
        await Assert.That(MoveRules.HasLegalMove(position)).IsTrue();
        await Assert.That(MoveRules.GetLegalMoves(position).Count()).IsEqualTo(20);
        await Result<MoveCounterOverflow>(MoveRules.Apply(position, Move("e7", "e5")));
    }

    private static int Perft(Position position, int depth)
    {
        if (depth == 0)
        {
            return 1;
        }
        var nodes = 0;
        foreach (var request in MoveRules.GetLegalMoves(position))
        {
            var next = MoveRules.Apply(position, request) switch
            {
                Position accepted => accepted,
                _ => throw new InvalidOperationException("Generated move was rejected.")
            };
            nodes += Perft(next, depth - 1);
        }
        return nodes;
    }
}
