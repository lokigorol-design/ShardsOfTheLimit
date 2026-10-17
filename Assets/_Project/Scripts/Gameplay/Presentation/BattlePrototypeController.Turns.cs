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
    /// <summary>Turn flow: end of turn, facing step and the start of the next activation.</summary>
    public sealed partial class BattlePrototypeController
    {        private IEnumerator EndTurnAfterDelay()
        {
            isBusy = true;
            yield return new WaitForSeconds(0.65f);
            isBusy = false;
            EndPlayerTurn();
        }

        /// <summary>
        /// Ending a player turn has two steps: first the facing step (choose where the resonant looks),
        /// then the confirmation that passes the turn on.
        /// </summary>
        private void EndPlayerTurn()
        {
            if (battleEnded || isBusy || turns?.CurrentUnit?.ControlType != ControlType.Player)
            {
                return;
            }

            if (mode != InteractionMode.Facing)
            {
                BeginFacingStep();
                return;
            }

            CombatUnit unit = turns.CurrentUnit;
            if (facingPreview.HasValue && facingPreview.Value != unit.Facing)
            {
                unit.SetFacing(facingPreview.Value);
                feedback.AddLog($"{unit.DisplayName} faces {DescribeDirection(unit.Facing)}");
            }

            if (unitViews.TryGetValue(unit, out CombatUnitView view)) view.PreviewFacing = null;
            facingPreview = null;
            unit.CurrentAction = "Activation ended";
            FinishActivation(unit);
            feedback.AddLog($"{unit.DisplayName} ends the activation");
            AdvanceTurn();
        }

        private GridCoordinate? facingPreview;

        private void BeginFacingStep()
        {
            if (EvaluateBattleEnd()) return;
            CombatUnit unit = turns.CurrentUnit;
            ClearInteractionState();
            selectedUnit = unit;
            mode = InteractionMode.Facing;
            facingPreview = unit.Facing;
            lastFacingPointer = Input.mousePosition;
            unit.CurrentAction = "Choosing facing";
            status = "Куда смотрит резонант? Наведи курсор на сторону и кликни. Enter или Space — подтвердить, Esc — вернуться к действиям.";
            RefreshHighlights();
        }

        private void PreviewFacing(GridCoordinate direction)
        {
            if (mode != InteractionMode.Facing || selectedUnit == null) return;
            facingPreview = direction;
            if (unitViews.TryGetValue(selectedUnit, out CombatUnitView view)) view.PreviewFacing = direction;
            RefreshHighlights();
        }

        private void CancelFacing()
        {
            if (mode != InteractionMode.Facing || selectedUnit == null) return;
            if (unitViews.TryGetValue(selectedUnit, out CombatUnitView view)) view.PreviewFacing = null;
            facingPreview = null;
            SelectUnit(selectedUnit);
        }

        private static string DescribeDirection(GridCoordinate direction) =>
            direction.X > 0 ? "+X" : direction.X < 0 ? "-X" : direction.Y > 0 ? "+Y" : "-Y";

        /// <summary>A resonant turns to face where it moved or what it targets.</summary>
        private void FaceTowards(CombatUnit unit, GridCoordinate from, GridCoordinate to)
        {
            if (unit == null || from == to) return;
            unit.SetFacing(FacingRules.Towards(from, to));
        }

        private void AdvanceTurn()
        {
            ClearInteractionState();
            selectedUnit = null;
            RefreshHighlights();
            BeginTurn(turns.Advance());
        }

        private void BeginTurn(CombatUnit unit)
        {
            PresentTimelineResults(turns.LastTimelineResults);
            UpdateArchonLink();
            RefreshCloudViews();
            if (EvaluateBattleEnd()) return;
            if (unit == null) return;
            MeasureActivationStart(unit);

            Color teamColor = unit.Team == Team.Player
                ? new Color(0.35f, 0.75f, 1f)
                : new Color(1f, 0.4f, 0.28f);
            feedback.ShowTurnBanner($"TICK {turns.CurrentTick} · {unit.DisplayName}", teamColor);
            feedback.AddLog(
                $"Tick {turns.CurrentTick}: {unit.DisplayName} activation #{unit.ActivationCount} ({unit.CurrentActionPoints} AP)");
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition))
            {
                string abilityState = string.Join(", ", definition.Abilities.Select(ability =>
                    unit.GetCooldown(ability.StableId) == 0
                        ? $"{ability.DisplayName}: ready"
                        : $"{ability.DisplayName}: CD {unit.GetCooldown(ability.StableId)}"));
                feedback.AddLog(abilityState);
            }

            RefreshNextEnemyIntent();

            if (unit.ControlType == ControlType.Player)
            {
                SelectUnit(unit);
            }
            else
            {
                selectedUnit = unit;
                StartCoroutine(RunBasicAI(unit));
            }
        }
    }
}
