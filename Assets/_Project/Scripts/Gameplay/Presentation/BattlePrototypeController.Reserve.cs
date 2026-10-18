using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Reserve and swap without an Archon unit: on any player turn the active resonant may, for 1 AP and the shared
    /// Concentration, summon a reserve resonant next to itself, swap itself for one (the newcomer takes over the
    /// remaining AP of this turn) or recall another resonant. Each resonant has an entry and a recall effect.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private readonly List<ReserveSlot> reserve = new List<ReserveSlot>();
        private long concentrationReadyTick;
        private int selectedReserveIndex = -1;

        public IReadOnlyList<ReserveSlot> Reserve => reserve;
        public long ConcentrationReadyTick => concentrationReadyTick;
        public bool ConcentrationReady => turns != null && turns.CurrentTick >= concentrationReadyTick;

        private IEnumerable<CombatUnit> FieldEcho => units.Where(unit => unit.Team == Team.Player && !unit.IsDefeated);

        private void SpawnReserve()
        {
            // Mist Panther and a technical Echo wait in the stone.
            foreach (string id in new[] { "MistPanther", "RangerEcho" })
            {
                UnitDefinition definition = Resources.Load<UnitDefinition>("Prototype/Units/" + id);
                if (definition == null) continue;
                CombatUnit unit = SpawnUnit(definition, Team.Player, ControlType.Player, new GridCoordinate(0, 0),
                    id == "RangerEcho" ? new Color(0.25f, 0.55f, 1f) : new Color(0.78f, 0.82f, 0.9f), false);
                reserve.Add(new ReserveSlot(unit, 0));
            }
        }

        private CombatUnit Commander => turns?.CurrentUnit;

        private bool CanCommand(out string reason)
        {
            reason = null;
            CombatUnit active = Commander;
            if (active == null || active.ControlType != ControlType.Player || isBusy || battleEnded) reason = "Сейчас не ход игрока.";
            else if (active.CurrentActionPoints < ReserveRules.ActionCost) reason = "Не осталось ОД.";
            else if (active.Channel != null) reason = "Пока поддерживается способность, смена недоступна.";
            else if (!ConcentrationReady) reason = $"Концентрация восстановится на тике {concentrationReadyTick}.";
            return reason == null;
        }

        /// <summary>Summon mode: free cells within 2 of the active resonant (if fewer than 3 on the field) or its own cell (swap).</summary>
        public bool BeginSummon(int reserveIndex)
        {
            if (!CanCommand(out string reason))
            {
                status = reason;
                return false;
            }

            if (reserveIndex < 0 || reserveIndex >= reserve.Count || !reserve[reserveIndex].IsAvailable(turns.CurrentTick))
            {
                status = "Этот резонант ещё не готов вернуться.";
                return false;
            }

            selectedReserveIndex = reserveIndex;
            var cells = new List<GridCoordinate> { Commander.Cell };
            if (FieldEcho.Count() < ReserveRules.FieldLimit)
                cells.AddRange(ReserveRules.SummonCells(grid, Commander.Cell, IsOccupied));
            EnterReserveMode(InteractionMode.Summon, cells);
            status = $"{reserve[reserveIndex].Unit.DisplayName}: клетка активного резонанта — замена (сменщик получает " +
                     "оставшиеся ОД), свободная клетка рядом — призыв (на поле не больше 3).";
            return true;
        }

        /// <summary>Recall mode: another own resonant on the field returns to the reserve.</summary>
        public bool BeginRecall()
        {
            if (!CanCommand(out string reason))
            {
                status = reason;
                return false;
            }

            selectedReserveIndex = -1;
            EnterReserveMode(InteractionMode.Recall,
                FieldEcho.Where(unit => unit != Commander).Select(unit => unit.Cell).ToArray());
            status = "Отзыв: выбери другого своего резонанта. Отзыв снимает с него отрицательные эффекты.";
            return true;
        }

        private void EnterReserveMode(InteractionMode reserveMode, IReadOnlyCollection<GridCoordinate> cells)
        {
            mode = reserveMode;
            selectedDestination = null;
            selectedPath = System.Array.Empty<GridCoordinate>();
            selectedAbilityTarget = null;
            abilityPreview = null;
            previewImpact = System.Array.Empty<GridCoordinate>();
            targetable = cells;
            targetingRange = reserveMode == InteractionMode.Summon
                ? ReserveRules.SummonCells(grid, Commander.Cell, _ => false)
                : cells;
            RefreshHighlights();
        }

        private bool IsReserveMode => mode == InteractionMode.Summon || mode == InteractionMode.Recall;

        /// <summary>Click in summon or recall mode.</summary>
        public bool RequestReserveAction(GridCoordinate cell)
        {
            if (!IsReserveMode || !targetable.Contains(cell)) return false;
            CombatUnit echo = FieldEcho.FirstOrDefault(unit => unit.Cell == cell);
            if (mode == InteractionMode.Recall)
            {
                if (echo == null || echo == Commander) return false;
                StartCoroutine(PerformReserveAction(echo, null, cell));
                return true;
            }

            ReserveSlot slot = selectedReserveIndex >= 0 && selectedReserveIndex < reserve.Count ? reserve[selectedReserveIndex] : null;
            if (slot == null || echo != null && echo != Commander) return false;
            StartCoroutine(PerformReserveAction(echo, slot, cell));
            return true;
        }

        private IEnumerator PerformReserveAction(CombatUnit recalled, ReserveSlot summoned, GridCoordinate cell)
        {
            CombatUnit commander = Commander;
            if (!commander.SpendActionPoints(ReserveRules.ActionCost)) yield break;
            isBusy = true;
            reserveActions++;
            concentrationReadyTick = turns.CurrentTick + ReserveRules.ConcentrationTicks;
            if (unitViews.TryGetValue(commander, out CombatUnitView commanderView))
                AbilityVfx.StoneCommand(commanderView.transform.position);
            bool swap = recalled == commander;
            int remaining = commander.CurrentActionPoints;
            if (swap) MeasureActivationEnd(commander);
            if (recalled != null) yield return RecallEcho(recalled, swap ? summoned?.Unit : null);
            if (summoned != null) yield return SummonEcho(summoned, cell, swap ? commander.Facing : commander.Facing, swap, remaining);
            resolvedActions++;
            isBusy = false;
            ClearInteractionState();
            selectedReserveIndex = -1;
            if (EvaluateBattleEnd()) yield break;
            CombatUnit active = turns.CurrentUnit;
            SelectUnit(active);
            RefreshNextEnemyIntent();
            if (active.CurrentActionPoints == 0) StartCoroutine(EndTurnAfterDelay());
        }

        private IEnumerator RecallEcho(CombatUnit echo, CombatUnit successor)
        {
            GridCoordinate cell = echo.Cell;
            feedback.AddLog($"Отзыв: {echo.DisplayName} уходит в камень");
            if (unitViews.TryGetValue(echo, out CombatUnitView view))
            {
                yield return AbilityVfx.RecallBeam(view.transform);
                Destroy(view.gameObject);
                unitViews.Remove(echo);
            }

            yield return RecallEffect(echo, cell);
            int cleansed = echo.Cleanse();
            if (cleansed > 0) feedback.AddLog($"{echo.DisplayName}: отзыв снял отрицательных эффектов: {cleansed}");
            echo.EndChannel();
            units.Remove(echo);
            // The active resonant hands its turn to the one swapped in; anyone else simply leaves the timeline.
            if (successor != null && echo == turns.CurrentUnit)
            {
                successor.MoveTo(cell);
                turns.ReplaceCurrent(echo, successor);
            }
            else turns.RemoveUnit(echo);
            reserve.Add(new ReserveSlot(echo, turns.CurrentTick + ReserveRules.ReturnLockTicks));
            RefreshCloudViews();
        }

        private IEnumerator SummonEcho(ReserveSlot slot, GridCoordinate cell, GridCoordinate facing, bool swap, int remainingActionPoints)
        {
            CombatUnit echo = slot.Unit;
            reserve.Remove(slot);
            echo.MoveTo(cell);
            echo.SetFacing(facing);
            units.Add(echo);
            if (swap)
            {
                // The newcomer continues this turn with the AP that were left.
                echo.BeginActivation(remainingActionPoints);
                MeasureActivationStart(echo);
            }
            else turns.AddUnit(echo, turns.CurrentTick + SpeedTimeline.GetInterval(echo.EffectiveSpeed));

            CombatUnitView view = CreateUnitView(echo);
            feedback.AddLog(swap ? $"Замена: {echo.DisplayName} выходит в {cell} с {remainingActionPoints} ОД"
                : $"Призыв: {echo.DisplayName} выходит в {cell}");
            yield return AbilityVfx.SummonPortal(view.transform, EffectWorldPosition(cell));
            yield return EntryEffect(echo, cell);
            RefreshCloudViews();
        }

        /// <summary>Entry effects (analysis 4, drafts per resonant). Friendly fire applies.</summary>
        private IEnumerator EntryEffect(CombatUnit echo, GridCoordinate cell)
        {
            var results = new List<ICombatEffectResult>();
            switch (echo.DefinitionId)
            {
                case "blaze-worm":
                {
                    // Bursts out of the ground: Burning on the 4 neighbouring cells (allies too); Magma Worm: Magma.
                    PassiveModifierDefinition modifiers = ModifiersOf(echo);
                    int power = BurningCalculator.PowerFromAttack(echo.Attack, 0.25f);
                    foreach (GridCoordinate neighbour in cell.FourWayNeighbors().Where(grid.Contains))
                    {
                        AbilityVfx.FireBurstAt(EffectWorldPosition(neighbour));
                        if (modifiers != null && modifiers.magmaWorm)
                        {
                            if (grid.GetCell(neighbour).IsWalkable) areaEffects.AddMagma(echo, neighbour, turns.CurrentTick, power);
                            continue;
                        }

                        CombatUnit victim = units.FirstOrDefault(unit => !unit.IsDefeated && unit.Cell == neighbour);
                        if (victim == null) continue;
                        BurnAppliedResult burn = BurningCalculator.Apply(victim, victim.BurningState, power, 3,
                            turns.CurrentTick, victim.GetMechanicStacks(StatusMechanic.Oil));
                        CombatEffectResolver.Apply(new ICombatEffectResult[] { burn });
                        results.Add(burn);
                    }

                    feedback.AddLog($"Вход {echo.DisplayName}: огонь вокруг");
                    break;
                }
                case "blightgullet":
                    AddCloudAt(echo, "PoisonCloud", cell, true);
                    feedback.AddLog($"Вход {echo.DisplayName}: Ядовитое облако крестом");
                    break;
                case "primal-idol":
                {
                    // Rises on a pillar: its own cell +2 (one Earth Pillar step).
                    TerrainChangeResult rise = TerrainChangeCalculator.ChangeHeight(grid.GetCell(cell), 2,
                        MinimumTerrainHeight, MaximumTerrainHeight);
                    if (rise.Changed)
                    {
                        grid.ApplyTerrainChange(rise);
                        yield return AnimateCellRise(rise);
                    }

                    feedback.AddLog($"Вход {echo.DisplayName}: встаёт на столб");
                    break;
                }
                case "mist-panther":
                {
                    // Appears in Mist.
                    var mist = StatusCalculator.Apply(echo, StealthRules.Mist, 1, turns.CurrentTick, echo.Id);
                    echo.ApplyStatus(mist);
                    results.Add(mist);
                    feedback.AddLog($"Вход {echo.DisplayName}: появляется в Тумане");
                    break;
                }
            }

            if (results.Count > 0) PresentTimelineResults(results);
        }

        private IEnumerator RecallEffect(CombatUnit echo, GridCoordinate cell)
        {
            switch (echo.DefinitionId)
            {
                case "blaze-worm":
                    areaEffects.AddMagma(echo, cell, turns.CurrentTick, BurningCalculator.PowerFromAttack(echo.Attack, 0.25f));
                    feedback.AddLog($"Отзыв {echo.DisplayName}: на клетке остаётся Магма");
                    break;
                case "blightgullet":
                    AddCloudAt(echo, "OilCloud", cell, false);
                    feedback.AddLog($"Отзыв {echo.DisplayName}: Масляное облако");
                    break;
                case "primal-idol":
                    areaEffects.AddTrap(new TrapObject(echo, "LiftTrap", cell, turns.CurrentTick + 300, 10));
                    feedback.AddLog($"Отзыв {echo.DisplayName}: Ловушка вихря на клетке");
                    break;
                case "mist-panther":
                {
                    // A free Parting Strike on a neighbouring enemy before leaving.
                    AbilityDefinition parting = definitionsByUnit[echo].Abilities.FirstOrDefault(a => a.StableId == "PartingStrike");
                    if (parting == null) break;
                    var targets = Runtime.AbilityRuntime.GetTargetableCells(parting, grid, echo, units);
                    if (targets.Count == 0) break;
                    Runtime.AbilityResolution strike = Runtime.AbilityRuntime.Execute(parting, grid, echo, targets.First(), units,
                        turns.CurrentTick, areaEffects, ModifiersOf(echo));
                    PresentTimelineResults(strike.Results.Where(result => !(result is PushResult)).ToArray());
                    foreach (PushResult push in strike.Results.OfType<PushResult>().Where(push => push.Target != echo && push.StepsMoved > 0))
                        yield return AnimateForcedMovement(push);
                    feedback.AddLog($"Отзыв {echo.DisplayName}: Удар отхода");
                    break;
                }
            }
        }

        private void AddCloudAt(CombatUnit owner, string cloudId, GridCoordinate cell, bool useShape)
        {
            CloudDefinition definition = Resources.Load<CloudDefinition>("Prototype/Clouds/" + cloudId);
            if (definition == null) return;
            IEnumerable<GridCoordinate> cells = useShape && definition.areaShape != null
                ? definition.areaShape.Evaluate(grid, cell)
                : new[] { cell };
            areaEffects.Add(areaEffects.PreviewCloud(owner, cloudId, definition.Snapshot(), cells, owner.Facing,
                grid.GetCell(cell).Height, turns.CurrentTick));
        }
    }
}
