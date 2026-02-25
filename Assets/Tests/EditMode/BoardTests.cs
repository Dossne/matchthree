using System.Collections.Generic;
using MatchThree.Core;
using NUnit.Framework;

namespace MatchThree.Tests
{
    public sealed class BoardTests
    {
        private sealed class SequenceRandom : IRandom
        {
            private readonly Queue<int> _values;

            public SequenceRandom(IEnumerable<int> values)
            {
                _values = new Queue<int>(values);
            }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (_values.Count == 0) return minInclusive;
                var value = _values.Dequeue();
                return minInclusive + (value % (maxExclusive - minInclusive));
            }
        }

        private static Board BuildPlayableBoard(string ascii)
        {
            var board = LevelParser.Parse(ascii, new[] { 1, 2, 3, 4 });
            return board;
        }

        [Test]
        public void HorizontalMatch_IsDetectedAndRemoved()
        {
            var board = BuildPlayableBoard("1234\n1234\n1234\n1234\n");
            board.Cells[0, 0].Tile = TileEntity.Piece(1);
            board.Cells[1, 0].Tile = TileEntity.Piece(1);
            board.Cells[2, 0].Tile = TileEntity.Piece(1);

            var resolver = new BoardResolver(board, new SequenceRandom(new[] { 1, 2, 3, 0 }));
            var result = resolver.TrySwapAndResolve(new Move(new BoardPosition(3, 0), new BoardPosition(3, 1)));

            Assert.That(result.Performed, Is.True);
            Assert.That(result.Reverted, Is.False);
            Assert.That(result.Steps.Count, Is.GreaterThan(0));
            Assert.That(result.Steps[0].Removed, Has.Member(new BoardPosition(0, 0)));
            Assert.That(result.Steps[0].Removed, Has.Member(new BoardPosition(1, 0)));
            Assert.That(result.Steps[0].Removed, Has.Member(new BoardPosition(2, 0)));
        }

        [Test]
        public void VerticalMatch_IsDetectedAndRemoved()
        {
            var board = BuildPlayableBoard("1234\n1234\n1234\n1234\n");
            board.Cells[0, 0].Tile = TileEntity.Piece(2);
            board.Cells[0, 1].Tile = TileEntity.Piece(2);
            board.Cells[0, 2].Tile = TileEntity.Piece(2);

            var resolver = new BoardResolver(board, new SequenceRandom(new[] { 0, 1, 2, 3 }));
            var result = resolver.TrySwapAndResolve(new Move(new BoardPosition(3, 2), new BoardPosition(3, 3)));

            Assert.That(result.Performed, Is.True);
            Assert.That(result.Reverted, Is.False);
            Assert.That(result.Steps[0].Removed, Has.Member(new BoardPosition(0, 0)));
            Assert.That(result.Steps[0].Removed, Has.Member(new BoardPosition(0, 1)));
            Assert.That(result.Steps[0].Removed, Has.Member(new BoardPosition(0, 2)));
        }

        [Test]
        public void SwapValidation_AcceptsMatchAndRevertsNonMatch()
        {
            var goodBoard = LevelParser.Parse("121\n314\n234\n", new[] { 1, 2, 3, 4 });
            var resolver = new BoardResolver(goodBoard, new SequenceRandom(new[] { 0, 1, 2, 3 }));
            var accepted = resolver.TrySwapAndResolve(new Move(new BoardPosition(1, 0), new BoardPosition(1, 1)));
            Assert.That(accepted.Reverted, Is.False);

            var badBoard = LevelParser.Parse("123\n231\n312\n", new[] { 1, 2, 3, 4 });
            var badResolver = new BoardResolver(badBoard, new SequenceRandom(new[] { 0, 1, 2, 3 }));
            var fromBefore = badBoard.Cells[0, 0].Tile.ColorId;
            var toBefore = badBoard.Cells[1, 0].Tile.ColorId;
            var rejected = badResolver.TrySwapAndResolve(new Move(new BoardPosition(0, 0), new BoardPosition(1, 0)));

            Assert.That(rejected.Performed, Is.True);
            Assert.That(rejected.Reverted, Is.True);
            Assert.That(badBoard.Cells[0, 0].Tile.ColorId, Is.EqualTo(fromBefore));
            Assert.That(badBoard.Cells[1, 0].Tile.ColorId, Is.EqualTo(toBefore));
        }

        [Test]
        public void InitialFill_ProducesNoImmediateMatches()
        {
            var board = LevelParser.Parse(".....\n.....\n.....\n.....\n.....\n", new[] { 1, 2, 3, 4 });
            var resolver = new BoardResolver(board, new SequenceRandom(new[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 }));

            resolver.FillBoardWithoutInitialMatches();

            Assert.That(resolver.GetCurrentMatchGroups().Count, Is.EqualTo(0));
        }
    }
}
