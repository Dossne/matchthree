using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MatchThree.Runtime
{
    public enum ObstacleSpriteType { Rock, Boulder }
    public enum BoosterSpriteType { Rocket, Bomb, SuperLightning }

    public sealed class TileSpriteLibrary
    {
        private readonly Dictionary<int, Sprite> _normalByColor = new();
        private readonly Dictionary<ObstacleSpriteType, Sprite> _obstacles = new();
        private readonly Dictionary<BoosterSpriteType, Sprite> _boosters = new();

        private static readonly Dictionary<string, Action<TileSpriteLibrary, Sprite>> Mapping = new(StringComparer.OrdinalIgnoreCase)
        {
            ["frog"] = (lib, sprite) => lib._normalByColor[1] = sprite,
            ["cat"] = (lib, sprite) => lib._normalByColor[2] = sprite,
            ["whaler"] = (lib, sprite) => lib._normalByColor[3] = sprite,
            ["capybara"] = (lib, sprite) => lib._normalByColor[4] = sprite,
            ["rock"] = (lib, sprite) => lib._obstacles[ObstacleSpriteType.Rock] = sprite,
            ["boulder"] = (lib, sprite) => lib._obstacles[ObstacleSpriteType.Boulder] = sprite,
            ["line"] = (lib, sprite) => lib._boosters[BoosterSpriteType.Rocket] = sprite,
            ["bomb"] = (lib, sprite) => lib._boosters[BoosterSpriteType.Bomb] = sprite,
            ["lightning"] = (lib, sprite) => lib._boosters[BoosterSpriteType.SuperLightning] = sprite
        };

        public static TileSpriteLibrary LoadFromTilesFolder()
        {
            var library = new TileSpriteLibrary();
            var sprites = LoadSprites();
            foreach (var sprite in sprites)
            {
                var key = sprite.name.Trim().ToLowerInvariant();
                if (Mapping.TryGetValue(key, out var assign))
                {
                    assign(library, sprite);
                }
            }

            library.ValidateRequiredSprites();
            return library;
        }

        public Sprite GetNormalSprite(int colorId) => TryGet(_normalByColor, colorId);
        public Sprite GetObstacleSprite(ObstacleSpriteType type) => TryGet(_obstacles, type);
        public Sprite GetBoosterSprite(BoosterSpriteType type) => TryGet(_boosters, type);

        private static IEnumerable<Sprite> LoadSprites()
        {
            var loaded = new List<Sprite>(Resources.LoadAll<Sprite>("Tiles"));
#if UNITY_EDITOR
            if (loaded.Count == 0)
            {
                var guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/Tiles" });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null)
                    {
                        loaded.Add(sprite);
                    }
                }
            }
#endif
            return loaded;
        }

        private static TValue TryGet<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict, TKey key)
            where TValue : class
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        private void ValidateRequiredSprites()
        {
            var missing = new List<string>();
            AddIfMissing(_normalByColor.ContainsKey(1), "frog.png (normal color #1)", missing);
            AddIfMissing(_normalByColor.ContainsKey(2), "cat.png (normal color #2)", missing);
            AddIfMissing(_normalByColor.ContainsKey(3), "whaler.png (normal color #3)", missing);
            AddIfMissing(_normalByColor.ContainsKey(4), "capybara.png (normal color #4)", missing);
            AddIfMissing(_obstacles.ContainsKey(ObstacleSpriteType.Rock), "rock.png (Rock obstacle)", missing);
            AddIfMissing(_obstacles.ContainsKey(ObstacleSpriteType.Boulder), "boulder.png (Boulder obstacle)", missing);
            AddIfMissing(_boosters.ContainsKey(BoosterSpriteType.Rocket), "line.png (Rocket booster)", missing);
            AddIfMissing(_boosters.ContainsKey(BoosterSpriteType.Bomb), "bomb.png (Bomb booster)", missing);
            AddIfMissing(_boosters.ContainsKey(BoosterSpriteType.SuperLightning), "lightning.png (Super Lightning booster)", missing);

            if (missing.Count > 0)
            {
                Debug.LogError($"Missing tile sprites in Assets/Tiles (or Resources/Tiles): {string.Join(", ", missing)}");
            }
        }

        private static void AddIfMissing(bool condition, string label, List<string> missing)
        {
            if (!condition)
            {
                missing.Add(label);
            }
        }
    }
}
