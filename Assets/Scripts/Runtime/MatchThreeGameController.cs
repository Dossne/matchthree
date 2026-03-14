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
        private const int MaxGoalFlyersPerStep = 8;
        private const int MaxGoalFlyersPerTargetPerStep = 6;
        private const int ClearParticlePoolSize = 16;
        private const int ClearUiShardPoolSize = 64;
        private const int MaxClearParticlesPerStep = 12;

        // UI layout tuning (portrait)
        private const float HudHeight = 220f;
        private const float BottomPadding = 110f;
        private const float BoardWidthUsage = 0.90f;
        private const float BoardHeightUsage = 0.96f;

        [SerializeField, Range(-12f, 10f)] private float iconInset = -6f;

        [Header("Clear Particles")]
        [SerializeField] private Material clearParticleMaterial;
        [SerializeField] private bool enableClearParticles = true;
        [SerializeField, Range(4, 40)] private int clearParticleCount = 16;
        [SerializeField, Range(0.15f, 0.6f)] private float clearParticleLifetime = 0.35f;
        [SerializeField, Range(0.1f, 3f)] private float clearParticleSpeed = 0.75f;
        [SerializeField, Range(0.05f, 1f)] private float clearParticleScale = 0.22f;

        [Header("Clear Shards (UI)")]
        [SerializeField] private Sprite[] clearShardSprites;
        [SerializeField] private bool useUiShards = true;

        [Header("Board Background")]
        [SerializeField] private Sprite boardGridSprite;

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
        private Image _boardGridBackgroundImage;
        private RectTransform _animationLayer;

        private Text _status;
        private GoalHudView _goalHudView;
        private int _lastHudMovesRemaining = -1;
        private int _lastHudGoalHash;

        private MatchThree.Core.MoveCounter _moveCounter;
        private GoalTracker _goalTracker;
        private GameStateController _gameStateController;
        private MatchFxPlayer _fxPlayer;

        private GameObject _winPanel;
        private GameObject _losePanel;
        private CanvasGroup _winOverlayBackgroundGroup;
        private CanvasGroup _winOverlayCardGroup;
        private RectTransform _winOverlayCardRect;
        private RectTransform _winOverlayTitleRect;
        private RectTransform _winOverlayIconRect;
        private Coroutine _winOverlayAnimationRoutine;
        private int _currentLevelIndex;
        private List<RuntimeLevelData> _levels = new();

        private readonly Dictionary<BoardPosition, CellView> _cells = new();
        private readonly HashSet<string> _loggedMissingSpriteKeys = new();

        private BoardPosition? _selected;
        private bool _isAnimating;
        private bool _isInputBlocked;

        private Camera _uiCamera;
        private Transform _clearParticleRoot;
        private readonly Queue<PooledClearParticle> _clearParticlePool = new();
        private readonly List<PooledClearParticle> _activeClearParticles = new();
        private readonly Queue<PooledUiShard> _clearUiShardPool = new();
        private readonly List<ActiveUiShard> _activeUiShards = new();
        private Sprite _defaultUiShardSprite;

        private sealed class CellView
        {
            public RectTransform Root;
            public Button Button;
            public Image Background;
            public Image Icon;
            public Text Label;
            public CanvasGroup Group;
        }

        private sealed class PooledClearParticle
        {
            public GameObject Root;
            public ParticleSystem System;
        }

        private sealed class PooledUiShard
        {
            public RectTransform Root;
            public Image Image;
            public RawImage RawImage;
            public CanvasGroup Group;
        }

        private struct ActiveUiShard
        {
            public PooledUiShard Shard;
            public Vector2 Start;
            public Vector2 End;
            public float Duration;
            public float Elapsed;
            public float StartRotation;
            public float RotationSpeed;
            public float BaseSize;
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
            TickClearParticlePool();
            TickClearUiShardPool();
            RefreshHudIfDirty();

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

            _uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? Camera.main : canvas.worldCamera;

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

            var boardGridBackgroundGo = FindOrCreateUiObject(_boardContainer, "BoardGridBackground");
            boardGridBackgroundGo.transform.SetParent(_boardContainer, false);
            _boardGridBackgroundImage = EnsureComponent<Image>(boardGridBackgroundGo);
            if (boardGridSprite != null)
            {
                _boardGridBackgroundImage.sprite = boardGridSprite;
            }

            _boardGridBackgroundImage.type = Image.Type.Simple;
            _boardGridBackgroundImage.preserveAspect = false;
            _boardGridBackgroundImage.raycastTarget = false;
            _boardGridBackgroundImage.rectTransform.localScale = Vector3.one;
            _boardGridBackgroundImage.rectTransform.localRotation = Quaternion.identity;
            _boardGridBackgroundImage.rectTransform.SetAsFirstSibling();

            // Animation layer
            var animationGo = FindOrCreateUiObject(canvas.transform, "AnimationLayer");
            _animationLayer = EnsureComponent<RectTransform>(animationGo);
            _animationLayer.anchorMin = Vector2.zero;
            _animationLayer.anchorMax = Vector2.one;
            _animationLayer.offsetMin = Vector2.zero;
            _animationLayer.offsetMax = Vector2.zero;

            _fxPlayer = new MatchFxPlayer(this, _animationLayer, GetBoardRectOnAnimationLayer, CellPosition, () => _grid.cellSize);
            EnsureClearParticlePool();
            EnsureClearUiShardPool();

            // Overlays
            _winPanel = BuildWinOverlayPanel(canvas.transform, "WinPanel", LoadNextLevel);
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
            ResetHudCache();
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
            if (state == GameState.Won)
            {
                ShowWinOverlayAnimated();
            }
            else if (_winPanel != null)
            {
                if (_winOverlayAnimationRoutine != null)
                {
                    StopCoroutine(_winOverlayAnimationRoutine);
                    _winOverlayAnimationRoutine = null;
                }

                _winPanel.SetActive(false);
            }

            if (_losePanel != null) _losePanel.SetActive(state == GameState.Lost);
        }

        private void ShowWinOverlayAnimated()
        {
            if (_winPanel == null) return;

            _winPanel.SetActive(true);

            if (_winOverlayAnimationRoutine != null)
            {
                StopCoroutine(_winOverlayAnimationRoutine);
            }

            _winOverlayAnimationRoutine = StartCoroutine(AnimateOverlayIn());
        }

        private IEnumerator AnimateOverlayIn()
        {
            if (_winOverlayBackgroundGroup == null || _winOverlayCardGroup == null || _winOverlayCardRect == null)
            {
                yield break;
            }

            const float fadeDuration = 0.20f;
            const float cardAppearDuration = 0.28f;
            const float settleDuration = 0.14f;
            const float iconPunchDuration = 0.18f;

            _winOverlayBackgroundGroup.alpha = 0f;
            _winOverlayCardGroup.alpha = 0f;
            _winOverlayCardRect.localScale = new Vector3(0.80f, 0.80f, 1f);
            if (_winOverlayTitleRect != null) _winOverlayTitleRect.localScale = Vector3.one;
            if (_winOverlayIconRect != null) _winOverlayIconRect.localScale = Vector3.one * 0.9f;

            var elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _winOverlayBackgroundGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
                yield return null;
            }

            _winOverlayBackgroundGroup.alpha = 1f;

            elapsed = 0f;
            while (elapsed < cardAppearDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / cardAppearDuration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                _winOverlayCardGroup.alpha = eased;
                var scale = Mathf.Lerp(0.80f, 1.05f, eased);
                _winOverlayCardRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < settleDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / settleDuration);
                var scale = Mathf.Lerp(1.05f, 1f, t);
                _winOverlayCardRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            _winOverlayCardRect.localScale = Vector3.one;
            _winOverlayCardGroup.alpha = 1f;

            if (_winOverlayTitleRect != null && _winOverlayIconRect != null)
            {
                elapsed = 0f;
                while (elapsed < iconPunchDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / iconPunchDuration);
                    var punch = 1f + Mathf.Sin(t * Mathf.PI) * 0.08f;
                    _winOverlayIconRect.localScale = Vector3.one * punch;
                    _winOverlayTitleRect.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.03f);
                    yield return null;
                }

                _winOverlayIconRect.localScale = Vector3.one;
                _winOverlayTitleRect.localScale = Vector3.one;
            }

            _winOverlayAnimationRoutine = null;
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

        private GameObject BuildWinOverlayPanel(
            Transform canvas,
            string panelName,
            UnityEngine.Events.UnityAction onClick)
        {
            var panelGo = FindOrCreateUiObject(canvas, panelName);
            var panelRect = EnsureComponent<RectTransform>(panelGo);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var blockInputGroup = EnsureComponent<CanvasGroup>(panelGo);
            blockInputGroup.interactable = true;
            blockInputGroup.blocksRaycasts = true;

            var backgroundGo = FindOrCreateUiObject(panelGo.transform, "Background");
            var backgroundImage = EnsureComponent<Image>(backgroundGo);
            backgroundImage.color = new Color(0f, 0f, 0f, 0.78f);
            backgroundImage.raycastTarget = true;

            var backgroundRect = backgroundImage.rectTransform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            _winOverlayBackgroundGroup = EnsureComponent<CanvasGroup>(backgroundGo);

            var cardGo = FindOrCreateUiObject(panelGo.transform, "RewardCard");
            var cardRect = EnsureComponent<RectTransform>(cardGo);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(700f, 780f);
            cardRect.anchoredPosition = new Vector2(0f, 60f);

            var cardImage = EnsureComponent<Image>(cardGo);
            cardImage.color = new Color(0.10f, 0.12f, 0.20f, 0.96f);
            _winOverlayCardGroup = EnsureComponent<CanvasGroup>(cardGo);

            var glowGo = FindOrCreateUiObject(cardGo.transform, "Glow");
            var glowImage = EnsureComponent<Image>(glowGo);
            glowImage.color = new Color(1f, 0.87f, 0.22f, 0.20f);
            var glowRect = glowImage.rectTransform;
            glowRect.anchorMin = new Vector2(0.5f, 1f);
            glowRect.anchorMax = new Vector2(0.5f, 1f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.sizeDelta = new Vector2(360f, 180f);
            glowRect.anchoredPosition = new Vector2(0f, -90f);

            var iconGo = FindOrCreateUiObject(cardGo.transform, "RewardIcon");
            var iconText = EnsureComponent<Text>(iconGo);
            iconText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            iconText.text = "★";
            iconText.alignment = TextAnchor.MiddleCenter;
            iconText.color = new Color(1f, 0.86f, 0.2f, 1f);
            iconText.fontSize = 120;

            var iconRect = iconText.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(200f, 120f);
            iconRect.anchoredPosition = new Vector2(0f, -120f);

            var titleGo = FindOrCreateUiObject(cardGo.transform, "Title");
            var titleText = EnsureComponent<Text>(titleGo);
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.text = "Level Complete!";
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.fontSize = 76;

            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(620f, 140f);
            titleRect.anchoredPosition = new Vector2(0f, -250f);

            var subtitleGo = FindOrCreateUiObject(cardGo.transform, "Subtitle");
            var subtitleText = EnsureComponent<Text>(subtitleGo);
            subtitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            subtitleText.text = "Great match! Ready for the next challenge?";
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.color = new Color(0.86f, 0.90f, 1f, 0.98f);
            subtitleText.fontSize = 38;

            var subtitleRect = subtitleText.rectTransform;
            subtitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            subtitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            subtitleRect.pivot = new Vector2(0.5f, 0.5f);
            subtitleRect.sizeDelta = new Vector2(620f, 150f);
            subtitleRect.anchoredPosition = new Vector2(0f, -30f);

            var buttonGo = FindOrCreateUiObject(cardGo.transform, "ActionButton");
            var buttonImage = EnsureComponent<Image>(buttonGo);
            buttonImage.color = new Color(1f, 0.77f, 0.18f, 1f);

            var button = EnsureComponent<Button>(buttonGo);
            button.targetGraphic = buttonImage;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(460f, 118f);
            buttonRect.anchoredPosition = new Vector2(0f, 100f);

            var buttonLabelGo = FindOrCreateUiObject(buttonGo.transform, "Label");
            var buttonLabel = EnsureComponent<Text>(buttonLabelGo);
            buttonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonLabel.text = "Next";
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            buttonLabel.color = new Color(0.18f, 0.11f, 0.02f, 1f);
            buttonLabel.fontSize = 52;

            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;

            _winOverlayCardRect = cardRect;
            _winOverlayTitleRect = titleRect;
            _winOverlayIconRect = iconRect;

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
                    ConfigureTileIcon(icon);

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
                _goalTracker.ApplyStepSummary(step.Summary);
                RefreshHud();

                var hasNextStep = i < result.Steps.Count - 1;
                if (step.DidChange && hasNextStep)
                {
                    yield return new WaitForSeconds(SettleDelaySeconds);
                }
            }

            _moveCounter.ConsumeIfAccepted(result);
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

                if (useUiShards)
                {
                    SpawnClearUiShards(removedViews);
                }
                else
                {
                    SpawnClearParticles(removedViews);
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

                if (_goalHudView != null && _goalTracker != null)
                {
                    yield return AnimateGoalFlyers(step, removedViews);
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

        private IEnumerator AnimateGoalFlyers(ResolveStep step, List<TransientTile> removedViews)
        {
            if (step.RemovedTiles.Count == 0 || removedViews.Count == 0)
            {
                yield break;
            }

            var flyers = new List<(TransientTile View, Vector2 From, Vector2 To, float Duration, float Delay)>();
            var perGoalCounts = new Dictionary<int, int>();

            for (var i = 0; i < step.RemovedTiles.Count && flyers.Count < MaxGoalFlyersPerStep; i++)
            {
                var removed = step.RemovedTiles[i];
                if (removed.Tile.Kind != TileKind.Piece)
                {
                    continue;
                }

                var colorId = removed.Tile.ColorId;
                if (!HasActiveCollectColorGoal(colorId))
                {
                    continue;
                }

                var existingCount = perGoalCounts.TryGetValue(colorId, out var countForGoal) ? countForGoal : 0;
                if (existingCount >= MaxGoalFlyersPerTargetPerStep)
                {
                    continue;
                }

                if (!_goalHudView.TryGetGoalTargetRect(GoalType.CollectColor, colorId, out var targetRect) || targetRect == null)
                {
                    continue;
                }

                var view = removedViews[i];
                var from = view.Root.anchoredPosition;
                var to = GetRectCenterOnAnimationLayer(targetRect);
                var delay = flyers.Count * 0.03f;
                var duration = 0.32f + Mathf.Min(0.12f, Vector2.Distance(from, to) * 0.0002f);

                flyers.Add((view, from, to, duration, delay));
                perGoalCounts[colorId] = existingCount + 1;
            }

            if (flyers.Count == 0)
            {
                yield break;
            }

            var elapsed = 0f;
            var maxDuration = flyers.Max(f => f.Delay + f.Duration);

            while (elapsed < maxDuration)
            {
                elapsed += Time.deltaTime;

                foreach (var flyer in flyers)
                {
                    var localTime = Mathf.Clamp01((elapsed - flyer.Delay) / flyer.Duration);
                    if (localTime <= 0f)
                    {
                        continue;
                    }

                    var eased = 1f - Mathf.Pow(1f - localTime, 2f);
                    flyer.View.Root.anchoredPosition = Vector2.LerpUnclamped(flyer.From, flyer.To, eased);

                    var scale = Mathf.LerpUnclamped(1f, 0.3f, localTime);
                    flyer.View.Root.localScale = Vector3.one * scale;

                    var alpha = 1f - localTime;
                    flyer.View.Background.color = SetAlpha(flyer.View.BaseBackgroundColor, alpha);
                    flyer.View.Icon.color = SetAlpha(flyer.View.BaseIconColor, alpha);
                }

                yield return null;
            }

            foreach (var flyer in flyers)
            {
                flyer.View.Root.anchoredPosition = flyer.To;
                flyer.View.Root.localScale = Vector3.one * 0.3f;
                flyer.View.Background.color = SetAlpha(flyer.View.BaseBackgroundColor, 0f);
                flyer.View.Icon.color = SetAlpha(flyer.View.BaseIconColor, 0f);
            }

            foreach (var colorId in perGoalCounts.Keys)
            {
                if (_goalHudView.TryGetGoalTargetRect(GoalType.CollectColor, colorId, out var targetRect) && targetRect != null)
                {
                    StartCoroutine(AnimateGoalTargetPunch(targetRect));
                }
            }
        }

        private bool HasActiveCollectColorGoal(int colorId)
        {
            if (_goalTracker == null)
            {
                return false;
            }

            return _goalTracker.GetProgress()
                .OfType<CollectColorProgress>()
                .Any(goal => goal.ColorId == colorId && goal.Current < goal.Target);
        }

        private IEnumerator AnimateGoalTargetPunch(RectTransform target)
        {
            if (target == null)
            {
                yield break;
            }

            const float duration = 0.16f;
            var elapsed = 0f;
            var baseScale = target.localScale;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var bump = 1f + Mathf.Sin(t * Mathf.PI) * 0.18f;
                target.localScale = baseScale * bump;
                yield return null;
            }

            target.localScale = baseScale;
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
            CacheHudState();
        }

        private void RefreshHudIfDirty()
        {
            if (_goalHudView == null || _moveCounter == null || _goalTracker == null)
            {
                return;
            }

            if (_lastHudMovesRemaining != _moveCounter.Remaining || _lastHudGoalHash != ComputeGoalProgressHash())
            {
                RefreshHud();
            }
        }

        private void ResetHudCache()
        {
            _lastHudMovesRemaining = -1;
            _lastHudGoalHash = int.MinValue;
        }

        private void CacheHudState()
        {
            _lastHudMovesRemaining = _moveCounter != null ? _moveCounter.Remaining : -1;
            _lastHudGoalHash = ComputeGoalProgressHash();
        }

        private int ComputeGoalProgressHash()
        {
            if (_goalTracker == null)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                foreach (var progress in _goalTracker.GetProgress())
                {
                    switch (progress)
                    {
                        case CollectColorProgress collect:
                            hash = (hash * 31) + collect.ColorId;
                            hash = (hash * 31) + collect.Current;
                            hash = (hash * 31) + collect.Target;
                            break;
                        case ClearAllRocksProgress rocks:
                            hash = (hash * 31) + rocks.RemainingRocks;
                            break;
                        default:
                            hash = (hash * 31) + (int)progress.GoalType;
                            break;
                    }
                }

                return hash;
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

        private Vector2 GetRectCenterOnAnimationLayer(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var worldCenter = (corners[0] + corners[2]) * 0.5f;
            var screen = RectTransformUtility.WorldToScreenPoint(null, worldCenter);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_animationLayer, screen, null, out var localPoint);
            return localPoint;
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

            var gridRt = _grid.GetComponent<RectTransform>();
            gridRt.sizeDelta = new Vector2(gridWidth, gridHeight);

            if (_boardGridBackgroundImage != null)
            {
                var bgRt = _boardGridBackgroundImage.rectTransform;
                bgRt.anchorMin = gridRt.anchorMin;
                bgRt.anchorMax = gridRt.anchorMax;
                bgRt.pivot = gridRt.pivot;
                bgRt.anchoredPosition = gridRt.anchoredPosition;
                bgRt.sizeDelta = gridRt.sizeDelta;
                bgRt.localScale = Vector3.one;
                bgRt.localRotation = Quaternion.identity;
            }
        }

        private void ConfigureTileIcon(Image icon)
        {
            icon.useSpriteMesh = true;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.type = Image.Type.Simple;

            var rt = icon.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = new Vector2(iconInset, iconInset);
            rt.offsetMax = new Vector2(-iconInset, -iconInset);
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
            ConfigureTileIcon(icon);

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

        private void EnsureClearParticlePool()
        {
            if (_clearParticleRoot != null)
            {
                return;
            }

            var rootGo = new GameObject("ClearParticlePool");
            _clearParticleRoot = rootGo.transform;

            for (var i = 0; i < ClearParticlePoolSize; i++)
            {
                _clearParticlePool.Enqueue(CreateClearParticleSystem(i));
            }
        }

        private void EnsureClearUiShardPool()
        {
            if (_animationLayer == null || _clearUiShardPool.Count + _activeUiShards.Count > 0)
            {
                return;
            }

            if (_defaultUiShardSprite == null)
            {
                _defaultUiShardSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            }

            for (var i = 0; i < ClearUiShardPoolSize; i++)
            {
                _clearUiShardPool.Enqueue(CreateClearUiShard(i));
            }
        }

        private PooledUiShard CreateClearUiShard(int index)
        {
            var go = new GameObject($"ClearUiShard_{index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(_animationLayer, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var imageGo = new GameObject("Image");
            imageGo.transform.SetParent(go.transform, false);
            var imageRect = imageGo.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;
            imageRect.sizeDelta = Vector2.zero;

            var image = imageGo.AddComponent<Image>();
            image.raycastTarget = false;

            var rawImageGo = new GameObject("RawImage");
            rawImageGo.transform.SetParent(go.transform, false);
            var rawImageRect = rawImageGo.AddComponent<RectTransform>();
            rawImageRect.anchorMin = Vector2.zero;
            rawImageRect.anchorMax = Vector2.one;
            rawImageRect.pivot = new Vector2(0.5f, 0.5f);
            rawImageRect.anchoredPosition = Vector2.zero;
            rawImageRect.sizeDelta = Vector2.zero;

            var rawImage = rawImageGo.AddComponent<RawImage>();
            rawImage.raycastTarget = false;

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            go.SetActive(false);

            return new PooledUiShard
            {
                Root = rect,
                Image = image,
                RawImage = rawImage,
                Group = group
            };
        }

        private static Rect CalculateSpriteUvRect(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || sprite.texture.width <= 0 || sprite.texture.height <= 0)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            var textureRect = sprite.textureRect;
            var texture = sprite.texture;
            return new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
        }

        private static Rect SliceUvRect(Rect sourceUvRect, int columns, int rows, int column, int row)
        {
            var fragmentWidth = sourceUvRect.width / Mathf.Max(1, columns);
            var fragmentHeight = sourceUvRect.height / Mathf.Max(1, rows);
            return new Rect(
                sourceUvRect.x + (fragmentWidth * column),
                sourceUvRect.y + (fragmentHeight * row),
                fragmentWidth,
                fragmentHeight);
        }

        private static void ConfigureShardAsFallbackSprite(PooledUiShard shard, Sprite sprite, Color color)
        {
            shard.Image.enabled = true;
            shard.Image.sprite = sprite;
            shard.Image.color = color;

            shard.RawImage.enabled = false;
            shard.RawImage.texture = null;
            shard.RawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        private static void ConfigureShardAsRawImageFragment(PooledUiShard shard, Sprite sourceSprite, Rect uvRect, Color color)
        {
            shard.RawImage.enabled = true;
            shard.RawImage.texture = sourceSprite.texture;
            shard.RawImage.uvRect = uvRect;
            shard.RawImage.color = color;

            shard.Image.enabled = false;
            shard.Image.sprite = null;
        }

        private PooledClearParticle CreateClearParticleSystem(int index)
        {
            var go = new GameObject($"ClearParticle_{index}");
            go.transform.SetParent(_clearParticleRoot, false);
            go.SetActive(false);

            var ps = go.AddComponent<ParticleSystem>();
            ConfigureClearParticleSystem(ps);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 30;
            if (clearParticleMaterial != null)
            {
                renderer.sharedMaterial = clearParticleMaterial;
            }

            return new PooledClearParticle
            {
                Root = go,
                System = ps
            };
        }

        private void ConfigureClearParticleSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = clearParticleLifetime;
            main.startLifetime = clearParticleLifetime;
            main.startSpeed = clearParticleSpeed;
            main.startSize = clearParticleScale;
            main.maxParticles = Mathf.Max(8, clearParticleCount + 8);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.92f),
                new Color(0.75f, 0.75f, 0.75f, 0.78f));

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(clearParticleCount, 1, 200)) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = clearParticleScale * 0.45f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var colorGradient = new Gradient();
            colorGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.82f, 0.82f, 0.82f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.6f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = colorGradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.65f, 1f, 1.2f));

            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
            velocityOverLifetime.orbitalZ = 0f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private void SpawnClearParticles(List<TransientTile> removedViews)
        {
            if (!enableClearParticles || removedViews == null || removedViews.Count == 0)
            {
                return;
            }

            EnsureClearParticlePool();
            var maxCount = Mathf.Min(MaxClearParticlesPerStep, removedViews.Count);
            for (var i = 0; i < maxCount; i++)
            {
                if (_clearParticlePool.Count == 0)
                {
                    break;
                }

                var world = TryGetParticleWorldPosition(removedViews[i].Root.position, out var particleWorldPos)
                    ? particleWorldPos
                    : (Vector3?)null;
                if (!world.HasValue)
                {
                    continue;
                }

                var pooled = _clearParticlePool.Dequeue();
                pooled.Root.SetActive(true);
                pooled.Root.transform.position = world.Value;
                ConfigureClearParticleSystem(pooled.System);
                pooled.System.Clear(true);
                pooled.System.Play(true);
                _activeClearParticles.Add(pooled);
            }
        }

        private void SpawnClearUiShards(List<TransientTile> removedViews)
        {
            if (!enableClearParticles || removedViews == null || removedViews.Count == 0)
            {
                return;
            }

            EnsureClearUiShardPool();
            if (_clearUiShardPool.Count == 0)
            {
                return;
            }

            var maxTiles = Mathf.Min(MaxClearParticlesPerStep, removedViews.Count);
            var hasCustomSprites = clearShardSprites != null && clearShardSprites.Length > 0;
            var cellSize = Mathf.Min(_grid.cellSize.x, _grid.cellSize.y);

            for (var i = 0; i < maxTiles; i++)
            {
                var origin = removedViews[i].Root.anchoredPosition;
                var sourceSprite = removedViews[i].Icon != null && removedViews[i].Icon.enabled ? removedViews[i].Icon.sprite : null;
                var sourceColor = removedViews[i].Icon != null ? removedViews[i].Icon.color : Color.white;
                var useSpriteFragments = sourceSprite != null;

                var columns = 2;
                var rows = useSpriteFragments && Random.value > 0.5f ? 3 : 2;
                var shardsPerTile = useSpriteFragments ? (columns * rows) : Random.Range(3, 7);
                var sourceUvRect = useSpriteFragments ? CalculateSpriteUvRect(sourceSprite) : default;

                for (var shardIndex = 0; shardIndex < shardsPerTile; shardIndex++)
                {
                    if (_clearUiShardPool.Count == 0)
                    {
                        return;
                    }

                    var shard = _clearUiShardPool.Dequeue();
                    var travelDistance = clearParticleSpeed * Random.Range(36f, 82f);
                    var baseSize = clearParticleScale * cellSize * Random.Range(0.25f, 0.45f);

                    Vector2 localOffset;
                    Vector2 dir;
                    if (useSpriteFragments)
                    {
                        var column = shardIndex % columns;
                        var row = shardIndex / columns;
                        var centerFromOrigin = new Vector2(
                            ((column + 0.5f) / columns) - 0.5f,
                            ((row + 0.5f) / rows) - 0.5f);

                        localOffset = centerFromOrigin * (cellSize * 0.16f);
                        dir = (centerFromOrigin + (Random.insideUnitCircle * 0.22f)).normalized;
                        if (dir.sqrMagnitude < 0.001f)
                        {
                            dir = Vector2.up;
                        }

                        baseSize *= Random.Range(0.95f, 1.2f);
                        ConfigureShardAsRawImageFragment(
                            shard,
                            sourceSprite,
                            SliceUvRect(sourceUvRect, columns, rows, column, row),
                            sourceColor);
                    }
                    else
                    {
                        dir = Random.insideUnitCircle;
                        if (dir.sqrMagnitude < 0.001f)
                        {
                            dir = Vector2.up;
                        }

                        localOffset = Random.insideUnitCircle * (cellSize * 0.04f);
                        ConfigureShardAsFallbackSprite(
                            shard,
                            hasCustomSprites ? clearShardSprites[Random.Range(0, clearShardSprites.Length)] : _defaultUiShardSprite,
                            Color.white);
                    }

                    shard.Root.anchoredPosition = origin + localOffset;
                    shard.Root.sizeDelta = Vector2.one * baseSize;
                    shard.Root.localScale = Vector3.one;
                    shard.Root.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));
                    shard.Group.alpha = 1f;
                    shard.Root.gameObject.SetActive(true);

                    _activeUiShards.Add(new ActiveUiShard
                    {
                        Shard = shard,
                        Start = origin + localOffset,
                        End = origin + localOffset + (dir.normalized * travelDistance),
                        Duration = clearParticleLifetime,
                        Elapsed = 0f,
                        StartRotation = shard.Root.localEulerAngles.z,
                        RotationSpeed = Random.Range(-360f, 360f),
                        BaseSize = baseSize
                    });
                }
            }
        }

        private bool TryGetParticleWorldPosition(Vector3 sourceWorldUiPosition, out Vector3 worldPosition)
        {
            worldPosition = default;

            var screenPoint = RectTransformUtility.WorldToScreenPoint(null, sourceWorldUiPosition);
            var cameraForConversion = _uiCamera != null ? _uiCamera : Camera.main;
            if (cameraForConversion == null)
            {
                return false;
            }

            var distance = Mathf.Max(0.3f, cameraForConversion.nearClipPlane + 0.6f);
            worldPosition = cameraForConversion.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, distance));
            return true;
        }

        private void TickClearParticlePool()
        {
            if (_activeClearParticles.Count == 0)
            {
                return;
            }

            for (var i = _activeClearParticles.Count - 1; i >= 0; i--)
            {
                var particle = _activeClearParticles[i];
                if (particle.System != null && particle.System.IsAlive(true))
                {
                    continue;
                }

                particle.System?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Root?.SetActive(false);
                _activeClearParticles.RemoveAt(i);
                _clearParticlePool.Enqueue(particle);
            }
        }

        private void TickClearUiShardPool()
        {
            if (_activeUiShards.Count == 0)
            {
                return;
            }

            for (var i = _activeUiShards.Count - 1; i >= 0; i--)
            {
                var active = _activeUiShards[i];
                active.Elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(active.Elapsed / Mathf.Max(0.01f, active.Duration));
                var eased = 1f - Mathf.Pow(1f - t, 2f);

                active.Shard.Root.anchoredPosition = Vector2.LerpUnclamped(active.Start, active.End, eased);
                active.Shard.Root.localEulerAngles = new Vector3(0f, 0f, active.StartRotation + (active.RotationSpeed * t));

                var scale = Mathf.LerpUnclamped(1f, 0.6f, t);
                active.Shard.Root.sizeDelta = Vector2.one * (active.BaseSize * scale);
                active.Shard.Group.alpha = 1f - t;

                if (t >= 1f)
                {
                    active.Shard.Group.alpha = 0f;
                    active.Shard.Root.gameObject.SetActive(false);
                    _clearUiShardPool.Enqueue(active.Shard);
                    _activeUiShards.RemoveAt(i);
                    continue;
                }

                _activeUiShards[i] = active;
            }
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
