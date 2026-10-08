using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Primal Idol card, design 2.5 (10.11): floor versions of its four surface abilities.</summary>
    public sealed class PrimalIdolTests
    {
        private TacticalGrid grid;
        private AreaEffectWorld world;
        private static GridCoordinate C(int x, int y) => new GridCoordinate(x, y);

        private static CombatUnit Unit(string name, GridCoordinate cell, Team team, int attack = 17, int defense = 0) =>
            new CombatUnit(name, name, 100, 0, 10, 2, team, ControlType.Player, cell, attack, defense);

        private static AbilityDefinition Load(string id) => Resources.Load<AbilityDefinition>("Prototype/Abilities/" + id);

        [SetUp]
        public void Setup()
        {
            grid = new TacticalGrid(Enumerable.Range(0, 8).SelectMany(x =>
                Enumerable.Range(0, 8).Select(y => new GridCell(C(x, y), 0))));
        }

        [Test]
        public void UnitMatchesCardStats()
        {
            var idol = Resources.Load<UnitDefinition>("Prototype/Units/PrimalIdol");
            Assert.That(idol, Is.Not.Null);
            Assert.That(idol.MaximumHealth, Is.EqualTo(90));
            Assert.That(idol.MaximumShield, Is.EqualTo(60));
            Assert.That(idol.Attack, Is.EqualTo(17));
            Assert.That(idol.Speed, Is.EqualTo(10));
            Assert.That(idol.MovementRange, Is.EqualTo(2));
            Assert.That(idol.Abilities.Select(a => a.StableId),
                Is.EqualTo(new[] { "StormMagnet", "FireGeyser", "EarthPillar", "LiftTrap" }));
            Assert.That(idol.Abilities.All(a => a.Icon != null), Is.True);
            Assert.That(idol.VisualPrefab, Is.Not.Null);
            Assert.That(idol.Portrait, Is.Not.Null);
        }

        [Test]
        public void StormMagnetPullsFirstResonantOnLineOntoPointDamagesAndImmobilizes()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var near = Unit("near", C(4, 2), Team.Opponent);
            var far = Unit("far", C(2, 4), Team.Opponent);
            near.SetFacing(C(-1, 0));
            var units = new[] { idol, near, far };
            world = new AreaEffectWorld(grid, units);
            var point = C(2, 2);

            AbilityResolution preview = AbilityRuntime.Preview(Load("StormMagnet"), grid, idol, point, units, 0, world);
            Assert.That(preview.Results.Select(r => r.Target).Distinct(), Is.EquivalentTo(new[] { near }),
                "Equal distance: +X line is checked first.");
            AbilityRuntime.Execute(Load("StormMagnet"), grid, idol, point, units, 0, world);
            Assert.That(near.Cell, Is.EqualTo(point));
            Assert.That(near.CurrentHealth, Is.EqualTo(83), "1 x ATK 17.");
            Assert.That(near.IsImmobilized, Is.True);
            Assert.That(far.Cell, Is.EqualTo(C(2, 4)));
        }

        [Test]
        public void StormMagnetReachesOnlyTwoCells()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var tooFar = Unit("far", C(5, 2), Team.Opponent);
            var units = new[] { idol, tooFar };
            world = new AreaEffectWorld(grid, units);
            AbilityResolution preview = AbilityRuntime.Preview(Load("StormMagnet"), grid, idol, C(2, 2), units, 0, world);
            Assert.That(preview.Results, Is.Empty);
        }

        [Test]
        public void EarthPillarRaisesOccupiedCellByTwoWithItsOccupant()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var ally = Unit("ally", C(2, 0), Team.Player);
            var units = new[] { idol, ally };
            world = new AreaEffectWorld(grid, units);
            var pillar = Load("EarthPillar");
            Assert.That(AbilityRuntime.GetTargetableCells(pillar, grid, idol, units), Has.Member(ally.Cell));
            AbilityRuntime.Execute(pillar, grid, idol, ally.Cell, units, 0, world);
            Assert.That(grid.GetCell(ally.Cell).Height, Is.EqualTo(2));
            Assert.That(ally.Cell, Is.EqualTo(C(2, 0)), "The resonant rides the pillar up.");
        }

        [Test]
        public void FireGeyserMarksNowAndStrikesAfterOneHundredTicks()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var enemy = Unit("enemy", C(3, 0), Team.Opponent, defense: 0);
            var units = new[] { idol, enemy };
            world = new AreaEffectWorld(grid, units);
            AbilityResolution cast = AbilityRuntime.Execute(Load("FireGeyser"), grid, idol, enemy.Cell, units, 50, world);
            Assert.That(cast.Strikes.Single().StrikeTick, Is.EqualTo(150));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(100), "Nothing happens before the delay.");
            Assert.That(world.NextEventTick, Is.EqualTo(150));

            world.ProcessTick(150);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(86), "0,8 x ATK 17 = 13.6 → 14.");
            Assert.That(enemy.IsBurning, Is.True);
            Assert.That(enemy.BurningState.DamagePerPulse, Is.EqualTo(4), "0,25 x ATK 17 = 4.25 → 4.");
            Assert.That(world.TakeFiredStrikes().Count, Is.EqualTo(1));
            Assert.That(world.PendingStrikes, Is.Empty);
        }

        [Test]
        public void FireGeyserHitsWhoeverStandsOnThePointWhenItFires()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var enemy = Unit("enemy", C(3, 0), Team.Opponent);
            var units = new[] { idol, enemy };
            world = new AreaEffectWorld(grid, units);
            AbilityRuntime.Execute(Load("FireGeyser"), grid, idol, enemy.Cell, units, 0, world);
            enemy.MoveTo(C(4, 0));
            world.ProcessTick(100);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(100), "The mark is visible, so the target could step away.");
        }

        [Test]
        public void LiftTrapFiresOnceOnFirstEntryForTenPercentIgnoringDefense()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var enemy = Unit("enemy", C(5, 5), Team.Opponent, defense: 50);
            var units = new[] { idol, enemy };
            world = new AreaEffectWorld(grid, units);
            var trapCell = C(2, 0);
            AbilityRuntime.Execute(Load("LiftTrap"), grid, idol, trapCell, units, 0, world);
            Assert.That(world.TrapAt(trapCell), Is.Not.Null);

            enemy.MoveTo(trapCell);
            DamageResult fall = world.TriggerTrap(enemy, trapCell);
            Assert.That(fall.HealthDamage, Is.EqualTo(10));
            Assert.That(world.TrapAt(trapCell), Is.Null, "A trap fires once.");
            Assert.That(world.TriggerTrap(enemy, trapCell), Is.Null);
        }

        [Test]
        public void LiftTrapExpiresAfterThreeTurns()
        {
            var idol = Unit("idol", C(0, 0), Team.Player);
            var units = new[] { idol };
            world = new AreaEffectWorld(grid, units);
            AbilityRuntime.Execute(Load("LiftTrap"), grid, idol, C(2, 0), units, 0, world);
            world.ProcessTick(299);
            Assert.That(world.Traps.Count, Is.EqualTo(1));
            world.ProcessTick(300);
            Assert.That(world.Traps, Is.Empty);
        }
    }
}
