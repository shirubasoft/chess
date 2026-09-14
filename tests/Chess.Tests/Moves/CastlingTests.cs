using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class CastlingTests
{
    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task CastlingMovesBothPiecesAndUpdatesThePosition(bool white, bool kingSide)
    {
        var rank = white ? '1' : '8';
        var rookFrom = $"{(kingSide ? 'h' : 'a')}{rank}";
        var kingTo = $"{(kingSide ? 'g' : 'c')}{rank}";
        var rookTo = $"{(kingSide ? 'f' : 'd')}{rank}";
        var position = Setup(("e1", 'K'), ("e8", 'k'), (rookFrom, white ? 'R' : 'r')) with
        {
            SideToMove = white ? Side.White : Side.Black,
            WhiteCastlingRights = CastlingRights.Both,
            BlackCastlingRights = CastlingRights.Both,
            EnPassant = new EnPassantTarget { Square = Square("b6") }
        };

        var next = await Result<Position>(MoveRules.Apply(position, new Castle
        {
            Wing = kingSide ? CastlingWing.KingSide : CastlingWing.QueenSide
        }));

        await Assert.That(At(next, kingTo)).IsEqualTo(white ? 'K' : 'k');
        await Assert.That(At(next, rookTo)).IsEqualTo(white ? 'R' : 'r');
        await Assert.That(At(next, $"e{rank}")).IsEqualTo('.');
        await Assert.That(At(next, rookFrom)).IsEqualTo('.');
        await Assert.That((white ? next.WhiteCastlingRights : next.BlackCastlingRights).Value)
            .IsSameReferenceAs(CastlingRights.None);
        await Assert.That((white ? next.BlackCastlingRights : next.WhiteCastlingRights).Value)
            .IsSameReferenceAs(CastlingRights.Both);
        await Assert.That(next.SideToMove is Black).IsEqualTo(white);
        await Assert.That(next.EnPassant.Value).IsSameReferenceAs(EnPassantState.None);
        await Assert.That(next.HalfmoveClock).IsEqualTo(9);
        await Assert.That(next.FullmoveNumber).IsEqualTo(white ? 12 : 13);
        await Assert.That(At(position, $"e{rank}")).IsEqualTo(white ? 'K' : 'k');
        await Assert.That(At(position, rookFrom)).IsEqualTo(white ? 'R' : 'r');
    }

    [Test]
    public async Task CastlingRequiresTheMatchingRightAndTheHomeKingAndRook()
    {
        var request = new Castle { Wing = CastlingWing.KingSide };
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("h1", 'R'));
        await Result<CastlingUnavailable>(MoveRules.Apply(position, request));
        await Result<CastlingUnavailable>(MoveRules.Apply(position with
        {
            WhiteCastlingRights = CastlingRights.QueenSide
        }, request));

        foreach (var rook in new[] { 'N', 'r' })
        {
            var wrongRook = Setup(("e1", 'K'), ("a8", 'k'), ("h1", rook)) with
            {
                WhiteCastlingRights = CastlingRights.KingSide
            };
            await Result<CastlingUnavailable>(MoveRules.Apply(wrongRook, request));
        }

        var missingRook = Setup(("e1", 'K'), ("a8", 'k')) with
        {
            WhiteCastlingRights = CastlingRights.KingSide
        };
        await Result<CastlingUnavailable>(MoveRules.Apply(missingRook, request));
        var awayKing = Setup(("d1", 'K'), ("a8", 'k'), ("h1", 'R')) with
        {
            WhiteCastlingRights = CastlingRights.KingSide
        };
        await Result<CastlingUnavailable>(MoveRules.Apply(awayKing, request));
    }

    [Test]
    [Arguments(true, "f1")]
    [Arguments(true, "g1")]
    [Arguments(false, "b1")]
    [Arguments(false, "c1")]
    [Arguments(false, "d1")]
    public async Task EverySquareBetweenKingAndRookMustBeEmpty(bool kingSide, string blocked)
    {
        var position = Setup(("e1", 'K'), ("e8", 'k'), (kingSide ? "h1" : "a1", 'R'), (blocked, 'N')) with
        {
            WhiteCastlingRights = CastlingRights.Both
        };

        await Result<PathBlocked>(MoveRules.Apply(position, new Castle
        {
            Wing = kingSide ? CastlingWing.KingSide : CastlingWing.QueenSide
        }));
    }

    [Test]
    [Arguments("e8")]
    [Arguments("f8")]
    [Arguments("g8")]
    public async Task KingCannotCastleOutOfThroughOrIntoCheck(string attacker)
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("h1", 'R'), (attacker, 'r')) with
        {
            WhiteCastlingRights = CastlingRights.KingSide
        };

        await Result<KingWouldBeInCheck>(MoveRules.Apply(position, new Castle { Wing = CastlingWing.KingSide }));

        await Assert.That(At(position, "e1")).IsEqualTo('K');
        await Assert.That(At(position, "h1")).IsEqualTo('R');
    }

    [Test]
    public async Task AttacksOnTheRookOrTheQueensideBSquareDoNotPreventCastling()
    {
        var kingSide = Setup(("e1", 'K'), ("a8", 'k'), ("h1", 'R'), ("h8", 'r')) with
        {
            WhiteCastlingRights = CastlingRights.KingSide
        };
        await Result<Position>(MoveRules.Apply(kingSide, new Castle { Wing = CastlingWing.KingSide }));

        var queenSide = Setup(("e1", 'K'), ("h8", 'k'), ("a1", 'R'), ("b8", 'r')) with
        {
            WhiteCastlingRights = CastlingRights.QueenSide
        };
        await Result<Position>(MoveRules.Apply(queenSide, new Castle { Wing = CastlingWing.QueenSide }));
    }

    [Test]
    public async Task ALongKingMoveRequiresAnExplicitCastlingRequest()
    {
        var position = Setup(("e1", 'K'), ("a8", 'k'), ("h1", 'R')) with
        {
            WhiteCastlingRights = CastlingRights.Both
        };

        await Result<InvalidMovement>(MoveRules.Apply(position, Move("e1", "g1")));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task MovingAKingClearsBothOfItsCastlingRights(bool white)
    {
        var position = Setup(("e1", 'K'), ("e8", 'k')) with
        {
            SideToMove = white ? Side.White : Side.Black,
            WhiteCastlingRights = CastlingRights.Both,
            BlackCastlingRights = CastlingRights.Both
        };

        var next = await Result<Position>(MoveRules.Apply(position, white ? Move("e1", "e2") : Move("e8", "e7")));

        await Assert.That((white ? next.WhiteCastlingRights : next.BlackCastlingRights).Value)
            .IsTypeOf<NoCastlingRights>().And.IsNotNull();
        await Assert.That((white ? next.BlackCastlingRights : next.WhiteCastlingRights).Value)
            .IsTypeOf<BothCastlingRights>().And.IsNotNull();
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task MovingAndCapturingHomeRooksRemoveOnlyTheirRespectiveRights(bool white, bool kingSide)
    {
        var file = kingSide ? 'h' : 'a';
        var position = Setup(("e1", 'K'), ("e8", 'k'), ($"{file}1", 'R'), ($"{file}8", 'r')) with
        {
            SideToMove = white ? Side.White : Side.Black,
            WhiteCastlingRights = CastlingRights.Both,
            BlackCastlingRights = CastlingRights.Both
        };
        var from = $"{file}{(white ? '1' : '8')}";
        var to = $"{file}{(white ? '8' : '1')}";

        var next = await Result<Position>(MoveRules.Apply(position, Move(from, to)));

        if (kingSide)
        {
            await Assert.That(next.WhiteCastlingRights.Value).IsTypeOf<QueenSideCastlingRights>().And.IsNotNull();
            await Assert.That(next.BlackCastlingRights.Value).IsTypeOf<QueenSideCastlingRights>().And.IsNotNull();
        }
        else
        {
            await Assert.That(next.WhiteCastlingRights.Value).IsTypeOf<KingSideCastlingRights>().And.IsNotNull();
            await Assert.That(next.BlackCastlingRights.Value).IsTypeOf<KingSideCastlingRights>().And.IsNotNull();
        }
        await Assert.That(next.HalfmoveClock).IsEqualTo(0);
    }

    [Test]
    public async Task ReturningARookHomeDoesNotRestoreLostRights()
    {
        var position = Setup(("e1", 'K'), ("e8", 'k'), ("h1", 'R')) with
        {
            WhiteCastlingRights = CastlingRights.Both
        };
        var moved = await Result<Position>(MoveRules.Apply(position, Move("h1", "h2")));
        var returned = await Result<Position>(MoveRules.Apply(moved with { SideToMove = Side.White }, Move("h2", "h1")));

        await Assert.That(returned.WhiteCastlingRights.Value).IsTypeOf<QueenSideCastlingRights>().And.IsNotNull();
    }
}
