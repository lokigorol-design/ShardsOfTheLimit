using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Calling a replacement (author's rule, 18.10.2026): on its turn any own resonant spends 1 AP to call a resonant
    /// from the reserve. For 200 ticks the caller takes +100% damage and keeps acting; then the newcomer appears in its
    /// place and the caller goes to the reserve. A fallen caller is not replaced. Entry and recall effects per resonant.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private readonly List<ReserveSlot> reserve = new List<ReserveSlot>();
        private readonly List<PendingSwap> pendingSwaps = new List<PendingSwap>();

        public IReadOnlyList<ReserveSlot> Reserve => reserve;
        public IReadOnlyList<PendingSwap> PendingSwaps => pendingSwaps;

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

        public bool IsCalling(CombatUnit unit) => pendingSwaps.Any(swap => swap.Caller == unit);

        /// <summary>Why the active resonant cannot call now, or null.</summary>
        private string CallBlockReason(CombatUnit active)
        {
            if (active == null || active.ControlType != ControlType.Player || isBusy || battleEnded) return "Сейчас не ход игрока.";
            if (active.CurrentActionPoints < ReserveRules.ActionCost) return "Не осталось ОД.";
            if (active.Channel != null) return "Пока поддерживается способность, вызов недоступен.";
            if (IsCalling(active)) return $"{active.DisplayName} уже ждёт замену.";
            return null;
        }

        /// <summary>The active resonant calls reserve resonant <paramref name="reserveIndex"/> to take its place in 200 ticks.</summary>
        public bool RequestCall(int reserveIndex)
        {
            CombatUnit caller = turns?.CurrentUnit;
            string reason = CallBlockReason(caller);
            if (reason == null && (reserveIndex < 0 || reserveIndex >= reserve.Count || !reserve[reserveIndex].IsAvailable(turns.CurrentTick)))
                reason = "Этот резонант сейчас не может выйти.";
            if (reason != null)
            {
                status = reason;
                return false;
            }

            caller.SpendActionPoints(ReserveRules.ActionCost);
            ReserveSlot slot = reserve[reserveIndex];
            slot.IsCalled = true;
            var summoning = StatusCalculator.Apply(caller, ReserveRules.Summoning, 1, turns.CurrentTick, caller.Id);
            caller.ApplyStatus(summoning);
            pendingSwaps.Add(new PendingSwap(caller, slot, turns.CurrentTick + ReserveRules.SwapTicks));
            reserveActions++;
            if (unitViews.TryGetValue(caller, out CombatUnitView view)) AbilityVfx.StoneCommand(view.transform.position);
            feedback.AddLog($"{caller.DisplayName} вызывает {slot.Unit.DisplayName}: +100% урона до тика " +
                            $"{turns.CurrentTick + ReserveRules.SwapTicks}, затем замена");
            status = $"{slot.Unit.DisplayName} выйдет на место {caller.DisplayName} через {ReserveRules.SwapTicks} тиков. " +
                     "До тех пор он получает двойной урон.";
            RefreshMovementOptions(caller);
            RefreshHighlights();
            if (caller.CurrentActionPoints == 0) StartCoroutine(EndTurnAfterDelay());
            return true;
        }

        private bool HasDueSwaps => pendingSwaps.Any(swap => swap.IsDue(turns.CurrentTick) || swap.Caller.IsDefeated);

        /// <summary>Called replacements whose 200 ticks passed arrive; calls of fallen resonants are void.</summary>
        private IEnumerator ResolveDueSwaps(CombatUnit nextUnit)
        {
            isBusy = true;
            foreach (PendingSwap swap in pendingSwaps.Where(swap => swap.IsDue(turns.CurrentTick) || swap.Caller.IsDefeated).ToArray())
            {
                pendingSwaps.Remove(swap);
                swap.Incoming.IsCalled = false;
                if (swap.Caller.IsDefeated)
                {
                    feedback.AddLog($"{swap.Caller.DisplayName} пал: {swap.Incoming.Unit.DisplayName} не выйдет на его место");
                    continue;
                }

                CombatUnit caller = swap.Caller;
                GridCoordinate cell = caller.Cell;
                bool takesTurn = caller == nextUnit;
                yield return RecallEcho(caller);
                CombatUnit newcomer = swap.Incoming.Unit;
                reserve.Remove(swap.Incoming);
                newcomer.MoveTo(cell);
                newcomer.SetFacing(caller.Facing);
                units.Add(newcomer);
                if (takesTurn)
                {
                    // The caller's turn has come: the newcomer acts in its place right away.
                    turns.ReplaceCurrent(caller, newcomer);
                    newcomer.BeginActivation(ActionsPerTurn);
                    nextUnit = newcomer;
                }
                else
                {
                    turns.RemoveUnit(caller);
                    turns.AddUnit(newcomer, turns.CurrentTick + SpeedTimeline.GetInterval(newcomer.EffectiveSpeed));
                }

                reserve.Add(new ReserveSlot(caller, turns.CurrentTick + ReserveRules.ReturnLockTicks));
                CombatUnitView view = CreateUnitView(newcomer);
                feedback.AddLog($"Замена: {newcomer.DisplayName} выходит в {cell} вместо {caller.DisplayName}");
                yield return AbilityVfx.SummonPortal(view.transform, EffectWorldPosition(cell));
                yield return EntryEffect(newcomer, cell);
                RefreshCloudViews();
            }

            isBusy = false;
            ContinueTurn(nextUnit);
        }

        private IEnumerator RecallEcho(CombatUnit echo)
        {
            GridCoordinate cell = echo.Cell;
            feedback.AddLog($"{echo.DisplayName} уходит в камень");
            if (unitViews.TryGetValue(echo, out CombatUnitView view))
            {
                yield return AbilityVfx.RecallBeam(view.transform);
                Destroy(view.gameObject);
                unitViews.Remove(echo);
            }

            yield return RecallEffect(echo, cell);
            int cleansed = echo.Cleanse();
            if (cleansed > 0) feedback.AddLog($"{echo.DisplayName}: отзыв снял отрицательных эффектов: {cleansed}");
            echo.RemoveSummoning();
            echo.EndChannel();
            units.Remove(echo);
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
