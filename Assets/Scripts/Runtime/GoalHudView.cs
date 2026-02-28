using System.Collections.Generic;
using MatchThree.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MatchThree.Runtime
{
    public sealed class GoalHudView
    {
        private readonly RectTransform _goalsPanel;
        private readonly RectTransform _goalsRow;
        private readonly Text _movesValueText;
        private readonly Text _movesMaxText;
        private readonly Font _font;

        private readonly List<GoalItemView> _goalItems = new();

        public GoalHudView(RectTransform goalsPanel, RectTransform movesPanel, Font font)
        {
            _goalsPanel = goalsPanel;
            _font = font;

            var goalsLabel = EnsureText(FindOrCreateUiObject(_goalsPanel, "Title"), "Goals", 28, TextAnchor.UpperLeft);
            var goalsLabelRect = goalsLabel.rectTransform;
            goalsLabelRect.anchorMin = new Vector2(0f, 1f);
            goalsLabelRect.anchorMax = new Vector2(1f, 1f);
            goalsLabelRect.pivot = new Vector2(0f, 1f);
            goalsLabelRect.offsetMin = new Vector2(0f, -42f);
            goalsLabelRect.offsetMax = new Vector2(0f, 0f);

            _goalsRow = EnsureRect(FindOrCreateUiObject(_goalsPanel, "Row"));
            _goalsRow.anchorMin = new Vector2(0f, 0f);
            _goalsRow.anchorMax = new Vector2(1f, 1f);
            _goalsRow.pivot = new Vector2(0f, 1f);
            _goalsRow.offsetMin = new Vector2(0f, 0f);
            _goalsRow.offsetMax = new Vector2(0f, -52f);

            var rowLayout = EnsureComponent<HorizontalLayoutGroup>(_goalsRow.gameObject);
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.spacing = 20f;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var rowFitter = EnsureComponent<ContentSizeFitter>(_goalsRow.gameObject);
            rowFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var movesLabel = EnsureText(FindOrCreateUiObject(movesPanel, "Label"), "Moves", 30, TextAnchor.UpperRight);
            var movesLabelRect = movesLabel.rectTransform;
            movesLabelRect.anchorMin = new Vector2(0f, 1f);
            movesLabelRect.anchorMax = new Vector2(1f, 1f);
            movesLabelRect.offsetMin = Vector2.zero;
            movesLabelRect.offsetMax = new Vector2(0f, -44f);

            _movesValueText = EnsureText(FindOrCreateUiObject(movesPanel, "Value"), "0", 62, TextAnchor.UpperRight);
            var movesValueRect = _movesValueText.rectTransform;
            movesValueRect.anchorMin = new Vector2(0f, 1f);
            movesValueRect.anchorMax = new Vector2(1f, 1f);
            movesValueRect.offsetMin = Vector2.zero;
            movesValueRect.offsetMax = new Vector2(0f, -126f);

            _movesMaxText = EnsureText(FindOrCreateUiObject(movesPanel, "Max"), string.Empty, 24, TextAnchor.UpperRight);
            var movesMaxRect = _movesMaxText.rectTransform;
            movesMaxRect.anchorMin = new Vector2(0f, 1f);
            movesMaxRect.anchorMax = new Vector2(1f, 1f);
            movesMaxRect.offsetMin = Vector2.zero;
            movesMaxRect.offsetMax = new Vector2(0f, -164f);
        }

        public void UpdateGoals(GoalTracker goalTracker, TileSpriteLibrary spriteLibrary)
        {
            if (goalTracker == null)
            {
                SetGoalItemCount(0);
                return;
            }

            var progressList = goalTracker.GetProgress();
            SetGoalItemCount(progressList.Count);

            for (var i = 0; i < progressList.Count; i++)
            {
                var progress = progressList[i];
                var item = _goalItems[i];
                UpdateGoalItem(item, progress, spriteLibrary);
            }
        }

        public void UpdateMoves(MoveCounter moveCounter)
        {
            if (moveCounter == null)
            {
                _movesValueText.text = "0";
                _movesMaxText.text = string.Empty;
                return;
            }

            _movesValueText.text = moveCounter.Remaining.ToString();
            _movesMaxText.text = $"/{moveCounter.MaxMoves}";
        }

        private void SetGoalItemCount(int count)
        {
            while (_goalItems.Count < count)
            {
                _goalItems.Add(CreateGoalItem(_goalsRow));
            }

            for (var i = 0; i < _goalItems.Count; i++)
            {
                _goalItems[i].Root.gameObject.SetActive(i < count);
            }
        }

        private void UpdateGoalItem(GoalItemView item, GoalProgress progress, TileSpriteLibrary spriteLibrary)
        {
            Sprite sprite = null;
            string fallbackLabel = "?";
            string counterText = string.Empty;

            switch (progress)
            {
                case CollectColorProgress collect:
                    sprite = spriteLibrary?.GetNormalSprite(collect.ColorId);
                    fallbackLabel = $"C{collect.ColorId}";
                    counterText = $"{collect.Current}/{collect.Target}";
                    break;
                case ClearAllRocksProgress rocks:
                    sprite = spriteLibrary?.GetObstacleSprite(ObstacleSpriteType.Rock);
                    fallbackLabel = "Rock";
                    counterText = rocks.RemainingRocks.ToString();
                    break;
            }

            item.Counter.text = counterText;
            item.Icon.sprite = sprite;
            var hasSprite = sprite != null;
            item.Icon.enabled = hasSprite;
            item.IconBackground.enabled = !hasSprite;
            item.IconFallback.text = hasSprite ? string.Empty : fallbackLabel;
        }

        private GoalItemView CreateGoalItem(RectTransform parent)
        {
            var root = EnsureRect(new GameObject("GoalItem"));
            root.SetParent(parent, false);
            root.sizeDelta = new Vector2(130f, 120f);

            var iconRoot = EnsureRect(FindOrCreateUiObject(root, "IconRoot"));
            iconRoot.anchorMin = new Vector2(0.5f, 1f);
            iconRoot.anchorMax = new Vector2(0.5f, 1f);
            iconRoot.pivot = new Vector2(0.5f, 1f);
            iconRoot.sizeDelta = new Vector2(70f, 70f);
            iconRoot.anchoredPosition = new Vector2(0f, 0f);

            var iconBackground = EnsureComponent<Image>(FindOrCreateUiObject(iconRoot, "Background"));
            iconBackground.color = new Color(0.2f, 0.2f, 0.2f, 0.85f);
            var iconBackgroundRect = iconBackground.rectTransform;
            iconBackgroundRect.anchorMin = Vector2.zero;
            iconBackgroundRect.anchorMax = Vector2.one;
            iconBackgroundRect.offsetMin = Vector2.zero;
            iconBackgroundRect.offsetMax = Vector2.zero;

            var icon = EnsureComponent<Image>(FindOrCreateUiObject(iconRoot, "Icon"));
            icon.useSpriteMesh = true;
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            var fallback = EnsureText(FindOrCreateUiObject(iconRoot, "Fallback"), string.Empty, 18, TextAnchor.MiddleCenter);
            fallback.color = Color.white;
            var fallbackRect = fallback.rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;

            var counter = EnsureText(FindOrCreateUiObject(root, "Counter"), string.Empty, 30, TextAnchor.UpperCenter);
            counter.horizontalOverflow = HorizontalWrapMode.Overflow;
            var counterRect = counter.rectTransform;
            counterRect.anchorMin = new Vector2(0f, 0f);
            counterRect.anchorMax = new Vector2(1f, 0f);
            counterRect.pivot = new Vector2(0.5f, 0f);
            counterRect.sizeDelta = new Vector2(0f, 40f);
            counterRect.anchoredPosition = new Vector2(0f, 0f);

            return new GoalItemView
            {
                Root = root,
                IconBackground = iconBackground,
                Icon = icon,
                IconFallback = fallback,
                Counter = counter
            };
        }

        private Text EnsureText(GameObject go, string defaultValue, int fontSize, TextAnchor alignment)
        {
            var text = EnsureComponent<Text>(go);
            text.font = _font;
            text.text = defaultValue;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static RectTransform EnsureRect(GameObject go) => EnsureComponent<RectTransform>(go);

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static GameObject FindOrCreateUiObject(Transform parent, string objectName)
        {
            var existing = parent.Find(objectName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var created = new GameObject(objectName);
            created.transform.SetParent(parent, false);
            return created;
        }

        private sealed class GoalItemView
        {
            public RectTransform Root;
            public Image IconBackground;
            public Image Icon;
            public Text IconFallback;
            public Text Counter;
        }
    }
}
