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
        private const string DefaultLevelResourcePath = "Levels/level_000";
        [SerializeField] private string registryResourcePath = "Levels/level_registry";

        public IReadOnlyList<LevelDefinition> LoadDefinitions()
        {
            Debug.Log($"[LevelRegistry] Loading registry from Resources/{registryResourcePath}.json");

            var asset = Resources.Load<TextAsset>(registryResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException($"Missing level registry at Resources/{registryResourcePath}.json");
            }

            var data = JsonUtility.FromJson<LevelRegistryData>(asset.text);
            var levelEntries = data?.GetLevelEntries();
            if (levelEntries == null || levelEntries.Length == 0)
            {
                throw new InvalidOperationException("Level registry has no level definitions.");
            }

            var result = new List<LevelDefinition>(levelEntries.Length);
            for (var i = 0; i < levelEntries.Length; i++)
            {
                var levelData = levelEntries[i];
                var levelPath = levelData.GetLevelPath();
                if (string.IsNullOrWhiteSpace(levelPath))
                {
                    Debug.LogWarning($"[LevelRegistry] Level entry {i} has an empty path; falling back to '{DefaultLevelResourcePath}'.");
                    levelPath = DefaultLevelResourcePath;
                }

                var definition = new LevelDefinition
                {
                    LevelPath = levelPath,
                    MaxMoves = levelData.GetMaxMoves(),
                    Goals = new List<GoalDefinition>()
                };

                var goals = levelData.GetGoals();
                if (goals != null)
                {
                    foreach (var goal in goals)
                    {
                        var goalType = goal.GetGoalType();
                        if (string.Equals(goalType, "CollectColor", StringComparison.OrdinalIgnoreCase))
                        {
                            definition.Goals.Add(new CollectColorGoalDefinition(goal.GetColorId(), goal.GetTarget()));
                        }
                        else if (string.Equals(goalType, "ClearAllRocks", StringComparison.OrdinalIgnoreCase))
                        {
                            definition.Goals.Add(new ClearAllRocksGoalDefinition());
                        }
                    }
                }

                result.Add(definition);
                Debug.Log($"[LevelRegistry] Parsed level[{i}] path='{definition.LevelPath}', maxMoves={definition.MaxMoves}, goals={definition.Goals.Count}");
            }

            Debug.Log($"[LevelRegistry] Parsed {result.Count} level definitions from '{registryResourcePath}'.");

            return result;
        }

        [Serializable]
        private sealed class LevelRegistryData
        {
            public LevelData[] levels;
            public LevelData[] configs;
            public LevelData[] entries;

            public LevelData[] GetLevelEntries()
            {
                if (levels != null && levels.Length > 0)
                {
                    return levels;
                }

                if (configs != null && configs.Length > 0)
                {
                    return configs;
                }

                if (entries != null && entries.Length > 0)
                {
                    return entries;
                }

                return null;
            }
        }

        [Serializable]
        private sealed class LevelData
        {
            public string levelPath;
            public string levelResourcePath;
            public string path;
            public int maxMoves;
            public int moves;
            public GoalData[] goals;
            public GoalData[] objectives;

            public string GetLevelPath()
            {
                if (!string.IsNullOrWhiteSpace(levelPath))
                {
                    return levelPath;
                }

                if (!string.IsNullOrWhiteSpace(levelResourcePath))
                {
                    return levelResourcePath;
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }

                return null;
            }

            public int GetMaxMoves()
            {
                return maxMoves > 0 ? maxMoves : moves;
            }

            public GoalData[] GetGoals()
            {
                if (goals != null && goals.Length > 0)
                {
                    return goals;
                }

                if (objectives != null && objectives.Length > 0)
                {
                    return objectives;
                }

                return null;
            }
        }

        [Serializable]
        private sealed class GoalData
        {
            public string type;
            public string goalType;
            public int colorId;
            public int color;
            public int target;
            public int targetCount;

            public string GetGoalType()
            {
                return string.IsNullOrWhiteSpace(type) ? goalType : type;
            }

            public int GetColorId()
            {
                return colorId != 0 ? colorId : color;
            }

            public int GetTarget()
            {
                return target > 0 ? target : targetCount;
            }
        }
    }
}
