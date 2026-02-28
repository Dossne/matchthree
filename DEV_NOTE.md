# Match-3 Prototype Notes

## 9x9 starter level
- The default level path is `Resources/Levels/level_000` and points to `Assets/Resources/Levels/level_000.txt`.
- `level_000.txt` is 9 rows x 9 columns of `.` so all cells are playable.
- Startup now calls `BoardResolver.FillBoardWithoutInitialMatches()` to populate all empty playable cells.

## No initial matches rule
- Initial fill and refill both use the same rule in `BoardResolver`:
  - when placing tile `(x, y)`, reject any color that would create `XXX` with left/left-left or up/up-up neighbors.
- If every color is blocked (edge case with very low color variety), fallback picks from the full color set.

## Auto sprite discovery (`Assets/Tiles`)
- Runtime uses `TileSpriteLibrary.LoadFromTilesFolder()`.
- It maps required filenames to game roles:
  - `frog, cat, whaler, capybara` -> normal colors 1..4
  - `rock, boulder` -> obstacles
  - `line, bomb, lightning` -> boosters
- In Editor play mode, sprites are loaded from `Assets/Tiles` via `AssetDatabase` when `Resources/Tiles` is empty.
- Missing assets now emit a clear `Debug.LogError` and the board falls back to placeholder squares for only missing sprites.

## Tile layering and placeholder behavior
- Each cell now renders with two layers:
  - foreground icon (`Image`) for real sprite assets
  - optional placeholder background (`Image`) + text for debug fallback
- Rule: when a sprite exists, placeholder background is disabled so only the icon is visible.

## Animation timing and cascade readability
- Input is blocked while animation coroutines are running.
- Swap animation uses `0.10s`.
- Clear animation uses a brief `0.08s` fade/scale pop.
- Falls/spawns animate per distance:
  - `duration = clamp(distance * 0.06s, 0.08s, 0.35s)`
- Each resolve step adds a small `0.04s` settle delay, so longer cascades naturally take longer.
- Core resolver now exposes per-step removed tile snapshots, movements, and spawn metadata to keep view animation data-driven and independent from gameplay logic.

## Match debugging helper
- Press `M` in play mode to log current detected match groups and coordinates.

## Move limit and acceptance rule
- Runtime now tracks a move limit with `LevelRuntimeConfig.MaxMoves` (default `20`).
- `MatchThreeGameController` creates `MoveCounter` at level start and displays `Moves: {Remaining}/{MaxMoves}` in the HUD.
- Move consumption rule: **consume exactly one move only for accepted swaps**.
  - Accepted = swap is performed and not reverted.
  - This includes either a normal match created by the swap or a booster activation from swapping specials.
  - Rejected swaps (no match and no special activation) are reverted and do not consume a move.
## Move limit UI counter
- Runtime has a move budget configured in `MatchThreeGameController` via `maxMoves`.
- A `MoveCounter` tracks remaining turns and updates an on-screen `Moves` text label.
- Valid (non-reverted) player moves consume one move; when the budget reaches zero, input is blocked.

## Goal system (A1)
- Core now supports two goal types:
  - `CollectColorGoalDefinition(colorId, targetCount)`
  - `ClearAllRocksGoalDefinition()`
- Runtime goals now come from `Assets/Resources/Levels/level_registry.json` per level entry.
- Goal progress is tracked by `GoalTracker`, initialized from board state and updated after each resolved player move.

### Goal update events
- Resolver emits `ResolveStepSummary` per resolve step with:
  - `ClearedPiecesByColor`
  - `DestroyedObstaclesByType`
- `CollectColor` increments from `ClearedPiecesByColor` for matches and special-effect clears alike (all piece removals are counted).
- `ClearAllRocks` decrements from destroyed rock/boulder events.

### Rock/Boulder counting rule
- Clear-rock goal completion rule is: **zero Rock and zero Boulder entities remain on the board**.
- Initialization count treats each `Rock` and `Boulder` tile as 1 remaining obstacle.
- Boulder damage (`Boulder -> Rock`) does not decrement the goal yet because an obstacle still remains.

## A4 win/lose overlays and level flow
- Runtime loads level order from `Assets/Resources/Levels/level_registry.json`.
- `currentLevelIndex` is tracked at runtime.
  - **Retry** reloads the current index and reinitializes board, moves, and goals.
  - **Next** increments index and wraps to the first level when it passes the end.
- Two Canvas overlays were added in runtime-created UI:
  - `WinPanel` with `You Win!` + `Next` button.
  - `LosePanel` with `You Lose!` + `Retry` button.
- Overlays are shown only when game state enters `Won`/`Lost`; they are hidden after level initialization.

## Portrait board sizing + HUD layout
- Runtime UI now forces `CanvasScaler` to:
  - `Scale With Screen Size`
  - reference resolution `1080x1920`
  - screen match mode `Match Width Or Height` with match `0.5`
- A top-level `Root` rect is split into:
  - `HUD` (top band, default `220` units high)
  - `BoardContainer` (center play area between HUD and bottom padding)
- `BuildGrid()` no longer uses hardcoded tile size. Cell size is computed dynamically from available board space:
  - `cellFromWidth = floor((availableWidth - spacing*(cols-1)) / cols)`
  - `cellFromHeight = floor((availableHeight - spacing*(rows-1)) / rows)`
  - `cellSize = min(cellFromWidth, cellFromHeight)`
- Tuning knobs in `MatchThreeGameController`:
  - `BoardWidthUsage` (default `0.90`) controls how much board-container width the grid targets.
  - `HudHeight` (default `220`) controls readable top HUD space.
  - `BottomPadding` (default `110`) reserves lower safe area.
- Icon insets for both board cells and transient animation tiles are set to `0` and `Image.useSpriteMesh = true` so artwork fills each tile cell.

## Tile sprite import fixer
- Use **Tools → MatchThree → Fix Tile Import Settings** to normalize all textures in `Assets/Tiles` in one click.
- Current enforced defaults:
  - `Texture Type = Sprite (2D and UI)`
  - `Sprite Mode = Single`
  - `Pixels Per Unit = 100`
  - `Max Size >= 512`
  - `Compression = None`
  - `Filter Mode = Bilinear` (chosen for smooth/cartoon art)
  - `Mip Maps = Disabled`
- For larger/smaller on-board icons, tweak `IconInset` in `MatchThreeGameController` (0 to 2 is the intended range).

## Level registry + per-level config (A5)
- Runtime level order and per-level rules now come from `Assets/Resources/Levels/level_registry.json`.
- Each entry defines:
  - `levelPath` (e.g. `Levels/level_001`)
  - `maxMoves`
  - `goals[]` with:
    - `{ "type": "CollectColor", "colorId": <id>, "target": <count> }`
    - `{ "type": "ClearAllRocks" }`
- `MatchThreeGameController` loads registry entries, then for each level loads the matching ASCII board `TextAsset` from `Resources`.
- Retry/Next behavior:
  - **Retry** reloads the current level index.
  - **Next** advances index and wraps to `0` after the last level.
  - If `levelAsset` override is set in the inspector, that override is inserted as index `0` and registry levels follow it.

### How to add a new level
1. Add a new ASCII file under `Assets/Resources/Levels/`, for example `level_003.txt`.
2. Add a new entry in `Assets/Resources/Levels/level_registry.json` with:
   - `"levelPath": "Levels/level_003"`
   - desired `maxMoves`
   - goal list.
3. Keep goals achievable:
   - include `ClearAllRocks` only if the board has `R`/`B` tiles.
   - keep collect targets realistic for the board size + move limit.

## HUD v2 (goal icons + big moves)
- Text goals list was replaced with a runtime `GoalHudView` (`Assets/Scripts/Runtime/GoalHudView.cs`).
- Goal icon mapping now uses `TileSpriteLibrary` directly:
  - `CollectColorProgress` -> `GetNormalSprite(colorId)`
  - `ClearAllRocksProgress` -> `GetObstacleSprite(ObstacleSpriteType.Rock)`
- If a goal sprite is missing, HUD falls back to a dark placeholder tile with a short label (`C{id}` / `Rock`) and still shows the counter.
- Moves panel now renders a large remaining value (`62` font size by default) and a smaller `/Max` line below.

### HUD tuning knobs
- `GoalHudView` sizing and readability tweaks:
  - Moves text sizes: `Value` (default `62`), `Max` (default `24`), label (default `30`).
  - Goal item footprint and icon size: `GoalItem` (`130x120`) and `IconRoot` (`70x70`).
- `MatchThreeGameController.EnsureUi()` controls panel anchoring/splits:
  - `GoalsPanel` occupies left side of HUD.
  - `MovesPanel` occupies right side of HUD.
