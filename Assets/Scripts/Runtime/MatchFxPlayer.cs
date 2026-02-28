using System.Collections;
using System.Collections.Generic;
using MatchThree.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MatchThree.Runtime
{
    public sealed class MatchFxPlayer
    {
        private const float RocketFxDurationSeconds = 0.16f;
        private const float BombFxDurationSeconds = 0.18f;
        private const float LightningFxDurationSeconds = 0.14f;

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

            var rect = fx.AddComponent<RectTransform>();
            var image = fx.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.35f);

            var boardRect = _boardRectProvider();
            var cellSize = _cellSizeProvider();
            var center = _cellPositionProvider(position);

            if (horizontal)
            {
                rect.sizeDelta = new Vector2(boardRect.width + cellSize.x, Mathf.Max(14f, cellSize.y * 0.28f));
                rect.anchoredPosition = new Vector2(boardRect.center.x, center.y);
            }
            else
            {
                rect.sizeDelta = new Vector2(Mathf.Max(14f, cellSize.x * 0.28f), boardRect.height + cellSize.y);
                rect.anchoredPosition = new Vector2(center.x, boardRect.center.y);
            }

            var elapsed = 0f;
            while (elapsed < RocketFxDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RocketFxDurationSeconds);
                var alpha = Mathf.Sin(t * Mathf.PI) * 0.42f;
                image.color = SetAlpha(image.color, alpha);
                yield return null;
            }

            Object.Destroy(fx);
        }

        private IEnumerator AnimateBombPulse(BoardPosition position)
        {
            var fx = new GameObject("Fx_BombPulse");
            fx.transform.SetParent(_animationLayer, false);

            var rect = fx.AddComponent<RectTransform>();
            var image = fx.AddComponent<Image>();
            image.color = new Color(1f, 0.92f, 0.45f, 0.35f);
            image.sprite = ResolveBombPulseSprite();
            image.type = Image.Type.Simple;

            var cellSize = _cellSizeProvider();
            rect.sizeDelta = cellSize * 0.9f;
            rect.anchoredPosition = _cellPositionProvider(position);
            rect.localScale = Vector3.one * 0.5f;

            var elapsed = 0f;
            while (elapsed < BombFxDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / BombFxDurationSeconds);
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.5f, 2.2f, t);
                image.color = SetAlpha(image.color, 0.42f * (1f - t));
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

            var elapsed = 0f;
            while (elapsed < LightningFxDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / LightningFxDurationSeconds);
                var alpha = Mathf.Sin(t * Mathf.PI) * 0.18f;
                image.color = SetAlpha(image.color, alpha);
                yield return null;
            }

            Object.Destroy(fx);
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
