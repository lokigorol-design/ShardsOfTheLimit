using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>
    /// Reserve and swap (no Archon unit on the field): on any player turn the active resonant spends 1 AP and the shared
    /// Concentration to summon a reserve resonant next to itself, swap itself for one (same cell, the newcomer keeps the
    /// remaining AP) or recall another resonant.
    /// </summary>
    public static class ReserveRules
    {
        /// <summary>Resonants on the field at the same time.</summary>
        public const int FieldLimit = 3;
        public const int SummonRange = 2;
        public const int ConcentrationTicks = 200;
        /// <summary>A recalled resonant cannot come back for 3 average turns.</summary>
        public const int ReturnLockTicks = 300;
        public const int ActionCost = 1;

        public static IReadOnlyList<GridCoordinate> SummonCells(TacticalGrid grid, GridCoordinate origin,
            Func<GridCoordinate, bool> isOccupied)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            return grid.Cells
                .Where(cell => cell.IsWalkable && cell.Coordinate != origin &&
                               origin.ManhattanDistance(cell.Coordinate) <= SummonRange &&
                               !(isOccupied?.Invoke(cell.Coordinate) ?? false))
                .Select(cell => cell.Coordinate)
                .ToArray();
        }
    }

    /// <summary>One resonant waiting in the reserve.</summary>
    public sealed class ReserveSlot
    {
        public ReserveSlot(CombatUnit unit, long availableTick)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            AvailableTick = availableTick;
        }

        public CombatUnit Unit { get; }
        /// <summary>Recalled resonants are locked for <see cref="ReserveRules.ReturnLockTicks"/>.</summary>
        public long AvailableTick { get; }
        public bool IsAvailable(long tick) => !Unit.IsDefeated && tick >= AvailableTick;
    }
}
