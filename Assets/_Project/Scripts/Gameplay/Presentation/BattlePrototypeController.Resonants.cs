using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Resonant-specific rules wired into the battle: branch variants (II/IV choices), Ritual Surfaces wall mode,
    /// channelled Flame Burst, Magma, Banked Heat.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        /// <summary>Chosen branch variant per unit definition; survives the scene reload of a restart.</summary>
        private static readonly Dictionary<string, int> chosenVariants = new Dictionary<string, int>();

        private readonly Dictionary<CombatUnit, PassiveModifierDefinition> modifiersByUnit =
            new Dictionary<CombatUnit, PassiveModifierDefinition>();

        private bool wallMode;

        public TacticalGrid Grid => grid;

        /// <summary>Ritual Surfaces: Surface abilities target a wall instead of the floor.</summary>
        public bool WallMode => wallMode;

        public PassiveModifierDefinition ModifiersOf(CombatUnit unit) =>
            unit != null && modifiersByUnit.TryGetValue(unit, out PassiveModifierDefinition value) ? value : null;

        private static PassiveModifierDefinition ChooseVariant(UnitDefinition definition)
        {
            IReadOnlyList<PassiveModifierDefinition> variants = definition.BranchVariants;
            if (variants.Count == 0) return definition.PassiveModifiers;
            chosenVariants.TryGetValue(definition.StableId, out int index);
            return variants[Mathf.Clamp(index, 0, variants.Count - 1)] ?? definition.PassiveModifiers;
        }

        /// <summary>Switches the active resonant to its next branch variant and restarts the battle with it.</summary>
        public void CycleBranchVariant()
        {
            CombatUnit unit = turns?.CurrentUnit;
            if (unit == null || !definitionsByUnit.TryGetValue(unit, out UnitDefinition definition)) return;
            int count = definition.BranchVariants.Count;
            if (count < 2) return;
            chosenVariants.TryGetValue(definition.StableId, out int index);
            chosenVariants[definition.StableId] = (index + 1) % count;
            Restart();
        }

        public static void SetBranchVariant(string unitStableId, int index) => chosenVariants[unitStableId] = index;

        public string BranchVariantName(CombatUnit unit)
        {
            PassiveModifierDefinition modifiers = ModifiersOf(unit);
            return modifiers != null && !string.IsNullOrWhiteSpace(modifiers.variantName) ? modifiers.variantName : string.Empty;
        }

        /// <summary>Branch numbers from the card: HP/ATK/Defense multipliers, Speed and Movement bonuses.</summary>
        private static void ScaleStats(UnitDefinition definition, PassiveModifierDefinition modifiers,
            out int health, out int attack, out int defense, out int movement)
        {
            health = definition.MaximumHealth;
            attack = definition.Attack;
            defense = definition.Defense;
            movement = definition.MovementRange;
            if (modifiers == null) return;
            health = Mathf.Max(1, Mathf.RoundToInt(health * modifiers.healthMultiplier));
            attack = Mathf.Max(0, Mathf.RoundToInt(attack * modifiers.attackMultiplier));
            defense = Mathf.RoundToInt(defense * modifiers.defenseMultiplier);
            movement = Mathf.Max(0, movement + modifiers.movementBonus);
        }

        private void ConfigureBranch(CombatUnit unit, UnitDefinition definition, PassiveModifierDefinition modifiers)
        {
            modifiersByUnit[unit] = modifiers;
            if (modifiers == null) return;
            unit.SpeedBonus = modifiers.speedBonus;
            if (modifiers.magmaWorm)
            {
                // Magma Trail replaces Deep Burrow: the worm walks and is immune to Magma.
                unit.ConfigureMovement(MovementMode.Walk, 1);
                unit.SetPeriodicResistance(AreaEffectWorld.MagmaResistanceId, 100);
            }
        }

        private void ToggleWallMode()
        {
            wallMode = !wallMode;
            if (mode == InteractionMode.AbilityTargeting) BeginAbilityTargeting(selectedAbilityIndex);
            else status = wallMode
                ? "Стена: способности «Поверхность» бьют из стены перпендикулярно ей."
                : "Пол: способности «Поверхность» бьют из пола вверх.";
        }

        private static ChannelConeEffectDefinition ChannelOf(AbilityDefinition ability) =>
            ability?.Effects.OfType<ChannelConeEffectDefinition>().FirstOrDefault();

        private static bool IsExtending(CombatUnit unit, AbilityDefinition ability) =>
            unit?.Channel != null && ability != null && unit.Channel.AbilityId == ability.StableId;

        /// <summary>AP this use costs: a channel extension costs its extend price instead of the launch cost.</summary>
        public static int AbilityCost(CombatUnit unit, AbilityDefinition ability)
        {
            ChannelConeEffectDefinition channel = ChannelOf(ability);
            return channel != null && IsExtending(unit, ability) ? channel.extendCost : ability.ActionCost;
        }

        /// <summary>Why the ability cannot be used now (channel rules), or null.</summary>
        private static string ChannelBlockReason(CombatUnit unit, AbilityDefinition ability)
        {
            if (unit?.Channel == null) return null;
            if (unit.Channel.AbilityId != ability.StableId)
                return "Пока поддерживается способность, другие способности недоступны.";
            if (unit.Channel.ExtendedOnActivation == unit.ActivationCount)
                return "Поддерживающая способность уже сработала в этом ходу.";
            return null;
        }

        public static bool CanUseAbility(CombatUnit unit, AbilityDefinition ability) =>
            unit != null && ability != null && unit.GetCooldown(ability.StableId) == 0 &&
            unit.CurrentActionPoints >= AbilityCost(unit, ability) && ChannelBlockReason(unit, ability) == null;

        /// <summary>Cooldown after a use: a channel starts its cooldown only when it ends.</summary>
        private void ApplyCooldown(CombatUnit source, AbilityDefinition ability, AbilityResolution resolution)
        {
            ChannelConeEffectDefinition channel = ChannelOf(ability);
            if (channel == null)
            {
                source.SetCooldown(ability.StableId, ability.Cooldown);
                feedback.AddLog($"{ability.DisplayName} cooldown set to {ability.Cooldown}");
                return;
            }

            if (resolution.ChannelEnded)
            {
                source.SetCooldown(ability.StableId, channel.cooldownAfterEnd);
                feedback.AddLog($"{ability.DisplayName} reached its last stage; cooldown {channel.cooldownAfterEnd}");
                return;
            }

            // Whoever stood in the cone when it flared was already hit at this stage.
            foreach (CombatUnit unit in units.Where(unit => !unit.IsDefeated && resolution.ImpactCells.Contains(unit.Cell)))
                source.Channel?.EnteredThisStage.Add(unit);
            feedback.AddLog($"{ability.DisplayName}: stage {resolution.ChannelStage}; extend next turn for {channel.extendCost} AP");
        }

        /// <summary>End of an activation: Banked Heat, and a channel that was not extended this turn goes out.</summary>
        private void FinishActivation(CombatUnit unit)
        {
            if (unit == null || unit.IsDefeated) return;
            PassiveModifierDefinition modifiers = ModifiersOf(unit);
            if (modifiers != null && modifiers.bankedHeat && unit.CurrentActionPoints > 0)
            {
                unit.BankActionPoint();
                feedback.AddLog($"{unit.DisplayName}: Banked Heat, +1 AP next turn");
                if (unitViews.TryGetValue(unit, out CombatUnitView view))
                    feedback.AddFloatingText(view.transform.position + Vector3.up * 0.9f, "+1 ОД", new Color(1f, 0.6f, 0.15f));
            }

            if (unit.Channel != null && unit.Channel.ExtendedOnActivation < unit.ActivationCount)
            {
                AbilityDefinition ability = definitionsByUnit.TryGetValue(unit, out UnitDefinition definition)
                    ? definition.Abilities.FirstOrDefault(candidate => candidate.StableId == unit.Channel.AbilityId)
                    : null;
                unit.EndChannel();
                ChannelConeEffectDefinition channel = ChannelOf(ability);
                if (channel != null) unit.SetCooldown(ability.StableId, channel.cooldownAfterEnd);
                feedback.AddLog($"{unit.DisplayName}: the channel was not extended and goes out");
            }
        }

        /// <summary>A resonant entered cells by walking or being displaced: Magma Trail, Magma and active cones.</summary>
        private IEnumerator EnterCells(CombatUnit unit, IReadOnlyList<GridCoordinate> cells, bool walked)
        {
            if (areaEffects == null || unit == null || cells.Count == 0) yield break;
            PassiveModifierDefinition modifiers = ModifiersOf(unit);
            if (walked && modifiers != null && modifiers.magmaWorm)
            {
                int power = BurningCalculator.PowerFromAttack(unit.Attack, 0.25f);
                foreach (GridCoordinate cell in cells) areaEffects.AddMagma(unit, cell, turns.CurrentTick, power);
                feedback.AddLog($"{unit.DisplayName} leaves Magma on {cells.Count} cell(s)");
            }

            var results = new List<ICombatEffectResult>();
            foreach (GridCoordinate cell in cells)
            {
                if (unit.IsDefeated) break;
                DamageResult magma = areaEffects.EnterMagma(unit, cell);
                if (magma != null && (magma.HealthDamage > 0 || magma.ShieldDamage > 0)) results.Add(magma);
                results.AddRange(EnterActiveCones(unit, cell));
            }

            if (results.Count > 0) PresentTimelineResults(results);
            yield break;
        }

        /// <summary>Flame Burst: whoever enters an active cone takes the current stage's damage once per stage.</summary>
        private IEnumerable<ICombatEffectResult> EnterActiveCones(CombatUnit unit, GridCoordinate cell)
        {
            foreach (CombatUnit owner in units.Where(owner => owner != unit && !owner.IsDefeated && owner.Channel != null).ToArray())
            {
                ChannelState channel = owner.Channel;
                if (channel.EnteredThisStage.Contains(unit) ||
                    !ConePattern.Cells(grid, owner.Cell, channel.Direction, channel.Stage).Contains(cell)) continue;
                AbilityDefinition ability = definitionsByUnit[owner].Abilities.FirstOrDefault(a => a.StableId == channel.AbilityId);
                ChannelConeEffectDefinition cone = ChannelOf(ability);
                if (cone == null || unit.IsDefeated) continue;
                channel.EnteredThisStage.Add(unit);
                DamageResult hit = CombatEffectCalculator.Damage(unit, unit.ResourceState,
                    cone.stageAttackMultipliers[channel.Stage - 1] * owner.Attack);
                CombatEffectResolver.Apply(new[] { hit });
                yield return hit;
                if (unit.IsDefeated) continue;
                int power = BurningCalculator.PowerFromAttack(owner.Attack, cone.burnAttackMultiplier);
                PassiveModifierDefinition modifiers = ModifiersOf(owner);
                if (modifiers != null && modifiers.magmaWorm)
                {
                    areaEffects.AddMagma(owner, cell, turns.CurrentTick, BurningCalculator.PowerFromAttack(owner.Attack, 0.25f));
                    continue;
                }

                BurnAppliedResult burn = BurningCalculator.Apply(unit, unit.BurningState, power, cone.burnDurationPulses,
                    turns.CurrentTick, unit.GetMechanicStacks(StatusMechanic.Oil));
                CombatEffectResolver.Apply(new ICombatEffectResult[] { burn });
                yield return burn;
            }
        }

        private float magmaMarkBudget;

        /// <summary>Magma cells and active Flame Burst cones glow on the board.</summary>
        private void EmitResonantMarks()
        {
            if (areaEffects == null) return;
            magmaMarkBudget += Time.deltaTime * 10f;
            if (magmaMarkBudget < 1f) return;
            magmaMarkBudget -= 1f;
            foreach (MagmaCell hot in areaEffects.Magma) AbilityVfx.MagmaMark(EffectWorldPosition(hot.Cell));
            foreach (CombatUnit owner in units.Where(owner => !owner.IsDefeated && owner.Channel != null))
                foreach (GridCoordinate cell in ConePattern.Cells(grid, owner.Cell, owner.Channel.Direction, owner.Channel.Stage))
                    AbilityVfx.FlameConeMark(EffectWorldPosition(cell));
        }
    }
}
