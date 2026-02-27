using System;
using System.Collections.Generic;
using System.Linq;

namespace MatchThree.Core
{
    public sealed class BoardResolver
    {
        private readonly Board _board;
        private readonly IRandom _random;

        public BoardResolver(Board board, IRandom random)
        {
            _board = board;
            _random = random;
        }

        /// <summary>
        /// Fills every playable empty cell with a color that does not immediately create a 3+ line.
        /// Rule: when evaluating (x,y), reject any color that would complete XXX with left/left-left
        /// or up/up-up neighbors. If all colors are blocked we fall back to the full color list.
        /// </summary>
        public void FillBoardWithoutInitialMatches()
        {
            for (var y = 0; y < _board.Height; y++)
            for (var x = 0; x < _board.Width; x++)
            {
                var cell = _board.Cells[x, y];
                if (!cell.IsPlayable || cell.Tile != null) continue;

                var color = ChooseRefillColor(x, y);
                cell.Tile = TileEntity.Piece(color);
            }
        }

        /// <summary>
        /// Special creation placement convention: special is created on the move destination cell.
        /// </summary>
        public MoveResult TrySwapAndResolve(Move move)
        {
            var result = new MoveResult();
            if (!_board.InBounds(move.From) || !_board.InBounds(move.To)) return result;
            if (!_board.IsOrthAdjacent(move.From, move.To)) return result;

            var fromCell = _board.CellAt(move.From);
            var toCell = _board.CellAt(move.To);
            if (!fromCell.IsPlayable || !toCell.IsPlayable || fromCell.Tile == null || toCell.Tile == null) return result;
            if (!fromCell.Tile.IsSwappable || !toCell.Tile.IsSwappable) return result;

            result.SwapFromTile = TileEntitySnapshot.From(fromCell.Tile);
            result.SwapToTile = TileEntitySnapshot.From(toCell.Tile);

            _board.Swap(move.From, move.To);
            result.Performed = true;

            var specialStep = TryResolveSpecialSwap(move);
            var matchExists = FindMatches().Any();
            if (!matchExists && specialStep == null)
            {
                _board.Swap(move.From, move.To);
                result.Reverted = true;
                return result;
            }

            if (specialStep != null) result.Steps.Add(specialStep);
            ResolveCascades(result, move.To);
            return result;
        }

        public bool AreAllStatuettesDelivered()
        {
            for (var y = 0; y < _board.Height; y++)
            for (var x = 0; x < _board.Width; x++)
            {
                var p = new BoardPosition(x, y);
                var tile = _board.Cells[x, y].Tile;
                if (tile?.Kind == TileKind.Statuette && !_board.IsBottomMostReachable(p)) return false;
            }
            return true;
        }

        private void ResolveCascades(MoveResult result, BoardPosition specialPlacement)
        {
            while (true)
            {
                var matches = FindMatches();
                if (matches.Count == 0) break;

                var step = new ResolveStep();
                var removalSet = new HashSet<BoardPosition>(matches.SelectMany(m => m.Cells));
                var special = DetermineSpecial(matches, specialPlacement);
                if (special.HasValue && removalSet.Contains(special.Value.Position))
                {
                    removalSet.Remove(special.Value.Position);
                    step.CreatedSpecials.Add((special.Value.Position, special.Value.Type));
                }

                foreach (var p in removalSet)
                {
                    if (_board.CellAt(p).Tile != null)
                    {
                        var removedTile = TileEntitySnapshot.From(_board.CellAt(p).Tile);
                        step.RemovedTiles.Add((p, removedTile));
                        AddRemovedTileToSummary(step.Summary, removedTile);
                        _board.CellAt(p).Tile = null;
                        step.Removed.Add(p);
                    }
                }

                ApplyObstacleDamage(step.Removed, step);
                if (special.HasValue)
                {
                    _board.CellAt(special.Value.Position).Tile = TileEntity.Special(special.Value.Type);
                }

                ApplyGravity(step);
                Refill(step);
                result.Steps.Add(step);
            }
        }

        private ResolveStep TryResolveSpecialSwap(Move move)
        {
            var from = _board.CellAt(move.From).Tile;
            var to = _board.CellAt(move.To).Tile;
            if (from?.Kind != TileKind.Special && to?.Kind != TileKind.Special) return null;

            var activated = new HashSet<BoardPosition>();
            var removed = new HashSet<BoardPosition>();
            var queue = new Queue<BoardPosition>();

            void Activate(BoardPosition p)
            {
                if (!activated.Add(p)) return;
                queue.Enqueue(p);
            }

            if (from.Kind == TileKind.Special) Activate(move.From);
            if (to.Kind == TileKind.Special) Activate(move.To);

            while (queue.Count > 0)
            {
                var pos = queue.Dequeue();
                var tile = _board.CellAt(pos).Tile;
                if (tile?.Kind != TileKind.Special) continue;
                switch (tile.SpecialType)
                {
                    case SpecialType.RocketHorizontal:
                        for (var x = 0; x < _board.Width; x++) Mark(new BoardPosition(x, pos.Y));
                        break;
                    case SpecialType.RocketVertical:
                        for (var y = 0; y < _board.Height; y++) Mark(new BoardPosition(pos.X, y));
                        break;
                    case SpecialType.Bomb:
                        for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var p = new BoardPosition(pos.X + dx, pos.Y + dy);
                            if (_board.InBounds(p)) Mark(p);
                        }
                        break;
                    case SpecialType.SuperLightning:
                    {
                        var other = pos == move.From ? move.To : move.From;
                        var color = _board.CellAt(other).Tile?.ColorId ?? 0;
                        if (color == 0) break;
                        for (var y = 0; y < _board.Height; y++)
                        for (var x = 0; x < _board.Width; x++)
                        {
                            var p = new BoardPosition(x, y);
                            if (_board.CellAt(p).Tile?.Kind == TileKind.Piece && _board.CellAt(p).Tile.ColorId == color)
                            {
                                Mark(p);
                            }
                        }
                        Mark(pos);
                        break;
                    }
                }
            }

            void Mark(BoardPosition p)
            {
                var cell = _board.CellAt(p);
                if (!cell.IsPlayable || cell.Tile == null) return;
                removed.Add(p);
                if (cell.Tile.Kind == TileKind.Special) Activate(p);
            }

            if (removed.Count == 0) return null;

            var step = new ResolveStep();
            foreach (var p in removed)
            {
                if (_board.CellAt(p).Tile != null)
                {
                    var removedTile = TileEntitySnapshot.From(_board.CellAt(p).Tile);
                    step.RemovedTiles.Add((p, removedTile));
                    AddRemovedTileToSummary(step.Summary, removedTile);
                    _board.CellAt(p).Tile = null;
                    step.Removed.Add(p);
                }
            }

            ApplyObstacleDamage(step.Removed, step);
            ApplyGravity(step);
            Refill(step);
            return step;
        }

        private void ApplyObstacleDamage(IEnumerable<BoardPosition> removed, ResolveStep step)
        {
            var affected = new HashSet<BoardPosition>();
            foreach (var p in removed)
            {
                foreach (var n in _board.OrthogonalNeighbors(p))
                {
                    var t = _board.CellAt(n).Tile;
                    if (t == null) continue;
                    if (t.Kind == TileKind.Rock || t.Kind == TileKind.Boulder) affected.Add(n);
                }
            }

            foreach (var p in affected)
            {
                var t = _board.CellAt(p).Tile;
                if (t.Kind == TileKind.Boulder)
                {
                    _board.CellAt(p).Tile = TileEntity.Rock();
                    step.DamagedObstacles.Add(p);
                }
                else if (t.Kind == TileKind.Rock)
                {
                    var destroyedKind = t.Kind;
                    _board.CellAt(p).Tile = null;
                    step.DestroyedObstacles.Add(p);
                    step.DestroyedObstacleDetails.Add(new DestroyedObstacleInfo(p, destroyedKind));
                    AddDestroyedObstacleToSummary(step.Summary, destroyedKind);
                }
            }
        }

        private static void AddRemovedTileToSummary(ResolveStepSummary summary, TileEntitySnapshot tile)
        {
            if (tile.Kind != TileKind.Piece) return;
            summary.ClearedPiecesByColor.TryGetValue(tile.ColorId, out var currentCount);
            summary.ClearedPiecesByColor[tile.ColorId] = currentCount + 1;
        }

        private static void AddDestroyedObstacleToSummary(ResolveStepSummary summary, TileKind obstacleKind)
        {
            summary.DestroyedObstaclesByType.TryGetValue(obstacleKind, out var currentCount);
            summary.DestroyedObstaclesByType[obstacleKind] = currentCount + 1;
        }

        private void ApplyGravity(ResolveStep step)
        {
            for (var x = 0; x < _board.Width; x++)
            {
                for (var y = _board.Height - 1; y >= 0; y--)
                {
                    var cell = _board.Cells[x, y];
                    if (!cell.IsPlayable || cell.Tile != null) continue;

                    for (var searchY = y - 1; searchY >= 0; searchY--)
                    {
                        var above = _board.Cells[x, searchY];
                        if (!above.IsPlayable) continue;
                        if (above.Tile == null) continue;
                        if (!above.Tile.IsMovable) break;
                        var from = new BoardPosition(x, searchY);
                        var to = new BoardPosition(x, y);
                        step.Movements.Add(new TileMovement(from, to, TileEntitySnapshot.From(above.Tile)));
                        cell.Tile = above.Tile;
                        above.Tile = null;
                        break;
                    }
                }
            }
        }

        private void Refill(ResolveStep step)
        {
            for (var y = 0; y < _board.Height; y++)
            for (var x = 0; x < _board.Width; x++)
            {
                var c = _board.Cells[x, y];
                if (!c.IsPlayable || c.Tile != null) continue;
                var color = ChooseRefillColor(x, y);
                c.Tile = TileEntity.Piece(color);
                step.Spawns.Add(new TileSpawn(new BoardPosition(x, y), y + 1, TileEntitySnapshot.From(c.Tile)));
            }
        }

        private int ChooseRefillColor(int x, int y)
        {
            var valid = new List<int>(_board.AvailableColors.Count);
            foreach (var color in _board.AvailableColors)
            {
                if (!WouldCreateImmediateLine(x, y, color))
                {
                    valid.Add(color);
                }
            }

            var pool = valid.Count > 0 ? valid : _board.AvailableColors;
            return pool[_random.NextInt(0, pool.Count)];
        }

        private bool WouldCreateImmediateLine(int x, int y, int color)
        {
            return SamePieceColor(x - 1, y, color) && SamePieceColor(x - 2, y, color)
                   || SamePieceColor(x, y - 1, color) && SamePieceColor(x, y - 2, color);
        }

        private bool SamePieceColor(int x, int y, int color)
        {
            if (x < 0 || y < 0 || x >= _board.Width || y >= _board.Height) return false;
            var cell = _board.Cells[x, y];
            return cell.IsPlayable && cell.Tile?.Kind == TileKind.Piece && cell.Tile.ColorId == color;
        }

        private sealed class MatchRun
        {
            public int Color;
            public readonly List<BoardPosition> Cells = new();
            public bool IsHorizontal;
        }

        private List<MatchRun> FindMatches()
        {
            var runs = new List<MatchRun>();
            for (var y = 0; y < _board.Height; y++)
            {
                var x = 0;
                while (x < _board.Width)
                {
                    var tile = _board.Cells[x, y].Tile;
                    if (tile?.Kind != TileKind.Piece) { x++; continue; }
                    var start = x;
                    while (x + 1 < _board.Width && _board.Cells[x + 1, y].Tile?.Kind == TileKind.Piece && _board.Cells[x + 1, y].Tile.ColorId == tile.ColorId) x++;
                    var len = x - start + 1;
                    if (len >= 3)
                    {
                        var run = new MatchRun { Color = tile.ColorId, IsHorizontal = true };
                        for (var i = start; i <= x; i++) run.Cells.Add(new BoardPosition(i, y));
                        runs.Add(run);
                    }
                    x++;
                }
            }

            for (var x = 0; x < _board.Width; x++)
            {
                var y = 0;
                while (y < _board.Height)
                {
                    var tile = _board.Cells[x, y].Tile;
                    if (tile?.Kind != TileKind.Piece) { y++; continue; }
                    var start = y;
                    while (y + 1 < _board.Height && _board.Cells[x, y + 1].Tile?.Kind == TileKind.Piece && _board.Cells[x, y + 1].Tile.ColorId == tile.ColorId) y++;
                    var len = y - start + 1;
                    if (len >= 3)
                    {
                        var run = new MatchRun { Color = tile.ColorId, IsHorizontal = false };
                        for (var i = start; i <= y; i++) run.Cells.Add(new BoardPosition(x, i));
                        runs.Add(run);
                    }
                    y++;
                }
            }

            return runs;
        }

        public IReadOnlyList<IReadOnlyList<BoardPosition>> GetCurrentMatchGroups()
        {
            return FindMatches()
                .Select(run => (IReadOnlyList<BoardPosition>)run.Cells)
                .ToList();
        }

        private (BoardPosition Position, SpecialType Type)? DetermineSpecial(List<MatchRun> runs, BoardPosition placement)
        {
            var longRun = runs.FirstOrDefault(r => r.Cells.Count >= 5 && r.Cells.Contains(placement));
            if (longRun != null) return (placement, SpecialType.SuperLightning);

            var colorGroups = runs.GroupBy(r => r.Color);
            foreach (var group in colorGroups)
            {
                var cells = new HashSet<BoardPosition>(group.SelectMany(r => r.Cells));
                if (!cells.Contains(placement) || cells.Count < 5) continue;
                var rows = cells.Select(c => c.Y).Distinct().Count();
                var cols = cells.Select(c => c.X).Distinct().Count();
                if (rows > 1 && cols > 1) return (placement, SpecialType.Bomb);
            }

            var fourRun = runs.FirstOrDefault(r => r.Cells.Count == 4 && r.Cells.Contains(placement));
            if (fourRun != null)
            {
                return (placement, fourRun.IsHorizontal ? SpecialType.RocketHorizontal : SpecialType.RocketVertical);
            }

            return null;
        }
    }
}
