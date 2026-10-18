using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Mist Panther card (design 2.5, 10.12) and the Invisibility, Mist and Bleed rules (16.10.8).</summary>
    public sealed class MistPantherTests
    {
        private TacticalGrid grid;
        private static GridCoordinate C(int x, int y) => new GridCoordinate(x, y);

        private static CombatUnit Unit(string name, GridCoordinate cell, Team team, int attack = 20, int hp = 100) =>
            new CombatUnit(name, name, hp, 0, 10, 4, team, ControlType.Player, cell, attack);

        private static AbilityDefinition Load(string id) => Resources.Load<AbilityDefinition>("Prototype/Abilities/" + id);

        private static PassiveModifierDefinition Branch(System.Action<PassiveModifierDefinition> configure)
        {
            var modifiers = ScriptableObject.CreateInstance<PassiveModifierDefinition>();
            configure(modifiers);
            return modifiers;
        }

        [SetUp]
        public void Setup()
        {
            grid = new TacticalGrid(Enumerable.Range(0, 8).SelectMany(x =>
                Enumerable.Range(0, 8).Select(y => new GridCell(C(x, y), 0))));
        }

        [Test]
        public void UnitMatchesCardStats()
        {
            var panther = Resources.Load<UnitDefinition>("Prototype/Units/MistPanther");
            Assert.That(panther.MaximumHealth, Is.EqualTo(80));
            Assert.That(panther.MaximumShield, Is.EqualTo(40));
            Assert.That(panther.Attack, Is.EqualTo(19));
            Assert.That(panther.Defense, Is.EqualTo(2));
            Assert.That(panther.Speed, Is.EqualTo(12));
            Assert.That(panther.MovementRange, Is.EqualTo(4));
            Assert.That(panther.Abilities.Select(a => a.StableId),
                Is.EqualTo(new[] { "WideSwipe", "VanishingStep", "PartingStrike", "BloodDash" }));
            Assert.That(panther.BranchVariants.Count, Is.EqualTo(2));
        }

        [Test]
        public void WideSwipeHitsThreeCellsInFrontIncludingAllies()
        {
            var panther = Unit("panther", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 1), Team.Opponent);
            var ally = Unit("ally", C(3, 3), Team.Player);
            var behind = Unit("behind", C(1, 2), Team.Opponent);
            var units = new[] { panther, enemy, ally, behind };
            AbilityResolution swipe = AbilityRuntime.Preview(Load("WideSwipe"), grid, panther, C(3, 2), units);
            Assert.That(swipe.ImpactCells, Is.EquivalentTo(new[] { C(3, 1), C(3, 2), C(3, 3) }));
            Assert.That(swipe.Results.OfType<DamageResult>().Select(hit => hit.Target), Is.EquivalentTo(new[] { enemy, ally }),
                "Friendly fire: the ally in the arc is hit too.");
        }

        [Test]
        public void InvisibilityHidesFromEnemiesAndTheFirstAttackHitsHarderThenReveals()
        {
            var panther = Unit("panther", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 2), Team.Opponent);
            enemy.SetFacing(C(-1, 0));
            var units = new[] { panther, enemy };
            AbilityRuntime.Execute(Load("VanishingStep"), grid, panther, panther.Cell, units);
            Assert.That(panther.IsInvisible, Is.True);
            Assert.That(AbilityRuntime.GetTargetableCells(Load("PartingStrike"), grid, enemy, units), Is.Empty,
                "Enemies cannot choose an invisible resonant.");

            AbilityResolution swipe = AbilityRuntime.Execute(Load("WideSwipe"), grid, panther, C(3, 2), units);
            Assert.That(swipe.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(22), "0,8 × 20 × 1,4.");
            Assert.That(swipe.FromInvisibility, Is.True);
            Assert.That(panther.IsInvisible, Is.False, "The first attack ends Invisibility.");
        }

        [Test]
        public void SilentExecutionerAddsAnotherFortyPercentAgainstWoundedTargets()
        {
            var panther = Unit("panther", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 2), Team.Opponent);
            enemy.SetFacing(C(-1, 0));
            CombatEffectResolver.Apply(new[] { CombatEffectCalculator.Damage(enemy, enemy.ResourceState, 60) });
            var units = new[] { panther, enemy };
            var shadow = Branch(b => b.silentExecutioner = true);
            AbilityRuntime.Execute(Load("VanishingStep"), grid, panther, panther.Cell, units, 0, null, shadow);
            AbilityResolution swipe = AbilityRuntime.Preview(Load("WideSwipe"), grid, panther, C(3, 2), units, 0, null, shadow);
            Assert.That(swipe.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(29), "0,8 × 20 × 1,8 = 28,8.");
        }

        [Test]
        public void MistVeilTurnsInvisibilityIntoMistWhichNothingTouchesAndAnAttackFromMistAppliesVulnerability()
        {
            var panther = Unit("panther", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 2), Team.Opponent);
            enemy.SetFacing(C(-1, 0));
            var units = new[] { panther, enemy };
            var mist = Branch(b => b.mistVeil = true);
            AbilityRuntime.Execute(Load("VanishingStep"), grid, panther, panther.Cell, units, 0, null, mist);
            Assert.That(panther.IsInMist, Is.True);
            Assert.That(panther.IsInvisible, Is.False);

            AbilityResolution enemySwipe = AbilityRuntime.Preview(Load("WideSwipe"), grid, enemy, C(2, 2), units);
            Assert.That(enemySwipe.Results.Where(result => result.Target == panther), Is.Empty, "Mist: no effect touches it.");

            AbilityResolution swipe = AbilityRuntime.Execute(Load("WideSwipe"), grid, panther, C(3, 2), units, 0, null, mist);
            Assert.That(swipe.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(16), "No Invisibility bonus from Mist.");
            Assert.That(enemy.GetMechanicStacks(StatusMechanic.Vulnerability), Is.EqualTo(1));
            Assert.That(panther.IsInMist, Is.False);
        }

        [Test]
        public void BloodDashRunsUpToTheTargetHitsAndBleeds()
        {
            var panther = Unit("panther", C(0, 0), Team.Player);
            var enemy = Unit("enemy", C(4, 0), Team.Opponent);
            enemy.SetFacing(C(-1, 0));
            var far = Unit("far", C(0, 6), Team.Opponent);
            var units = new[] { panther, enemy, far };
            AbilityDefinition dash = Load("BloodDash");
            Assert.That(AbilityRuntime.GetTargetableCells(dash, grid, panther, units), Is.EqualTo(new[] { enemy.Cell }),
                "Up to 3 cells of running: 4 cells away is reachable, 6 is not.");

            AbilityResolution hit = AbilityRuntime.Execute(dash, grid, panther, enemy.Cell, units);
            Assert.That(panther.Cell, Is.EqualTo(C(3, 0)));
            Assert.That(hit.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(18), "0,9 × 20.");
            Assert.That(enemy.GetMechanicStacks(StatusMechanic.Bleed), Is.EqualTo(1));

            enemy.BeginActivation(2);
            int before = enemy.CurrentHealth;
            enemy.SpendActionPoints(1);
            Assert.That(before - enemy.CurrentHealth, Is.EqualTo(3), "Bleed: 0,15 × 20 each time AP is spent.");
            Assert.That(enemy.TakeBleedHits().Count, Is.EqualTo(1));
        }

        [Test]
        public void DashIsBlockedByAnyoneInTheWay()
        {
            var panther = Unit("panther", C(0, 0), Team.Player);
            var blocker = Unit("blocker", C(2, 0), Team.Player);
            var enemy = Unit("enemy", C(3, 0), Team.Opponent);
            Assert.That(AbilityRuntime.GetTargetableCells(Load("BloodDash"), grid, panther, new[] { panther, blocker, enemy }), Is.Empty);
        }

        [Test]
        public void DeepWoundsLetsBleedStack()
        {
            var panther = Unit("panther", C(0, 0), Team.Player);
            var enemy = Unit("enemy", C(1, 0), Team.Opponent);
            var units = new[] { panther, enemy };
            var wounds = Branch(b => b.deepWounds = true);
            AbilityRuntime.Execute(Load("BloodDash"), grid, panther, enemy.Cell, units, 0, null, wounds);
            AbilityRuntime.Execute(Load("BloodDash"), grid, panther, enemy.Cell, units, 0, null, wounds);
            Assert.That(enemy.GetMechanicStacks(StatusMechanic.Bleed), Is.EqualTo(2));
            enemy.BeginActivation(2);
            int before = enemy.CurrentHealth;
            enemy.SpendActionPoints(1);
            Assert.That(before - enemy.CurrentHealth, Is.EqualTo(6));
        }

        [Test]
        public void PartingStrikeKnocksTheTargetBackAndThePantherLeapsAway()
        {
            var panther = Unit("panther", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 2), Team.Opponent);
            enemy.SetFacing(C(-1, 0));
            var units = new[] { panther, enemy };
            AbilityResolution strike = AbilityRuntime.Execute(Load("PartingStrike"), grid, panther, enemy.Cell, units);
            Assert.That(enemy.Cell, Is.EqualTo(C(4, 2)));
            Assert.That(panther.Cell, Is.EqualTo(C(1, 2)));
            Assert.That(strike.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(10), "0,5 × 20.");
        }
    }
}
