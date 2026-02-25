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
        [SerializeField] private TextAsset levelAsset;
        [SerializeField] private string levelResourcePath = "Levels/level_000";
        [SerializeField] private int randomSeed = 1234;

        private Board _board;
        private BoardResolver _resolver;
        private TileSpriteLibrary _spriteLibrary;
        private GridLayoutGroup _grid;
        private Text _status;
        private readonly Dictionary<BoardPosition, Button> _buttons = new();
        private BoardPosition? _selected;

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

            try
            {
                _spriteLibrary = TileSpriteLibrary.LoadFromTilesFolder();
            }
            catch (System.Exception ex)
            {
                _status.text = ex.Message;
            }

            BuildGrid();
            Render();
        }

        private void Update()
        {
            if (_resolver == null) return;
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
        }

        private void BuildGrid()
        {
            foreach (Transform child in _grid.transform) Destroy(child.gameObject);
            _buttons.Clear();
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
                go.transform.SetParent(_grid.transform, false);
                var image = go.AddComponent<Image>();
                var button = go.AddComponent<Button>();
                button.onClick.AddListener(() => OnCellClicked(pos));

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(go.transform, false);
                var txt = labelGo.AddComponent<Text>();
                txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.black;
                var lrt = txt.rectTransform;
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
                _buttons[pos] = button;
            }
        }

        private void OnCellClicked(BoardPosition pos)
        {
            if (_selected == null) { _selected = pos; Render(); return; }
            if (_selected.Value == pos) { _selected = null; Render(); return; }
            var result = _resolver.TrySwapAndResolve(new Move(_selected.Value, pos));
            _selected = null;
            _status.text = result.Reverted ? "Invalid move: no match." : $"Resolved steps: {result.Steps.Count}. Delivered: {_resolver.AreAllStatuettesDelivered()}";
            Render();
        }

        private void Render()
        {
            foreach (var kvp in _buttons)
            {
                var p = kvp.Key;
                var button = kvp.Value;
                var cell = _board.Cells[p.X, p.Y];
                var image = button.GetComponent<Image>();
                var text = button.GetComponentInChildren<Text>();
                image.sprite = null;

                button.interactable = cell.IsPlayable;
                if (!cell.IsPlayable)
                {
                    image.color = Color.black;
                    text.text = "#";
                }
                else if (cell.Tile == null)
                {
                    image.color = new Color(0.2f, 0.2f, 0.2f);
                    text.text = ".";
                }
                else
                {
                    var tile = cell.Tile;
                    switch (tile.Kind)
                    {
                        case TileKind.Piece:
                            image.sprite = _spriteLibrary?.GetNormalSprite(tile.ColorId);
                            image.color = image.sprite != null ? Color.white : ColorFor(tile.ColorId);
                            text.text = image.sprite != null ? string.Empty : tile.ColorId.ToString();
                            break;
                        case TileKind.Rock:
                            image.sprite = _spriteLibrary?.GetObstacleSprite(ObstacleSpriteType.Rock);
                            image.color = image.sprite != null ? Color.white : Color.gray;
                            text.text = image.sprite != null ? string.Empty : "R";
                            break;
                        case TileKind.Boulder:
                            image.sprite = _spriteLibrary?.GetObstacleSprite(ObstacleSpriteType.Boulder);
                            image.color = image.sprite != null ? Color.white : new Color(0.35f, 0.25f, 0.2f);
                            text.text = image.sprite != null ? string.Empty : "B";
                            break;
                        case TileKind.Statuette:
                            image.color = Color.yellow;
                            text.text = "S";
                            break;
                        case TileKind.Special:
                            image.sprite = tile.SpecialType switch
                            {
                                SpecialType.RocketHorizontal => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.Rocket),
                                SpecialType.RocketVertical => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.Rocket),
                                SpecialType.Bomb => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.Bomb),
                                SpecialType.SuperLightning => _spriteLibrary?.GetBoosterSprite(BoosterSpriteType.SuperLightning),
                                _ => null
                            };
                            image.color = image.sprite != null ? Color.white : new Color(1f, 0.6f, 0.1f);
                            text.text = image.sprite == null ? tile.SpecialType switch
                            {
                                SpecialType.RocketHorizontal => "RH",
                                SpecialType.RocketVertical => "RV",
                                SpecialType.Bomb => "BO",
                                SpecialType.SuperLightning => "SL",
                                _ => "?"
                            } : string.Empty;
                            break;
                    }
                }

                if (_selected.HasValue && _selected.Value == p) image.color = Color.white;
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
