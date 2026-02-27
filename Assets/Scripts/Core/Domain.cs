using System;
using System.Collections.Generic;
using System.Linq;

namespace MatchThree.Core
{
    public enum TileKind { Piece, Rock, Boulder, Statuette, Special }
    public enum SpecialType { None, RocketHorizontal, RocketVertical, Bomb, SuperLightning }
    public enum GoalType { CollectColor, ClearAllRocks }

    public readonly struct BoardPosition : IEquatable<BoardPosition>
    {
        public readonly int X;
        public readonly int Y;
        public BoardPosition(int x, int y) { X = x; Y = y; }
        public bool Equals(BoardPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is BoardPosition other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(BoardPosition a, BoardPosition b) => a.Equals(b);
        public static bool operator !=(BoardPosition a, BoardPosition b) => !a.Equals(b);
    }

    public readonly struct Move
    {
        public readonly BoardPosition From;
        public readonly BoardPosition To;
        public Move(BoardPosition from, BoardPosition to) { From = from; To = to; }
    }

    public sealed class TileEntity
    {
        public TileKind Kind;
        public int ColorId;
        public SpecialType SpecialType;

        public static TileEntity Piece(int colorId) => new TileEntity { Kind = TileKind.Piece, ColorId = colorId };
        public static TileEntity Rock() => new TileEntity { Kind = TileKind.Rock };
        public static TileEntity Boulder() => new TileEntity { Kind = TileKind.Boulder };
        public static TileEntity Statuette() => new TileEntity { Kind = TileKind.Statuette };
        public static TileEntity Special(SpecialType type) => new TileEntity { Kind = TileKind.Special, SpecialType = type };

        public TileEntity Clone() => new TileEntity { Kind = Kind, ColorId = ColorId, SpecialType = SpecialType };
        public bool IsColoredPiece => Kind == TileKind.Piece;
        public bool IsSwappable => Kind == TileKind.Piece || Kind == TileKind.Special;
        public bool IsMovable => Kind == TileKind.Piece || Kind == TileKind.Special || Kind == TileKind.Statuette;
    }

    public sealed class Cell
    {
        public bool IsPlayable;
        public TileEntity Tile;
    }

    public interface IRandom
    {
        int NextInt(int minInclusive, int maxExclusive);
    }

    public sealed class SeededRandom : IRandom
    {
        private readonly Random _random;
        public SeededRandom(int seed) { _random = new Random(seed); }
        public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
    }

    public sealed class Board
    {
        private static readonly BoardPosition[] OrthoDirs =
        {
            new BoardPosition(1,0), new BoardPosition(-1,0), new BoardPosition(0,1), new BoardPosition(0,-1)
        };

        public int Width { get; }
        public int Height { get; }
        public Cell[,] Cells { get; }
        public IReadOnlyList<int> AvailableColors { get; }

        public Board(int width, int height, IReadOnlyList<int> availableColors)
        {
            Width = width;
            Height = height;
            AvailableColors = availableColors;
            Cells = new Cell[width, height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                Cells[x, y] = new Cell();
        }

        public bool InBounds(BoardPosition p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        public Cell CellAt(BoardPosition p) => Cells[p.X, p.Y];
        public bool IsOrthAdjacent(BoardPosition a, BoardPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

        public IEnumerable<BoardPosition> OrthogonalNeighbors(BoardPosition p)
        {
            foreach (var d in OrthoDirs)
            {
                var n = new BoardPosition(p.X + d.X, p.Y + d.Y);
                if (InBounds(n)) yield return n;
            }
        }

        public void Swap(BoardPosition a, BoardPosition b)
        {
            var tmp = CellAt(a).Tile;
            CellAt(a).Tile = CellAt(b).Tile;
            CellAt(b).Tile = tmp;
        }

        public bool IsBottomMostReachable(BoardPosition p)
        {
            if (!CellAt(p).IsPlayable) return false;
            for (var y = p.Y + 1; y < Height; y++)
            {
                if (Cells[p.X, y].IsPlayable) return false;
            }
            return true;
        }
    }

    public sealed class ResolveStep
    {
        public readonly List<BoardPosition> Removed = new();
        public readonly List<(BoardPosition Position, TileEntitySnapshot Tile)> RemovedTiles = new();
        public readonly List<TileMovement> Movements = new();
        public readonly List<TileSpawn> Spawns = new();
        public readonly List<BoardPosition> DamagedObstacles = new();
        public readonly List<BoardPosition> DestroyedObstacles = new();
        public readonly List<DestroyedObstacleInfo> DestroyedObstacleDetails = new();
        public readonly List<(BoardPosition Position, SpecialType Type)> CreatedSpecials = new();
        public readonly ResolveStepSummary Summary = new();
        public bool DidChange => Removed.Count > 0 || DamagedObstacles.Count > 0 || DestroyedObstacles.Count > 0 || CreatedSpecials.Count > 0 || Movements.Count > 0 || Spawns.Count > 0;
    }

    public readonly struct DestroyedObstacleInfo
    {
        public readonly BoardPosition Position;
        public readonly TileKind Kind;

        public DestroyedObstacleInfo(BoardPosition position, TileKind kind)
        {
            Position = position;
            Kind = kind;
        }
    }

    public sealed class ResolveStepSummary
    {
        public readonly Dictionary<int, int> ClearedPiecesByColor = new();
        public readonly Dictionary<TileKind, int> DestroyedObstaclesByType = new();
    }

    public readonly struct TileEntitySnapshot
    {
        public readonly TileKind Kind;
        public readonly int ColorId;
        public readonly SpecialType SpecialType;

        public TileEntitySnapshot(TileKind kind, int colorId, SpecialType specialType)
        {
            Kind = kind;
            ColorId = colorId;
            SpecialType = specialType;
        }

        public static TileEntitySnapshot From(TileEntity tile)
            => new TileEntitySnapshot(tile.Kind, tile.ColorId, tile.SpecialType);
    }

    public readonly struct TileMovement
    {
        public readonly BoardPosition From;
        public readonly BoardPosition To;
        public readonly TileEntitySnapshot Tile;

        public TileMovement(BoardPosition from, BoardPosition to, TileEntitySnapshot tile)
        {
            From = from;
            To = to;
            Tile = tile;
        }
    }

    public readonly struct TileSpawn
    {
        public readonly BoardPosition To;
        public readonly int SpawnDistance;
        public readonly TileEntitySnapshot Tile;

        public TileSpawn(BoardPosition to, int spawnDistance, TileEntitySnapshot tile)
        {
            To = to;
            SpawnDistance = spawnDistance;
            Tile = tile;
        }
    }

    public sealed class MoveResult
    {
        public bool Performed;
        public bool Reverted;
        public TileEntitySnapshot? SwapFromTile;
        public TileEntitySnapshot? SwapToTile;
        public readonly List<ResolveStep> Steps = new();
    }

    public abstract class GoalDefinition
    {
        public abstract GoalType GoalType { get; }
    }

    public sealed class CollectColorGoalDefinition : GoalDefinition
    {
        public override GoalType GoalType => GoalType.CollectColor;
        public int ColorId { get; }
        public int TargetCount { get; }

        public CollectColorGoalDefinition(int colorId, int targetCount)
        {
            ColorId = colorId;
            TargetCount = targetCount;
        }
    }

    public sealed class ClearAllRocksGoalDefinition : GoalDefinition
    {
        public override GoalType GoalType => GoalType.ClearAllRocks;
    }

    public abstract class GoalProgress
    {
        public GoalType GoalType { get; protected set; }
        public bool IsComplete { get; protected set; }
    }

    public sealed class CollectColorProgress : GoalProgress
    {
        public int ColorId { get; }
        public int Current { get; private set; }
        public int Target { get; }

        public CollectColorProgress(int colorId, int target)
        {
            GoalType = GoalType.CollectColor;
            ColorId = colorId;
            Target = target;
            Current = 0;
            IsComplete = target <= 0;
        }

        public void Increment(int amount)
        {
            if (amount <= 0 || IsComplete) return;
            Current = Math.Min(Target, Current + amount);
            IsComplete = Current >= Target;
        }
    }

    public sealed class ClearAllRocksProgress : GoalProgress
    {
        public int RemainingRocks { get; private set; }

        public ClearAllRocksProgress()
        {
            GoalType = GoalType.ClearAllRocks;
        }

        public void SetRemaining(int remaining)
        {
            RemainingRocks = Math.Max(0, remaining);
            IsComplete = RemainingRocks == 0;
        }

        public void Decrement(int amount)
        {
            if (amount <= 0 || IsComplete) return;
            SetRemaining(RemainingRocks - amount);
        }
    }

    public sealed class GoalTracker
    {
        private readonly List<GoalProgress> _progress = new();

        public GoalTracker(IEnumerable<GoalDefinition> definitions)
        {
            foreach (var definition in definitions)
            {
                switch (definition)
                {
                    case CollectColorGoalDefinition collect:
                        _progress.Add(new CollectColorProgress(collect.ColorId, collect.TargetCount));
                        break;
                    case ClearAllRocksGoalDefinition:
                        _progress.Add(new ClearAllRocksProgress());
                        break;
                }
            }
        }

        public void Initialize(Board board)
        {
            var rockCount = 0;
            for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
            {
                var tile = board.Cells[x, y].Tile;
                if (tile == null) continue;
                if (tile.Kind == TileKind.Rock || tile.Kind == TileKind.Boulder) rockCount++;
            }

            foreach (var goal in _progress.OfType<ClearAllRocksProgress>())
            {
                goal.SetRemaining(rockCount);
            }
        }

        public void ApplyStepSummary(ResolveStepSummary summary)
        {
            foreach (var goal in _progress)
            {
                switch (goal)
                {
                    case CollectColorProgress collect:
                        if (summary.ClearedPiecesByColor.TryGetValue(collect.ColorId, out var count))
                        {
                            collect.Increment(count);
                        }
                        break;
                    case ClearAllRocksProgress rocks:
                    {
                        var destroyed = 0;
                        if (summary.DestroyedObstaclesByType.TryGetValue(TileKind.Rock, out var rockDestroyed))
                        {
                            destroyed += rockDestroyed;
                        }
                        if (summary.DestroyedObstaclesByType.TryGetValue(TileKind.Boulder, out var boulderDestroyed))
                        {
                            destroyed += boulderDestroyed;
                        }
                        rocks.Decrement(destroyed);
                        break;
                    }
                }
            }
        }

        public void ApplyMoveResult(MoveResult result)
        {
            foreach (var step in result.Steps)
            {
                ApplyStepSummary(step.Summary);
            }
        }

        public IReadOnlyList<GoalProgress> GetProgress() => _progress;
        public bool AllComplete => _progress.Count > 0 && _progress.All(g => g.IsComplete);
    }
}
