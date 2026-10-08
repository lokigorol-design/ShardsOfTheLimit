using System;

namespace ShardsOfTheLimit.Domain
{
    public enum AbilityRangeType
    {
        Melee,
        Ranged
    }

    /// <summary>
    /// Design 2.5, 16.10.11. One height unit is half a cell (a resonant is 2 units tall).
    /// Melee: −1 cell of range for every full 3 units of difference in either direction; at 0 the attack fails.
    /// Ranged: ±1 cell for every full 2 units — a lower target is farther, a higher one closer.
    /// </summary>
    public static class AbilityRangeCalculator
    {
        public static int GetEffectiveRange(
            int baseRange,
            AbilityRangeType rangeType,
            int sourceHeight,
            int targetHeight)
        {
            if (baseRange < 0) throw new ArgumentOutOfRangeException(nameof(baseRange));
            int sourceAbove = sourceHeight - targetHeight;
            if (rangeType == AbilityRangeType.Melee)
            {
                return Math.Max(0, baseRange - Math.Abs(sourceAbove) / 3);
            }

            int steps = Math.Abs(sourceAbove) / 2;
            return Math.Max(0, baseRange + (sourceAbove >= 0 ? steps : -steps));
        }

        public static bool IsWithinRange(
            int baseRange,
            AbilityRangeType rangeType,
            int sourceHeight,
            int targetHeight,
            int gridDistance)
        {
            if (gridDistance < 0) throw new ArgumentOutOfRangeException(nameof(gridDistance));
            int range = GetEffectiveRange(baseRange, rangeType, sourceHeight, targetHeight);
            return gridDistance <= range && (range > 0 || gridDistance == 0);
        }
    }

    /// <summary>
    /// Damage modifiers of a direct hit (16.10.11 and the facing decision D-033):
    /// height — melee ±10% and ranged ±5% per unit, attacker higher deals more, no cap;
    /// facing — +25% into the back, +10% into a side.
    /// </summary>
    public static class HitModifiers
    {
        public const double MeleePerUnit = 0.10d;
        public const double RangedPerUnit = 0.05d;
        public const double BackBonus = 0.25d;
        public const double SideBonus = 0.10d;

        public static double HeightMultiplier(AbilityRangeType rangeType, int sourceHeight, int targetHeight)
        {
            double perUnit = rangeType == AbilityRangeType.Melee ? MeleePerUnit : RangedPerUnit;
            return Math.Max(0.1d, 1d + perUnit * (sourceHeight - targetHeight));
        }

        public static double FacingMultiplier(AttackSide side) =>
            side == AttackSide.Back ? 1d + BackBonus : side == AttackSide.Side ? 1d + SideBonus : 1d;
    }

    /// <summary>
    /// Line of fire (16.10.12): from the centre of the shooter's top block to the centre of the target's top block.
    /// Terrain columns and obstacles (2 units tall) block it; resonants never do. A wall in your own height right
    /// next to the target covers it unless the shooter is high enough — the cover formula follows from the geometry.
    /// </summary>
    public static class LineOfFire
    {
        public const int ObstacleHeight = 2;
        private const double BlockCentre = 1.5d;

        public static bool IsClear(TacticalGrid grid, GridCoordinate from, GridCoordinate to)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (from == to || !grid.Contains(from) || !grid.Contains(to)) return true;
            double startHeight = grid.GetCell(from).Height + BlockCentre;
            double endHeight = grid.GetCell(to).Height + BlockCentre;
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            int samples = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 8d);
            for (int index = 1; index < samples; index++)
            {
                double t = index / (double)samples;
                var cell = new GridCoordinate((int)Math.Round(from.X + dx * t), (int)Math.Round(from.Y + dy * t));
                if (cell == from || cell == to || !grid.Contains(cell)) continue;
                GridCell blocker = grid.GetCell(cell);
                double top = blocker.Height + (blocker.IsWalkable ? 0 : ObstacleHeight);
                double lineHeight = startHeight + (endHeight - startHeight) * t;
                if (top > lineHeight) return false;
            }

            return true;
        }
    }

    /// <summary>Falling (16.10.11): a forced drop of 3+ units costs 5% × (drop − 2) of maximum health, Defense ignored.</summary>
    public static class FallRules
    {
        public const int SafeDrop = 2;
        public const double PercentPerUnit = 5d;

        public static double DamageForDrop(int maximumHealth, int drop) =>
            drop <= SafeDrop ? 0d : maximumHealth * PercentPerUnit * (drop - SafeDrop) / 100d;
    }
}
