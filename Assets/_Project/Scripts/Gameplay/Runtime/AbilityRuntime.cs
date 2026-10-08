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
        public IReadOnlyList<DelayedStrike> Strikes { get; internal set; } = Array.Empty<DelayedStrike>();
        public IReadOnlyList<TrapObject> Traps { get; internal set; } = Array.Empty<TrapObject>();
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
            if (ability.Effects.Any(effect => effect is CreateCloudEffectDefinition || effect is DelayedStrikeEffectDefinition ||
                                              effect is PullToPointEffectDefinition))
                geometricTargets = geometricTargets.Where(c => grid.GetCell(c).IsWalkable).ToArray();
            if (ability.Effects.OfType<CreateTrapEffectDefinition>().Any())
                geometricTargets = geometricTargets.Where(c => grid.GetCell(c).IsWalkable &&
                    !battleUnits.Any(unit => !unit.IsDefeated && unit.Cell == c)).ToArray();

            if (ability.TargetType != AbilityTargetType.Unit)
            {
                return geometricTargets;
            }

            if (ability.TargetingPattern is LinePatternDefinition line && line.StopAtFirstUnit)
            {
                // Only the first resonant on each line is reachable; friendly units block the line too.
                geometricTargets = line.FirstOccupiedCells(grid, source.Cell, cell =>
                    battleUnits.Any(unit => unit != source && !unit.IsDefeated && unit.Cell == cell));
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
            PullToPointEffectDefinition magnet = ability.Effects.OfType<PullToPointEffectDefinition>().FirstOrDefault();
            if (magnet != null)
            {
                // Storm Magnet acts on one resonant: the first on a line from the point within reach.
                CombatUnit pulled = ForcedMovementCalculator.FindFirstOnLines(grid, selectedTarget, magnet.reach, battleUnits);
                affectedTargets = pulled != null ? new[] { pulled } : Array.Empty<CombatUnit>();
            }

            var strikes = new List<DelayedStrike>();
            var traps = new List<TrapObject>();
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

            foreach (AbilityEffectDefinition effect in ability.Effects)
            {
                if (effect is CreateCloudEffectDefinition createCloud)
                {
                    clouds.Add(PreviewCloud(createCloud.cloud, areas, grid, source, ability, selectedTarget, timelineTick));
                    continue;
                }
                if (effect is DelayedStrikeEffectDefinition delayed)
                {
                    strikes.Add(new DelayedStrike(source, ability.StableId, affected.Where(c => grid.GetCell(c).IsWalkable),
                        timelineTick + delayed.delayTicks, delayed.attackMultiplier * (double)source.Attack,
                        BurningCalculator.PowerFromAttack(source.Attack, delayed.burnAttackMultiplier), delayed.burnDurationPulses));
                    continue;
                }
                if (effect is CreateTrapEffectDefinition trapEffect)
                {
                    traps.Add(new TrapObject(source, ability.StableId, selectedTarget, timelineTick + trapEffect.lifetimeTicks,
                        trapEffect.fallHealthPercent));
                    continue;
                }
                if (effect is ModifyTerrainEffectDefinition terrainEffect)
                {
                    foreach (GridCoordinate coordinate in affected)
                    {
                        if (!forecastTerrain.TryGetValue(coordinate, out GridCell cell) ||
                            !terrainEffect.LiftsOccupant &&
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
                        StatusDefinition status = ResolveStatus(statusEffect.status, ability, modifiers);
                        forecastStatuses[target].TryGetValue(status.stableId, out var previous);
                        var statusResult = StatusCalculator.Apply(target, status.Snapshot(), statusEffect.stacks,
                            timelineTick, source.Id, previous);
                        forecastStatuses[target][status.stableId] = statusResult.State;
                        result = statusResult;
                    }
                    else result = Calculate(
                        effect,
                        source,
                        target,
                        state,
                        forecastBurning[target],
                        CountOil(forecastStatuses[target]),
                        timelineTick,
                        grid,
                        forecastPositions[source],
                        forecastPositions[target],
                        coordinate => forecastPositions.Any(entry =>
                            entry.Key != target && entry.Value == coordinate),
                        selectedTarget);
                    results.Add(result);
                    if (result is PushResult push)
                    {
                        forecastPositions[target] = push.Destination;
                        if (push.Collided) AddCollisionDamage(push, source, battleUnits, forecast, forecastPositions, results);
                    }
                    else if (result is BurnAppliedResult burn)
                    {
                        forecastBurning[target] = new BurningState(
                            burn.NewDamagePerPulse,
                            burn.NewNextPulseTick,
                            burn.NewExpireTick);
                        if (burn.ConsumedOilStacks > 0)
                            foreach (string oilId in forecastStatuses[target]
                                         .Where(s => s.Value.Rules.Mechanic == StatusMechanic.Oil)
                                         .Select(s => s.Key).ToArray())
                                forecastStatuses[target].Remove(oilId);
                    }
                    else
                    {
                        forecast[target] = CombatEffectCalculator.Apply(state, result);
                    }
                }
            }

            if (apply)
            {
                if (clouds.Count > 0 && areas == null) throw new InvalidOperationException("Cloud execution requires battle AreaEffectWorld.");
                CombatEffectResolver.Apply(results);
                foreach (var cloud in clouds) areas.Add(cloud);
                if ((strikes.Count > 0 || traps.Count > 0) && areas == null)
                    throw new InvalidOperationException("Delayed strikes and traps require battle AreaEffectWorld.");
                foreach (DelayedStrike strike in strikes) areas.AddStrike(strike);
                foreach (TrapObject trap in traps) areas.AddTrap(trap);
                foreach (TerrainChangeResult terrainResult in terrainResults)
                {
                    grid.ApplyTerrainChange(terrainResult);
                }
            }

            return new AbilityResolution(affected, results, terrainResults) { Clouds = clouds, Strikes = strikes, Traps = traps };
        }

        private static CloudAreaEffect PreviewCloud(CloudDefinition definition, AreaEffectWorld areas, TacticalGrid grid,
            CombatUnit source, AbilityDefinition ability, GridCoordinate target, long tick)
        {
            var world = areas ?? new AreaEffectWorld(grid, new[] { source });
            return world.PreviewCloud(source, ability.StableId, definition.Snapshot(), definition.areaShape.Evaluate(grid, target),
                CloudAreaEffect.DirectionFrom(source.Cell, target), grid.GetCell(target).Height, tick);
        }

        /// <summary>Sticky Mud: the modified ability applies its replacement status instead of Slow.</summary>
        private static StatusDefinition ResolveStatus(
            StatusDefinition status,
            AbilityDefinition ability,
            PassiveModifierDefinition modifiers)
        {
            return modifiers != null && modifiers.stickyMud && modifiers.modifiedAbility == ability &&
                   modifiers.stickyMudStatus != null && status.mechanic == StatusMechanic.Slow
                ? modifiers.stickyMudStatus
                : status;
        }

        private static int CountOil(IReadOnlyDictionary<string, StackStatusState> statuses) =>
            statuses.Values.Where(s => s.Rules.Mechanic == StatusMechanic.Oil).Sum(s => s.Stacks);

        /// <summary>Collision (16.10.8): the displaced unit and the unit it hit each take 0.2 × ATK of the displacer.</summary>
        private static void AddCollisionDamage(
            PushResult push,
            CombatUnit source,
            IEnumerable<CombatUnit> units,
            IDictionary<CombatUnit, CombatResourceState> forecast,
            IReadOnlyDictionary<CombatUnit, GridCoordinate> positions,
            ICollection<ICombatEffectResult> results)
        {
            double raw = source.Attack * ForcedMovementCalculator.CollisionAttackMultiplier;
            if (raw <= 0) return;
            CombatUnit struck = units.FirstOrDefault(unit => unit != push.Target && forecast.ContainsKey(unit) &&
                                                             positions.TryGetValue(unit, out var cell) &&
                                                             cell == push.CollisionCell.Value);
            foreach (CombatUnit victim in new[] { push.Target, struck })
            {
                if (victim == null || forecast[victim].Health <= 0) continue;
                DamageResult hit = CombatEffectCalculator.Damage(victim, forecast[victim], raw);
                results.Add(hit);
                forecast[victim] = CombatEffectCalculator.Apply(forecast[victim], hit);
            }
        }

        private static ICombatEffectResult Calculate(
            AbilityEffectDefinition effect,
            CombatUnit source,
            CombatUnit target,
            CombatResourceState state,
            BurningState burning,
            int oilStacks,
            long timelineTick,
            TacticalGrid grid,
            GridCoordinate sourcePosition,
            GridCoordinate targetPosition,
            Func<GridCoordinate, bool> isOccupied,
            GridCoordinate point)
        {
            switch (effect)
            {
                case PullToPointEffectDefinition _:
                    return ForcedMovementCalculator.PullToPoint(grid, target, point, targetPosition, isOccupied);
                case DamageEffectDefinition damage:
                    return CombatEffectCalculator.Damage(target, state, damage.RawDamage(source.Attack));
                case HealEffectDefinition heal:
                    return CombatEffectCalculator.Heal(target, state, heal.Amount);
                case GrantShieldEffectDefinition shield:
                    return CombatEffectCalculator.GrantShield(target, state, shield.Amount);
                case ApplyBurnEffectDefinition burn:
                    return BurningCalculator.Apply(
                        target,
                        burning,
                        BurningCalculator.PowerFromAttack(source.Attack, burn.AttackMultiplier),
                        burn.Duration,
                        timelineTick,
                        oilStacks);
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
            bool liftsOccupants = effects.All(effect => effect.LiftsOccupant);
            if (!cell.IsWalkable || !liftsOccupants && units.Any(unit =>
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
