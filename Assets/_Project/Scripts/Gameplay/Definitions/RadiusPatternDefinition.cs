using System.Collections.Generic;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Patterns/Radius", fileName = "RadiusPattern")]
    public sealed class RadiusPatternDefinition : PatternDefinition
    {
        [SerializeField, Min(0)] private int radius = 1;
        [SerializeField] private bool includeCenter = true;

        public int Radius => radius;
        public bool IncludeCenter => includeCenter;

        public override IReadOnlyCollection<GridCoordinate> Evaluate(
            TacticalGrid grid,
            GridCoordinate origin)
        {
            return PatternCalculator.Diamond(grid, origin, radius, includeCenter);
        }
    }
}
