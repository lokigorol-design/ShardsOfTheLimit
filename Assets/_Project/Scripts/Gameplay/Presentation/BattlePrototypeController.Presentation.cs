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
    /// <summary>Animations and on-field presentation of resolved abilities, timeline results and VFX.</summary>
    public sealed partial class BattlePrototypeController
    {        private IEnumerator AnimateMovement(CombatUnit unit, IReadOnlyList<GridCoordinate> path)
        {
            if (unit.MovementMode == MovementMode.Burrow && path.Count > 1)
            {
                FaceTowards(unit, path[0], path[path.Count - 1]);
                yield return AnimateBurrow(unit, path[path.Count - 1]);
                yield break;
            }

            for (int index = 1; index < path.Count; index++)
            {
                GridCoordinate next = path[index];
                FaceTowards(unit, path[index - 1], next);
                Vector3 destination = UnitWorldPosition(next);
                Transform body = unitViews[unit].transform;
                while ((body.position - destination).sqrMagnitude > 0.0004f)
                {
                    body.position = Vector3.MoveTowards(body.position, destination, Time.deltaTime * 3.6f);
                    yield return null;
                }

                unit.MoveTo(next);
            }
        }

        /// <summary>Deep Burrow: sink into the ground, travel unseen, emerge at the destination.</summary>
        private IEnumerator AnimateBurrow(CombatUnit unit, GridCoordinate destinationCell)
        {
            const float depth = 1.4f;
            const float speed = 4.5f;
            Transform body = unitViews[unit].transform;
            Vector3 startGround = EffectWorldPosition(unit.Cell);
            AbilityVfx.BurrowDig(startGround);
            Vector3 sunk = body.position + Vector3.down * depth;
            while ((body.position - sunk).sqrMagnitude > 0.0004f)
            {
                body.position = Vector3.MoveTowards(body.position, sunk, Time.deltaTime * speed);
                if (UnityEngine.Random.value < 0.5f) AbilityVfx.BurrowTrail(startGround);
                yield return null;
            }

            // A line of dust and rumbling ground runs from the entry hole to the exit.
            Vector3 surface = UnitWorldPosition(destinationCell);
            Vector3 endGround = EffectWorldPosition(destinationCell);
            body.position = surface + Vector3.down * depth;
            float travel = 0f;
            float travelDuration = Mathf.Clamp(Vector3.Distance(startGround, endGround) * 0.08f, 0.2f, 0.55f);
            while (travel < travelDuration)
            {
                travel += Time.deltaTime;
                Vector3 underground = Vector3.Lerp(startGround, endGround, travel / travelDuration);
                AbilityVfx.BurrowTrail(underground);
                AbilityVfx.BurrowTrail(underground);
                VfxSystem.Instance.Shake(0.02f, 0.05f);
                yield return null;
            }

            AbilityVfx.BurrowEmerge(endGround);
            while ((body.position - surface).sqrMagnitude > 0.0004f)
            {
                body.position = Vector3.MoveTowards(body.position, surface, Time.deltaTime * speed * 1.6f);
                yield return null;
            }

            unit.MoveTo(destinationCell);
        }

        private IEnumerator AnimateForcedMovement(PushResult push)
        {
            if (!unitViews.TryGetValue(push.Target, out CombatUnitView view)) yield break;
            for (int index = 1; index < push.Path.Count; index++)
            {
                Vector3 destination = UnitWorldPosition(push.Path[index]);
                while ((view.transform.position - destination).sqrMagnitude > 0.0004f)
                {
                    view.transform.position = Vector3.MoveTowards(
                        view.transform.position,
                        destination,
                        Time.deltaTime * 5.2f);
                    yield return null;
                }
            }
        }

        private IEnumerator PresentAbilityResults(CombatUnit source, AbilityResolution resolution)
        {
            IReadOnlyList<ICombatEffectResult> results = resolution.Results;
            RefreshCloudViews();
            foreach (var cloud in resolution.Clouds) feedback.AddLog($"{cloud.Rules.Id}: {cloud.OccupiedCells.Count} cells, {cloud.StacksPerTick} stacks/tick, direction {cloud.MovementDirection}");
            var hitViews = new List<CombatUnitView>();
            foreach (ICombatEffectResult result in results)
            {
                if (!unitViews.TryGetValue(result.Target, out CombatUnitView view)) continue;
                Vector3 labelPosition = view.transform.position + Vector3.up * 0.75f;
                if (result.Kind == CombatEffectKind.Damage)
                {
                    if (!hitViews.Contains(view)) hitViews.Add(view);
                    if (result.HealthDelta < 0 || result.ShieldDelta < 0)
                        AbilityVfx.Hit(view.transform.position, result.HealthDelta == 0);
                    if (result.ShieldDelta < 0)
                    {
                        feedback.AddFloatingText(labelPosition, $"{result.ShieldDelta} SHIELD", new Color(0.2f, 0.75f, 1f));
                    }

                    if (result.HealthDelta < 0)
                    {
                        feedback.AddFloatingText(labelPosition + Vector3.up * 0.22f, $"{result.HealthDelta} HP", new Color(1f, 0.28f, 0.2f));
                    }
                }
                else if (result.Kind == CombatEffectKind.Heal && result.HealthDelta > 0)
                {
                    feedback.AddFloatingText(labelPosition, $"+{result.HealthDelta} HP", new Color(0.28f, 1f, 0.45f));
                }
                else if (result.Kind == CombatEffectKind.Shield && result.ShieldDelta > 0)
                {
                    feedback.AddFloatingText(labelPosition, $"+{result.ShieldDelta} SHIELD", new Color(0.25f, 0.9f, 1f));
                }
                else if (result is PushResult push)
                {
                    string pushText = (push.StepsMoved > 0 ? $"PUSH {push.StepsMoved}" : "PUSH BLOCKED") +
                                      (push.Collided ? " · COLLISION" : string.Empty);
                    feedback.AddFloatingText(labelPosition, pushText, new Color(0.85f, 0.45f, 1f));
                }
                else if (result is BurnAppliedResult burn)
                {
                    feedback.AddFloatingText(
                        labelPosition + Vector3.up * 0.22f,
                        $"BURN {burn.NewDamagePerPulse}/100t · {GetBurnPulseCount(burn)}x" +
                        (burn.ConsumedOilStacks > 0 ? $" · OIL ×{burn.ConsumedOilStacks}" : string.Empty),
                        new Color(1f, 0.42f, 0.05f));
                }
                else if (result is StatusAppliedResult statusResult)
                    feedback.AddFloatingText(labelPosition, $"{statusResult.State.Rules.Id} ×{statusResult.State.Stacks}", Color.yellow);

                feedback.AddLog(DescribeResult(result, false));
                if (result.Kind == CombatEffectKind.Damage && result.Target.Team == source.Team)
                {
                    feedback.AddLog($"Friendly fire: {result.Target.DisplayName} was caught in the impact area");
                }
                else if (result.Kind == CombatEffectKind.Heal && result.Target.Team != source.Team &&
                         result.HealthDelta > 0)
                {
                    feedback.AddLog($"Enemy aided: {result.Target.DisplayName} received area healing");
                }
            }

            foreach (CombatUnitView hitView in hitViews)
                if (hitView.TryGetComponent(out ProceduralAnimator animator) && !hitView.Unit.IsDefeated)
                    StartCoroutine(animator.Hit());
            yield return feedback.PlayHitReactions(hitViews);
            foreach (DamageResult result in results.OfType<DamageResult>().Where(result => result.Defeated))
            {
                if (unitViews.TryGetValue(result.Target, out CombatUnitView defeatedView))
                {
                    feedback.AddLog($"{result.Target.DisplayName} is defeated");
                    AbilityVfx.Defeat(defeatedView.transform.position);
                    HideDefeated(defeatedView);
                }
            }

            foreach (PushResult push in results.OfType<PushResult>().Where(result => result.StepsMoved > 0))
            {
                yield return AnimateForcedMovement(push);
                yield return EnterCells(push.Target, push.Path.Skip(1).ToArray(), false);
                yield return TriggerTrapIfAny(push.Target, push.Path.Skip(1));
            }

            foreach (TerrainChangeResult terrainResult in resolution.TerrainResults)
            {
                yield return AnimateCellRise(terrainResult);
                Vector3 labelPosition = EffectWorldPosition(terrainResult.Coordinate) + Vector3.up * 0.35f;
                feedback.AddFloatingText(
                    labelPosition,
                    $"HEIGHT {terrainResult.PreviousHeight}→{terrainResult.NewHeight}",
                    new Color(0.78f, 0.55f, 1f));
                feedback.AddLog(
                    $"Cell {terrainResult.Coordinate}: height {terrainResult.PreviousHeight} → {terrainResult.NewHeight}");
                foreach (CombatUnit unit in units.Where(unit =>
                             !unit.IsDefeated && unit.Cell == terrainResult.Coordinate))
                {
                    yield return AnimateUnitToCurrentCell(unit);
                }
            }
        }

        /// <summary>A defeated resonant topples and sinks (procedural death), then disappears.</summary>
        private void HideDefeated(CombatUnitView view)
        {
            if (view == null || !view.gameObject.activeSelf) return;
            if (view.TryGetComponent(out ProceduralAnimator animator))
            {
                if (!animator.IsDying) StartCoroutine(animator.Die());
                return;
            }

            view.gameObject.SetActive(false);
        }

        /// <summary>Attack animation of the caster; Long Bite has its own lunge in the signature effect.</summary>
        private void PlayCastAnimation(CombatUnit source, AbilityDefinition ability)
        {
            if (ability.StableId == "blaze-long-bite" || !unitViews.TryGetValue(source, out CombatUnitView view) ||
                !view.TryGetComponent(out ProceduralAnimator animator)) return;
            StartCoroutine(animator.Lunge(ability.RangeType == AbilityRangeType.Melee));
        }

        /// <summary>A walking path ends on the first trapped cell: the trap stops the resonant there.</summary>
        private IReadOnlyList<GridCoordinate> StopAtTrap(IReadOnlyList<GridCoordinate> path)
        {
            if (areaEffects == null || path.Count < 2) return path;
            for (int index = 1; index < path.Count; index++)
            {
                if (areaEffects.TrapAt(path[index]) != null) return path.Take(index + 1).ToArray();
            }

            return path;
        }

        /// <summary>Fires the first trap among <paramref name="cells"/> on the unit that entered it.</summary>
        private IEnumerator TriggerTrapIfAny(CombatUnit unit, IEnumerable<GridCoordinate> cells)
        {
            if (areaEffects == null || unit == null || unit.IsDefeated) yield break;
            GridCoordinate? trapped = cells.Cast<GridCoordinate?>().FirstOrDefault(cell => areaEffects.TrapAt(cell.Value) != null);
            if (!trapped.HasValue) yield break;
            TrapTrigger trigger = areaEffects.TriggerTrap(unit, trapped.Value);
            if (trigger == null) yield break;
            if (trigger.Push != null)
            {
                // Wall trap: the whirlwind shoves the victim away from the wall.
                feedback.AddLog($"{unit.DisplayName} triggers a wall Lift Trap at {trapped.Value}: pushed to {trigger.Push.Destination}" +
                                (trigger.Push.Collided ? $", collision at {trigger.Push.CollisionCell.Value}" : string.Empty));
                AbilityVfx.TrapMark(EffectWorldPosition(trapped.Value));
                yield return AnimateForcedMovement(trigger.Push);
                if (trigger.Push.StepsMoved > 0)
                    yield return EnterCells(unit, trigger.Push.Path.Skip(1).ToArray(), false);
            }
            else if (unitViews.TryGetValue(unit, out CombatUnitView tossed))
            {
                feedback.AddLog($"{unit.DisplayName} triggers Lift Trap at {trapped.Value}: thrown up");
                yield return AbilityVfx.LiftTrapToss(tossed.transform, EffectWorldPosition(trapped.Value));
            }

            foreach (DamageResult hit in trigger.Damage)
            {
                feedback.AddLog(DescribeResult(hit, false));
                if (!unitViews.TryGetValue(hit.Target, out CombatUnitView view)) continue;
                Vector3 label = view.transform.position + Vector3.up * 0.75f;
                if (hit.ShieldDelta < 0) feedback.AddFloatingText(label, $"{hit.ShieldDelta} SHIELD", new Color(0.2f, 0.75f, 1f));
                if (hit.HealthDelta < 0) feedback.AddFloatingText(label + Vector3.up * 0.22f, $"{hit.HealthDelta} HP", new Color(1f, 0.28f, 0.2f));
                if (hit.Target.IsDefeated)
                {
                    AbilityVfx.Defeat(view.transform.position);
                    HideDefeated(view);
                }
            }
        }

        /// <summary>Earth Pillar and other terrain: the column grinds to its new height, units ride along.</summary>
        private IEnumerator AnimateCellRise(TerrainChangeResult change)
        {
            if (!cellViews.TryGetValue(change.Coordinate, out CellView view))
            {
                RefreshCellVisual(change.Coordinate);
                yield break;
            }

            GetCellColumnGeometry(change.PreviousHeight, out float fromCenter, out float fromHeight);
            GetCellColumnGeometry(change.NewHeight, out float toCenter, out float toHeight);
            AbilityVfx.PillarRumble(EffectWorldPosition(change.Coordinate));
            CombatUnit rider = units.FirstOrDefault(unit => !unit.IsDefeated && unit.Cell == change.Coordinate);
            Transform riderView = rider != null && unitViews.TryGetValue(rider, out CombatUnitView riderBody) ? riderBody.transform : null;
            float riderStart = riderView != null ? riderView.position.y : 0f;
            float rise = (change.NewHeight - change.PreviousHeight) * HeightStep;
            float elapsed = 0f;
            const float duration = 0.45f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                Vector3 position = view.transform.position;
                position.y = Mathf.Lerp(fromCenter, toCenter, t);
                view.transform.position = position;
                Vector3 scale = view.transform.localScale;
                scale.y = Mathf.Lerp(fromHeight, toHeight, t);
                view.transform.localScale = scale;
                if (riderView != null)
                {
                    Vector3 riderPosition = riderView.position;
                    riderPosition.y = riderStart + rise * t;
                    riderView.position = riderPosition;
                }

                yield return null;
            }

            RefreshCellVisual(change.Coordinate);
        }

        private IEnumerator AnimateUnitToCurrentCell(CombatUnit unit)
        {
            if (!unitViews.TryGetValue(unit, out CombatUnitView view)) yield break;
            Vector3 destination = UnitWorldPosition(unit.Cell);
            while ((view.transform.position - destination).sqrMagnitude > 0.0004f)
            {
                view.transform.position = Vector3.MoveTowards(
                    view.transform.position,
                    destination,
                    Time.deltaTime * 3.2f);
                yield return null;
            }
        }

        private static string BuildAbilitySummary(AbilityResolution resolution, bool preview)
        {
            var descriptions = resolution.Results
                .Where(result => result.HealthDelta != 0 || result.ShieldDelta != 0 ||
                                 result.Kind == CombatEffectKind.Push ||
                                 result.Kind == CombatEffectKind.Burning || result.Kind == CombatEffectKind.Status)
                .Select(result => DescribeResult(result, preview))
                .Concat(resolution.TerrainResults.Select(result =>
                    $"cell {result.Coordinate}: {(preview ? "expected " : string.Empty)}height {result.PreviousHeight}→{result.NewHeight}"))
                .Concat(resolution.Clouds.Select(c => $"{c.Rules.Id}: {c.OccupiedCells.Count} cells, +{c.StacksPerTick} stacks/{c.Rules.PulseInterval}t"))
                .ToArray();
            return descriptions.Length == 0
                ? "No effect."
                : string.Join(", ", descriptions);
        }

        private static string DescribeResult(ICombatEffectResult result, bool preview)
        {
            string prefix = preview ? "expected " : string.Empty;
            switch (result.Kind)
            {
                case CombatEffectKind.Status:
                    var statusResult = (StatusAppliedResult)result;
                    return $"{result.Target.DisplayName}: {statusResult.State.Rules.Id} ×{statusResult.State.Stacks}, until tick {statusResult.State.ExpireTick}";
                case CombatEffectKind.Damage:
                    return $"{result.Target.DisplayName}: {prefix}{-result.ShieldDelta} shield damage, {-result.HealthDelta} health damage";
                case CombatEffectKind.Heal:
                    return $"{result.Target.DisplayName}: {prefix}+{result.HealthDelta} health";
                case CombatEffectKind.Shield:
                    return $"{result.Target.DisplayName}: {prefix}+{result.ShieldDelta} shield";
                case CombatEffectKind.Push:
                    var push = (PushResult)result;
                    string collision = push.Collided ? $", collision at {push.CollisionCell.Value}" : string.Empty;
                    return push.StepsMoved > 0
                        ? $"{result.Target.DisplayName}: {prefix}push {push.Origin}→{push.Destination}{collision}"
                        : $"{result.Target.DisplayName}: {prefix}push blocked{collision}";
                case CombatEffectKind.Burning:
                    if (result is BurnAppliedResult burn)
                    {
                        string oil = burn.ConsumedOilStacks > 0 ? $", burned {burn.ConsumedOilStacks} oil" : string.Empty;
                        return $"{result.Target.DisplayName}: {prefix}burn {burn.AddedDamagePerPulse}/100 ticks{oil}, " +
                               $"strongest {burn.NewDamagePerPulse}/100 ticks for {GetBurnPulseCount(burn)} pulses";
                    }

                    var tick = (BurnTickResult)result;
                    int remainingPulses = tick.NextPulseTick <= tick.ExpireTickAfterTick
                        ? (int)((tick.ExpireTickAfterTick - tick.NextPulseTick) /
                                BurningCalculator.DefaultPulseInterval) + 1
                        : 0;
                    return $"{result.Target.DisplayName}: burn pulse {tick.ShieldDamage} shield, " +
                           $"{tick.HealthDamage} health; {remainingPulses} pulses remain";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>Signature effect for worm and toad abilities; generic animation for the rest.</summary>
        private IEnumerator PlayAbilityVisual(
            CombatUnit source,
            AbilityDefinition ability,
            GridCoordinate target,
            IReadOnlyCollection<GridCoordinate> impact)
        {
            PlayCastAnimation(source, ability);
            Transform sourceView = unitViews.TryGetValue(source, out CombatUnitView view) ? view.transform : null;
            CombatUnit targetUnit = units.FirstOrDefault(unit => !unit.IsDefeated && unit.Cell == target);
            PullToPointEffectDefinition magnet = ability.Effects.OfType<PullToPointEffectDefinition>().FirstOrDefault();
            if (magnet != null) targetUnit = ForcedMovementCalculator.FindFirstOnLines(grid, target, magnet.reach, units);
            var cloudEffect = ability.Effects.OfType<CreateCloudEffectDefinition>().FirstOrDefault();
            var context = new AbilityVfxContext
            {
                SourceView = sourceView,
                Source = sourceView != null ? sourceView.position : UnitWorldPosition(source.Cell),
                Target = targetUnit != null && unitViews.TryGetValue(targetUnit, out CombatUnitView targetView)
                    ? targetView.transform.position
                    : EffectWorldPosition(target) + Vector3.up * 0.3f,
                TargetView = targetUnit != null && unitViews.TryGetValue(targetUnit, out CombatUnitView targetBody)
                    ? targetBody.transform
                    : null,
                ImpactCells = impact.Select(EffectWorldPosition).ToArray(),
                CloudColor = cloudEffect?.cloud != null ? cloudEffect.cloud.color : Color.green,
                Point = EffectWorldPosition(target)
            };
            if (AbilityVfx.TryCreate(ability.StableId, context, out IEnumerator signature))
            {
                yield return signature;
                yield break;
            }

            yield return feedback.PlayAbility(
                context.Source + Vector3.up * 0.4f,
                EffectWorldPosition(target) + Vector3.up * 0.45f,
                context.ImpactCells,
                GetAbilityColor(ability),
                GetAbilityVisualKind(ability));
        }

        private void PresentTimelineResults(IReadOnlyList<ICombatEffectResult> results)
        {
            foreach (DelayedStrike strike in areaEffects?.TakeFiredStrikes() ?? Array.Empty<DelayedStrike>())
            {
                foreach (GridCoordinate cell in strike.Cells) AbilityVfx.GeyserEruption(EffectWorldPosition(cell));
                feedback.AddLog($"Fire Geyser erupts at {string.Join(", ", strike.Cells)}");
            }

            foreach (ICombatEffectResult tick in results)
            {
                if (unitViews.TryGetValue(tick.Target, out CombatUnitView view))
                {
                    Vector3 position = view.transform.position + Vector3.up * 0.85f;
                    feedback.AddFloatingText(position, tick is StatusAppliedResult s ? $"{s.State.Rules.Id} ×{s.State.Stacks}" :
                        $"{tick.Kind} HP {tick.HealthDelta:+0;-0;0} SH {tick.ShieldDelta:+0;-0;0}", new Color(1f, 0.65f, 0.1f));
                    if (tick.HealthDelta < 0 || tick.ShieldDelta < 0) AbilityVfx.Hit(view.transform.position, tick.HealthDelta == 0);
                    if (tick.Target.IsDefeated)
                    {
                        AbilityVfx.Defeat(view.transform.position);
                        HideDefeated(view);
                    }
                }

                feedback.AddLog(DescribeResult(tick, false));
            }
        }

        private static void EnsurePreviewMatchesExecution(
            AbilityResolution preview,
            AbilityResolution execution)
        {
            if (preview == null)
            {
                throw new InvalidOperationException("Ability was executed without a preview.");
            }

            if (!new HashSet<GridCoordinate>(preview.ImpactCells).SetEquals(execution.ImpactCells) ||
                preview.Results.Count != execution.Results.Count ||
                preview.Clouds.Count != execution.Clouds.Count ||
                preview.TerrainResults.Count != execution.TerrainResults.Count)
            {
                throw new InvalidOperationException("Ability preview diverged from execution.");
            }

            for (int index = 0; index < preview.Results.Count; index++)
            {
                ICombatEffectResult expected = preview.Results[index];
                ICombatEffectResult actual = execution.Results[index];
                if (expected.Target != actual.Target || expected.Kind != actual.Kind ||
                    expected.HealthDelta != actual.HealthDelta || expected.ShieldDelta != actual.ShieldDelta)
                {
                    throw new InvalidOperationException("Ability preview resource changes diverged from execution.");
                }

                if (expected is PushResult expectedPush && actual is PushResult actualPush &&
                    !expectedPush.Path.SequenceEqual(actualPush.Path))
                {
                    throw new InvalidOperationException("Ability push preview diverged from execution.");
                }

                if (expected is StatusAppliedResult expectedStatus && actual is StatusAppliedResult actualStatus &&
                    (expectedStatus.State.Rules.Id != actualStatus.State.Rules.Id ||
                     expectedStatus.State.Stacks != actualStatus.State.Stacks ||
                     expectedStatus.State.ExpireTick != actualStatus.State.ExpireTick))
                    throw new InvalidOperationException("Ability status preview diverged from execution.");

                if (expected is BurnAppliedResult expectedBurn && actual is BurnAppliedResult actualBurn &&
                    (expectedBurn.AddedDamagePerPulse != actualBurn.AddedDamagePerPulse ||
                     expectedBurn.NewDamagePerPulse != actualBurn.NewDamagePerPulse ||
                     expectedBurn.NewNextPulseTick != actualBurn.NewNextPulseTick ||
                     expectedBurn.NewExpireTick != actualBurn.NewExpireTick))
                {
                    throw new InvalidOperationException("Ability burn preview diverged from execution.");
                }
            }

            for (int index = 0; index < preview.Clouds.Count; index++)
            {
                var expected = preview.Clouds[index]; var actual = execution.Clouds[index];
                if (!new HashSet<GridCoordinate>(expected.OccupiedCells).SetEquals(actual.OccupiedCells) ||
                    expected.OwnerId != actual.OwnerId || expected.MovementDirection != actual.MovementDirection ||
                    expected.LowerHeight != actual.LowerHeight || expected.StacksPerTick != actual.StacksPerTick)
                    throw new InvalidOperationException("Cloud preview diverged from execution.");
            }
            for (int index = 0; index < preview.TerrainResults.Count; index++)
            {
                TerrainChangeResult expected = preview.TerrainResults[index];
                TerrainChangeResult actual = execution.TerrainResults[index];
                if (expected.Coordinate != actual.Coordinate ||
                    expected.PreviousHeight != actual.PreviousHeight ||
                    expected.NewHeight != actual.NewHeight)
                {
                    throw new InvalidOperationException("Ability terrain preview diverged from execution.");
                }
            }
        }

        private static Color GetAbilityColor(AbilityDefinition ability)
        {
            if (ability.Effects.Any(effect => effect is HealEffectDefinition))
            {
                return new Color(0.2f, 1f, 0.42f);
            }

            if (ability.Effects.Any(effect => effect is GrantShieldEffectDefinition))
            {
                return new Color(0.18f, 0.82f, 1f);
            }

            if (ability.Effects.Any(effect => effect is PushEffectDefinition))
            {
                return new Color(0.78f, 0.3f, 1f);
            }

            if (ability.Effects.Any(effect => effect is ModifyTerrainEffectDefinition))
            {
                return new Color(0.68f, 0.42f, 1f);
            }

            return new Color(1f, 0.32f, 0.06f);
        }

        private static AbilityVisualKind GetAbilityVisualKind(AbilityDefinition ability)
        {
            if (ability.Effects.Any(effect => effect is ModifyTerrainEffectDefinition ||
                                              effect is PushEffectDefinition))
            {
                return AbilityVisualKind.Special;
            }

            if (ability.Effects.Any(effect => effect is HealEffectDefinition))
            {
                return AbilityVisualKind.Healing;
            }

            return ability.Effects.Any(effect => effect is GrantShieldEffectDefinition)
                ? AbilityVisualKind.Shield
                : AbilityVisualKind.Attack;
        }
    }
}
