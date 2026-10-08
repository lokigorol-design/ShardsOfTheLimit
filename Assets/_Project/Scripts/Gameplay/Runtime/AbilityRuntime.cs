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
        /// <summary>Cells that get Magma (Magma Worm: Burning is replaced by Magma on the target's cell).</summary>
        public IReadOnlyList<GridCoordinate> MagmaCells { get; internal set; } = Array.Empty<GridCoordinate>();
        /// <summary>Channel stage this cast or extension reached (0 for other abilities).</summary>
        public int ChannelStage { get; internal set; }
        /// <summary>True when the channel ended with this resolution (last stage reached).</summary>
        public bool ChannelEnded { get; internal set; }
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

        /// <summary>Surface abilities (Ritual Surfaces) can be cast on a wall as well as on the floor.</summary>
        public static bool IsSurfaceAbility(AbilityDefinition ability) => ability.Effects.Any(effect =>
            effect is PullToPointEffectDefinition || effect is DelayedStrikeEffectDefinition ||
            effect is CreateTrapEffectDefinition || effect is ModifyTerrainEffectDefinition terrain && terrain.LiftsOccupant);

        public static IReadOnlyCollection<GridCoordinate> GetTargetableCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            IEnumerable<CombatUnit> units,
            bool wall = false)
        {
            Validate(ability);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (units == null) throw new ArgumentNullException(nameof(units));

            CombatUnit[] battleUnits = units.Where(unit => unit != null).ToArray();

            if (ability.Effects.OfType<ChannelConeEffectDefinition>().Any())
            {
                // A channel picks a direction: one of the four neighbours; an active channel keeps its direction.
                if (source.Channel != null && source.Channel.AbilityId == ability.StableId)
                {
                    var kept = new GridCoordinate(source.Cell.X + source.Channel.Direction.X, source.Cell.Y + source.Channel.Direction.Y);
                    return grid.Contains(kept) ? new[] { kept } : Array.Empty<GridCoordinate>();
                }

                return source.Cell.FourWayNeighbors().Where(grid.Contains).ToArray();
            }

            IReadOnlyCollection<GridCoordinate> geometricTargets =
                GetTargetingRangeCells(ability, grid, source);

            if (wall && IsSurfaceAbility(ability))
            {
                // Ritual Surfaces: a wall facing the caster, with a free cell in front where the effect emerges.
                return geometricTargets
                    .Where(cell => SurfaceRules.TryGetWall(grid, source.Cell, cell, out GridCoordinate front, out _) &&
                                   (ability.RangeType == AbilityRangeType.Melee || LineOfFire.IsClear(grid, source.Cell, front)))
                    .ToArray();
            }

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

            if (ability.RangeType == AbilityRangeType.Ranged)
            {
                // A ranged ability needs a line of fire to the chosen cell (16.10.12).
                geometricTargets = geometricTargets.Where(cell => LineOfFire.IsClear(grid, source.Cell, cell)).ToArray();
            }

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
            IEnumerable<CombatUnit> units,
            bool wall = false)
        {
            IReadOnlyCollection<GridCoordinate> legalTargets =
                GetTargetableCells(ability, grid, source, units, wall);
            // A unit ability keeps its geometric radius visible even when only a few units occupy it.
            // A cell ability paints only cells accepted by execution, including terrain constraints.
            return ability.TargetType == AbilityTargetType.Unit
                ? GetTargetingRangeCells(ability, grid, source)
                : legalTargets;
        }

        /// <summary>Next channel stage for this source (Roaring Furnace skips stage 2).</summary>
        public static int NextChannelStage(AbilityDefinition ability, CombatUnit source, ChannelConeEffectDefinition channel,
            PassiveModifierDefinition modifiers)
        {
            int previous = source?.Channel != null && source.Channel.AbilityId == ability.StableId ? source.Channel.Stage : 0;
            int next = previous + 1;
            if (next == 2 && modifiers != null && modifiers.roaringFurnace) next = 3;
            return Math.Min(next, channel.MaximumStage);
        }

        public static IReadOnlyCollection<GridCoordinate> GetImpactCells(
            AbilityDefinition ability,
            TacticalGrid grid,
            GridCoordinate selectedTarget,
            CombatUnit source = null,
            bool wall = false,
            PassiveModifierDefinition modifiers = null)
        {
            Validate(ability);
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var channel = ability.Effects.OfType<ChannelConeEffectDefinition>().FirstOrDefault();
            if (channel != null && source != null)
            {
                int stage = NextChannelStage(ability, source, channel, modifiers);
                return ConePattern.Cells(grid, source.Cell, CloudAreaEffect.DirectionFrom(source.Cell, selectedTarget), stage).ToArray();
            }

            if (wall && source != null && IsSurfaceAbility(ability) &&
                SurfaceRules.TryGetWall(grid, source.Cell, selectedTarget, out GridCoordinate front, out GridCoordinate outward))
            {
                int length = ability.Effects.OfType<DelayedStrikeEffectDefinition>().Any() ? 3
                    : ability.Effects.OfType<PullToPointEffectDefinition>().Any() ? 1 + MagnetReach(ability, modifiers) : 1;
                return Ray(grid, front, outward, length).ToArray();
            }

            var cloud = ability.Effects.OfType<CreateCloudEffectDefinition>().FirstOrDefault();
            if (cloud != null) return cloud.cloud.areaShape.Evaluate(grid, selectedTarget)
                .Where(c => grid.GetCell(c).IsWalkable && grid.GetCell(c).Height <
                    grid.GetCell(selectedTarget).Height + cloud.cloud.verticalHeight).ToArray();
            return ability.ImpactPattern.Evaluate(grid, selectedTarget);
        }

        private static IEnumerable<GridCoordinate> Ray(TacticalGrid grid, GridCoordinate start, GridCoordinate direction, int length)
        {
            for (int step = 0; step < length; step++)
            {
                var cell = new GridCoordinate(start.X + direction.X * step, start.Y + direction.Y * step);
                if (!grid.Contains(cell) || !grid.GetCell(cell).IsWalkable) yield break;
                yield return cell;
            }
        }

        private static int MagnetReach(AbilityDefinition ability, PassiveModifierDefinition modifiers)
        {
            int reach = ability.Effects.OfType<PullToPointEffectDefinition>().Select(m => m.reach).DefaultIfEmpty(0).First();
            return reach + (modifiers != null && modifiers.stormOvercharge ? 1 : 0);
        }

        public static AbilityResolution Preview(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units,
            long timelineTick = 0, AreaEffectWorld areas = null, PassiveModifierDefinition modifiers = null, bool wall = false)
        {
            return Resolve(ability, grid, source, selectedTarget, units, false, timelineTick, areas, modifiers, wall);
        }

        public static AbilityResolution Execute(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units,
            long timelineTick = 0, AreaEffectWorld areas = null, PassiveModifierDefinition modifiers = null, bool wall = false)
        {
            return Resolve(ability, grid, source, selectedTarget, units, true, timelineTick, areas, modifiers, wall);
        }

        private static AbilityResolution Resolve(
            AbilityDefinition ability,
            TacticalGrid grid,
            CombatUnit source,
            GridCoordinate selectedTarget,
            IEnumerable<CombatUnit> units,
            bool apply,
            long timelineTick, AreaEffectWorld areas, PassiveModifierDefinition modifiers, bool wall)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            CombatUnit[] battleUnits = units.Where(unit => unit != null).ToArray();
            IReadOnlyCollection<GridCoordinate> targetable =
                GetTargetableCells(ability, grid, source, battleUnits, wall);
            if (!targetable.Contains(selectedTarget))
            {
                throw new InvalidOperationException($"Cell {selectedTarget} is not a valid target for {ability.DisplayName}.");
            }

            bool onWall = wall && IsSurfaceAbility(ability) &&
                          SurfaceRules.TryGetWall(grid, source.Cell, selectedTarget, out _, out _);
            GridCoordinate front = selectedTarget;
            GridCoordinate outward = default;
            if (onWall) SurfaceRules.TryGetWall(grid, source.Cell, selectedTarget, out front, out outward);

            IReadOnlyCollection<GridCoordinate> affected = GetImpactCells(ability, grid, selectedTarget, source, onWall, modifiers);
            var affectedSet = new HashSet<GridCoordinate>(affected);
            CombatUnit[] affectedTargets = battleUnits
                .Where(unit => !unit.IsDefeated && affectedSet.Contains(unit.Cell))
                .ToArray();
            GridCoordinate magnetPoint = onWall ? front : selectedTarget;
            PullToPointEffectDefinition magnet = ability.Effects.OfType<PullToPointEffectDefinition>().FirstOrDefault();
            if (magnet != null)
            {
                // Storm Magnet acts on one resonant: the first on a line from the point within reach
                // (from a wall: only on the line coming straight out of it).
                int reach = MagnetReach(ability, modifiers);
                CombatUnit pulled = onWall
                    ? ForcedMovementCalculator.FindFirstOnRay(grid, front, outward, reach, battleUnits)
                    : ForcedMovementCalculator.FindFirstOnLines(grid, selectedTarget, reach, battleUnits);
                affectedTargets = pulled != null ? new[] { pulled } : Array.Empty<CombatUnit>();
            }

            var strikes = new List<DelayedStrike>();
            var traps = new List<TrapObject>();
            var magmaCells = new List<GridCoordinate>();
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
            int channelStage = 0;
            bool channelEnded = false;

            void AddBurnOrMagma(CombatUnit target, int power, int durationPulses)
            {
                if (modifiers != null && modifiers.magmaWorm)
                {
                    // Magma Trail: every Burning this resonant would apply becomes Magma on the target's cell.
                    if (!magmaCells.Contains(forecastPositions[target])) magmaCells.Add(forecastPositions[target]);
                    return;
                }

                BurnAppliedResult burn = BurningCalculator.Apply(target, forecastBurning[target], power, durationPulses,
                    timelineTick, CountOil(forecastStatuses[target]));
                results.Add(burn);
                TrackBurn(burn, forecastBurning, forecastStatuses);
            }

            void AfterHit(CombatUnit target, ICombatEffectResult result)
            {
                // Purifying Flame: whoever hits the resonant with a melee ability catches fire.
                if (!(result is DamageResult) || target == source || ability.RangeType != AbilityRangeType.Melee) return;
                if (!forecastStatuses[target].Values.Any(s => s.Rules.Mechanic == StatusMechanic.PurifyingFlame)) return;
                if (!forecast.TryGetValue(source, out CombatResourceState sourceState) || sourceState.Health <= 0) return;
                BurnAppliedResult retaliation = BurningCalculator.Apply(source, forecastBurning[source],
                    BurningCalculator.PowerFromAttack(target.Attack, 0.25f), 3, timelineTick, CountOil(forecastStatuses[source]));
                results.Add(retaliation);
                TrackBurn(retaliation, forecastBurning, forecastStatuses);
            }

            ChannelConeEffectDefinition channel = ability.Effects.OfType<ChannelConeEffectDefinition>().FirstOrDefault();
            if (channel != null)
            {
                channelStage = NextChannelStage(ability, source, channel, modifiers);
                channelEnded = channelStage >= channel.MaximumStage;
                double multiplier = channel.stageAttackMultipliers[channelStage - 1];
                foreach (CombatUnit target in affectedTargets)
                {
                    if (forecast[target].Health <= 0) continue;
                    DamageResult hit = CombatEffectCalculator.Damage(target, forecast[target],
                        multiplier * source.Attack * HitMultiplier(ability, grid, target, source.Cell, forecastPositions[target]));
                    results.Add(hit);
                    forecast[target] = CombatEffectCalculator.Apply(forecast[target], hit);
                    if (forecast[target].Health > 0)
                        AddBurnOrMagma(target, BurningCalculator.PowerFromAttack(source.Attack, channel.burnAttackMultiplier), channel.burnDurationPulses);
                    AfterHit(target, hit);
                }
            }

            foreach (AbilityEffectDefinition effect in ability.Effects)
            {
                if (effect is ChannelConeEffectDefinition) continue;
                if (effect is CreateCloudEffectDefinition createCloud)
                {
                    clouds.Add(PreviewCloud(createCloud.cloud, areas, grid, source, ability, selectedTarget, timelineTick));
                    continue;
                }
                if (effect is DelayedStrikeEffectDefinition delayed)
                {
                    IEnumerable<GridCoordinate> jet = onWall ? affected : affected.Where(c => grid.GetCell(c).IsWalkable);
                    double raw = delayed.attackMultiplier * (double)source.Attack;
                    int burnPower = modifiers != null && modifiers.magmaWorm ? 0 : BurningCalculator.PowerFromAttack(source.Attack, delayed.burnAttackMultiplier);
                    strikes.Add(new DelayedStrike(source, ability.StableId, jet, timelineTick + delayed.delayTicks, raw, burnPower,
                        delayed.burnDurationPulses));
                    if (modifiers != null && modifiers.twinEruption)
                        strikes.Add(new DelayedStrike(source, ability.StableId, jet, timelineTick + delayed.delayTicks + 200, raw,
                            burnPower, delayed.burnDurationPulses));
                    continue;
                }
                if (effect is CreateTrapEffectDefinition trapEffect)
                {
                    bool tornado = modifiers != null && modifiers.ragingTornado;
                    traps.Add(onWall
                        ? new TrapObject(source, ability.StableId, front, timelineTick + trapEffect.lifetimeTicks, 0, outward, tornado ? 3 : 2)
                        : new TrapObject(source, ability.StableId, selectedTarget, timelineTick + trapEffect.lifetimeTicks,
                            tornado ? (int)FallRules.DamageForDrop(100, 6) : trapEffect.fallHealthPercent));
                    continue;
                }
                if (effect is ModifyTerrainEffectDefinition terrainEffect)
                {
                    bool titan = modifiers != null && modifiers.titanPillar && terrainEffect.LiftsOccupant;
                    if (onWall)
                    {
                        // A ledge slides out of the wall into the cell in front of it and shoves whoever stands there.
                        CombatUnit occupant = forecastPositions.Where(entry => entry.Value == front && forecast[entry.Key].Health > 0)
                            .Select(entry => entry.Key).FirstOrDefault();
                        if (occupant != null)
                        {
                            var wallCell = new GridCoordinate(front.X - outward.X, front.Y - outward.Y);
                            PushResult shove = ForcedMovementCalculator.Push(grid, occupant, wallCell, front, titan ? 2 : 1,
                                coordinate => forecastPositions.Any(entry => entry.Key != occupant && entry.Value == coordinate));
                            results.Add(shove);
                            forecastPositions[occupant] = shove.Destination;
                            if (shove.Collided) AddCollisionDamage(shove, source, battleUnits, forecast, forecastPositions, results);
                            AddFallDamage(shove, forecastTerrain, forecast, results);
                        }

                        GridCell ground = forecastTerrain[front];
                        int ledge = Math.Min(terrainEffect.MaximumHeight, SurfaceRules.WallTop(grid, new GridCoordinate(front.X - outward.X, front.Y - outward.Y)));
                        TerrainChangeResult ledgeResult = TerrainChangeCalculator.ChangeHeight(ground, ledge - ground.Height,
                            terrainEffect.MinimumHeight, terrainEffect.MaximumHeight);
                        if (ledgeResult.Changed && !forecastPositions.Any(entry => entry.Value == front && forecast[entry.Key].Health > 0))
                        {
                            terrainResults.Add(ledgeResult);
                            forecastTerrain[front] = new GridCell(front, ledgeResult.NewHeight, ledgeResult.IsWalkable);
                        }

                        continue;
                    }

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
                            terrainEffect.HeightDelta + (titan ? 1 : 0),
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
                    if (effect is ApplyBurnEffectDefinition burnEffect)
                    {
                        AddBurnOrMagma(target, BurningCalculator.PowerFromAttack(source.Attack, burnEffect.AttackMultiplier), burnEffect.Duration);
                        continue;
                    }

                    ICombatEffectResult result;
                    if (effect is ApplyStatusEffectDefinition statusEffect)
                    {
                        StatusDefinition status = ResolveStatus(statusEffect.status, ability, modifiers);
                        StatusRules rules = status.Snapshot();
                        if (magnet != null && modifiers != null && modifiers.stormOvercharge && rules.PreventsMovement)
                            rules = new StatusRules(rules.Id, rules.MaximumStacks, rules.Duration * 2, rules.PulseInterval,
                                rules.DamagePerStack, rules.PreventsMovement, rules.Mechanic);
                        forecastStatuses[target].TryGetValue(status.stableId, out var previous);
                        var statusResult = StatusCalculator.Apply(target, rules, statusEffect.stacks,
                            timelineTick, source.Id, previous);
                        forecastStatuses[target][status.stableId] = statusResult.State;
                        result = statusResult;
                    }
                    else result = Calculate(
                        ability,
                        effect,
                        source,
                        target,
                        state,
                        timelineTick,
                        grid,
                        forecastPositions[source],
                        forecastPositions[target],
                        coordinate => forecastPositions.Any(entry =>
                            entry.Key != target && entry.Value == coordinate),
                        magnetPoint);
                    results.Add(result);
                    if (result is PushResult push)
                    {
                        forecastPositions[target] = push.Destination;
                        if (push.Collided) AddCollisionDamage(push, source, battleUnits, forecast, forecastPositions, results);
                        AddFallDamage(push, forecastTerrain, forecast, results);
                    }
                    else if (!(result is StatusAppliedResult))
                    {
                        forecast[target] = CombatEffectCalculator.Apply(state, result);
                        AfterHit(target, result);
                    }
                }
            }

            if (apply)
            {
                if ((clouds.Count > 0 || strikes.Count > 0 || traps.Count > 0 || magmaCells.Count > 0) && areas == null)
                    throw new InvalidOperationException("Clouds, delayed strikes, traps and Magma require battle AreaEffectWorld.");
                CombatEffectResolver.Apply(results);
                foreach (var cloud in clouds) areas.Add(cloud);
                foreach (DelayedStrike strike in strikes) areas.AddStrike(strike);
                foreach (TrapObject trap in traps) areas.AddTrap(trap);
                foreach (GridCoordinate cell in magmaCells)
                    areas.AddMagma(source, cell, timelineTick, BurningCalculator.PowerFromAttack(source.Attack, 0.25f));
                foreach (TerrainChangeResult terrainResult in terrainResults)
                {
                    grid.ApplyTerrainChange(terrainResult);
                }

                if (channel != null)
                {
                    if (channelEnded) source.EndChannel();
                    else source.SetChannel(new ChannelState(ability.StableId, channelStage, source.Cell,
                        CloudAreaEffect.DirectionFrom(source.Cell, selectedTarget), source.ActivationCount));
                }
            }

            return new AbilityResolution(affected, results, terrainResults)
            {
                Clouds = clouds,
                Strikes = strikes,
                Traps = traps,
                MagmaCells = magmaCells,
                ChannelStage = channelStage,
                ChannelEnded = channelEnded
            };
        }

        private static void TrackBurn(BurnAppliedResult burn, IDictionary<CombatUnit, BurningState> forecastBurning,
            IDictionary<CombatUnit, Dictionary<string, StackStatusState>> forecastStatuses)
        {
            forecastBurning[burn.Target] = new BurningState(burn.NewDamagePerPulse, burn.NewNextPulseTick, burn.NewExpireTick);
            if (burn.ConsumedOilStacks <= 0) return;
            foreach (string oilId in forecastStatuses[burn.Target]
                         .Where(s => s.Value.Rules.Mechanic == StatusMechanic.Oil)
                         .Select(s => s.Key).ToArray())
                forecastStatuses[burn.Target].Remove(oilId);
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

        /// <summary>Collision (16.10.8): the displaced unit and the unit it hit each take 0.2 Г— ATK of the displacer.</summary>
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

        /// <summary>Forced displacement off a ledge: every step that drops 3+ units costs fall damage (16.10.11).</summary>
        private static void AddFallDamage(
            PushResult push,
            IReadOnlyDictionary<GridCoordinate, GridCell> terrain,
            IDictionary<CombatUnit, CombatResourceState> forecast,
            ICollection<ICombatEffectResult> results)
        {
            for (int index = 1; index < push.Path.Count; index++)
            {
                if (!forecast.TryGetValue(push.Target, out CombatResourceState state) || state.Health <= 0) return;
                int drop = terrain[push.Path[index - 1]].Height - terrain[push.Path[index]].Height;
                double damage = FallRules.DamageForDrop(push.Target.MaximumHealth, drop);
                if (damage <= 0) continue;
                DamageResult fall = CombatEffectCalculator.PeriodicDamage(push.Target, state, damage, AreaEffectWorld.FallResistanceId);
                results.Add(fall);
                forecast[push.Target] = CombatEffectCalculator.Apply(state, fall);
            }
        }

        /// <summary>Height (16.10.11) and facing (D-033) multipliers of a direct hit from the source position.</summary>
        public static double HitMultiplier(AbilityDefinition ability, TacticalGrid grid, CombatUnit target,
            GridCoordinate sourcePosition, GridCoordinate targetPosition)
        {
            if (sourcePosition == targetPosition) return 1d;
            double height = HitModifiers.HeightMultiplier(ability.RangeType,
                grid.GetCell(sourcePosition).Height, grid.GetCell(targetPosition).Height);
            double facing = HitModifiers.FacingMultiplier(FacingRules.GetSide(targetPosition, target.Facing, sourcePosition));
            return height * facing;
        }

        private static ICombatEffectResult Calculate(
            AbilityDefinition ability,
            AbilityEffectDefinition effect,
            CombatUnit source,
            CombatUnit target,
            CombatResourceState state,
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
                    return CombatEffectCalculator.Damage(target, state,
                        damage.RawDamage(source.Attack) * HitMultiplier(ability, grid, target, sourcePosition, targetPosition));
                case HealEffectDefinition heal:
                    return CombatEffectCalculator.Heal(target, state, heal.Amount);
                case GrantShieldEffectDefinition shield:
                    return CombatEffectCalculator.GrantShield(target, state, shield.Amount);
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
