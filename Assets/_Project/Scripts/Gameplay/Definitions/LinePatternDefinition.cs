using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Straight lines in the four cardinal directions (design 2.5: no diagonals). Obstacles end a line.
    /// With Stop At First Unit only the first resonant on each line can be targeted (Long Bite, Reeling Pull).
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Patterns/Line", fileName = "LinePattern")]
    public sealed class LinePatternDefinition : PatternDefinition
    {
        [SerializeField, Min(1)] private int length = 2;
        [SerializeField] private bool stopAtFirstUnit = true;

        public int Length => length;
        public bool StopAtFirstUnit => stopAtFirstUnit;

        public override IReadOnlyCollection<GridCoordinate> Evaluate(TacticalGrid grid, GridCoordinate origin)
        {
            return Directions.SelectMany(direction => Ray(grid, origin, direction)).ToArray();
        }

        /// <summary>The first cell on each line that <paramref name="isOccupied"/> accepts.</summary>
        public IReadOnlyCollection<GridCoordinate> FirstOccupiedCells(
            TacticalGrid grid,
            GridCoordinate origin,
            System.Func<GridCoordinate, bool> isOccupied)
        {
            var result = new List<GridCoordinate>();
            foreach (GridCoordinate direction in Directions)
            {
                foreach (GridCoordinate cell in Ray(grid, origin, direction))
                {
                    if (!isOccupied(cell)) continue;
                    result.Add(cell);
                    break;
                }
            }

            return result;
        }

        private static readonly GridCoordinate[] Directions =
        {
            new GridCoordinate(1, 0), new GridCoordinate(-1, 0), new GridCoordinate(0, 1), new GridCoordinate(0, -1)
        };

        private IEnumerable<GridCoordinate> Ray(TacticalGrid grid, GridCoordinate origin, GridCoordinate direction)
        {
            for (int step = 1; step <= length; step++)
            {
                var cell = new GridCoordinate(origin.X + direction.X * step, origin.Y + direction.Y * step);
                if (!grid.Contains(cell) || !grid.GetCell(cell).IsWalkable) yield break;
                yield return cell;
            }
        }
    }
}