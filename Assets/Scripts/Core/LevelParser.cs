using System;
using System.Collections.Generic;
using System.Linq;

namespace MatchThree.Core
{
    public static class LevelParser
    {
        /// <summary>
        /// Unknown symbols are rejected with a clear exception to avoid silently malformed levels.
        /// </summary>
        public static Board Parse(string ascii, IReadOnlyList<int> availableColors = null)
        {
            availableColors ??= Enumerable.Range(1, 9).ToArray();
            var lines = ascii.Replace("\r", string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) throw new FormatException("Level is empty.");

            var width = lines[0].Length;
            if (width == 0) throw new FormatException("Level has empty first line.");

            foreach (var line in lines)
            {
                if (line.Length != width) throw new FormatException("All level lines must have equal width.");
            }

            var board = new Board(width, lines.Length, availableColors);
            var allowed = new HashSet<int>(availableColors);
            for (var y = 0; y < lines.Length; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = lines[y][x];
                    var cell = board.Cells[x, y];
                    switch (c)
                    {
                        case '#':
                            cell.IsPlayable = false;
                            break;
                        case '.':
                            cell.IsPlayable = true;
                            break;
                        case 'R':
                            cell.IsPlayable = true;
                            cell.Tile = TileEntity.Rock();
                            break;
                        case 'B':
                            cell.IsPlayable = true;
                            cell.Tile = TileEntity.Boulder();
                            break;
                        case 'S':
                            cell.IsPlayable = true;
                            cell.Tile = TileEntity.Statuette();
                            break;
                        default:
                            if (char.IsDigit(c) && c != '0')
                            {
                                var color = c - '0';
                                if (!allowed.Contains(color))
                                {
                                    throw new FormatException($"Color '{color}' at ({x},{y}) is not in available colors.");
                                }
                                cell.IsPlayable = true;
                                cell.Tile = TileEntity.Piece(color);
                            }
                            else
                            {
                                throw new FormatException($"Unknown symbol '{c}' at ({x},{y}).");
                            }
                            break;
                    }
                }
            }

            return board;
        }
    }
}
