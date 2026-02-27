using MatchThree.Core;
using NUnit.Framework;

namespace MatchThree.Tests
{
    public sealed class GameStateControllerTests
    {
        [Test]
        public void EvaluateAfterMove_GoalsCompleteWithMovesRemaining_Wins()
        {
            var tracker = new GoalTracker(new GoalDefinition[]
            {
                new CollectColorGoalDefinition(1, 1)
            });
            var counter = new MoveCounter(5);
            var controller = new GameStateController(tracker, counter);

            tracker.ApplyStepSummary(new ResolveStepSummary
            {
                ClearedPiecesByColor = { [1] = 1 }
            });

            var state = controller.EvaluateAfterMove();

            Assert.That(state, Is.EqualTo(GameState.Won));
        }

        [Test]
        public void EvaluateAfterMove_MovesAtZeroAndGoalsIncomplete_Loses()
        {
            var tracker = new GoalTracker(new GoalDefinition[]
            {
                new CollectColorGoalDefinition(1, 2)
            });
            var counter = new MoveCounter(1);
            var controller = new GameStateController(tracker, counter);

            counter.ConsumeIfAccepted(new MoveResult { Performed = true });

            var state = controller.EvaluateAfterMove();

            Assert.That(counter.Remaining, Is.EqualTo(0));
            Assert.That(tracker.AllComplete, Is.False);
            Assert.That(state, Is.EqualTo(GameState.Lost));
        }

        [Test]
        public void EvaluateAfterMove_GoalsCompleteOnLastMove_WinHasPriority()
        {
            var tracker = new GoalTracker(new GoalDefinition[]
            {
                new CollectColorGoalDefinition(1, 1)
            });
            var counter = new MoveCounter(1);
            var controller = new GameStateController(tracker, counter);

            counter.ConsumeIfAccepted(new MoveResult { Performed = true });
            tracker.ApplyStepSummary(new ResolveStepSummary
            {
                ClearedPiecesByColor = { [1] = 1 }
            });

            var state = controller.EvaluateAfterMove();

            Assert.That(counter.Remaining, Is.EqualTo(0));
            Assert.That(tracker.AllComplete, Is.True);
            Assert.That(state, Is.EqualTo(GameState.Won));
        }
    }
}
