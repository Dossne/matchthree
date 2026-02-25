using MatchThree.Core;
using NUnit.Framework;

namespace MatchThree.Tests
{
    public sealed class BoardTests
    {
        private sealed class StubRandom : IRandom
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        [Test]
        public void Parser_ParsesLegendAndBoundaries()
        {
            var board = LevelParser.Parse("##\nS1\nBR\n", new[] {1,2,3,4,5});
            Assert.That(board.Width, Is.EqualTo(2));
            Assert.That(board.Height, Is.EqualTo(3));
            Assert.That(board.Cells[0,0].IsPlayable, Is.False);
            Assert.That(board.Cells[0,1].Tile.Kind, Is.EqualTo(TileKind.Statuette));
            Assert.That(board.Cells[1,1].Tile.ColorId, Is.EqualTo(1));
            Assert.That(board.Cells[0,2].Tile.Kind, Is.EqualTo(TileKind.Boulder));
            Assert.That(board.Cells[1,2].Tile.Kind, Is.EqualTo(TileKind.Rock));
        }

        [Test]
        public void Resolver_DetectsAndResolvesHorizontalMatch()
        {
            var board = LevelParser.Parse("111\n...\n...\n", new[] {1});
            var resolver = new BoardResolver(board, new StubRandom());
            var result = resolver.TrySwapAndResolve(new Move(new BoardPosition(0,0), new BoardPosition(1,0)));
            Assert.That(result.Performed, Is.True);
            Assert.That(result.Reverted, Is.False);
            Assert.That(result.Steps.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ObstacleDamage_BoulderTurnsRockThenDestroyed()
        {
            var board = LevelParser.Parse("111\n.B.\n...\n", new[] {1});
            var resolver = new BoardResolver(board, new StubRandom());
            resolver.TrySwapAndResolve(new Move(new BoardPosition(0,0), new BoardPosition(1,0)));
            Assert.That(board.Cells[1,1].Tile.Kind, Is.EqualTo(TileKind.Rock));

            board.Cells[0,0].Tile = TileEntity.Piece(1);
            board.Cells[1,0].Tile = TileEntity.Piece(1);
            board.Cells[2,0].Tile = TileEntity.Piece(1);
            resolver.TrySwapAndResolve(new Move(new BoardPosition(0,0), new BoardPosition(1,0)));
            Assert.That(board.Cells[1,1].Tile, Is.Null);
        }

        [Test]
        public void Gravity_RespectsIrregularBoardShape()
        {
            var board = LevelParser.Parse("#.#\n#1#\n#.#\n", new[] {1,2});
            var resolver = new BoardResolver(board, new StubRandom());
            board.Cells[1,1].Tile = null;
            resolver.TrySwapAndResolve(new Move(new BoardPosition(1,0), new BoardPosition(1,1)));
            Assert.That(board.Cells[1,2].Tile, Is.Not.Null);
            Assert.That(board.Cells[0,2].IsPlayable, Is.False);
        }

        [Test]
        public void Specials_CreatedFor4And5AndLShape()
        {
            var boardRocket = LevelParser.Parse("1112\n....\n....\n....\n", new[] {1,2,3});
            var resolverRocket = new BoardResolver(boardRocket, new StubRandom());
            var rocket = resolverRocket.TrySwapAndResolve(new Move(new BoardPosition(2,0), new BoardPosition(3,0)));
            Assert.That(boardRocket.Cells[3,0].Tile.Kind, Is.EqualTo(TileKind.Special));

            var boardLightning = LevelParser.Parse("11112\n.....\n.....\n", new[] {1,2});
            var resolverLightning = new BoardResolver(boardLightning, new StubRandom());
            resolverLightning.TrySwapAndResolve(new Move(new BoardPosition(3,0), new BoardPosition(4,0)));
            Assert.That(boardLightning.Cells[4,0].Tile.SpecialType, Is.EqualTo(SpecialType.SuperLightning));

            var boardBomb = LevelParser.Parse("11..\n1...\n12..\n....\n", new[] {1,2});
            boardBomb.Cells[1,1].Tile = TileEntity.Piece(1);
            var resolverBomb = new BoardResolver(boardBomb, new StubRandom());
            resolverBomb.TrySwapAndResolve(new Move(new BoardPosition(1,2), new BoardPosition(1,1)));
            Assert.That(boardBomb.Cells[1,1].Tile.Kind, Is.EqualTo(TileKind.Special));
            Assert.That(boardBomb.Cells[1,1].Tile.SpecialType, Is.EqualTo(SpecialType.Bomb));
        }
    }
}
