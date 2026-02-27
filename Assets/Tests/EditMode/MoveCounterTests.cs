using MatchThree.Core;
using NUnit.Framework;

namespace MatchThree.Tests
{
    public sealed class MoveCounterTests
    {
        [Test]
        public void StartingRemaining_EqualsMaxMoves()
        {
            var counter = new MoveCounter(20);

            Assert.That(counter.MaxMoves, Is.EqualTo(20));
            Assert.That(counter.Remaining, Is.EqualTo(20));
            Assert.That(counter.CanMakeMove, Is.True);
        }

        [Test]
        public void RejectedMove_DoesNotDecrement()
        {
            var counter = new MoveCounter(5);
            var rejected = new MoveResult { Performed = true, Reverted = true };

            counter.ConsumeIfAccepted(rejected);

            Assert.That(counter.Remaining, Is.EqualTo(5));
        }

        [Test]
        public void AcceptedMove_DecrementsOnce()
        {
            var counter = new MoveCounter(5);
            var accepted = new MoveResult { Performed = true, Reverted = false };

            counter.ConsumeIfAccepted(accepted);

            Assert.That(counter.Remaining, Is.EqualTo(4));
        }

        [Test]
        public void Remaining_NeverGoesBelowZero()
        {
            var counter = new MoveCounter(1);
            var accepted = new MoveResult { Performed = true, Reverted = false };

            counter.ConsumeIfAccepted(accepted);
            counter.ConsumeIfAccepted(accepted);

            Assert.That(counter.Remaining, Is.EqualTo(0));
            Assert.That(counter.CanMakeMove, Is.False);
        }
    }
}
