using System;
using System.Collections.Generic;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>
    /// A channelled ability (tag «Поддерживающее», 16.10.7): it runs over several of the owner's turns; each turn the owner
    /// pays to extend it or lets it end. While it lasts the owner neither moves nor uses other abilities.
    /// </summary>
    public sealed class ChannelState
    {
        public ChannelState(string abilityId, int stage, GridCoordinate origin, GridCoordinate direction, int extendedOnActivation)
        {
            AbilityId = abilityId;
            Stage = stage;
            Origin = origin;
            Direction = direction;
            ExtendedOnActivation = extendedOnActivation;
        }

        public string AbilityId { get; }
        public int Stage { get; }
        public GridCoordinate Origin { get; }
        public GridCoordinate Direction { get; }
        /// <summary>Owner activation on which the channel was started or last extended.</summary>
        public int ExtendedOnActivation { get; }
        /// <summary>Resonants already hit at this stage by walking into the active cone.</summary>
        public HashSet<CombatUnit> EnteredThisStage { get; } = new HashSet<CombatUnit>();
    }

    /// <summary>Flame Burst cone (10.2.2): rows in front of the source; stage 1 «1-3», stage 2 «1-3-3», stage 3 «1-3-3-5-5».</summary>
    public static class ConePattern
    {
        private static readonly int[][] RowsByStage =
        {
            new[] { 1, 3 },
            new[] { 1, 3, 3 },
            new[] { 1, 3, 3, 5, 5 }
        };

        public static IReadOnlyList<GridCoordinate> Cells(TacticalGrid grid, GridCoordinate origin, GridCoordinate direction, int stage)
        {
            if (Math.Abs(direction.X) + Math.Abs(direction.Y) != 1) throw new ArgumentException("Cone direction must be cardinal.");
            int[] rows = RowsByStage[Math.Max(1, Math.Min(RowsByStage.Length, stage)) - 1];
            var side = new GridCoordinate(-direction.Y, direction.X);
            var cells = new List<GridCoordinate>();
            for (int row = 0; row < rows.Length; row++)
            {
                int half = rows[row] / 2;
                for (int offset = -half; offset <= half; offset++)
                {
                    var cell = new GridCoordinate(origin.X + direction.X * (row + 1) + side.X * offset,
                        origin.Y + direction.Y * (row + 1) + side.Y * offset);
                    if (grid.Contains(cell)) cells.Add(cell);
                }
            }

            return cells;
        }
    }
}