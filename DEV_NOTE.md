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
- Missing assets throw a clear exception listing missing file roles.

## Match debugging helper
- Press `M` in play mode to log current detected match groups and coordinates.
