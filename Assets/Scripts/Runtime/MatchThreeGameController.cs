using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MatchThree.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MatchThree.Runtime
{
    public sealed class MatchThreeGameController : MonoBehaviour
    {
        private const float SwapDurationSeconds = 0.10f;
        private const float ClearDurationSeconds = 0.08f;
        private const float FallPerCellSeconds = 0.06f;
        private const float MinFallDurationSeconds = 0.08f;
        private const float MaxFallDurationSeconds = 0.35f;
        private const float SettleDelaySeconds = 0.04f;

        [SerializeField] private TextAsset levelAsset;
        [SerializeField] private string levelResourcePath = "Levels/level_000";
        [SerializeField] private int randomSeed = 1234;

        private Board _board;
        private BoardResolver _resolver;
        private TileSpriteLibrary _spriteLibrary;
        private GridLayoutGroup _grid;
        private RectTransform _animationLayer;
        private Text _status;
        private readonly Dictionary<BoardPosition, CellView> _cells = new();
        private BoardPosition? _selected;
        private bool _isAnimating;

        private sealed class CellView
        {
            public RectTransform Root;
            public Button Button;
            public Image Background;
            public Image Icon;
            public Text Label;
            public CanvasGroup Group;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<MatchThreeGameController>() == null)
            {
                var go = new GameObject("MatchThreeGameController");
                go.AddComponent<MatchThreeGameController>();
            }
        }

        private void Start()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            EnsureUi();
            var asset = levelAsset != null ? levelAsset : Resources.Load<TextAsset>(levelResourcePath);
            if (asset == null)
            {
                _status.text = $"Missing level asset at Resources/{levelResourcePath}";
                return;
            }

            _board = LevelParser.Parse(asset.text, new[] { 1, 2, 3, 4 });
            _resolver = new BoardResolver(_board, new SeededRandom(randomSeed));
            _resolver.FillBoardWithoutInitialMatches();
            _spriteLibrary = TileSpriteLibrary.LoadFromTilesFolder();

            BuildGrid();
            Render();
        }

        private void Update()
        {
            if (_resolver == null || _isAnimating) return;
            if (!Input.GetKeyDown(KeyCode.M)) return;

            var groups = _resolver.GetCurrentMatchGroups();
            if (groups.Count == 0)
            {
                Debug.Log("No active matches detected.");
                return;
            }

            for (var i = 0; i < groups.Count; i++)
            {
                var coords = string.Join(", ", groups[i].Select(p => $"({p.X},{p.Y})"));
                Debug.Log($"Match[{i}]: {coords}");
            }
        }

        private void EnsureUi()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var cgo = new GameObject("Canvas");
                canvas = cgo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                cgo.AddComponent<CanvasScaler>();
                cgo.AddComponent<GraphicRaycaster>();
            }

            var statusGo = new GameObject("Status");
            statusGo.transform.SetParent(canvas.transform, false);
            _status = statusGo.AddComponent<Text>();
            _status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _status.alignment = TextAnchor.UpperLeft;
            _status.color = Color.white;
            var srt = _status.rectTransform;
            srt.anchorMin = new Vector2(0, 1);
            srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 1);
            srt.offsetMin = new Vector2(20, -80);
            srt.offsetMax = new Vector2(-20, -20);

            var gridGo = new GameObject("BoardGrid");
            gridGo.transform.SetParent(canvas.transform, false);
            _grid = gridGo.AddComponent<GridLayoutGroup>();
            _grid.spacing = new Vector2(2, 2);
            var rt = _grid.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var animationGo = new GameObject("AnimationLayer");
            animationGo.transform.SetParent(canvas.transform, false);
            _animationLayer = animationGo.AddComponent<RectTransform>();
            _animationLayer.anchorMin = Vector2.zero;
            _animationLayer.anchorMax = Vector2.one;
            _animationLayer.offsetMin = Vector2.zero;
            _animationLayer.offsetMax = Vector2.zero;
        }

        private void BuildGrid()
        {
            foreach (Transform child in _grid.transform) Destroy(child.gameObject);
            _cells.Clear();
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = _board.Width;
            _grid.cellSize = new Vector2(70, 70);

            var rt = _grid.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(_board.Width * 72, _board.Height * 72);

            for (var y = 0; y < _board.Height; y++)
            for (var x = 0; x < _board.Width; x++)
            {
                var pos = new BoardPosition(x, y);
                var go = new GameObject($"Cell_{x}_{y}");
                var root = go.AddComponent<RectTransform>();
                go.transform.SetParent(_grid.transform, false);

                var background = go.AddComponent<Image>();
                var button = go.AddComponent<Button>();
                button.targetGraphic = background;
                button.onClick.AddListener(() => OnCellClicked(pos));

                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, false);
                var icon = iconGo.AddComponent<Image>();
                var irt = icon.rectTransform;
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(6, 6);
                irt.offsetMax = new Vector2(-6, -6);

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(go.transform, false);
                var txt = labelGo.AddComponent<Text>();
                txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.black;
                var lrt = txt.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;

                var group = go.AddComponent<CanvasGroup>();
                _cells[pos] = new CellView
                {
                    Root = root,
                    Button = button,
                    Background = background,
                    Icon = icon,
                    Label = txt,
                    Group = group
                };
            }
        }

        private void OnCellClicked(BoardPosition pos)
        {
            if (_isAnimating) return;

            if (_selected == null)
            {
                _selected = pos;
                Render();
                return;
            }

            if (_selected.Value == pos)
            {
                _selected = null;
                Render();
                return;
            }

            StartCoroutine(HandleMove(_selected.Value, pos));
        }

        private IEnumerator HandleMove(BoardPosition from, BoardPosition to)
        {
            _isAnimating = true;
            SetInputEnabled(false);
            var result = _resolver.TrySwapAndResolve(new Move(from, to));
            _selected = null;

            if (!result.Performed)
            {
                _status.text = "Invalid move.";
                _isAnimating = false;
                Render();
                SetInputEnabled(true);
                yield break;
            }

            yield return AnimateSwap(from, to, result.SwapFromTile, result.SwapToTile, result.Reverted);

            if (result.Reverted)
            {
                _status.text = "Invalid move: no match.";
                _isAnimating = false;
                Render();
                SetInputEnabled(true);
                yield break;
            }

            foreach (var step in result.Steps)
            {
                yield return AnimateResolveStep(step);
            }

            _status.text = $"Resolved steps: {result.Steps.Count}. Delivered: {_resolver.AreAllStatuettesDelivered()}";
            _isAnimating = false;
            Render();
            SetInputEnabled(true);
        }

        private IEnumerator AnimateSwap(BoardPosition from, BoardPosition to, TileEntitySnapshot? fromTile, TileEntitySnapshot? toTile, bool animateBack)
        {
            if (!fromTile.HasValue || !toTile.HasValue) yield break;

            var hidden = new[] { from, to };
            SetCellsVisible(hidden, false);
            var fromView = CreateTransientTile(fromTile.Value, from);
            var toView = CreateTransientTile(toTile.Value, to);

            var fromStart = CellPosition(from);
            var toStart = CellPosition(to);

            yield return AnimateManyMoves(new List<(RectTransform Rect, Vector2 From, Vector2 To, float Duration)>
            {
                (fromView.Root, fromStart, toStart, SwapDurationSeconds),
                (toView.Root, toStart, fromStart, SwapDurationSeconds)
            });

            if (animateBack)
            {
                yield return AnimateManyMoves(new List<(RectTransform Rect, Vector2 From, Vector2 To, float Duration)>
                {
                    (fromView.Root, toStart, fromStart, SwapDurationSeconds),
                    (toView.Root, fromStart, toStart, SwapDurationSeconds)
                });
            }

            Destroy(fromView.Root.gameObject);
            Destroy(toView.Root.gameObject);
            SetCellsVisible(hidden, true);
        }

        private IEnumerator AnimateResolveStep(ResolveStep step)
        {
            if (step.RemovedTiles.Count > 0)
            {
                var removedViews = new List<TransientTile>(step.RemovedTiles.Count);
                var hidden = step.RemovedTiles.Select(r => r.Position).ToList();
                SetCellsVisible(hidden, false);
                foreach (var removed in step.RemovedTiles)
                {
                    removedViews.Add(CreateTransientTile(removed.Tile, removed.Position));
                }

                var elapsed = 0f;
                while (elapsed < ClearDurationSeconds)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / ClearDurationSeconds);
                    foreach (var view in removedViews)
                    {
                        var alpha = 1f - t;
                        view.Background.color = SetAlpha(view.Background.color, alpha);
                        view.Icon.color = SetAlpha(view.Icon.color, alpha);
                        view.Root.localScale = Vector3.one * (1f - 0.25f * t);
                    }
                    yield return null;
                }

                foreach (var view in removedViews) Destroy(view.Root.gameObject);
                SetCellsVisible(hidden, true);
            }

            if (step.Movements.Count > 0)
            {
                var moving = new List<(RectTransform Rect, Vector2 From, Vector2 To, float Duration)>();
                var hidden = new HashSet<BoardPosition>();
                foreach (var movement in step.Movements)
                {
                    hidden.Add(movement.From);
                    hidden.Add(movement.To);
                    var tile = CreateTransientTile(movement.Tile, movement.From);
                    var dist = Mathf.Abs(movement.To.Y - movement.From.Y) + Mathf.Abs(movement.To.X - movement.From.X);
                    var duration = Mathf.Clamp(dist * FallPerCellSeconds, MinFallDurationSeconds, MaxFallDurationSeconds);
                    moving.Add((tile.Root, CellPosition(movement.From), CellPosition(movement.To), duration));
                }

                SetCellsVisible(hidden, false);
                yield return AnimateManyMoves(moving);
                foreach (var m in moving) Destroy(m.Rect.gameObject);
                SetCellsVisible(hidden, true);
            }

            if (step.Spawns.Count > 0)
            {
                var spawning = new List<(RectTransform Rect, Vector2 From, Vector2 To, float Duration)>();
                var hidden = new HashSet<BoardPosition>();
                foreach (var spawn in step.Spawns)
                {
                    hidden.Add(spawn.To);
                    var tile = CreateTransientTile(spawn.Tile, spawn.To);
                    var to = CellPosition(spawn.To);
                    var from = to + new Vector2(0f, (_grid.cellSize.y + _grid.spacing.y) * spawn.SpawnDistance);
                    var duration = Mathf.Clamp(spawn.SpawnDistance * FallPerCellSeconds, MinFallDurationSeconds, MaxFallDurationSeconds);
                    tile.Root.anchoredPosition = from;
                    spawning.Add((tile.Root, from, to, duration));
                }

                SetCellsVisible(hidden, false);
                yield return AnimateManyMoves(spawning);
                foreach (var s in spawning) Destroy(s.Rect.gameObject);
                SetCellsVisible(hidden, true);
            }

            if (step.DidChange) yield return new WaitForSeconds(SettleDelaySeconds);
        }

        private IEnumerator AnimateManyMoves(List<(RectTransform Rect, Vector2 From, Vector2 To, float Duration)> moves)
        {
            var maxDuration = moves.Max(m => m.Duration);
            var elapsed = 0f;
            while (elapsed < maxDuration)
            {
                elapsed += Time.deltaTime;
                foreach (var move in moves)
                {
                    var t = Mathf.Clamp01(elapsed / move.Duration);
                    move.Rect.anchoredPosition = Vector2.LerpUnclamped(move.From, move.To, t);
                }
                yield return null;
            }

            foreach (var move in moves)
            {
                move.Rect.anchoredPosition = move.To;
            }
        }

        private IEnumerator AnimateMove(RectTransform rect, Vector2 from, Vector2 to, float duration)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                rect.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
                yield return null;
            }

            rect.anchoredPosition = to;
        }

        private void SetInputEnabled(bool enabled)
        {
            _isAnimating = !enabled;
            Render();
        }

        private void Render()
        {
            foreach (var kvp in _cells)
            {
                var p = kvp.Key;
                var view = kvp.Value;
                var cell = _board.Cells[p.X, p.Y];
                ApplyVisual(view, cell.Tile, cell.IsPlayable);

                if (_selected.HasValue && _selected.Value == p)
                {
                    view.Background.enabled = true;
                    view.Background.color = new Color(1f, 1f, 1f, 0.35f);
                }

                view.Button.interactable = !_isAnimating && cell.IsPlayable;
            }
        }

        private void ApplyVisual(CellView view, TileEntity tile, bool isPlayable)
        {
            view.Icon.sprite = null;
            view.Icon.enabled = false;
            view.Label.text = string.Empty;

            if (!isPlayable)
            {
                view.Background.enabled = true;
                view.Background.color = Color.black;
                view.Label.text = "#";
                return;
            }

            if (tile == null)
            {
                view.Background.enabled = true;
                view.Background.color = new Color(0.2f, 0.2f, 0.2f);
                view.Label.text = ".";
                return;
            }

            var visual = ResolveVisual(TileEntitySnapshot.From(tile));
            ApplyResolvedVisual(view.Background, view.Icon, view.Label, visual);
        }

        private TileVisual ResolveVisual(TileEntitySnapshot tile)
        {
            Sprite sprite = null;
            Color placeholder = Color.white;
            string label = string.Empty;

            switch (tile.Kind)
            {
                case TileKind.Piece:
                    sprite = _spriteLibrary?.GetNormalSprite(tile.ColorId);
                    placeholder = ColorFor(tile.ColorId);
                    label = tile.ColorId.ToString();
                    break;
                case TileKind.Rock:
                    sprite = _spriteLibrary?.GetObstacleSprite(ObstacleSpriteType.Rock);
                    placeholder = Color.gray;
                    label = "R";
                    break;
                case TileKind.Boulder:
                    sprite = _spriteLibrary?.GetObstacleSprite(ObstacleSpriteType.Boulder);
                    placeholder = new Color(0.35f, 0.25f, 0.2f);
                    label = "B";
                    break;
                case TileKind.Statuette:
                    placeholder = Color.yellow;
                    label = "S";
                    break;
                case TileKind.Special:
                    sprite = tile.SpecialType switch
                    {
                        SpecialType.RocketHorizontal => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.Rocket),
                        SpecialType.RocketVertical => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.Rocket),
                        SpecialType.Bomb => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.Bomb),
                        SpecialType.SuperLightning => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.SuperLightning),
                        _ => null
                    };
                    placeholder = new Color(1f, 0.6f, 0.1f);
                    label = tile.SpecialType switch
                    {
                        SpecialType.RocketHorizontal => "RH",
                        SpecialType.RocketVertical => "RV",
                        SpecialType.Bomb => "BO",
                        SpecialType.SuperLightning => "SL",
                        _ => "?"
                    };
                    break;
            }

            if (sprite == null && (tile.Kind == TileKind.Piece || tile.Kind == TileKind.Rock || tile.Kind == TileKind.Boulder || tile.Kind == TileKind.Special))
            {
                Debug.LogError($"Missing sprite for tile kind={tile.Kind}, color={tile.ColorId}, special={tile.SpecialType}. Placeholder will be shown.");
            }

            return new TileVisual(sprite, placeholder, label);
        }

        private static void ApplyResolvedVisual(Image background, Image icon, Text label, TileVisual visual)
        {
            if (visual.Sprite != null)
            {
                background.enabled = false;
                icon.enabled = true;
                icon.sprite = visual.Sprite;
                icon.color = Color.white;
                label.text = string.Empty;
            }
            else
            {
                background.enabled = true;
                background.color = visual.Placeholder;
                icon.enabled = false;
                label.text = visual.Label;
            }
        }

        private void SetCellsVisible(IEnumerable<BoardPosition> positions, bool visible)
        {
            foreach (var pos in positions)
            {
                if (!_cells.TryGetValue(pos, out var cell)) continue;
                cell.Group.alpha = visible ? 1f : 0f;
            }
        }

        private Vector2 CellPosition(BoardPosition pos)
        {
            var root = _cells[pos].Root;
            var screen = RectTransformUtility.WorldToScreenPoint(null, root.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_animationLayer, screen, null, out var localPoint);
            return localPoint;
        }

        private TransientTile CreateTransientTile(TileEntitySnapshot tile, BoardPosition at)
        {
            var go = new GameObject("TransientTile");
            go.transform.SetParent(_animationLayer, false);
            var root = go.AddComponent<RectTransform>();
            root.sizeDelta = _grid.cellSize;
            root.anchoredPosition = CellPosition(at);

            var bg = go.AddComponent<Image>();
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = new Vector2(6, 6);
            icon.rectTransform.offsetMax = new Vector2(-6, -6);

            var visual = ResolveVisual(tile);
            if (visual.Sprite != null)
            {
                bg.enabled = false;
                icon.enabled = true;
                icon.sprite = visual.Sprite;
                icon.color = Color.white;
            }
            else
            {
                bg.enabled = true;
                bg.color = visual.Placeholder;
                icon.enabled = false;
            }

            return new TransientTile(root, bg, icon);
        }

        private readonly struct TileVisual
        {
            public readonly Sprite Sprite;
            public readonly Color Placeholder;
            public readonly string Label;

            public TileVisual(Sprite sprite, Color placeholder, string label)
            {
                Sprite = sprite;
                Placeholder = placeholder;
                Label = label;
            }
        }

        private readonly struct TransientTile
        {
            public readonly RectTransform Root;
            public readonly Image Background;
            public readonly Image Icon;

            public TransientTile(RectTransform root, Image background, Image icon)
            {
                Root = root;
                Background = background;
                Icon = icon;
            }
        }

        private static Color SetAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color ColorFor(int id) => id switch
        {
            1 => new Color(0.8f, 0.2f, 0.2f),
            2 => new Color(0.2f, 0.8f, 0.2f),
            3 => new Color(0.2f, 0.4f, 0.9f),
            4 => new Color(0.85f, 0.85f, 0.2f),
            5 => new Color(0.8f, 0.2f, 0.8f),
            _ => Color.white
        };
    }
}
