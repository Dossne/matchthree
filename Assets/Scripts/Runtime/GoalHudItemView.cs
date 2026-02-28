using UnityEngine;
using UnityEngine.UI;

namespace MatchThree.Runtime
{
    public sealed class GoalHudItemView : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public Image IconBackground { get; private set; }
        public Image Icon { get; private set; }
        public Text IconFallback { get; private set; }
        public Text Counter { get; private set; }

        public static GoalHudItemView Create(Transform parent, Font font)
        {
            var root = new GameObject("GoalItem");
            root.transform.SetParent(parent, false);

            var view = root.AddComponent<GoalHudItemView>();
            view.Build(font);
            return view;
        }

        public void SetContent(Sprite sprite, string fallbackLabel, int remaining)
        {
            Counter.text = Mathf.Max(0, remaining).ToString();
            Icon.sprite = sprite;

            var hasSprite = sprite != null;
            Icon.enabled = hasSprite;
            IconBackground.enabled = !hasSprite;
            IconFallback.text = hasSprite ? string.Empty : fallbackLabel;
        }

        private void Build(Font font)
        {
            Root = gameObject.GetComponent<RectTransform>();
            if (Root == null)
            {
                Root = gameObject.AddComponent<RectTransform>();
            }

            Root.sizeDelta = new Vector2(180f, 74f);

            var chipBackground = EnsureComponent<Image>(gameObject);
            chipBackground.color = new Color(0.12f, 0.14f, 0.18f, 0.86f);

            var horizontalLayout = EnsureComponent<HorizontalLayoutGroup>(gameObject);
            horizontalLayout.padding = new RectOffset(10, 14, 9, 9);
            horizontalLayout.spacing = 10f;
            horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
            horizontalLayout.childControlHeight = false;
            horizontalLayout.childControlWidth = false;
            horizontalLayout.childForceExpandHeight = false;
            horizontalLayout.childForceExpandWidth = false;

            var iconRoot = EnsureRect(FindOrCreateUiObject(transform, "IconRoot"));
            iconRoot.sizeDelta = new Vector2(56f, 56f);

            var iconBgObject = FindOrCreateUiObject(iconRoot, "Background");
            IconBackground = EnsureComponent<Image>(iconBgObject);
            IconBackground.color = new Color(0.22f, 0.22f, 0.22f, 0.92f);
            Stretch(IconBackground.rectTransform);

            var iconObject = FindOrCreateUiObject(iconRoot, "Icon");
            Icon = EnsureComponent<Image>(iconObject);
            Icon.useSpriteMesh = true;
            Icon.preserveAspect = true;
            Stretch(Icon.rectTransform, 2f);

            var fallbackObject = FindOrCreateUiObject(iconRoot, "Fallback");
            IconFallback = EnsureComponent<Text>(fallbackObject);
            IconFallback.font = font;
            IconFallback.fontSize = 18;
            IconFallback.alignment = TextAnchor.MiddleCenter;
            IconFallback.color = Color.white;
            Stretch(IconFallback.rectTransform);

            var counterObject = FindOrCreateUiObject(transform, "Counter");
            Counter = EnsureComponent<Text>(counterObject);
            Counter.font = font;
            Counter.fontSize = 40;
            Counter.alignment = TextAnchor.MiddleLeft;
            Counter.color = Color.white;

            var counterLayout = EnsureComponent<LayoutElement>(counterObject);
            counterLayout.minWidth = 48f;
            counterLayout.preferredWidth = 80f;
        }

        private static void Stretch(RectTransform rectTransform, float inset = 0f)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(inset, inset);
            rectTransform.offsetMax = new Vector2(-inset, -inset);
        }

        private static RectTransform EnsureRect(GameObject gameObject)
        {
            return EnsureComponent<RectTransform>(gameObject);
        }

        private static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            var existing = gameObject.GetComponent<T>();
            return existing != null ? existing : gameObject.AddComponent<T>();
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
