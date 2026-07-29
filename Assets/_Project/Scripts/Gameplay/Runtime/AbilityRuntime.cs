using System;
using System.Collections.Generic;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Runtime
{
    /// <summary>Single source of targeting and impact coordinates for preview and execution.</summary>
    public static class AbilityRuntime
    {
        public static IReadOnlyCollection<GridCoordinate> GetTargetableCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source)
        {
            Validate(ability);
            return ability.TargetingPattern.Evaluate(grid, source.Cell);
        }

        public static IReadOnlyCollection<GridCoordinate> GetImpactCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            GridCoordinate selectedTarget)
        {
            Validate(ability);
            return ability.ImpactPattern.Evaluate(grid, selectedTarget);
        }

        public static IReadOnlyList<DamageResult> Execute(
            AbilityDefinition ability,
            TacticalGrid grid,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units)
        {
            IReadOnlyCollection<GridCoordinate> affected = GetImpactCells(ability, grid, selectedTarget);
            var results = new List<DamageResult>();
            foreach (AbilityEffectDefinition effect in ability.Effects)
            {
                if (effect is DamageEffectDefinition damage)
                {
                    results.AddRange(DamageResolver.Apply(units, affected, damage.Amount));
                }
                else
                {
                    throw new NotSupportedException($"Unsupported prototype effect: {effect?.GetType().Name ?? "null"}.");
                }
            }

            return results;
        }

        private static void Validate(AbilityDefinition ability)
        {
            if (ability == null) throw new ArgumentNullException(nameof(ability));
            if (ability.TargetingPattern == null) throw new InvalidOperationException("Ability targeting pattern is missing.");
            if (ability.ImpactPattern == null) throw new InvalidOperationException("Ability impact pattern is missing.");
        }
    }
}
