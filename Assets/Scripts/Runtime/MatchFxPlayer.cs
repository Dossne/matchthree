using System.Collections;
using System.Collections.Generic;
using MatchThree.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MatchThree.Runtime
{
    public sealed class MatchFxPlayer
    {
        private readonly MonoBehaviour _host;
        private readonly RectTransform _animationLayer;
        private readonly System.Func<Rect> _boardRectProvider;
        private readonly System.Func<BoardPosition, Vector2> _cellPositionProvider;
        private readonly System.Func<Vector2> _cellSizeProvider;

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

            const float duration = 0.16f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
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

            var cellSize = _cellSizeProvider();
            rect.sizeDelta = cellSize * 0.9f;
            rect.anchoredPosition = _cellPositionProvider(position);
            rect.localScale = Vector3.one * 0.5f;

            const float duration = 0.18f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
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

            const float duration = 0.14f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
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
    }
}
