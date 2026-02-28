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

        private readonly List<GoalHudItemView> _goalItems = new();

        public GoalHudView(RectTransform goalsPanel, RectTransform movesPanel, Font font)
        {
            _goalsPanel = goalsPanel;
            _font = font;

            _goalsRow = EnsureRect(FindOrCreateUiObject(_goalsPanel, "Row"));
            _goalsRow.anchorMin = new Vector2(0f, 0f);
            _goalsRow.anchorMax = new Vector2(1f, 1f);
            _goalsRow.offsetMin = Vector2.zero;
            _goalsRow.offsetMax = Vector2.zero;

            var rowLayout = EnsureComponent<HorizontalLayoutGroup>(_goalsRow.gameObject);
            rowLayout.padding = new RectOffset(0, 0, 0, 0);
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var rowFitter = EnsureComponent<ContentSizeFitter>(_goalsRow.gameObject);
            rowFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var movesLabel = EnsureText(FindOrCreateUiObject(movesPanel, "Label"), "Moves", 34, TextAnchor.UpperRight);
            var movesLabelRect = movesLabel.rectTransform;
            movesLabelRect.anchorMin = new Vector2(0f, 1f);
            movesLabelRect.anchorMax = new Vector2(1f, 1f);
            movesLabelRect.offsetMin = Vector2.zero;
            movesLabelRect.offsetMax = new Vector2(0f, -48f);

            _movesValueText = EnsureText(FindOrCreateUiObject(movesPanel, "Value"), "0", 108, TextAnchor.UpperRight);
            var movesValueRect = _movesValueText.rectTransform;
            movesValueRect.anchorMin = new Vector2(0f, 1f);
            movesValueRect.anchorMax = new Vector2(1f, 1f);
            movesValueRect.offsetMin = Vector2.zero;
            movesValueRect.offsetMax = new Vector2(0f, -172f);

            _movesMaxText = EnsureText(FindOrCreateUiObject(movesPanel, "Max"), string.Empty, 30, TextAnchor.UpperRight);
            var movesMaxRect = _movesMaxText.rectTransform;
            movesMaxRect.anchorMin = new Vector2(0f, 1f);
            movesMaxRect.anchorMax = new Vector2(1f, 1f);
            movesMaxRect.offsetMin = Vector2.zero;
            movesMaxRect.offsetMax = new Vector2(0f, -216f);
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
                _goalItems.Add(GoalHudItemView.Create(_goalsRow, _font));
            }

            for (var i = 0; i < _goalItems.Count; i++)
            {
                _goalItems[i].gameObject.SetActive(i < count);
            }
        }

        private static void UpdateGoalItem(GoalHudItemView item, GoalProgress progress, TileSpriteLibrary spriteLibrary)
        {
            Sprite sprite = null;
            var fallbackLabel = "?";
            var remaining = 0;

            switch (progress)
            {
                case CollectColorProgress collect:
                    sprite = spriteLibrary?.GetNormalSprite(collect.ColorId);
                    fallbackLabel = $"C{collect.ColorId}";
                    remaining = Mathf.Max(0, collect.Target - collect.Current);
                    break;
                case ClearAllRocksProgress rocks:
                    sprite = spriteLibrary?.GetObstacleSprite(ObstacleSpriteType.Rock);
                    fallbackLabel = "R";
                    remaining = rocks.RemainingRocks;
                    break;
            }

            item.SetContent(sprite, fallbackLabel, remaining);
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
    }
}
