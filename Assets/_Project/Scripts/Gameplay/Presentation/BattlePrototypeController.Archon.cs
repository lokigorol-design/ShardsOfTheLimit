using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// The Archon on the field: summon from the reserve next to itself, recall within the link radius, swap
    /// (recall + summon into the same cell). Each resonant has an entry and a recall effect. Link Break at 0 health.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private readonly List<ReserveSlot> reserve = new List<ReserveSlot>();
        private CombatUnit archon;
        private long concentrationReadyTick;
        private long? archonReturnTick;
        private int selectedReserveIndex = -1;

        public CombatUnit Archon => archon;
        public IReadOnlyList<ReserveSlot> Reserve => reserve;
        public long ConcentrationReadyTick => concentrationReadyTick;
        public bool IsArchon(CombatUnit unit) => unit != null && unit == archon;

        private IEnumerable<CombatUnit> FieldEcho => units.Where(unit =>
            unit.Team == Team.Player && unit != archon && !unit.IsDefeated);

        private void SpawnArchonAndReserve()
        {
            UnitDefinition archonDefinition = Resources.Load<UnitDefinition>("Prototype/Units/Archon");
            if (archonDefinition == null) return;
            GridCoordinate start = grid.Cells
                .Where(cell => cell.IsWalkable && !IsOccupied(cell.Coordinate))
                .Select(cell => cell.Coordinate)
                .OrderBy(cell => cell.ManhattanDistance(new GridCoordinate(0, 2)))
                .ThenBy(cell => cell.X)
                .First();
            archon = SpawnUnit(archonDefinition, Team.Player, ControlType.Player, start, new Color(0.95f, 0.78f, 0.35f));

            // Technical Echo wait in the stone until the Archon calls them.
            foreach (string id in new[] { "RangerEcho", "SkirmisherEcho" })
            {
                UnitDefinition definition = Resources.Load<UnitDefinition>("Prototype/Units/" + id);
                if (definition == null) continue;
                CombatUnit unit = SpawnUnit(definition, Team.Player, ControlType.Player, start,
                    id == "RangerEcho" ? new Color(0.25f, 0.55f, 1f) : new Color(0.2f, 0.9f, 0.75f), false);
                reserve.Add(new ReserveSlot(unit, 0));
            }
        }

        public bool ConcentrationReady => turns != null && turns.CurrentTick >= concentrationReadyTick;

        private bool CanArchonAct(out string reason)
        {
            reason = null;
            if (archon == null || turns?.CurrentUnit != archon || isBusy || battleEnded) reason = "Сейчас не ход Архонта.";
            else if (archon.CurrentActionPoints < ArchonRules.ActionCost) reason = "У Архонта не осталось ОД.";
            else if (!ConcentrationReady)
                reason = $"Концентрация восстановится на тике {concentrationReadyTick}.";
            return reason == null;
        }

        /// <summary>Summon mode: free cells within 2 of the Archon, or an own resonant in the link radius to swap.</summary>
        public bool BeginSummon(int reserveIndex)
        {
            if (!CanArchonAct(out string reason))
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
            var cells = new List<GridCoordinate>();
            if (FieldEcho.Count() < ArchonRules.FieldLimit)
                cells.AddRange(ArchonRules.SummonCells(grid, archon.Cell, IsOccupied));
            cells.AddRange(FieldEcho.Where(unit => ArchonRules.InLink(archon.Cell, unit.Cell)).Select(unit => unit.Cell));
            EnterArchonMode(InteractionMode.Summon, cells);
            status = $"Призыв: {reserve[reserveIndex].Unit.DisplayName}. Свободная клетка рядом с Архонтом — призвать " +
                     "(на поле не больше 3), свой резонант в радиусе связи 4 — заменить.";
            return true;
        }

        public bool BeginRecall()
        {
            if (!CanArchonAct(out string reason))
            {
                status = reason;
                return false;
            }

            selectedReserveIndex = -1;
            EnterArchonMode(InteractionMode.Recall,
                FieldEcho.Where(unit => ArchonRules.InLink(archon.Cell, unit.Cell)).Select(unit => unit.Cell).ToArray());
            status = "Отзыв: выбери своего резонанта в радиусе связи 4. Отзыв снимает с него отрицательные эффекты.";
            return true;
        }

        private void EnterArchonMode(InteractionMode archonMode, IReadOnlyCollection<GridCoordinate> cells)
        {
            mode = archonMode;
            selectedDestination = null;
            selectedPath = System.Array.Empty<GridCoordinate>();
            selectedAbilityTarget = null;
            abilityPreview = null;
            previewImpact = System.Array.Empty<GridCoordinate>();
            targetable = cells;
            targetingRange = ArchonRules.SummonCells(grid, archon.Cell, _ => false);
            RefreshHighlights();
        }

        private bool IsArchonMode => mode == InteractionMode.Summon || mode == InteractionMode.Recall;

        /// <summary>Click in summon or recall mode.</summary>
        public bool RequestArchonAction(GridCoordinate cell)
        {
            if (!IsArchonMode || !targetable.Contains(cell)) return false;
            CombatUnit echo = FieldEcho.FirstOrDefault(unit => unit.Cell == cell);
            if (mode == InteractionMode.Recall)
            {
                if (echo == null) return false;
                StartCoroutine(PerformArchonAction(echo, null, cell));
                return true;
            }

            ReserveSlot slot = selectedReserveIndex >= 0 && selectedReserveIndex < reserve.Count ? reserve[selectedReserveIndex] : null;
            if (slot == null) return false;
            StartCoroutine(PerformArchonAction(echo, slot, cell));
            return true;
        }

        private IEnumerator PerformArchonAction(CombatUnit recalled, ReserveSlot summoned, GridCoordinate cell)
        {
            if (!archon.SpendActionPoints(ArchonRules.ActionCost)) yield break;
            isBusy = true;
            archonActions++;
            concentrationReadyTick = turns.CurrentTick + ArchonRules.ConcentrationTicks;
            FaceTowards(archon, archon.Cell, cell);
            if (unitViews.TryGetValue(archon, out CombatUnitView archonView))
                AbilityVfx.ArchonCommand(archonView.transform.position);
            if (recalled != null) yield return RecallEcho(recalled);
            if (summoned != null) yield return SummonEcho(summoned, cell);
            resolvedActions++;
            isBusy = false;
            ClearInteractionState();
            selectedReserveIndex = -1;
            if (EvaluateBattleEnd()) yield break;
            SelectUnit(archon);
            RefreshNextEnemyIntent();
            if (archon.CurrentActionPoints == 0) StartCoroutine(EndTurnAfterDelay());
        }

        private IEnumerator RecallEcho(CombatUnit echo)
        {
            GridCoordinate cell = echo.Cell;
            feedback.AddLog($"Архонт отзывает {echo.DisplayName}");
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
            turns.RemoveUnit(echo);
            reserve.Add(new ReserveSlot(echo, turns.CurrentTick + ArchonRules.ReturnLockTicks));
            RefreshCloudViews();
        }

        private IEnumerator SummonEcho(ReserveSlot slot, GridCoordinate cell)
        {
            CombatUnit echo = slot.Unit;
            reserve.Remove(slot);
            echo.MoveTo(cell);
            echo.SetFacing(archon.Facing);
            units.Add(echo);
            turns.AddUnit(echo, turns.CurrentTick + SpeedTimeline.GetInterval(echo.EffectiveSpeed));
            CombatUnitView view = CreateUnitView(echo);
            feedback.AddLog($"Архонт призывает {echo.DisplayName} в {cell}");
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
            }

            yield break;
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

        /// <summary>Link Break: at 0 health the Archon is out for 3 turns, then gets up with 30% health.</summary>
        private void UpdateArchonLink()
        {
            if (archon == null || turns == null) return;
            if (archon.IsDefeated && !archonReturnTick.HasValue)
            {
                archonReturnTick = turns.CurrentTick + ArchonRules.LinkBreakTicks;
                feedback.AddLog($"Разрыв связи: Архонт выбыл до тика {archonReturnTick.Value}, смены недоступны");
                return;
            }

            if (!archon.IsDefeated || turns.CurrentTick < archonReturnTick.Value) return;
            GridCoordinate cell = IsOccupied(archon.Cell)
                ? grid.Cells.Where(c => c.IsWalkable && !IsOccupied(c.Coordinate)).Select(c => c.Coordinate)
                    .OrderBy(c => c.ManhattanDistance(archon.Cell)).First()
                : archon.Cell;
            archon.MoveTo(cell);
            archon.Revive(ArchonRules.ReviveHealth(archon.MaximumHealth));
            archonReturnTick = null;
            turns.SetNextActivation(archon, turns.CurrentTick + SpeedTimeline.GetInterval(archon.EffectiveSpeed));
            if (unitViews.TryGetValue(archon, out CombatUnitView view))
            {
                view.gameObject.SetActive(true);
                view.transform.position = UnitWorldPosition(cell);
            }

            feedback.AddLog($"Архонт встаёт с {archon.CurrentHealth} здоровья");
        }
    }
}
