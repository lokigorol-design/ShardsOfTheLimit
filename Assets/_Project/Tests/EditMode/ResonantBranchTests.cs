using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Fire Worm II/IV and Primal Idol IV branches, Ritual Surfaces on walls (design 2.5, 10.2 and 10.11).</summary>
    public sealed class ResonantBranchTests
    {
        private TacticalGrid grid;
        private static GridCoordinate C(int x, int y) => new GridCoordinate(x, y);

        private static CombatUnit Unit(string name, GridCoordinate cell, Team team, int attack = 20, int hp = 100) =>
            new CombatUnit(name, name, hp, 0, 10, 2, team, ControlType.Player, cell, attack);

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
        public void WormCardHasAllFourAbilitiesAndFourBranchVariants()
        {
            var worm = Resources.Load<UnitDefinition>("Prototype/Units/BlazeWorm");
            Assert.That(worm.Abilities.Select(a => a.StableId),
                Is.EqualTo(new[] { "blaze-long-bite", "blaze-fiery-spit", "PurifyingFlame", "FlameBurst" }));
            Assert.That(worm.Abilities.All(a => a.Icon != null), Is.True);
            Assert.That(worm.BranchVariants.Count, Is.EqualTo(4));
            Assert.That(Resources.Load<UnitDefinition>("Prototype/Units/PrimalIdol").BranchVariants.Count, Is.EqualTo(4));
            Assert.That(Resources.Load<UnitDefinition>("Prototype/Units/Blightgullet").BranchVariants.Count, Is.EqualTo(3));
        }

        [Test]
        public void FlameBurstGrowsByStageAndEndsAfterTheThird()
        {
            var worm = Unit("worm", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 2), Team.Opponent);
            enemy.SetFacing(C(-1, 0));
            var units = new[] { worm, enemy };
            var world = new AreaEffectWorld(grid, units);
            AbilityDefinition burst = Load("FlameBurst");
            worm.BeginActivation(2);

            Assert.That(AbilityRuntime.GetTargetableCells(burst, grid, worm, units), Is.EquivalentTo(worm.Cell.FourWayNeighbors()));
            AbilityResolution first = AbilityRuntime.Execute(burst, grid, worm, C(3, 2), units, 0, world);
            Assert.That(first.ImpactCells, Is.EquivalentTo(new[] { C(3, 2), C(4, 1), C(4, 2), C(4, 3) }), "Stage 1: 1-3.");
            Assert.That(first.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(16), "0,8 × ATK 20.");
            Assert.That(first.Results.OfType<BurnAppliedResult>().Count(), Is.EqualTo(1));
            Assert.That(worm.Channel.Stage, Is.EqualTo(1));
            Assert.That(AbilityRuntime.GetTargetableCells(burst, grid, worm, units), Is.EqualTo(new[] { C(3, 2) }),
                "An active channel keeps its direction.");

            worm.BeginActivation(2);
            AbilityResolution second = AbilityRuntime.Execute(burst, grid, worm, C(3, 2), units, 100, world);
            Assert.That(second.ImpactCells.Count, Is.EqualTo(7), "Stage 2: 1-3-3.");
            Assert.That(second.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(20));

            worm.BeginActivation(2);
            AbilityResolution third = AbilityRuntime.Execute(burst, grid, worm, C(3, 2), units, 200, world);
            Assert.That(third.ImpactCells.Count, Is.EqualTo(17), "Stage 3: 1-3-3-5-5 (clipped by nothing on an 8×8 board).");
            Assert.That(third.Results.OfType<DamageResult>().Single().HealthDamage, Is.EqualTo(24));
            Assert.That(third.ChannelEnded, Is.True);
            Assert.That(worm.Channel, Is.Null);
        }

        [Test]
        public void RoaringFurnaceJumpsToFullPowerOnTheSecondTurn()
        {
            var worm = Unit("worm", C(2, 2), Team.Player);
            var units = new[] { worm };
            var world = new AreaEffectWorld(grid, units);
            var furnace = Branch(b => b.roaringFurnace = true);
            AbilityDefinition burst = Load("FlameBurst");
            AbilityRuntime.Execute(burst, grid, worm, C(2, 3), units, 0, world, furnace);
            AbilityResolution second = AbilityRuntime.Execute(burst, grid, worm, C(2, 3), units, 100, world, furnace);
            Assert.That(second.ChannelStage, Is.EqualTo(3));
            Assert.That(second.ChannelEnded, Is.True);
        }

        [Test]
        public void MagmaWormTurnsBurningIntoMagmaOnTheTargetCell()
        {
            var worm = Unit("worm", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(4, 2), Team.Opponent);
            var units = new[] { worm, enemy };
            var world = new AreaEffectWorld(grid, units);
            var magma = Branch(b => b.magmaWorm = true);
            AbilityResolution spit = AbilityRuntime.Execute(Load("BlazeFierySpit"), grid, worm, enemy.Cell, units, 0, world, magma);
            Assert.That(spit.Results.OfType<BurnAppliedResult>(), Is.Empty);
            Assert.That(spit.MagmaCells, Is.EqualTo(new[] { enemy.Cell }));
            Assert.That(world.MagmaAt(enemy.Cell), Is.Not.Null);
            Assert.That(world.MagmaAt(enemy.Cell).Power, Is.EqualTo(5), "0,25 × ATK 20.");
        }

        [Test]
        public void MagmaIsLimitedToTwelveCellsPerCreator()
        {
            var worm = Unit("worm", C(0, 0), Team.Player);
            var world = new AreaEffectWorld(grid, new[] { worm });
            for (int index = 0; index < 14; index++) world.AddMagma(worm, C(index % 8, index / 8), index, 5);
            Assert.That(world.Magma.Count, Is.EqualTo(12));
            Assert.That(world.MagmaAt(C(0, 0)), Is.Null, "The oldest cells disappear first.");
        }

        [Test]
        public void BloodOfTheDeepHealsEightPercentOnMagma()
        {
            var worm = Unit("worm", C(1, 1), Team.Player);
            var world = new AreaEffectWorld(grid, new[] { worm });
            world.Register(worm, new PassiveModifiers(magmaHealPercent: 8));
            CombatEffectResolver.Apply(new[] { CombatEffectCalculator.Damage(worm, worm.ResourceState, 50) });
            int before = worm.CurrentHealth;
            world.OnActivation(worm);
            Assert.That(worm.CurrentHealth, Is.EqualTo(before), "No Magma, no healing.");
            world.AddMagma(worm, worm.Cell, 0, 5);
            world.OnActivation(worm);
            Assert.That(worm.CurrentHealth, Is.EqualTo(before + 8));
        }

        [Test]
        public void BankedHeatGivesOneExtraActionPointThatDoesNotAccumulate()
        {
            var worm = Unit("worm", C(1, 1), Team.Player);
            worm.BankActionPoint();
            worm.BankActionPoint();
            worm.BeginActivation(2);
            Assert.That(worm.CurrentActionPoints, Is.EqualTo(3));
            worm.BeginActivation(2);
            Assert.That(worm.CurrentActionPoints, Is.EqualTo(2));
        }

        [Test]
        public void PurifyingFlameCleansesAtTurnStartAndBurnsMeleeAttackers()
        {
            var worm = Unit("worm", C(2, 2), Team.Player);
            var enemy = Unit("enemy", C(3, 2), Team.Opponent);
            var units = new[] { worm, enemy };
            var world = new AreaEffectWorld(grid, units);
            AbilityRuntime.Execute(Load("PurifyingFlame"), grid, worm, worm.Cell, units, 0, world);
            Assert.That(worm.GetMechanicStacks(StatusMechanic.PurifyingFlame), Is.EqualTo(1));

            var poison = new StatusRules("POISON", 99, 300, 100, 0, false, StatusMechanic.Poison);
            worm.ApplyStatus(StatusCalculator.Apply(worm, poison, 3, 0, enemy.Id));
            world.OnActivation(worm);
            Assert.That(worm.GetStacks("POISON"), Is.Zero, "Negative effects are cleansed at the start of the turn.");
            Assert.That(worm.GetMechanicStacks(StatusMechanic.PurifyingFlame), Is.EqualTo(1), "The buff itself stays.");

            AbilityResolution bite = AbilityRuntime.Preview(Load("BlazeLongBite"), grid, enemy, worm.Cell, units, 0, world);
            BurnAppliedResult retaliation = bite.Results.OfType<BurnAppliedResult>().Single(burn => burn.Target == enemy);
            Assert.That(retaliation.AddedDamagePerPulse, Is.EqualTo(5), "0,25 × worm ATK 20.");
        }

        [Test]
        public void TwinEruptionStrikesAgainTwoTurnsLater()
        {
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var units = new[] { idol };
            var world = new AreaEffectWorld(grid, units);
            AbilityResolution geyser = AbilityRuntime.Preview(Load("FireGeyser"), grid, idol, C(2, 2), units, 0, world,
                Branch(b => b.twinEruption = true));
            Assert.That(geyser.Strikes.Select(s => s.StrikeTick), Is.EqualTo(new[] { 100L, 300L }));
        }

        [Test]
        public void StormOverchargeReachesThreeCellsAndImmobilizesForTwoTurns()
        {
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var enemy = Unit("enemy", C(5, 2), Team.Opponent);
            var units = new[] { idol, enemy };
            var world = new AreaEffectWorld(grid, units);
            AbilityDefinition magnet = Load("StormMagnet");
            Assert.That(AbilityRuntime.Preview(magnet, grid, idol, C(2, 2), units, 0, world).Results, Is.Empty,
                "Base reach is 2 cells.");
            AbilityResolution overcharged = AbilityRuntime.Preview(magnet, grid, idol, C(2, 2), units, 0, world,
                Branch(b => b.stormOvercharge = true));
            Assert.That(overcharged.Results.OfType<PushResult>().Single().Destination, Is.EqualTo(C(2, 2)));
            Assert.That(overcharged.Results.OfType<StatusAppliedResult>().Single().State.ExpireTick, Is.EqualTo(200));
        }

        [Test]
        public void TitanPillarRisesThreeAndRagingTornadoThrowsSixUnits()
        {
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var units = new[] { idol };
            var world = new AreaEffectWorld(grid, units);
            AbilityResolution pillar = AbilityRuntime.Preview(Load("EarthPillar"), grid, idol, C(2, 2), units, 0, world,
                Branch(b => b.titanPillar = true));
            Assert.That(pillar.TerrainResults.Single().NewHeight, Is.EqualTo(3));
            AbilityResolution trap = AbilityRuntime.Preview(Load("LiftTrap"), grid, idol, C(2, 2), units, 0, world,
                Branch(b => b.ragingTornado = true));
            Assert.That(trap.Traps.Single().FallHealthPercent, Is.EqualTo(20), "A 6-unit fall: 5% × (6 − 2).");
        }

        /// <summary>A rock at (4,0) faces the idol at (0,0); the cell in front of it is (3,0), outward is −X.</summary>
        private TacticalGrid WallGrid() => new TacticalGrid(Enumerable.Range(0, 8).SelectMany(x =>
            Enumerable.Range(0, 8).Select(y => new GridCell(C(x, y), 0, !(x == 4 && y == 0)))));

        [Test]
        public void SurfaceAbilitiesTargetWallsFacingTheCaster()
        {
            grid = WallGrid();
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var units = new[] { idol };
            Assert.That(SurfaceRules.TryGetWall(grid, idol.Cell, C(4, 0), out GridCoordinate front, out GridCoordinate outward), Is.True);
            Assert.That(front, Is.EqualTo(C(3, 0)));
            Assert.That(outward, Is.EqualTo(C(-1, 0)));
            Assert.That(AbilityRuntime.GetTargetableCells(Load("FireGeyser"), grid, idol, units, true), Is.EqualTo(new[] { C(4, 0) }));
            Assert.That(AbilityRuntime.GetTargetableCells(Load("FireGeyser"), grid, idol, units).Contains(C(4, 0)), Is.False,
                "On the floor a rock is not a target.");
        }

        [Test]
        public void WallGeyserShootsSidewaysThreeCells()
        {
            grid = WallGrid();
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var units = new[] { idol };
            var world = new AreaEffectWorld(grid, units);
            AbilityResolution geyser = AbilityRuntime.Preview(Load("FireGeyser"), grid, idol, C(4, 0), units, 0, world, null, true);
            Assert.That(geyser.Strikes.Single().Cells, Is.EquivalentTo(new[] { C(3, 0), C(2, 0), C(1, 0) }));
        }

        [Test]
        public void WallPillarShovesTheOccupantAwayAndTitanShovesTwo()
        {
            grid = WallGrid();
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var enemy = Unit("enemy", C(3, 0), Team.Opponent);
            var units = new[] { idol, enemy };
            var world = new AreaEffectWorld(grid, units);
            AbilityResolution ledge = AbilityRuntime.Preview(Load("EarthPillar"), grid, idol, C(4, 0), units, 0, world, null, true);
            Assert.That(ledge.Results.OfType<PushResult>().Single().Destination, Is.EqualTo(C(2, 0)));
            Assert.That(ledge.TerrainResults.Single().Coordinate, Is.EqualTo(C(3, 0)));
            AbilityResolution titan = AbilityRuntime.Preview(Load("EarthPillar"), grid, idol, C(4, 0), units, 0, world,
                Branch(b => b.titanPillar = true), true);
            Assert.That(titan.Results.OfType<PushResult>().Single().Destination, Is.EqualTo(C(1, 0)));
        }

        [Test]
        public void WallTrapPushesTwoCellsAwayFromTheWall()
        {
            grid = WallGrid();
            var idol = Unit("idol", C(0, 0), Team.Player, 17);
            var enemy = Unit("enemy", C(3, 3), Team.Opponent);
            var units = new[] { idol, enemy };
            var world = new AreaEffectWorld(grid, units);
            AbilityRuntime.Execute(Load("LiftTrap"), grid, idol, C(4, 0), units, 0, world, null, true);
            Assert.That(world.TrapAt(C(3, 0)), Is.Not.Null);
            enemy.MoveTo(C(3, 0));
            TrapTrigger trigger = world.TriggerTrap(enemy, C(3, 0));
            Assert.That(trigger.Push.Destination, Is.EqualTo(C(1, 0)));
            Assert.That(enemy.Cell, Is.EqualTo(C(1, 0)));
        }
    }
}
