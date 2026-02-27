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
- Runtime currently configures goals in `MatchThreeGameController.BuildGoalDefinitions()` (hardcoded until level goal config exists).
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
- Runtime now owns a simple ordered level path list in `MatchThreeGameController.levelResourcePaths`.
  - Default order: `Levels/level_000`, `Levels/level_001`, `Levels/level_002`.
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
  - `BoardWidthUsage` (default `0.78`) controls how much board-container width the grid targets.
  - `HudHeight` (default `220`) controls readable top HUD space.
  - `BottomPadding` (default `110`) reserves lower safe area.
- Icon insets for both board cells and transient animation tiles were reduced from `6` to `2` to make sprites fill cells more.
