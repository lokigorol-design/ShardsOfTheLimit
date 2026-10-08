using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>
    /// Archon on the battlefield (prototype of the swap proposal, analysis 8.2): a unit with its own turn that barely
    /// fights. It summons from the reserve next to itself, recalls within its link radius and swaps (recall + summon
    /// into the same cell). Summon, recall and swap share one Concentration cooldown.
    /// </summary>
    public static class ArchonRules
    {
        /// <summary>Echo on the field at the same time (the Archon itself does not count).</summary>
        public const int FieldLimit = 3;
        public const int SummonRange = 2;
        public const int LinkRadius = 4;
        public const int ConcentrationTicks = 200;
        /// <summary>A recalled resonant cannot come back for 3 average turns.</summary>
        public const int ReturnLockTicks = 300;
        /// <summary>Link Break: at 0 health the Archon is out for 3 turns, then gets up with 30% health.</summary>
        public const int LinkBreakTicks = 300;
        public const double ReviveHealthFraction = 0.3d;
        public const int ActionCost = 1;

        public static IReadOnlyList<GridCoordinate> SummonCells(TacticalGrid grid, GridCoordinate archon,
            Func<GridCoordinate, bool> isOccupied)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            return grid.Cells
                .Where(cell => cell.IsWalkable && cell.Coordinate != archon &&
                               archon.ManhattanDistance(cell.Coordinate) <= SummonRange &&
                               !(isOccupied?.Invoke(cell.Coordinate) ?? false))
                .Select(cell => cell.Coordinate)
                .ToArray();
        }

        public static bool InLink(GridCoordinate archon, GridCoordinate echo) => archon.ManhattanDistance(echo) <= LinkRadius;

        public static int ReviveHealth(int maximumHealth) =>
            Math.Max(1, (int)Math.Round(maximumHealth * ReviveHealthFraction, MidpointRounding.AwayFromZero));
    }

    /// <summary>One resonant waiting in the Archon's stone.</summary>
    public sealed class ReserveSlot
    {
        public ReserveSlot(CombatUnit unit, long availableTick)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            AvailableTick = availableTick;
        }

        public CombatUnit Unit { get; }
        /// <summary>Recalled resonants are locked for <see cref="ArchonRules.ReturnLockTicks"/>.</summary>
        public long AvailableTick { get; }
        public bool IsAvailable(long tick) => !Unit.IsDefeated && tick >= AvailableTick;
    }
}
