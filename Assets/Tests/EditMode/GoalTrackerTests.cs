using MatchThree.Core;
using NUnit.Framework;

namespace MatchThree.Tests
{
    public sealed class GoalTrackerTests
    {
        [Test]
        public void CollectColorGoal_IncrementsFromResolverSummary()
        {
            var tracker = new GoalTracker(new GoalDefinition[]
            {
                new CollectColorGoalDefinition(2, 5)
            });

            var summary = new ResolveStepSummary();
            summary.ClearedPiecesByColor[2] = 3;
            summary.ClearedPiecesByColor[1] = 10;

            tracker.ApplyStepSummary(summary);

            var progress = (CollectColorProgress)tracker.GetProgress()[0];
            Assert.That(progress.Current, Is.EqualTo(3));
            Assert.That(progress.IsComplete, Is.False);

            var second = new ResolveStepSummary();
            second.ClearedPiecesByColor[2] = 2;
            tracker.ApplyStepSummary(second);

            Assert.That(progress.Current, Is.EqualTo(5));
            Assert.That(progress.IsComplete, Is.True);
        }

        [Test]
        public void ClearAllRocksGoal_InitializesFromBoardRockAndBoulderCount()
        {
            var board = LevelParser.Parse("R.B\n.BR\n...\n", new[] { 1, 2, 3, 4 });
            var tracker = new GoalTracker(new GoalDefinition[]
            {
                new ClearAllRocksGoalDefinition()
            });

            tracker.Initialize(board);

            var progress = (ClearAllRocksProgress)tracker.GetProgress()[0];
            Assert.That(progress.RemainingRocks, Is.EqualTo(4));
            Assert.That(progress.IsComplete, Is.False);
        }

        [Test]
        public void ClearAllRocksGoal_DecrementsOnObstacleDestroyed_AndCompletesAtZero()
        {
            var board = LevelParser.Parse("RB\n..\n", new[] { 1, 2, 3, 4 });
            var tracker = new GoalTracker(new GoalDefinition[]
            {
                new ClearAllRocksGoalDefinition()
            });
            tracker.Initialize(board);

            var first = new ResolveStepSummary();
            first.DestroyedObstaclesByType[TileKind.Rock] = 1;
            tracker.ApplyStepSummary(first);

            var progress = (ClearAllRocksProgress)tracker.GetProgress()[0];
            Assert.That(progress.RemainingRocks, Is.EqualTo(1));
            Assert.That(progress.IsComplete, Is.False);

            var second = new ResolveStepSummary();
            second.DestroyedObstaclesByType[TileKind.Boulder] = 1;
            tracker.ApplyStepSummary(second);

            Assert.That(progress.RemainingRocks, Is.EqualTo(0));
            Assert.That(progress.IsComplete, Is.True);
            Assert.That(tracker.AllComplete, Is.True);
        }
    }
}
