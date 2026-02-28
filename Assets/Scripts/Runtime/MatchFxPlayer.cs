using System.Collections;
using System.Collections.Generic;
using MatchThree.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MatchThree.Runtime
{
    public sealed class MatchFxPlayer
    {
        private const float RocketFxDurationSeconds = 0.28f;
        private const float BombFxDurationSeconds = 0.24f;
        private const float LightningFxDurationSeconds = 0.20f;

        private readonly MonoBehaviour _host;
        private readonly RectTransform _animationLayer;
        private readonly System.Func<Rect> _boardRectProvider;
        private readonly System.Func<BoardPosition, Vector2> _cellPositionProvider;
        private readonly System.Func<Vector2> _cellSizeProvider;
        private Sprite _bombPulseSprite;
        private bool _didResolveBombSprite;
        private static bool _didWarnBombPulseFallback;

        public MatchFxPlayer(
            MonoBehaviour host,
            RectTransform animationLayer,
            System.Func<Rect> boardRectProvider,
            System.Func<BoardPosition, Vector2> cellPositionProvider,
            System.Func<Vector2> cellSizeProvider)
        {
            _host = host;
            _animationLayer = animationLayer;
            _boardRectProvider = boardRectProvider;
            _cellPositionProvider = cellPositionProvider;
            _cellSizeProvider = cellSizeProvider;
        }

        public void PlaySpecialActivationFx(IEnumerable<(BoardPosition Position, SpecialType Type)> activatedSpecials)
        {
            foreach (var special in activatedSpecials)
            {
                switch (special.Type)
                {
                    case SpecialType.RocketHorizontal:
                        _host.StartCoroutine(AnimateRocketSweep(special.Position, true));
                        break;
                    case SpecialType.RocketVertical:
                        _host.StartCoroutine(AnimateRocketSweep(special.Position, false));
                        break;
                    case SpecialType.Bomb:
                        _host.StartCoroutine(AnimateBombPulse(special.Position));
                        break;
                    case SpecialType.SuperLightning:
                        _host.StartCoroutine(AnimateLightningFlash());
                        break;
                }
            }
        }

        private IEnumerator AnimateRocketSweep(BoardPosition position, bool horizontal)
        {
            var fx = new GameObject(horizontal ? "Fx_RocketH" : "Fx_RocketV");
            fx.transform.SetParent(_animationLayer, false);

            var head = CreateFxImage(fx.transform, "Head", new Color(1f, 1f, 1f, 0.7f));
            var trail = CreateFxImage(fx.transform, "Trail", new Color(1f, 0.92f, 0.56f, 0.52f));

            var boardRect = _boardRectProvider();
            var cellSize = _cellSizeProvider();
            var center = _cellPositionProvider(position);
            var thickness = horizontal
                ? Mathf.Max(12f, cellSize.y * 0.22f)
                : Mathf.Max(12f, cellSize.x * 0.22f);

            if (horizontal)
            {
                head.Rect.sizeDelta = new Vector2(Mathf.Max(cellSize.x * 0.9f, 16f), thickness * 1.35f);
                trail.Rect.sizeDelta = new Vector2(Mathf.Max(cellSize.x * 1.4f, 20f), thickness);
            }
            else
            {
                head.Rect.sizeDelta = new Vector2(thickness * 1.35f, Mathf.Max(cellSize.y * 0.9f, 16f));
                trail.Rect.sizeDelta = new Vector2(thickness, Mathf.Max(cellSize.y * 1.4f, 20f));
            }

            var pathStart = horizontal
                ? new Vector2(boardRect.xMin - (cellSize.x * 0.4f), center.y)
                : new Vector2(center.x, boardRect.yMax + (cellSize.y * 0.4f));
            var pathEnd = horizontal
                ? new Vector2(boardRect.xMax + (cellSize.x * 0.4f), center.y)
                : new Vector2(center.x, boardRect.yMin - (cellSize.y * 0.4f));
            var trailOffset = horizontal
                ? new Vector2(-cellSize.x * 0.7f, 0f)
                : new Vector2(0f, cellSize.y * 0.7f);

            var elapsed = 0f;
            while (elapsed < RocketFxDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RocketFxDurationSeconds);
                var positionT = Mathf.SmoothStep(0f, 1f, t);
                head.Rect.anchoredPosition = Vector2.LerpUnclamped(pathStart, pathEnd, positionT);
                trail.Rect.anchoredPosition = head.Rect.anchoredPosition + trailOffset;

                var alpha = Mathf.Sin(t * Mathf.PI) * 0.9f;
                head.Image.color = SetAlpha(head.Image.color, alpha * 0.75f);
                trail.Image.color = SetAlpha(trail.Image.color, alpha * 0.5f);
                yield return null;
            }

            Object.Destroy(fx);
        }

        private IEnumerator AnimateBombPulse(BoardPosition position)
        {
            var fx = new GameObject("Fx_BombPulse");
            fx.transform.SetParent(_animationLayer, false);

            var pulsePrimary = CreateFxImage(fx.transform, "PulsePrimary", new Color(1f, 0.92f, 0.45f, 0.4f));
            var pulseSecondary = CreateFxImage(fx.transform, "PulseSecondary", new Color(1f, 0.82f, 0.35f, 0.3f));
            var pulseSprite = ResolveBombPulseSprite();
            pulsePrimary.Image.sprite = pulseSprite;
            pulseSecondary.Image.sprite = pulseSprite;

            var cellSize = _cellSizeProvider();
            var center = _cellPositionProvider(position);
            pulsePrimary.Rect.sizeDelta = cellSize * 0.95f;
            pulseSecondary.Rect.sizeDelta = cellSize * 0.9f;
            pulsePrimary.Rect.anchoredPosition = center;
            pulseSecondary.Rect.anchoredPosition = center;
            pulsePrimary.Rect.localScale = Vector3.one * 0.55f;
            pulseSecondary.Rect.localScale = Vector3.one * 0.2f;

            var elapsed = 0f;
            while (elapsed < BombFxDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / BombFxDurationSeconds);

                pulsePrimary.Rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 2.3f, t);
                pulsePrimary.Image.color = SetAlpha(pulsePrimary.Image.color, 0.48f * (1f - t));

                var delayedT = Mathf.Clamp01((t - 0.18f) / 0.82f);
                pulseSecondary.Rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1.85f, delayedT);
                pulseSecondary.Image.color = SetAlpha(pulseSecondary.Image.color, 0.36f * (1f - delayedT));
                yield return null;
            }

            Object.Destroy(fx);
        }

        private IEnumerator AnimateLightningFlash()
        {
            var fx = new GameObject("Fx_LightningFlash");
            fx.transform.SetParent(_animationLayer, false);

            var rect = fx.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = fx.AddComponent<Image>();
            image.color = new Color(1f, 1f, 0.88f, 0f);

            var boardRect = _boardRectProvider();
            var boltA = CreateLightningBolt(fx.transform, "BoltA", boardRect, -0.18f);
            var boltB = CreateLightningBolt(fx.transform, "BoltB", boardRect, 0.22f);
            var boltABasePosition = boltA.rectTransform.anchoredPosition;
            var boltBBasePosition = boltB.rectTransform.anchoredPosition;

            var elapsed = 0f;
            while (elapsed < LightningFxDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / LightningFxDurationSeconds);

                var alpha = Mathf.Sin(t * Mathf.PI) * 0.18f;
                image.color = SetAlpha(image.color, alpha);
                boltA.color = SetAlpha(boltA.color, alpha * 2.8f);
                boltB.color = SetAlpha(boltB.color, alpha * 2.1f);

                var jitter = Mathf.Sin(t * Mathf.PI * 16f) * 4f;
                boltA.rectTransform.anchoredPosition = boltABasePosition + new Vector2(jitter, 0f);
                boltB.rectTransform.anchoredPosition = boltBBasePosition + new Vector2(-jitter, 0f);
                yield return null;
            }

            Object.Destroy(fx);
        }

        private readonly struct FxImage
        {
            public readonly RectTransform Rect;
            public readonly Image Image;

            public FxImage(RectTransform rect, Image image)
            {
                Rect = rect;
                Image = image;
            }
        }

        private static FxImage CreateFxImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            var image = go.AddComponent<Image>();
            image.color = color;
            return new FxImage(rect, image);
        }

        private static Image CreateLightningBolt(Transform parent, string name, Rect boardRect, float horizontalOffsetRatio)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);

            var rect = image.rectTransform;
            rect.sizeDelta = new Vector2(Mathf.Max(8f, boardRect.width * 0.03f), boardRect.height * 1.05f);
            rect.anchoredPosition = new Vector2(boardRect.center.x + boardRect.width * horizontalOffsetRatio, boardRect.center.y);
            rect.localRotation = Quaternion.Euler(0f, 0f, horizontalOffsetRatio * 24f);
            return image;
        }

        private static Color SetAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private Sprite ResolveBombPulseSprite()
        {
            if (_didResolveBombSprite)
            {
                return _bombPulseSprite;
            }

            _didResolveBombSprite = true;

            _bombPulseSprite = Resources.Load<Sprite>("Fx/bomb_pulse");
            if (_bombPulseSprite != null)
            {
                return _bombPulseSprite;
            }

            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var center = new Vector2(31.5f, 31.5f);
            var maxDistance = 31.5f;
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), center) / maxDistance;
                    var radialFalloff = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(distance));
                    var alpha = radialFalloff * radialFalloff;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            _bombPulseSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);

            if (!_didWarnBombPulseFallback)
            {
                _didWarnBombPulseFallback = true;
                Debug.LogWarning("[MatchFxPlayer] Missing Resources/Fx/bomb_pulse sprite; using procedural bomb pulse fallback.");
            }

            return _bombPulseSprite;
        }
    }
}
