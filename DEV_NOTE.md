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
