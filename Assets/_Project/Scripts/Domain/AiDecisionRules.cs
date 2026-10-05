using System;

namespace ShardsOfTheLimit.Domain
{
    public static class AiDecisionMath
    {
        /// <summary>
        /// Equal values produce 50/50. A twofold advantage reaches the 95% cap; larger advantages
        /// never become deterministic.
        /// </summary>
        public static double CalculateFirstOptionProbability(int firstValue, int secondValue)
        {
            if (firstValue < 0) throw new ArgumentOutOfRangeException(nameof(firstValue));
            if (secondValue < 0) throw new ArgumentOutOfRangeException(nameof(secondValue));
            if (firstValue == secondValue) return 0.5d;

            int higher = Math.Max(firstValue, secondValue);
            int lower = Math.Min(firstValue, secondValue);
            double relativeAdvantage = lower == 0
                ? 1d
                : Math.Min(1d, (higher - lower) / (double)lower);
            double higherProbability = 0.5d + 0.45d * relativeAdvantage;
            return firstValue > secondValue ? higherProbability : 1d - higherProbability;
        }
    }
}
