using System;
using System.Collections.Generic;
using MatchThree.Core;
using UnityEngine;

namespace MatchThree.Runtime
{
    [Serializable]
    public sealed class LevelDefinition
    {
        public string LevelPath;
        public int MaxMoves;
        public List<GoalDefinition> Goals = new();
    }

    [Serializable]
    public sealed class LevelRegistry
    {
        [SerializeField] private string registryResourcePath = "Levels/level_registry";

        public IReadOnlyList<LevelDefinition> LoadDefinitions()
        {
            var asset = Resources.Load<TextAsset>(registryResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException($"Missing level registry at Resources/{registryResourcePath}.json");
            }

            var data = JsonUtility.FromJson<LevelRegistryData>(asset.text);
            if (data?.levels == null || data.levels.Length == 0)
            {
                throw new InvalidOperationException("Level registry has no level definitions.");
            }

            var result = new List<LevelDefinition>(data.levels.Length);
            foreach (var levelData in data.levels)
            {
                var definition = new LevelDefinition
                {
                    LevelPath = levelData.levelPath,
                    MaxMoves = levelData.maxMoves,
                    Goals = new List<GoalDefinition>()
                };

                if (levelData.goals != null)
                {
                    foreach (var goal in levelData.goals)
                    {
                        if (string.Equals(goal.type, "CollectColor", StringComparison.OrdinalIgnoreCase))
                        {
                            definition.Goals.Add(new CollectColorGoalDefinition(goal.colorId, goal.target));
                        }
                        else if (string.Equals(goal.type, "ClearAllRocks", StringComparison.OrdinalIgnoreCase))
                        {
                            definition.Goals.Add(new ClearAllRocksGoalDefinition());
                        }
                    }
                }

                result.Add(definition);
            }

            return result;
        }

        [Serializable]
        private sealed class LevelRegistryData
        {
            public LevelData[] levels;
        }

        [Serializable]
        private sealed class LevelData
        {
            public string levelPath;
            public int maxMoves;
            public GoalData[] goals;
        }

        [Serializable]
        private sealed class GoalData
        {
            public string type;
            public int colorId;
            public int target;
        }
    }
}
