using System;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Runtime
{
    public sealed class AbilityResolution
    {
        public AbilityResolution(
            IReadOnlyCollection<GridCoordinate> impactCells,
            IReadOnlyList<ICombatEffectResult> results,
            IReadOnlyList<TerrainChangeResult> terrainResults)
        {
            ImpactCells = impactCells ?? throw new ArgumentNullException(nameof(impactCells));
            Results = results ?? throw new ArgumentNullException(nameof(results));
            TerrainResults = terrainResults ?? throw new ArgumentNullException(nameof(terrainResults));
        }

        public IReadOnlyCollection<GridCoordinate> ImpactCells { get; }
        public IReadOnlyList<ICombatEffectResult> Results { get; }
        public IReadOnlyList<TerrainChangeResult> TerrainResults { get; }
        public IReadOnlyList<CloudAreaEffect> Clouds { get; internal set; } = Array.Empty<CloudAreaEffect>();
    }

    /// <summary>Single source of targeting, preview and execution math for prototype abilities.</summary>
    public static class AbilityRuntime
    {
        public static IReadOnlyCollection<GridCoordinate> GetTargetingRangeCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source)
        {
            Validate(ability);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (!(ability.TargetingPattern is RadiusPatternDefinition radiusPattern))
            {
                return ability.TargetingPattern.Evaluate(grid, source.Cell);
            }

            int sourceHeight = grid.GetCell(source.Cell).Height;
            return grid.Cells
                .Where(cell => radiusPattern.IncludeCenter || cell.Coordinate != source.Cell)
                .Where(cell => AbilityRangeCalculator.IsWithinRange(
                    radiusPattern.Radius,
                    ability.RangeType,
                    sourceHeight,
                    cell.Height,
                    source.Cell.ManhattanDistance(cell.Coordinate)))
                .Select(cell => cell.Coordinate)
                .OrderBy(coordinate => coordinate.X)
                .ThenBy(coordinate => coordinate.Y)
                .ToArray();
        }

        public static IReadOnlyCollection<GridCoordinate> GetTargetableCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            IEnumerable<CombatUnit> units)
        {
            Validate(ability);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (units == null) throw new ArgumentNullException(nameof(units));

            CombatUnit[] battleUnits = units.Where(unit => unit != null).ToArray();

            IReadOnlyCollection<GridCoordinate> geometricTargets =
                GetTargetingRangeCells(ability, grid, source);
            ModifyTerrainEffectDefinition[] terrainEffects = ability.Effects
                .OfType<ModifyTerrainEffectDefinition>()
                .ToArray();
            if (terrainEffects.Length > 0)
            {
                geometricTargets = geometricTargets
                    .Where(coordinate => IsValidTerrainTarget(coordinate, grid, source, battleUnits, terrainEffects))
                    .ToArray();
            }
            if (ability.Effects.OfType<CreateCloudEffectDefinition>().Any())
                geometricTargets = geometricTargets.Where(c => grid.GetCell(c).IsWalkable).ToArray();

            if (ability.TargetType != AbilityTargetType.Unit)
            {
                return geometricTargets;
            }

            var geometricSet = new HashSet<GridCoordinate>(geometricTargets);
            return battleUnits
                .Where(unit => !unit.IsDefeated)
                .Where(unit => IsTeamEligible(source, unit, ability.TargetTeam))
                .Where(unit => geometricSet.Contains(unit.Cell))
                .Select(unit => unit.Cell)
                .Distinct()
                .OrderBy(coordinate => coordinate.X)
                .ThenBy(coordinate => coordinate.Y)
                .ToArray();
        }

        public static IReadOnlyCollection<GridCoordinate> GetTargetingHighlightCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            IEnumerable<CombatUnit> units)
        {
            IReadOnlyCollection<GridCoordinate> legalTargets =
                GetTargetableCells(ability, grid, source, units);
            // A unit ability keeps its geometric radius visible even when only a few units occupy it.
            // A cell ability paints only cells accepted by execution, including terrain constraints.
            return ability.TargetType == AbilityTargetType.Unit
                ? GetTargetingRangeCells(ability, grid, source)
                : legalTargets;
        }

        public static IReadOnlyCollection<GridCoordinate> GetImpactCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            GridCoordinate selectedTarget)
        {
            Validate(ability);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var cloud = ability.Effects.OfType<CreateCloudEffectDefinition>().FirstOrDefault();
            if (cloud != null) return cloud.cloud.areaShape.Evaluate(grid, selectedTarget)
                .Where(c => grid.GetCell(c).IsWalkable && grid.GetCell(c).Height <
                    grid.GetCell(selectedTarget).Height + cloud.cloud.verticalHeight).ToArray();
            return ability.ImpactPattern.Evaluate(grid, selectedTarget);
        }

        public static AbilityResolution Preview(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units,
            long timelineTick = 0, AreaEffectWorld areas = null, PassiveModifierDefinition modifiers = null)
        {
            return Resolve(ability, grid, source, selectedTarget, units, false, timelineTick, areas, modifiers);
        }

        public static AbilityResolution Execute(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units,
            long timelineTick = 0, AreaEffectWorld areas = null, PassiveModifierDefinition modifiers = null)
        {
            return Resolve(ability, grid, source, selectedTarget, units, true, timelineTick, areas, modifiers);
        }

        private static AbilityResolution Resolve(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units,
            bool apply,
            long timelineTick, AreaEffectWorld areas, PassiveModifierDefinition modifiers)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            CombatUnit[] battleUnits = units.Where(unit => unit != null).ToArray();
            IReadOnlyCollection<GridCoordinate> targetable =
                GetTargetableCells(ability, grid, source, battleUnits);
            if (!targetable.Contains(selectedTarget))
            {
                throw new InvalidOperationException($"Cell {selectedTarget} is not a valid target for {ability.DisplayName}.");
            }

            IReadOnlyCollection<GridCoordinate> affected = GetImpactCells(ability, grid, selectedTarget);
            var affectedSet = new HashSet<GridCoordinate>(affected);
            CombatUnit[] affectedTargets = battleUnits
                .Where(unit => !unit.IsDefeated && affectedSet.Contains(unit.Cell))
                .ToArray();
            var forecast = battleUnits
                .Where(unit => !unit.IsDefeated)
                .ToDictionary(unit => unit, unit => unit.ResourceState);
            var forecastBurning = battleUnits
                .Where(unit => !unit.IsDefeated)
                .ToDictionary(unit => unit, unit => unit.BurningState);
            var forecastPositions = battleUnits
                .Where(unit => !unit.IsDefeated)
                .ToDictionary(unit => unit, unit => unit.Cell);
            var forecastTerrain = grid.Cells.ToDictionary(cell => cell.Coordinate, cell => cell);
            var results = new List<ICombatEffectResult>();
            var terrainResults = new List<TerrainChangeResult>();
            var clouds = new List<CloudAreaEffect>();
            var forecastStatuses = battleUnits.ToDictionary(u => u,
                u => u.Statuses.ToDictionary(s => s.Key, s => s.Value));
            int baseAbilityDamage = ability.Effects
                .OfType<DamageEffectDefinition>()
                .Sum(effect => effect.Amount);

            foreach (AbilityEffectDefinition effect in ability.Effects)
            {
                if (effect is CreateCloudEffectDefinition createCloud)
                {
                    clouds.Add(PreviewCloud(createCloud.cloud, areas, grid, source, ability, selectedTarget, timelineTick));
                    continue;
                }
                if (effect is ModifyTerrainEffectDefinition terrainEffect)
                {
                    foreach (GridCoordinate coordinate in affected)
                    {
                        if (!forecastTerrain.TryGetValue(coordinate, out GridCell cell) ||
                            forecastPositions.Any(entry => entry.Key != source && entry.Value == coordinate))
                        {
                            continue;
                        }

                        TerrainChangeResult terrainResult = TerrainChangeCalculator.ChangeHeight(
                            cell,
                            terrainEffect.HeightDelta,
                            terrainEffect.MinimumHeight,
                            terrainEffect.MaximumHeight);
                        if (!terrainResult.Changed) continue;
                        terrainResults.Add(terrainResult);
                        forecastTerrain[coordinate] = new GridCell(
                            coordinate,
                            terrainResult.NewHeight,
                            terrainResult.IsWalkable);
                    }

                    continue;
                }

                foreach (CombatUnit target in affectedTargets.Where(target =>
                             IsEffectTeamEligible(source, target, ability, effect, affected.Count)))
                {
                    CombatResourceState state = forecast[target];
                    if (state.Health <= 0) continue;
                    ICombatEffectResult result;
                    if (effect is ApplyStatusEffectDefinition statusEffect)
                    {
                        forecastStatuses[target].TryGetValue(statusEffect.status.stableId, out var previous);
                        var statusResult = StatusCalculator.Apply(target, statusEffect.status.Snapshot(), statusEffect.stacks,
                            timelineTick, source.Id, previous);
                        forecastStatuses[target][statusEffect.status.stableId] = statusResult.State;
                        result = statusResult;
                    }
                    else result = Calculate(
                        effect,
                        target,
                        state,
                        forecastBurning[target],
                        baseAbilityDamage,
                        timelineTick,
                        grid,
                        forecastPositions[source],
                        forecastPositions[target],
                        coordinate => forecastPositions.Any(entry =>
                            entry.Key != target && entry.Value == coordinate));
                    results.Add(result);
                    if (result is PushResult push)
                    {
                        forecastPositions[target] = push.Destination;
                    }
                    else if (result is BurnAppliedResult burn)
                    {
                        forecastBurning[target] = new BurningState(
                            burn.NewDamagePerPulse,
                            burn.NewNextPulseTick,
                            burn.NewExpireTick);
                    }
                    else
                    {
                        forecast[target] = CombatEffectCalculator.Apply(state, result);
                    }
                }
            }

            if (modifiers != null && modifiers.venomousMud && modifiers.modifiedAbility == ability &&
                modifiers.onHitCloud != null && results.OfType<DamageResult>().Any(r => r.HealthDelta < 0 || r.ShieldDelta < 0))
                clouds.Add(PreviewCloud(modifiers.onHitCloud, areas, grid, source, ability, selectedTarget, timelineTick));

            if (apply)
            {
                if (clouds.Count > 0 && areas == null) throw new InvalidOperationException("Cloud execution requires battle AreaEffectWorld.");
                CombatEffectResolver.Apply(results);
                foreach (var cloud in clouds) areas.Add(cloud);
                foreach (TerrainChangeResult terrainResult in terrainResults)
                {
                    grid.ApplyTerrainChange(terrainResult);
                }
            }

            return new AbilityResolution(affected, results, terrainResults) { Clouds = clouds };
        }

        private static CloudAreaEffect PreviewCloud(CloudDefinition definition, AreaEffectWorld areas, TacticalGrid grid,
            CombatUnit source, AbilityDefinition ability, GridCoordinate target, long tick)
        {
            var world = areas ?? new AreaEffectWorld(grid, new[] { source });
            return world.PreviewCloud(source, ability.StableId, definition.Snapshot(), definition.areaShape.Evaluate(grid, target),
                CloudAreaEffect.DirectionFrom(source.Cell, target), grid.GetCell(target).Height, tick);
        }

        private static ICombatEffectResult Calculate(
            AbilityEffectDefinition effect,
            CombatUnit target,
            CombatResourceState state,
            BurningState burning,
            int baseAbilityDamage,
            long timelineTick,
            TacticalGrid grid,
            GridCoordinate sourcePosition,
            GridCoordinate targetPosition,
            Func<GridCoordinate, bool> isOccupied)
        {
            switch (effect)
            {
                case DamageEffectDefinition damage:
                    return CombatEffectCalculator.Damage(target, state, damage.Amount);
                case HealEffectDefinition heal:
                    return CombatEffectCalculator.Heal(target, state, heal.Amount);
                case GrantShieldEffectDefinition shield:
                    return CombatEffectCalculator.GrantShield(target, state, shield.Amount);
                case ApplyBurnEffectDefinition burn:
                    return BurningCalculator.Apply(
                        target,
                        burning,
                        baseAbilityDamage,
                        burn.DamagePercentage,
                        burn.Duration,
                        timelineTick);
                case PushEffectDefinition push:
                    return ForcedMovementCalculator.Push(
                        grid,
                        target,
                        sourcePosition,
                        targetPosition,
                        push.Distance,
                        isOccupied);
                case PullEffectDefinition pull:
                    return ForcedMovementCalculator.Pull(grid, target, sourcePosition, targetPosition, pull.distance, isOccupied);
                default:
                    throw new NotSupportedException(
                        $"Unsupported prototype effect: {effect?.GetType().Name ?? "null"}.");
            }
        }

        private static bool IsValidTerrainTarget(
            GridCoordinate coordinate,
            TacticalGrid grid,
            CombatUnit source,
            IReadOnlyCollection<CombatUnit> units,
            IEnumerable<ModifyTerrainEffectDefinition> effects)
        {
            if (!grid.Contains(coordinate)) return false;
            GridCell cell = grid.GetCell(coordinate);
            if (!cell.IsWalkable || units.Any(unit =>
                    unit != source && !unit.IsDefeated && unit.Cell == coordinate))
            {
                return false;
            }

            return effects.Any(effect => TerrainChangeCalculator.ChangeHeight(
                cell,
                effect.HeightDelta,
                effect.MinimumHeight,
                effect.MaximumHeight).Changed);
        }

        private static bool IsTeamEligible(
            CombatUnit source,
            CombatUnit target,
            AbilityTargetTeam targetTeam)
        {
            return targetTeam == AbilityTargetTeam.Any ||
                   targetTeam == AbilityTargetTeam.Allies && target.Team == source.Team ||
                   targetTeam == AbilityTargetTeam.Enemies && target.Team != source.Team;
        }

        private static bool IsEffectTeamEligible(
            CombatUnit source,
            CombatUnit target,
            AbilityDefinition ability,
            AbilityEffectDefinition effect,
            int affectedCellCount)
        {
            bool affectsBothTeams = affectedCellCount > 1 &&
                                    (effect is DamageEffectDefinition ||
                                     effect is HealEffectDefinition ||
                                     effect is ApplyBurnEffectDefinition);
            return affectsBothTeams || IsTeamEligible(source, target, ability.TargetTeam);
        }

        private static void Validate(AbilityDefinition ability)
        {
            if (ability == null) throw new ArgumentNullException(nameof(ability));
            if (ability.TargetingPattern == null) throw new InvalidOperationException("Ability targeting pattern is missing.");
            if (ability.ImpactPattern == null) throw new InvalidOperationException("Ability impact pattern is missing.");
        }
    }
}
