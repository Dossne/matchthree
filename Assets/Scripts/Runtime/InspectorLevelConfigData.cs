using System;
using UnityEngine;

namespace MatchThree.Runtime
{
    /// <summary>
    /// Inspector-driven per-level config container.
    /// Field names and types intentionally match what MatchThreeGameController expects.
    /// </summary>
    [Serializable]
    public sealed class InspectorLevelConfigData
    {
        public string levelResourcePath = "Levels/level_000";
        public int maxMoves = 20;
        public InspectorGoalConfigData[] goals;
    }

    [Serializable]
    public sealed class InspectorGoalConfigData
    {
        // Match controller expectations: it compares this to strings like "CollectColor" / "ClearAllRocks".
        public string type = "CollectColor";

        // for CollectColor
        public int colorId = 1;
        public int target = 10;
    }
}