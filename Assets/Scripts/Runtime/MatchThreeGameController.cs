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
        private const string DefaultLevelResourcePath = "Levels/level_000";
        private const float SwapDurationSeconds = 0.10f;
        private const float ClearDurationSeconds = 0.22f;
        private const float FallPerCellSeconds = 0.08f;
        private const float MinFallDurationSeconds = 0.10f;
        private const float MaxFallDurationSeconds = 0.48f;
        private const float SettleDelaySeconds = 0.14f;

        // UI layout tuning (portrait)
        private const float HudHeight = 220f;
        private const float BottomPadding = 110f;
        private const float BoardWidthUsage = 0.90f;
        private const float BoardHeightUsage = 0.96f;

        // Icon padding inside cell (0 = ìàêñèìàëüíî êðóïíî)
        private const float IconInset = 0f;

        [SerializeField] private TextAsset levelAsset;
        [SerializeField] private TextAsset levelConfigAsset;
        [SerializeField] private LevelRegistry levelRegistry = new();
        [SerializeField] private int randomSeed = 1234;

        private Board _board;
        private BoardResolver _resolver;
        private TileSpriteLibrary _spriteLibrary;

        private RectTransform _uiRoot;
        private RectTransform _hud;
        private RectTransform _boardContainer;

        private GridLayoutGroup _grid;
        private RectTransform _animationLayer;

        private Text _status;
        private GoalHudView _goalHudView;

        private MatchThree.Core.MoveCounter _moveCounter;
        private GoalTracker _goalTracker;
        private GameStateController _gameStateController;
        private MatchFxPlayer _fxPlayer;

        private GameObject _winPanel;
        private GameObject _losePanel;
        private int _currentLevelIndex;
        private List<RuntimeLevelData> _levels = new();

        private readonly Dictionary<BoardPosition, CellView> _cells = new();
        private readonly HashSet<string> _loggedMissingSpriteKeys = new();

        private BoardPosition? _selected;
        private bool _isAnimating;
        private bool _isInputBlocked;

        private sealed class CellView
        {
            public RectTransform Root;
            public Button Button;
            public Image Background;
            public Image Icon;
            public Text Label;
            public CanvasGroup Group;
        }

        private readonly struct RuntimeLevelData
        {
            public readonly string Name;
            public readonly TextAsset Asset;
            public readonly LevelDefinition Config;

            public RuntimeLevelData(string name, TextAsset asset, LevelDefinition config)
            {
                Name = name;
                Asset = asset;
                Config = config;
            }
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

            _spriteLibrary = TileSpriteLibrary.LoadFromTilesFolder();
            _goalHudView?.UpdateGoals(_goalTracker, _spriteLibrary);

            if (!TryBuildLevelList(out var error))
            {
                _status.text = error;
                return;
            }

            _currentLevelIndex = 0;
            InitializeLevel(_levels[_currentLevelIndex]);
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
                cgo.AddComponent<GraphicRaycaster>();
            }

            var scaler = EnsureComponent<CanvasScaler>(canvas.gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var rootGo = FindOrCreateUiObject(canvas.transform, "Root");
            _uiRoot = EnsureComponent<RectTransform>(rootGo);
            _uiRoot.anchorMin = Vector2.zero;
            _uiRoot.anchorMax = Vector2.one;
            _uiRoot.offsetMin = Vector2.zero;
            _uiRoot.offsetMax = Vector2.zero;

            var hudGo = FindOrCreateUiObject(_uiRoot, "HUD");
            _hud = EnsureComponent<RectTransform>(hudGo);
            _hud.anchorMin = new Vector2(0f, 1f);
            _hud.anchorMax = new Vector2(1f, 1f);
            _hud.pivot = new Vector2(0.5f, 1f);
            _hud.sizeDelta = new Vector2(0f, HudHeight);
            _hud.anchoredPosition = Vector2.zero;

            var boardContainerGo = FindOrCreateUiObject(_uiRoot, "BoardContainer");
            _boardContainer = EnsureComponent<RectTransform>(boardContainerGo);
            _boardContainer.anchorMin = new Vector2(0f, 0f);
            _boardContainer.anchorMax = new Vector2(1f, 1f);
            _boardContainer.offsetMin = new Vector2(0f, BottomPadding);
            _boardContainer.offsetMax = new Vector2(0f, -HudHeight);

            // Status
            var statusGo = FindOrCreateUiObject(_hud, "Status");
            _status = EnsureComponent<Text>(statusGo);
            _status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _status.alignment = TextAnchor.UpperLeft;
            _status.color = Color.white;
            _status.fontSize = 30;

            var srt = _status.rectTransform;
            srt.anchorMin = new Vector2(0, 0);
            srt.anchorMax = new Vector2(0.65f, 1);
            srt.pivot = new Vector2(0.5f, 1);
            srt.offsetMin = new Vector2(24, 16);
            srt.offsetMax = new Vector2(-12, -16);

            // Goals panel
            var goalsPanelGo = FindOrCreateUiObject(_hud, "GoalsPanel");
            var goalsPanel = EnsureComponent<RectTransform>(goalsPanelGo);
            goalsPanel.anchorMin = new Vector2(0f, 0f);
            goalsPanel.anchorMax = new Vector2(0.7f, 1f);
            goalsPanel.pivot = new Vector2(0f, 1f);
            goalsPanel.offsetMin = new Vector2(24f, 16f);
            goalsPanel.offsetMax = new Vector2(-12f, -78f);

            // Moves panel
            var movesPanelGo = FindOrCreateUiObject(_hud, "MovesPanel");
            var movesPanel = EnsureComponent<RectTransform>(movesPanelGo);
            movesPanel.anchorMin = new Vector2(0.7f, 0f);
            movesPanel.anchorMax = new Vector2(1f, 1f);
            movesPanel.pivot = new Vector2(1f, 1f);
            movesPanel.offsetMin = new Vector2(12f, 16f);
            movesPanel.offsetMax = new Vector2(-24f, -24f);

            var hudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _goalHudView = new GoalHudView(goalsPanel, movesPanel, hudFont);

            // Grid (inside board container)
            var gridGo = FindOrCreateUiObject(_boardContainer, "BoardGrid");
            _grid = EnsureComponent<GridLayoutGroup>(gridGo);
            _grid.spacing = new Vector2(4, 4);

            var gridRt = _grid.GetComponent<RectTransform>();
            gridRt.anchorMin = new Vector2(0.5f, 0.5f);
            gridRt.anchorMax = new Vector2(0.5f, 0.5f);
            gridRt.pivot = new Vector2(0.5f, 0.5f);

            // Animation layer
            var animationGo = FindOrCreateUiObject(canvas.transform, "AnimationLayer");
            _animationLayer = EnsureComponent<RectTransform>(animationGo);
            _animationLayer.anchorMin = Vector2.zero;
            _animationLayer.anchorMax = Vector2.one;
            _animationLayer.offsetMin = Vector2.zero;
            _animationLayer.offsetMax = Vector2.zero;

            _fxPlayer = new MatchFxPlayer(this, _animationLayer, GetBoardRectOnAnimationLayer, CellPosition, () => _grid.cellSize);

            // Overlays
            _winPanel = BuildOverlayPanel(canvas.transform, "WinPanel", "You Win!", "Next", LoadNextLevel);
            _losePanel = BuildOverlayPanel(canvas.transform, "LosePanel", "You Lose!", "Retry", RetryLevel);
            ShowOverlay(null);
        }

        private void InitializeLevel(RuntimeLevelData level)
        {
            _selected = null;
            _isAnimating = false;
            _isInputBlocked = false;
            ShowOverlay(null);

            _board = LevelParser.Parse(level.Asset.text, new[] { 1, 2, 3, 4 });
            _resolver = new BoardResolver(_board, new SeededRandom(randomSeed));

            _moveCounter = new MatchThree.Core.MoveCounter(level.Config.MaxMoves);
            _resolver.FillBoardWithoutInitialMatches();

            _goalTracker = new GoalTracker(level.Config.Goals);
            _goalTracker.Initialize(_board);

            _gameStateController = new GameStateController(_goalTracker, _moveCounter);

            BuildGrid();
            RefreshHud();

            _status.text = "Make a move.";
            SetInputEnabled(true);
            Render();
        }

        private bool TryBuildLevelList(out string error)
        {
            error = null;
            _levels.Clear();

            if (levelAsset != null)
            {
                _levels.Add(new RuntimeLevelData(levelAsset.name, levelAsset, BuildInspectorLevelConfig()));
            }

            IReadOnlyList<LevelDefinition> registryDefinitions;
            try
            {
                registryDefinitions = levelRegistry.LoadDefinitions();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MatchThreeGameController] Registry parse/load failed: {ex.Message}. Falling back to '{DefaultLevelResourcePath}'.");
                if (_levels.Count > 0)
                {
                    return true;
                }

                if (TryAddFallbackLevel(out error))
                {
                    return true;
                }

                return false;
            }

            foreach (var definition in registryDefinitions)
            {
                var levelPath = string.IsNullOrWhiteSpace(definition.LevelPath) ? DefaultLevelResourcePath : definition.LevelPath;
                var asset = Resources.Load<TextAsset>(levelPath);
                if (asset == null)
                {
                    Debug.LogWarning($"[MatchThreeGameController] Missing level asset at Resources/{levelPath}; trying fallback '{DefaultLevelResourcePath}'.");

                    if (TryAddFallbackLevel(out error))
                    {
                        return true;
                    }

                    return false;
                }

                definition.LevelPath = levelPath;
                _levels.Add(new RuntimeLevelData(levelPath, asset, definition));
            }

            if (_levels.Count == 0)
            {
                error = "No playable levels were found.";
                return false;
            }

            return true;
        }

        private bool TryAddFallbackLevel(out string error)
        {
            var fallbackAsset = Resources.Load<TextAsset>(DefaultLevelResourcePath);
            if (fallbackAsset == null)
            {
                error = $"Missing fallback level asset at Resources/{DefaultLevelResourcePath}";
                return false;
            }

            var fallbackDefinition = new LevelDefinition
            {
                LevelPath = DefaultLevelResourcePath,
                MaxMoves = 20,
                Goals = new List<GoalDefinition>
                {
                    new CollectColorGoalDefinition(1, 10)
                }
            };

            _levels.Add(new RuntimeLevelData(DefaultLevelResourcePath, fallbackAsset, fallbackDefinition));
            error = null;
            Debug.Log($"[MatchThreeGameController] Added fallback level '{DefaultLevelResourcePath}'.");
            return true;
        }

        private LevelDefinition BuildInspectorLevelConfig()
        {
            if (levelConfigAsset == null)
            {
                return new LevelDefinition
                {
                    LevelPath = levelAsset != null ? levelAsset.name : "InspectorLevel",
                    MaxMoves = 20,
                    Goals = new List<GoalDefinition>
                    {
                        new CollectColorGoalDefinition(1, 10)
                    }
                };
            }

            var data = JsonUtility.FromJson<InspectorLevelConfigData>(levelConfigAsset.text);
            var definition = new LevelDefinition
            {
                LevelPath = levelAsset != null ? levelAsset.name : "InspectorLevel",
                MaxMoves = data.maxMoves,
                Goals = new List<GoalDefinition>()
            };

            if (data.goals != null)
            {
                foreach (var goal in data.goals)
                {
                    if (goal.type == "CollectColor")
                    {
                        definition.Goals.Add(new CollectColorGoalDefinition(goal.colorId, goal.target));
                    }
                    else if (goal.type == "ClearAllRocks")
                    {
                        definition.Goals.Add(new ClearAllRocksGoalDefinition());
                    }
                }
            }

            return definition;
        }

        private void RetryLevel()
        {
            if (_isAnimating) return;
            InitializeLevel(_levels[_currentLevelIndex]);
        }

        private void LoadNextLevel()
        {
            if (_isAnimating) return;

            _currentLevelIndex = (_currentLevelIndex + 1) % _levels.Count;
            InitializeLevel(_levels[_currentLevelIndex]);
        }

        private void ShowOverlay(GameState? state)
        {
            if (_winPanel != null) _winPanel.SetActive(state == GameState.Won);
            if (_losePanel != null) _losePanel.SetActive(state == GameState.Lost);
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

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent<T>(out var existing) ? existing : go.AddComponent<T>();
        }

        private GameObject BuildOverlayPanel(
            Transform canvas,
            string panelName,
            string title,
            string buttonText,
            UnityEngine.Events.UnityAction onClick)
        {
            var panelGo = FindOrCreateUiObject(canvas, panelName);
            var panelRect = EnsureComponent<RectTransform>(panelGo);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var background = EnsureComponent<Image>(panelGo);
            background.color = new Color(0f, 0f, 0f, 0.72f);
            background.raycastTarget = true;

            var group = EnsureComponent<CanvasGroup>(panelGo);
            group.interactable = true;
            group.blocksRaycasts = true;

            var titleGo = FindOrCreateUiObject(panelGo.transform, "Title");
            var titleText = EnsureComponent<Text>(titleGo);
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.text = title;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.fontSize = 52;

            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(520f, 120f);
            titleRect.anchoredPosition = new Vector2(0f, 90f);

            var buttonGo = FindOrCreateUiObject(panelGo.transform, "ActionButton");
            var buttonImage = EnsureComponent<Image>(buttonGo);
            buttonImage.color = new Color(1f, 1f, 1f, 0.92f);

            var button = EnsureComponent<Button>(buttonGo);
            button.targetGraphic = buttonImage;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(320f, 90f);
            buttonRect.anchoredPosition = new Vector2(0f, -20f);

            var buttonLabelGo = FindOrCreateUiObject(buttonGo.transform, "Label");
            var buttonLabel = EnsureComponent<Text>(buttonLabelGo);
            buttonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonLabel.text = buttonText;
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            buttonLabel.color = Color.black;
            buttonLabel.fontSize = 42;

            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;

            panelGo.SetActive(false);
            return panelGo;
        }

        private void BuildGrid()
        {
            foreach (Transform child in _grid.transform) Destroy(child.gameObject);
            _cells.Clear();

            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = _board.Width;
            ConfigureBoardLayout();

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
                    icon.useSpriteMesh = true;

                    var irt = icon.rectTransform;
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.offsetMin = new Vector2(IconInset, IconInset);
                    irt.offsetMax = new Vector2(-IconInset, -IconInset);

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
            if (_isAnimating || _isInputBlocked) return;

            if (_moveCounter != null && !_moveCounter.CanMakeMove)
            {
                _status.text = "No moves remaining.";
                return;
            }

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

            for (var i = 0; i < result.Steps.Count; i++)
            {
                var step = result.Steps[i];
                yield return AnimateResolveStep(step);

                var hasNextStep = i < result.Steps.Count - 1;
                if (step.DidChange && hasNextStep)
                {
                    yield return new WaitForSeconds(SettleDelaySeconds);
                }
            }

            _moveCounter.ConsumeIfAccepted(result);
            _goalTracker.ApplyMoveResult(result);
            RefreshHud();
            UpdateStatusAfterEvaluation();

            _isAnimating = false;
            Render();
            SetInputEnabled(_gameStateController.State == GameState.Playing);
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
                var activatedSpecials = step.RemovedTiles
                    .Where(t => t.Tile.Kind == TileKind.Special)
                    .Select(t => (t.Position, t.Tile.SpecialType))
                    .Distinct()
                    .ToList();
                _fxPlayer?.PlaySpecialActivationFx(activatedSpecials);

                var removedViews = new List<TransientTile>(step.RemovedTiles.Count);
                var hidden = step.RemovedTiles.Select(r => r.Position).ToList();
                SetCellsVisible(hidden, false);

                foreach (var removed in step.RemovedTiles)
                {
                    removedViews.Add(CreateTransientTile(removed.Tile, removed.Position));
                }

                var elapsed = 0f;
                const float popPhase = 0.28f;
                while (elapsed < ClearDurationSeconds)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / ClearDurationSeconds);
                    var popT = Mathf.Clamp01(t / popPhase);
                    var fadeT = Mathf.Clamp01((t - popPhase) / (1f - popPhase));
                    var flash = Mathf.Clamp01(1f - (t / 0.5f));
                    foreach (var view in removedViews)
                    {
                        var alpha = 1f - fadeT;

                        var litBackground = Color.Lerp(view.BaseBackgroundColor, Color.white, 0.18f * flash);
                        var litIcon = Color.Lerp(view.BaseIconColor, Color.white, 0.22f * flash);

                        view.Background.color = SetAlpha(litBackground, alpha);
                        view.Icon.color = SetAlpha(litIcon, alpha);

                        var scale = t < popPhase
                            ? Mathf.LerpUnclamped(1f, 1.16f, popT)
                            : Mathf.LerpUnclamped(1.16f, 0.78f, fadeT);
                        view.Root.localScale = Vector3.one * scale;
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

            if (step.CreatedSpecials.Count > 0)
            {
                yield return AnimateCreatedSpecials(step.CreatedSpecials);
            }

        }

        private IEnumerator AnimateCreatedSpecials(IEnumerable<(BoardPosition Position, SpecialType Type)> createdSpecials)
        {
            var targets = createdSpecials
                .Select(s => s.Position)
                .Distinct()
                .Where(pos => _cells.TryGetValue(pos, out var cell) && cell.Icon.enabled)
                .Select(pos => _cells[pos].Icon.rectTransform)
                .ToList();

            if (targets.Count == 0) yield break;

            const float duration = 0.12f;
            var elapsed = 0f;

            foreach (var target in targets)
            {
                target.localScale = Vector3.one * 0.6f;
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var glow = Mathf.Sin(t * Mathf.PI) * 0.35f;

                foreach (var target in targets)
                {
                    var image = target.GetComponent<Image>();
                    target.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, t);
                    image.color = new Color(1f, 1f, 1f, 1f - glow * 0.25f);
                }

                yield return null;
            }

            foreach (var target in targets)
            {
                var image = target.GetComponent<Image>();
                target.localScale = Vector3.one;
                image.color = Color.white;
            }
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

        private void SetInputEnabled(bool enabled)
        {
            _isInputBlocked = !enabled;
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

                view.Button.interactable = !_isAnimating && !_isInputBlocked && cell.IsPlayable;
            }
        }

        private void UpdateStatusAfterEvaluation()
        {
            var state = _gameStateController.EvaluateAfterMove();
            ShowOverlay(state);

            switch (state)
            {
                case GameState.Won:
                    _status.text = "You win!";
                    SetInputEnabled(false);
                    break;
                case GameState.Lost:
                    _status.text = "You lose! Out of moves.";
                    SetInputEnabled(false);
                    break;
                default:
                    _status.text = "Make a move.";
                    break;
            }
        }

        private void RefreshHud()
        {
            if (_goalHudView == null) return;
            _goalHudView.UpdateGoals(_goalTracker, _spriteLibrary);
            _goalHudView.UpdateMoves(_moveCounter);
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

            var missingKey = GetExpectedSpriteKey(tile);
            if (sprite == null && !string.IsNullOrEmpty(missingKey))
            {
                LogMissingSpriteOnce(missingKey);
            }

            return new TileVisual(sprite, placeholder, label);
        }

        private string GetExpectedSpriteKey(TileEntitySnapshot tile)
        {
            return tile.Kind switch
            {
                TileKind.Piece => tile.ColorId switch
                {
                    1 => "frog",
                    2 => "cat",
                    3 => "whaler",
                    4 => "capybara",
                    _ => null
                },
                TileKind.Rock => "rock",
                TileKind.Boulder => "boulder",
                TileKind.Special => tile.SpecialType switch
                {
                    SpecialType.RocketHorizontal => "line",
                    SpecialType.RocketVertical => "line",
                    SpecialType.Bomb => "bomb",
                    SpecialType.SuperLightning => "lightning",
                    _ => null
                },
                _ => null
            };
        }

        private void LogMissingSpriteOnce(string key)
        {
            if (!_loggedMissingSpriteKeys.Add(key)) return;
            Debug.LogWarning($"Missing sprite for key='{key}' (expected in Assets/Tiles). Using placeholder.");
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

        private Rect GetBoardRectOnAnimationLayer()
        {
            var gridRect = _grid.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            gridRect.GetWorldCorners(corners);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (var i = 0; i < corners.Length; i++)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_animationLayer, screen, null, out var local);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void ConfigureBoardLayout()
        {
            Canvas.ForceUpdateCanvases();

            var cols = Mathf.Max(1, _board.Width);
            var rows = Mathf.Max(1, _board.Height);
            var spacingX = _grid.spacing.x;
            var spacingY = _grid.spacing.y;

            var availableWidth = Mathf.Max(1f, _boardContainer.rect.width * BoardWidthUsage);
            var availableHeight = Mathf.Max(1f, _boardContainer.rect.height * BoardHeightUsage);

            var cellFromWidth = Mathf.Floor((availableWidth - spacingX * (cols - 1)) / cols);
            var cellFromHeight = Mathf.Floor((availableHeight - spacingY * (rows - 1)) / rows);
            var cellSize = Mathf.Max(1f, Mathf.Min(cellFromWidth, cellFromHeight));

            _grid.cellSize = new Vector2(cellSize, cellSize);

            var gridWidth = cellSize * cols + spacingX * (cols - 1);
            var gridHeight = cellSize * rows + spacingY * (rows - 1);

            var rt = _grid.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(gridWidth, gridHeight);
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
            icon.useSpriteMesh = true;
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = new Vector2(IconInset, IconInset);
            icon.rectTransform.offsetMax = new Vector2(-IconInset, -IconInset);

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
            public readonly Color BaseBackgroundColor;
            public readonly Color BaseIconColor;

            public TransientTile(RectTransform root, Image background, Image icon)
            {
                Root = root;
                Background = background;
                Icon = icon;
                BaseBackgroundColor = background.color;
                BaseIconColor = icon.color;
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
