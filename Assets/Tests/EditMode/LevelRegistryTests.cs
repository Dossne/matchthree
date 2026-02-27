using MatchThree.Core;
using MatchThree.Runtime;
using NUnit.Framework;

namespace MatchThree.Tests
{
    public sealed class LevelRegistryTests
    {
        [Test]
        public void Registry_LoadsThreePlayableLevels()
        {
            var registry = new LevelRegistry();
            var levels = registry.LoadDefinitions();

            Assert.That(levels.Count, Is.EqualTo(3));
            Assert.That(levels[0].LevelPath, Is.EqualTo("Levels/level_000"));
            Assert.That(levels[1].LevelPath, Is.EqualTo("Levels/level_001"));
            Assert.That(levels[2].LevelPath, Is.EqualTo("Levels/level_002"));
        }

        [Test]
        public void Registry_ParsesMovesAndGoals()
        {
            var registry = new LevelRegistry();
            var levels = registry.LoadDefinitions();

            Assert.That(levels[1].MaxMoves, Is.EqualTo(24));
            Assert.That(levels[1].Goals.Count, Is.EqualTo(2));
            Assert.That(levels[1].Goals[0], Is.TypeOf<CollectColorGoalDefinition>());
            Assert.That(levels[1].Goals[1], Is.TypeOf<ClearAllRocksGoalDefinition>());

            var collect = (CollectColorGoalDefinition)levels[1].Goals[0];
            Assert.That(collect.ColorId, Is.EqualTo(2));
            Assert.That(collect.TargetCount, Is.EqualTo(12));
        }
    }
}
