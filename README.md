# Match-3 Prototype (Portrait)

A lightweight match-3 prototype designed for **vertical (portrait) orientation**.

This README documents:
- the **ASCII level file format**
- the **board boundaries**
- core **match rules**
- **special pieces** (Rocket, Bomb, Super Lightning)
- obstacles & objectives (Rock, Boulder, Statuette)

---

## Board & Orientation

- The game runs in **portrait** orientation.
- The board is a **grid** parsed from an ASCII text file.
- Rows in the file are read **top → bottom**.
- Columns in each row are read **left → right**.

### Board boundaries (playable vs non-playable)
To define irregular board shapes, the level file supports “outside” cells.

- `#` = **outside / wall** (non-playable; no pieces spawn here)
- `.` = **empty playable cell** (a piece may spawn/fall into this cell)

> Anything other than the defined symbols should be treated as invalid input (or parsed as `#`, depending on your parser policy).

---

## Level Files (ASCII)

Each level is a plain text file, e.g. `Levels/level_001.txt`.

### Tile legend

#### Colored pieces
- `1` = color 1
- `2` = color 2
- `3` = color 3
- `4` = color 4

#### Obstacles / objectives
- `R` = **Rock**  
  - Takes damage from **any adjacent match** (orthogonal neighbors recommended: up/down/left/right).
  - Destroyed when its HP reaches 0 (suggested: 1 hit).

- `B` = **Boulder** (Rock level 2)  
  - Takes damage from **any adjacent match**.
  - On first hit, **turns into `R`** (Rock).
  - Next hit destroys it (i.e., 2 total hits if Rock is 1 HP).

- `S` = **Statuette (carry-down objective)**  
  - Must be **moved to the bottom** of the board.
  - The statuette occupies a cell and should fall with gravity like other items (unless you decide otherwise).
  - Level is complete when **all `S` items reach the bottom row** (or bottom-most reachable cell).

---

## Example Level

```text
#########
##..1..##
##.2R3.##
##.2B3.##
##..S..##
##.444.##
##..5..##
#########

# defines the non-playable boundary.

. are empty playable cells.

Numbers spawn normal pieces.

R, B, S are placed directly from the file.

Core Match Rules
3-in-a-row (basic match)

Match 3+ same-colored pieces (orthogonally aligned).

Matched pieces are removed.

Gravity applies; board refills from the top.

Damage from matches (adjacent damage)

When a match resolves, it deals 1 damage to destructibles adjacent to any matched cell:

R takes 1 damage and is destroyed (if 1 HP).

B takes 1 damage and turns into R.

(Adjacency policy can be defined as orthogonal only; diagonals are optional but should be consistent.)

Special Pieces
Rocket (Line Clear)

Created by: a 4-in-a-row match in a straight line:

4 horizontal → creates a horizontal Rocket

4 vertical → creates a vertical Rocket

Effect: when activated, Rocket:

clears its entire row (horizontal) or column (vertical)

deals damage to obstacles hit by the blast (e.g., R, B)

then disappears

Activation methods (any of the following are valid):

by swapping it (as part of a move)

by tapping it

by taking damage (e.g., being hit by another special’s blast)

Choose one or support multiple activation inputs; document it in code/UI.

Bomb (Area Clear)

Created by: matching 5 same-colored pieces in a non-linear connected shape:

L-shape (and all rotations/mirrors)

T-shape (and rotations)

Plus / Cross shape (only via refill/cascade; not directly by a player swap)

Illustrative shapes (X = same color):

L-shape examples

XXX      XXX
X          X
X          X

XXX
 X
 X

 X
XXX
 X

Effect (suggested):

clears a local area around itself (e.g., 3×3 or radius-based)

damages obstacles in the affected area

then disappears

Activation: same as Rocket (swap / tap / damage), unless specified otherwise.

Super Lightning (Color Clear)

Created by: a 5-in-a-row straight match (horizontal or vertical).

Effect: when Super Lightning is matched with a colored piece:

removes all pieces of that matched color from the board

may also apply damage to obstacles adjacent to removed pieces (if your damage system supports it)

This acts like a “color bomb” / “color clearer”.

---

## Runtime level progression config

Level order, move limits, and goals are configured in:
- `Assets/Resources/Levels/level_registry.json`

Each level entry uses:
- `levelPath` → ASCII board at `Assets/Resources/<levelPath>.txt`
- `maxMoves`
- `goals` array (`CollectColor`, `ClearAllRocks`)

`Next` advances to the next registry level and loops back to level 0 after the last level.
`Retry` reloads the current level index.
