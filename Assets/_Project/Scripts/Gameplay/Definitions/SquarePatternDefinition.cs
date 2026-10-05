using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Square area around the origin: half-size 1 is the 3 × 3 square of Oil Cloud.</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Patterns/Square", fileName = "SquarePattern")]
    public sealed class SquarePatternDefinition : PatternDefinition
    {
        [SerializeField, Min(0)] private int halfSize = 1;

        public int HalfSize => halfSize;

        public override IReadOnlyCollection<GridCoordinate> Evaluate(TacticalGrid grid, GridCoordinate origin)
        {
            return grid.Cells
                .Select(cell => cell.Coordinate)
                .Where(c => System.Math.Abs(c.X - origin.X) <= halfSize && System.Math.Abs(c.Y - origin.Y) <= halfSize)
                .OrderBy(c => c.X)
                .ThenBy(c => c.Y)
                .ToArray();
        }
    }
}