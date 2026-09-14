using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class PromotionTests
{
    [Test]
    [Arguments('Q')]
    [Arguments('R')]
    [Arguments('B')]
    [Arguments('N')]
    public async Task PromotionReplacesThePawnOnEitherSideWithOrWithoutCapture(char choice)
    {
        PromotionPiece promotion = choice switch
        {
            'Q' => new Queen(),
            'R' => new Rook(),
            'B' => new Bishop(),
            'N' => new Knight(),
            _ => throw new ArgumentException("Unknown promotion.", nameof(choice))
        };
        foreach (var white in new[] { true, false })
        {
            foreach (var capture in new[] { true, false })
            {
                var from = white ? "e7" : "e2";
                var to = white ? capture ? "f8" : "e8" : capture ? "f1" : "e1";
                var pieces = new List<(string, char)> { ("a1", 'K'), ("h8", 'k'), (from, white ? 'P' : 'p') };
                if (capture)
                {
                    pieces.Add((to, white ? 'r' : 'R'));
                }

                var position = Setup(pieces.ToArray()) with { SideToMove = white ? new White() : new Black() };
                var request = new Promote { From = Square(from), To = Square(to), Piece = promotion };

                var next = await Result<Position>(MoveRules.Apply(position, request));

                await Assert.That(At(next, to)).IsEqualTo(white ? choice : char.ToLowerInvariant(choice));
                await Assert.That(At(next, from)).IsEqualTo('.');
                await Assert.That(next.HalfmoveClock).IsEqualTo(0);
                await Assert.That(next.FullmoveNumber).IsEqualTo(white ? 12 : 13);
                await Assert.That(At(position, from)).IsEqualTo(white ? 'P' : 'p');
            }
        }
    }

    [Test]
    [Arguments(true, "e7", "e8")]
    [Arguments(false, "e2", "e1")]
    public async Task MissingPromotionChoicePreservesThePositionUntilTheCallerChooses(bool white, string from, string to)
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), (from, white ? 'P' : 'p')) with
        {
            SideToMove = white ? new White() : new Black()
        };
        var originalBoard = position.Board;

        await Result<PromotionRequired>(MoveRules.Apply(position, Move(from, to)));

        await Assert.That(position.Board).IsSameReferenceAs(originalBoard);
        await Assert.That(At(position, from)).IsEqualTo(white ? 'P' : 'p');
        await Assert.That(At(position, to)).IsEqualTo('.');
        await Assert.That(position.HalfmoveClock).IsEqualTo(8);
        var next = await Result<Position>(MoveRules.Apply(position, new Promote
        {
            From = Square(from),
            To = Square(to),
            Piece = new Knight()
        }));
        await Assert.That(At(next, to)).IsEqualTo(white ? 'N' : 'n');
    }

    [Test]
    [Arguments('P', "e6", "e7")]
    [Arguments('R', "e7", "e8")]
    public async Task PromotionRequiresAPawnReachingItsFinalRank(char piece, string from, string to)
    {
        var position = Setup(("a1", 'K'), ("h8", 'k'), (from, piece));

        await Result<InvalidPromotion>(MoveRules.Apply(position, new Promote
        {
            From = Square(from),
            To = Square(to),
            Piece = new Queen()
        }));
    }
}
