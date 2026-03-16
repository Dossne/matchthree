using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const string TilesFolderPath = "Assets/Tiles";

#if UNITY_EDITOR
        private const int TilePixelsPerUnit = 100;
        private const int MinimumMaxTextureSize = 512;
        private const FilterMode TileFilterMode = FilterMode.Bilinear;
#endif

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
            var sprites = LoadSpritesByKey();
            foreach (var pair in sprites)
            {
                var key = pair.Key;
                var sprite = pair.Value;
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

        private static IReadOnlyDictionary<string, Sprite> LoadSpritesByKey()
        {
            var loaded = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            var foundPaths = new List<string>();

#if UNITY_EDITOR
            EnsureTilesAreImportedAsSprites();
            var guids = AssetDatabase.FindAssets("t:Sprite", new[] { TilesFolderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    continue;
                }

                var key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                loaded[key] = sprite;
                foundPaths.Add(path);
            }
#else
            foreach (var sprite in Resources.LoadAll<Sprite>("Tiles"))
            {
                if (sprite == null)
                {
                    continue;
                }

                var key = sprite.name.Trim().ToLowerInvariant();
                loaded[key] = sprite;
            }
#endif

            ValidateExpectedKeys(loaded.Keys, foundPaths);
            return loaded;
        }

        private static void ValidateExpectedKeys(IEnumerable<string> foundKeys, IReadOnlyCollection<string> foundPaths)
        {
            var expectedKeys = Mapping.Keys.OrderBy(key => key).ToArray();
            var keySet = new HashSet<string>(foundKeys, StringComparer.OrdinalIgnoreCase);
            var missing = expectedKeys.Where(expected => !keySet.Contains(expected)).ToArray();
            if (missing.Length > 0)
            {
                var pathsText = foundPaths.Count > 0 ? string.Join(", ", foundPaths.OrderBy(path => path)) : "(none)";
                Debug.LogWarning($"Tile sprite load check: missing keys [{string.Join(", ", missing)}] in {TilesFolderPath}. Found sprite paths: {pathsText}");
            }
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
                Debug.LogError($"Missing tile sprites in {TilesFolderPath}: {string.Join(", ", missing)}");
            }
        }

        private static void AddIfMissing(bool condition, string label, List<string> missing)
        {
            if (!condition)
            {
                missing.Add(label);
            }
        }

#if UNITY_EDITOR
        [MenuItem("Tools/MatchThree/Fix Tile Import Settings")]
        private static void FixTileImportSettingsFromMenu()
        {
            EnsureTilesAreImportedAsSprites(logPrefix: "Tile import fix");
        }

        private static void EnsureTilesAreImportedAsSprites()
        {
            EnsureTilesAreImportedAsSprites("Tiles import check");
        }

        private static void EnsureTilesAreImportedAsSprites(string logPrefix)
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { TilesFolderPath });
            var foundNames = new List<string>();
            var changedPaths = new List<string>();

            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                foundNames.Add(Path.GetFileNameWithoutExtension(path));
                var changed = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }

                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }

                if (importer.spritePixelsPerUnit != TilePixelsPerUnit)
                {
                    importer.spritePixelsPerUnit = TilePixelsPerUnit;
                    changed = true;
                }

                if (importer.spriteMeshType != SpriteMeshType.FullRect)
                {
                    importer.spriteMeshType = SpriteMeshType.FullRect;
                    changed = true;
                }

                if (importer.maxTextureSize < MinimumMaxTextureSize)
                {
                    importer.maxTextureSize = MinimumMaxTextureSize;
                    changed = true;
                }

                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }

                if (importer.filterMode != TileFilterMode)
                {
                    importer.filterMode = TileFilterMode;
                    changed = true;
                }

                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }

                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                importer.SaveAndReimport();
                changedPaths.Add(path);
            }

            foundNames.Sort(StringComparer.OrdinalIgnoreCase);
            changedPaths.Sort(StringComparer.OrdinalIgnoreCase);

            var expectedKeys = Mapping.Keys.OrderBy(key => key).ToArray();
            var keySet = new HashSet<string>(foundNames, StringComparer.OrdinalIgnoreCase);
            var missing = expectedKeys.Where(expected => !keySet.Contains(expected)).ToArray();

            var changedText = changedPaths.Count > 0
                ? $" changed [{string.Join(", ", changedPaths)}]"
                : " changed [none]";
            var missingText = missing.Length > 0
                ? $" missing expected keys [{string.Join(", ", missing)}]"
                : " missing expected keys [none]";

            Debug.Log($"{logPrefix}: found {foundNames.Count} textures in {TilesFolderPath}.{changedText}.{missingText}");
        }
#endif
    }
}
