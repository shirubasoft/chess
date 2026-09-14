using static Chess.Tests.MoveFixtures;

namespace Chess.Tests;

public sealed class PositionHistoryTests
{
    [Test]
    public async Task InitialPositionCountsAndNonconsecutiveRepetitionsAccumulate()
    {
        var original = PositionHistory.Start(Position.Initial);
        var history = original;
        await Assert.That(original.CurrentOccurrences).IsEqualTo(1);
        for (var repetition = 2; repetition <= 5; repetition++)
        {
            foreach (var (from, to) in new[] { ("g1", "f3"), ("g8", "f6"), ("f3", "g1"), ("f6", "g8") })
            {
                history = history.Record(await Result<Position>(MoveRules.Apply(history.Current, Move(from, to))));
            }
            await Assert.That(history.CurrentOccurrences).IsEqualTo(repetition);
            await Assert.That(history.IsThreefoldRepetition).IsEqualTo(repetition >= 3);
            await Assert.That(history.IsFivefoldRepetition).IsEqualTo(repetition >= 5);
        }
        await Assert.That(history.Keys.Count).IsEqualTo(17);
        await Assert.That(history.CurrentKey).IsEqualTo(original.CurrentKey);
        await Assert.That(original.Keys.Count).IsEqualTo(1);
        await Assert.That(original.CurrentOccurrences).IsEqualTo(1);
        await Assert.That(original.IsThreefoldRepetition).IsFalse();
    }

    [Test]
    public async Task BranchingHistoryDoesNotAlterEitherContinuation()
    {
        var initial = PositionHistory.Start(Position.Initial);
        var e4 = initial.Record(await Result<Position>(MoveRules.Apply(initial.Current, Move("e2", "e4"))));
        var d4 = initial.Record(await Result<Position>(MoveRules.Apply(initial.Current, Move("d2", "d4"))));
        await Assert.That(e4.Occurrences(d4.CurrentKey)).IsEqualTo(0);
        await Assert.That(d4.Occurrences(e4.CurrentKey)).IsEqualTo(0);
        await Assert.That(initial.Keys.Count).IsEqualTo(1);
        await Assert.That(e4.Keys[0]).IsEqualTo(initial.CurrentKey);
        await Assert.That(e4.Keys[1]).IsEqualTo(e4.CurrentKey);
    }

    [Test]
    public async Task ReturningTheRookDoesNotRestoreRepetitionIdentity()
    {
        var initial = PositionHistory.Start(Setup(("e1", 'K'), ("h1", 'R'), ("e8", 'k')) with
        {
            WhiteCastlingRights = CastlingRights.KingSide
        });
        var history = initial;
        foreach (var (from, to) in new[] { ("h1", "h2"), ("e8", "e7"), ("h2", "h1"), ("e7", "e8") })
        {
            history = history.Record(await Result<Position>(MoveRules.Apply(history.Current, Move(from, to))));
        }
        await Assert.That(history.CurrentKey.PiecePlacement).IsEqualTo(initial.CurrentKey.PiecePlacement);
        await Assert.That(history.CurrentKey).IsNotEqualTo(initial.CurrentKey);
        await Assert.That(history.CurrentOccurrences).IsEqualTo(1);
    }
}
