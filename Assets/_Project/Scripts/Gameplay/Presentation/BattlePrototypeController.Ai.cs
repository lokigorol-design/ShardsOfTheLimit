using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Basic AI turn, ability planning and the next-enemy intent preview.</summary>
    public sealed partial class BattlePrototypeController
    {        private IEnumerator RunBasicAI(CombatUnit unit)
        {
            isBusy = true;
            ClearInteractionState();
            status = $"{unit.DisplayName} is evaluating its turn...";
            unit.CurrentAction = "Thinking";
            RefreshHighlights();
            yield return new WaitForSeconds(0.5f);

            CombatUnit nearestTarget = units
                .Where(candidate => candidate.Team != unit.Team && !candidate.IsDefeated)
                .OrderBy(candidate => candidate.Cell.ManhattanDistance(unit.Cell))
                .ThenBy(candidate => candidate.CurrentHealth + candidate.CurrentShield)
                .FirstOrDefault();
            if (nearestTarget == null)
            {
                aiDecisionRolls.Remove(unit);
                isBusy = false;
                if (!EvaluateBattleEnd()) AdvanceTurn();
                yield break;
            }

            bool canAttack = TryPlanBestAbility(unit, out AbilityDefinition ability, out GridCoordinate abilityTarget);
            bool seekOwnCloud = areaEffects.GetModifiers(unit).NativeMire && !areaEffects.IsInsideAnyCloud(unit.Cell) &&
                MovementRules.BuildMap(grid,unit,IsOccupied).ReachableCosts.Keys
                    .Any(c => areaEffects.IsInsideAnyCloud(c));
            if ((!canAttack || seekOwnCloud) && unit.Channel == null && unit.CurrentActionPoints >= unit.MovementActionCost)
            {
                MovementMap aiMovementMap = MovementRules.BuildMap(grid, unit, IsOccupied);

                IReadOnlyDictionary<GridCoordinate, int> aiReachable = aiMovementMap.ReachableCosts;
                if (aiReachable.Count > 0)
                {
                    GridCoordinate destination = aiReachable.Keys
                        .OrderByDescending(coordinate => areaEffects.GetModifiers(unit).NativeMire && areaEffects.IsInsideAnyCloud(coordinate))
                        .ThenBy(coordinate => coordinate.ManhattanDistance(nearestTarget.Cell))
                        .ThenBy(coordinate => aiReachable[coordinate])
                        .ThenBy(coordinate => coordinate.X)
                        .ThenBy(coordinate => coordinate.Y)
                        .First();
                    IReadOnlyList<GridCoordinate> path = StopAtTrap(aiMovementMap.GetPath(destination));
                    if (path.Count > 1 && unit.SpendActionPoints(unit.MovementActionCost))
                    {
                        int movementCost = MovementRules.PathCost(grid, selectedUnit, path);
                        unit.CurrentAction = $"Moving to {destination}";
                        selectedPath = path;
                        status = $"{unit.DisplayName} moves closer.";
                        feedback.AddLog(
                            $"{unit.DisplayName} moves {unit.Cell} → {destination} " +
                            $"({movementCost}/{MovementBudget(unit)} movement, -{unit.MovementActionCost} AP)");
                        RefreshHighlights();
                        yield return AnimateMovement(unit, path);
                        yield return EnterCells(unit, path.Skip(1).ToArray(), true);
                        yield return TriggerTrapIfAny(unit, new[] { unit.Cell });
                        if (EvaluateBattleEnd()) yield break;
                        resolvedActions++;
                        feedback.AddLog($"{unit.DisplayName} reached {unit.Cell}");
                        selectedPath = Array.Empty<GridCoordinate>();
                        canAttack = TryPlanBestAbility(unit, out ability, out abilityTarget);
                    }
                }
            }

            int abilityCost = canAttack ? AbilityCost(unit, ability) : 0;
            if (canAttack && CanUseAbility(unit, ability) && unit.SpendActionPoints(abilityCost))
            {
                mode = InteractionMode.AbilityTargeting;
                selectedAbilityTarget = abilityTarget;
                targetable = AbilityRuntime.GetTargetableCells(ability, grid, unit, units);
                targetingRange = AbilityRuntime.GetTargetingHighlightCells(ability, grid, unit, units);
                abilityPreview = AbilityRuntime.Preview(
                    ability, grid, unit, abilityTarget, units, turns.CurrentTick, areaEffects, ModifiersOf(unit));
                previewImpact = abilityPreview.ImpactCells;
                unit.CurrentAction = $"Targeting {ability.DisplayName}";
                status = $"{unit.DisplayName} previews {ability.DisplayName}.";
                feedback.AddLog($"{unit.DisplayName} casts {ability.DisplayName} at {abilityTarget} (-{abilityCost} AP)");
                RefreshHighlights();
                yield return new WaitForSeconds(0.35f);
                FaceTowards(unit, unit.Cell, abilityTarget);
                yield return PlayAbilityVisual(unit, ability, abilityTarget, previewImpact);


                AbilityResolution resolution = ResolveAbility(unit, ability, abilityTarget);
                EnsurePreviewMatchesExecution(abilityPreview, resolution);
                yield return PresentAbilityResults(unit, resolution);
                resolvedActions++;
                status = BuildAbilitySummary(resolution, false);
                ClearInteractionState();
                RefreshHighlights();
                yield return new WaitForSeconds(0.45f);
            }

            if (EvaluateBattleEnd()) yield break;

            // AI facing step: turn towards the nearest enemy so its back is not left open.
            CombatUnit closestEnemy = units
                .Where(candidate => candidate.Team != unit.Team && !candidate.IsDefeated)
                .OrderBy(candidate => candidate.Cell.ManhattanDistance(unit.Cell))
                .FirstOrDefault();
            if (closestEnemy != null) FaceTowards(unit, unit.Cell, closestEnemy.Cell);
            unit.CurrentAction = "Turn complete";
            feedback.AddLog($"{unit.DisplayName} ends the turn");
            aiDecisionRolls.Remove(unit);
            FinishActivation(unit);
            isBusy = false;
            AdvanceTurn();
        }

        private bool TryPlanBestAbility(
            CombatUnit source,
            out AbilityDefinition selectedAbility,
            out GridCoordinate selectedTarget,
            bool logDecision = true,
            int? actionPointsOverride = null)
        {
            selectedAbility = null;
            selectedTarget = default;
            if (!definitionsByUnit.TryGetValue(source, out UnitDefinition definition)) return false;
            int availableActionPoints = actionPointsOverride ?? source.CurrentActionPoints;

            var plans = new List<AiAbilityPlan>();
            foreach (AbilityDefinition ability in definition.Abilities)
            {
                if (source.GetCooldown(ability.StableId) > 0 || availableActionPoints < AbilityCost(source, ability) ||
                    ChannelBlockReason(source, ability) != null)
                {
                    continue;
                }

                foreach (GridCoordinate candidate in AbilityRuntime.GetTargetableCells(ability, grid, source, units))
                {
                    AbilityResolution preview = AbilityRuntime.Preview(
                        ability, grid, source, candidate, units, turns.CurrentTick, areaEffects, ModifiersOf(source));
                    int damageValue = preview.Results
                        .Where(result => result.Kind == CombatEffectKind.Damage)
                        .Sum(result => (result.Target.Team == source.Team ? -1 : 1) *
                                       (Math.Max(0, -result.HealthDelta) + Math.Max(0, -result.ShieldDelta)));
                    damageValue += preview.Results.OfType<BurnAppliedResult>()
                        .Sum(result => (result.Target.Team == source.Team ? -1 : 1) *
                                       result.AddedDamagePerPulse * GetBurnPulseCount(result));
                    int healingValue = preview.Results
                        .Where(result => result.Kind == CombatEffectKind.Heal)
                        .Sum(result => (result.Target.Team == source.Team ? 1 : -1) *
                                       Math.Max(0, result.HealthDelta));
                    int shieldValue = preview.Results
                        .Where(result => result.Kind == CombatEffectKind.Shield)
                        .Sum(result => (result.Target.Team == source.Team ? 1 : -1) *
                                       Math.Max(0, result.ShieldDelta));
                    int pushValue = preview.Results.OfType<PushResult>()
                        .Sum(result => (result.Target.Team == source.Team ? -1 : 1) * result.StepsMoved * 2);
                    int terrainValue = preview.TerrainResults.Count(result => result.Changed);
                    int utility = shieldValue + pushValue + terrainValue - source.Cell.ManhattanDistance(candidate) + ScoreAreaControl(source, preview);
                    if (damageValue <= 0 && healingValue <= 0 && utility <= 0) continue;
                    plans.Add(new AiAbilityPlan
                    {
                        Ability = ability,
                        Target = candidate,
                        DamageValue = Math.Max(0, damageValue),
                        HealingValue = Math.Max(0, healingValue),
                        UtilityValue = utility
                    });
                }
            }

            AiAbilityPlan bestDamage = plans
                .Where(plan => plan.DamageValue > 0)
                .OrderByDescending(plan => plan.DamageValue)
                .ThenByDescending(plan => plan.UtilityValue)
                .FirstOrDefault();
            AiAbilityPlan bestHealing = plans
                .Where(plan => plan.HealingValue > 0)
                .OrderByDescending(plan => plan.HealingValue)
                .ThenByDescending(plan => plan.UtilityValue)
                .FirstOrDefault();

            AiAbilityPlan chosen;
            if (bestDamage != null && bestHealing != null)
            {
                double attackProbability = AiDecisionMath.CalculateFirstOptionProbability(
                    bestDamage.DamageValue,
                    bestHealing.HealingValue);
                if (!aiDecisionRolls.TryGetValue(source, out float roll))
                {
                    roll = UnityEngine.Random.value;
                    aiDecisionRolls[source] = roll;
                }

                chosen = roll < attackProbability ? bestDamage : bestHealing;
                if (logDecision)
                {
                    feedback.AddLog(
                        $"{source.DisplayName} weighs damage {bestDamage.DamageValue} vs heal {bestHealing.HealingValue}: " +
                        $"attack {attackProbability:P0}, roll {roll:P0} → {chosen.Ability.DisplayName}");
                }
            }
            else
            {
                chosen = bestDamage ?? bestHealing ?? plans
                    .OrderByDescending(plan => plan.UtilityValue)
                    .FirstOrDefault();
            }

            if (definition.Abilities.Any(a => a.Effects.OfType<CreateCloudEffectDefinition>().Any()))
            {
                var controlPlan = plans.OrderByDescending(p => p.DamageValue + p.HealingValue + p.UtilityValue).FirstOrDefault();
                if (controlPlan != null) chosen = controlPlan;
            }
            if (chosen == null) return false;
            selectedAbility = chosen.Ability;
            selectedTarget = chosen.Target;
            return true;
        }

        private void RefreshNextEnemyIntent()
        {
            if (turns == null || battleEnded)
            {
                nextEnemyIntent = null;
                return;
            }

            CombatUnit enemy = turns.GetUpcomingEvents(30, false, false)
                .Where(entry => entry.Kind == TimelineEventKind.Activation)
                .Select(entry => entry.Unit)
                .FirstOrDefault(unit => unit.Team == Team.Opponent && !unit.IsDefeated);
            if (enemy == null)
            {
                nextEnemyIntent = null;
                return;
            }

            BattleIntentKind first;
            BattleIntentKind second = BattleIntentKind.None;
            if (TryPlanBestAbility(enemy, out AbilityDefinition ability, out _, false, ActionsPerTurn))
            {
                first = GetIntentKind(ability);
            }
            else if (enemy.CurrentActionPoints >= enemy.MovementActionCost || turns.CurrentUnit != enemy)
            {
                first = BattleIntentKind.Movement;
                second = PredictFollowUpIntent(enemy);
            }
            else
            {
                first = BattleIntentKind.None;
            }

            var updated = new EnemyIntentPreview { Unit = enemy, First = first, Second = second };
            bool changed = nextEnemyIntent == null || nextEnemyIntent.Unit != updated.Unit ||
                           nextEnemyIntent.First != updated.First || nextEnemyIntent.Second != updated.Second;
            nextEnemyIntent = updated;
            if (changed && feedback != null)
            {
                feedback.AddLog($"Next enemy intent: {enemy.DisplayName} {updated.Codes}");
            }
        }

        private BattleIntentKind PredictFollowUpIntent(CombatUnit unit)
        {
            if (!definitionsByUnit.TryGetValue(unit, out UnitDefinition definition)) return BattleIntentKind.None;
            IReadOnlyList<AbilityDefinition> ready = definition.Abilities
                .Where(ability => unit.GetCooldown(ability.StableId) == 0 &&
                                  ability.ActionCost <= Math.Max(0, ActionsPerTurn - unit.MovementActionCost))
                .ToArray();
            bool woundedAlly = units.Any(candidate => candidate.Team == unit.Team && !candidate.IsDefeated &&
                                                        (candidate.CurrentHealth < candidate.MaximumHealth ||
                                                         candidate.CurrentShield < candidate.MaximumShield));
            if (woundedAlly && ready.Any(ability => GetIntentKind(ability) == BattleIntentKind.Help))
            {
                return BattleIntentKind.Help;
            }

            AbilityDefinition offensive = ready.FirstOrDefault(ability =>
                GetIntentKind(ability) == BattleIntentKind.Attack);
            if (offensive != null) return BattleIntentKind.Attack;
            return ready.Any(ability => GetIntentKind(ability) == BattleIntentKind.Special)
                ? BattleIntentKind.Special
                : BattleIntentKind.None;
        }

        private static BattleIntentKind GetIntentKind(AbilityDefinition ability)
        {
            if (ability.Effects.Any(effect => effect is PushEffectDefinition ||
                                              effect is ModifyTerrainEffectDefinition))
            {
                return BattleIntentKind.Special;
            }

            if (ability.Effects.Any(effect => effect is HealEffectDefinition ||
                                              effect is GrantShieldEffectDefinition))
            {
                return BattleIntentKind.Help;
            }

            return ability.Effects.Any(effect => effect is DamageEffectDefinition)
                ? BattleIntentKind.Attack
                : BattleIntentKind.Special;
        }
    }
}
