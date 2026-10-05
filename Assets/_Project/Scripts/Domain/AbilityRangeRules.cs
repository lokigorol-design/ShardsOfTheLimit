using System;

namespace ShardsOfTheLimit.Domain
{
    public enum AbilityRangeType
    {
        Melee,
        Ranged
    }

    /// <summary>Pure height adjustment shared by targeting preview, validation and AI.</summary>
    public static class AbilityRangeCalculator
    {
        public static int GetEffectiveRange(
            int baseRange,
            AbilityRangeType rangeType,
            int sourceHeight,
            int targetHeight)
        {
            if (baseRange < 0) throw new ArgumentOutOfRangeException(nameof(baseRange));
            int heightDifference = targetHeight - sourceHeight;
            int adjustment = (Math.Abs(heightDifference) + 1) / 2;
            if (rangeType == AbilityRangeType.Melee)
            {
                return Math.Max(0, baseRange - adjustment);
            }

            return Math.Max(0, baseRange + (heightDifference <= 0 ? adjustment : -adjustment));
        }

        public static bool IsWithinRange(
            int baseRange,
            AbilityRangeType rangeType,
            int sourceHeight,
            int targetHeight,
            int gridDistance)
        {
            if (gridDistance < 0) throw new ArgumentOutOfRangeException(nameof(gridDistance));
            return gridDistance <= GetEffectiveRange(baseRange, rangeType, sourceHeight, targetHeight);
        }
    }
}
