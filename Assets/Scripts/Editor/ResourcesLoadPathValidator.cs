#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MatchThree.Editor
{
    public sealed class ResourcesLoadPathValidator : IPreprocessBuildWithReport
    {
        private const bool ValidationEnabled = true;
        private const string MenuPath = "Tools/Validate/Resources.Load Paths";
        private const string AssetsRoot = "Assets";
        private const string CSharpSearchFilter = "t:Script";

        private static readonly Regex ResourcesLoadRegex = new(
            "Resources\\.Load(?:\\s*<[^>]+>)?\\s*\\(\\s*\"(?<key>[^\"\\r\\n]+)\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int callbackOrder => 0;

        [MenuItem(MenuPath)]
        private static void ValidateFromMenu()
        {
            var result = ValidateAndReport();
            if (result.HasErrors)
            {
                throw new InvalidOperationException($"Resources.Load path validation failed with {result.Issues.Count} issue(s). See console for details.");
            }

            Debug.Log($"Resources.Load path validation passed. Checked {result.UsedKeysCount} distinct key(s).");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ValidationEnabled)
            {
                return;
            }

            var result = ValidateAndReport();
            if (!result.HasErrors)
            {
                return;
            }

            throw new BuildFailedException($"Resources.Load path validation failed with {result.Issues.Count} issue(s). Open Console for details.");
        }

        private static ValidationResult ValidateAndReport()
        {
            var keyUsages = FindUsedResourceKeys();
            var resourcesIndex = BuildResourcesIndex();
            var issues = new List<ValidationIssue>();

            foreach (var usage in keyUsages.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var usedKey = usage.Key;
                if (resourcesIndex.ExactKeyToAssetPath.ContainsKey(usedKey))
                {
                    continue;
                }

                var lowerKey = usedKey.ToLowerInvariant();
                if (resourcesIndex.LowerKeyToExactKey.TryGetValue(lowerKey, out var suggestedKey))
                {
                    issues.Add(ValidationIssue.CaseMismatch(usedKey, suggestedKey, usage.Value));
                    continue;
                }

                issues.Add(ValidationIssue.Missing(usedKey, usage.Value));
            }

            LogSummary(keyUsages.Count, resourcesIndex.ExactKeyToAssetPath.Count, issues);
            return new ValidationResult(keyUsages.Count, issues);
        }

        private static Dictionary<string, List<CodeUsage>> FindUsedResourceKeys()
        {
            var keyUsages = new Dictionary<string, List<CodeUsage>>(StringComparer.Ordinal);
            var scriptGuids = AssetDatabase.FindAssets(CSharpSearchFilter, new[] { AssetsRoot });

            foreach (var guid in scriptGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = File.ReadAllText(assetPath);
                var sourceWithoutComments = StripCommentsPreservingLines(source);
                var lineIndex = new LineIndex(sourceWithoutComments);

                foreach (Match match in ResourcesLoadRegex.Matches(sourceWithoutComments))
                {
                    var key = match.Groups["key"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (!keyUsages.TryGetValue(key, out var usages))
                    {
                        usages = new List<CodeUsage>();
                        keyUsages[key] = usages;
                    }

                    usages.Add(new CodeUsage(assetPath, lineIndex.GetLineNumber(match.Index)));
                }
            }

            return keyUsages;
        }

        private static ResourcesIndex BuildResourcesIndex()
        {
            var exactKeyToAssetPath = new Dictionary<string, string>(StringComparer.Ordinal);
            var lowerKeyToExactKey = new Dictionary<string, string>(StringComparer.Ordinal);
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { AssetsRoot });

            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(assetPath) || assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetResourcesKey(assetPath, out var key))
                {
                    continue;
                }

                if (!exactKeyToAssetPath.ContainsKey(key))
                {
                    exactKeyToAssetPath[key] = assetPath;
                }

                var lowerKey = key.ToLowerInvariant();
                if (!lowerKeyToExactKey.ContainsKey(lowerKey))
                {
                    lowerKeyToExactKey[lowerKey] = key;
                }
            }

            return new ResourcesIndex(exactKeyToAssetPath, lowerKeyToExactKey);
        }

        private static bool TryGetResourcesKey(string assetPath, out string key)
        {
            const string resourcesSegment = "/Resources/";
            key = string.Empty;
            var markerIndex = assetPath.IndexOf(resourcesSegment, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return false;
            }

            var relativePath = assetPath.Substring(markerIndex + resourcesSegment.Length);
            key = Path.ChangeExtension(relativePath, null)?.Replace('\\', '/');
            return !string.IsNullOrEmpty(key);
        }

        private static string StripCommentsPreservingLines(string source)
        {
            var result = new StringBuilder(source.Length);
            var state = ScanState.Code;

            for (var i = 0; i < source.Length; i++)
            {
                var current = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                switch (state)
                {
                    case ScanState.Code:
                        if (current == '"')
                        {
                            state = ScanState.String;
                            result.Append(current);
                        }
                        else if (current == '\'' )
                        {
                            state = ScanState.Char;
                            result.Append(current);
                        }
                        else if (current == '/' && next == '/')
                        {
                            state = ScanState.LineComment;
                            result.Append(' ');
                            result.Append(' ');
                            i++;
                        }
                        else if (current == '/' && next == '*')
                        {
                            state = ScanState.BlockComment;
                            result.Append(' ');
                            result.Append(' ');
                            i++;
                        }
                        else
                        {
                            result.Append(current);
                        }
                        break;

                    case ScanState.String:
                        result.Append(current);
                        if (current == '\\' && i + 1 < source.Length)
                        {
                            i++;
                            result.Append(source[i]);
                        }
                        else if (current == '"')
                        {
                            state = ScanState.Code;
                        }
                        break;

                    case ScanState.Char:
                        result.Append(current);
                        if (current == '\\' && i + 1 < source.Length)
                        {
                            i++;
                            result.Append(source[i]);
                        }
                        else if (current == '\'')
                        {
                            state = ScanState.Code;
                        }
                        break;

                    case ScanState.LineComment:
                        if (current == '\n')
                        {
                            state = ScanState.Code;
                            result.Append('\n');
                        }
                        else if (current == '\r')
                        {
                            result.Append('\r');
                        }
                        else
                        {
                            result.Append(' ');
                        }
                        break;

                    case ScanState.BlockComment:
                        if (current == '*' && next == '/')
                        {
                            result.Append(' ');
                            result.Append(' ');
                            i++;
                            state = ScanState.Code;
                        }
                        else if (current == '\n' || current == '\r')
                        {
                            result.Append(current);
                        }
                        else
                        {
                            result.Append(' ');
                        }
                        break;
                }
            }

            return result.ToString();
        }

        private static void LogSummary(int usedKeysCount, int indexedResourceCount, IReadOnlyCollection<ValidationIssue> issues)
        {
            if (issues.Count == 0)
            {
                Debug.Log($"Resources.Load path validation passed. Used keys: {usedKeysCount}. Indexed resources: {indexedResourceCount}.");
                return;
            }

            Debug.LogError($"Resources.Load path validation failed. Used keys: {usedKeysCount}. Indexed resources: {indexedResourceCount}. Issues: {issues.Count}.");

            foreach (var issue in issues)
            {
                var firstUsage = issue.Usages[0];
                var extraHits = issue.Usages.Count - 1;
                var extraHitsText = extraHits > 0 ? $" (+{extraHits} more)" : string.Empty;

                if (issue.Type == IssueType.CaseMismatch)
                {
                    Debug.LogError($"[Resources.Load] Case mismatch: \"{issue.Key}\" should be \"{issue.SuggestedKey}\". First usage: {firstUsage.FilePath}:{firstUsage.Line}.{extraHitsText}");
                }
                else
                {
                    Debug.LogError($"[Resources.Load] Missing resource: \"{issue.Key}\". First usage: {firstUsage.FilePath}:{firstUsage.Line}.{extraHitsText}");
                }

                foreach (var usage in issue.Usages)
                {
                    Debug.LogError($"  at {usage.FilePath}:{usage.Line}");
                }
            }
        }

        private enum ScanState
        {
            Code,
            String,
            Char,
            LineComment,
            BlockComment
        }

        private enum IssueType
        {
            Missing,
            CaseMismatch
        }

        private readonly struct ValidationResult
        {
            public ValidationResult(int usedKeysCount, IReadOnlyList<ValidationIssue> issues)
            {
                UsedKeysCount = usedKeysCount;
                Issues = issues;
            }

            public int UsedKeysCount { get; }
            public IReadOnlyList<ValidationIssue> Issues { get; }
            public bool HasErrors => Issues.Count > 0;
        }

        private readonly struct ResourcesIndex
        {
            public ResourcesIndex(Dictionary<string, string> exactKeyToAssetPath, Dictionary<string, string> lowerKeyToExactKey)
            {
                ExactKeyToAssetPath = exactKeyToAssetPath;
                LowerKeyToExactKey = lowerKeyToExactKey;
            }

            public Dictionary<string, string> ExactKeyToAssetPath { get; }
            public Dictionary<string, string> LowerKeyToExactKey { get; }
        }

        private sealed class ValidationIssue
        {
            private ValidationIssue(IssueType type, string key, string suggestedKey, List<CodeUsage> usages)
            {
                Type = type;
                Key = key;
                SuggestedKey = suggestedKey;
                Usages = usages;
            }

            public IssueType Type { get; }
            public string Key { get; }
            public string SuggestedKey { get; }
            public List<CodeUsage> Usages { get; }

            public static ValidationIssue Missing(string key, List<CodeUsage> usages)
            {
                return new ValidationIssue(IssueType.Missing, key, string.Empty, usages);
            }

            public static ValidationIssue CaseMismatch(string key, string suggestedKey, List<CodeUsage> usages)
            {
                return new ValidationIssue(IssueType.CaseMismatch, key, suggestedKey, usages);
            }
        }

        private readonly struct CodeUsage
        {
            public CodeUsage(string filePath, int line)
            {
                FilePath = filePath;
                Line = line;
            }

            public string FilePath { get; }
            public int Line { get; }
        }

        private sealed class LineIndex
        {
            private readonly List<int> _lineStartIndices;

            public LineIndex(string content)
            {
                _lineStartIndices = new List<int> { 0 };
                for (var i = 0; i < content.Length; i++)
                {
                    if (content[i] == '\n')
                    {
                        _lineStartIndices.Add(i + 1);
                    }
                }
            }

            public int GetLineNumber(int index)
            {
                var position = _lineStartIndices.BinarySearch(index);
                if (position >= 0)
                {
                    return position + 1;
                }

                position = ~position - 1;
                return Math.Max(position + 1, 1);
            }
        }
    }
}
#endif
