using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Tests
{
    public sealed class FacingTests
    {
        private static CombatUnit Unit(GridCoordinate cell) =>
            new CombatUnit("u", "u", 100, 0, 10, 3, Team.Opponent, ControlType.Passive, cell);

        [Test]
        public void FacingAcceptsOnlyCardinalDirections()
        {
            CombatUnit unit = Unit(new GridCoordinate(2, 2));
            unit.SetFacing(new GridCoordinate(0, -1));
            Assert.That(unit.Facing, Is.EqualTo(new GridCoordinate(0, -1)));
            Assert.Throws<ArgumentException>(() => unit.SetFacing(new GridCoordinate(1, 1)));
            Assert.Throws<ArgumentException>(() => unit.SetFacing(new GridCoordinate(0, 0)));
        }

        [Test]
        public void AttackSideIsFrontBackOrSideRelativeToFacing()
        {
            CombatUnit target = Unit(new GridCoordinate(2, 2));
            target.SetFacing(new GridCoordinate(1, 0));
            Assert.That(FacingRules.GetSide(target, new GridCoordinate(5, 2)), Is.EqualTo(AttackSide.Front));
            Assert.That(FacingRules.GetSide(target, new GridCoordinate(0, 2)), Is.EqualTo(AttackSide.Back));
            Assert.That(FacingRules.GetSide(target, new GridCoordinate(2, 4)), Is.EqualTo(AttackSide.Side));
            Assert.That(FacingRules.GetSide(target, new GridCoordinate(1, 0)), Is.EqualTo(AttackSide.Side),
                "Dominant axis decides: (−1, −2) from the target is the −Y side.");
            Assert.That(FacingRules.GetSide(target, new GridCoordinate(0, 1)), Is.EqualTo(AttackSide.Back),
                "Diagonal ties resolve along X.");
        }
        [Test]
        public void BackAndSideHitsDealMoreDamage()
        {
            Assert.That(HitModifiers.FacingMultiplier(AttackSide.Back), Is.EqualTo(1.25d));
            Assert.That(HitModifiers.FacingMultiplier(AttackSide.Side), Is.EqualTo(1.10d));
            Assert.That(HitModifiers.FacingMultiplier(AttackSide.Front), Is.EqualTo(1d));

            AbilityDefinition heavy = Resources.Load<AbilityDefinition>("Prototype/Abilities/HeavyStrike");
            var grid = new TacticalGrid(Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4)
                .Select(y => new GridCell(new GridCoordinate(x, y), 0))));
            var source = new CombatUnit("s", "s", 100, 0, 10, 3, Team.Player, ControlType.Passive, new GridCoordinate(1, 1), 20);
            var target = new CombatUnit("t", "t", 100, 0, 10, 3, Team.Opponent, ControlType.Passive, new GridCoordinate(2, 1));
            var units = new[] { source, target };
            int Hit() => -AbilityRuntime.Preview(heavy, grid, source, target.Cell, units).Results.OfType<DamageResult>().Single().HealthDelta;
            target.SetFacing(new GridCoordinate(-1, 0));
            Assert.That(Hit(), Is.EqualTo(24), "Front: 1.2 × ATK 20.");
            target.SetFacing(new GridCoordinate(1, 0));
            Assert.That(Hit(), Is.EqualTo(30), "Back: +25%.");
            target.SetFacing(new GridCoordinate(0, 1));
            Assert.That(Hit(), Is.EqualTo(26), "Side: +10% (26.4).");
        }

        [Test]
        public void HeightChangesDamageTenPercentMeleeFivePercentRangedPerUnit()
        {
            Assert.That(HitModifiers.HeightMultiplier(AbilityRangeType.Melee, 2, 0), Is.EqualTo(1.2d).Within(1e-9));
            Assert.That(HitModifiers.HeightMultiplier(AbilityRangeType.Melee, 0, 2), Is.EqualTo(0.8d).Within(1e-9));
            Assert.That(HitModifiers.HeightMultiplier(AbilityRangeType.Ranged, 3, 0), Is.EqualTo(1.15d).Within(1e-9));
            Assert.That(HitModifiers.HeightMultiplier(AbilityRangeType.Ranged, 0, 4), Is.EqualTo(0.8d).Within(1e-9));
        }

        [Test]
        public void LineOfFireIsBlockedByWallsAndObstaclesButNotFromHighGround()
        {
            var cells = new List<GridCell>();
            for (int x = 0; x < 6; x++) cells.Add(new GridCell(new GridCoordinate(x, 0), x == 2 ? 4 : 0));
            var walled = new TacticalGrid(cells);
            Assert.That(LineOfFire.IsClear(walled, new GridCoordinate(0, 0), new GridCoordinate(4, 0)), Is.False,
                "A 4-unit wall blocks a shot between two ground cells.");
            Assert.That(LineOfFire.IsClear(walled, new GridCoordinate(0, 0), new GridCoordinate(1, 0)), Is.True);

            var rock = new TacticalGrid(Enumerable.Range(0, 5).Select(x => new GridCell(new GridCoordinate(x, 0), 0, x != 2)));
            Assert.That(LineOfFire.IsClear(rock, new GridCoordinate(0, 0), new GridCoordinate(4, 0)), Is.False,
                "An obstacle stands 2 units tall.");

            var high = new TacticalGrid(Enumerable.Range(0, 5).Select(x =>
                new GridCell(new GridCoordinate(x, 0), x == 0 ? 6 : 0, x != 2)));
            Assert.That(LineOfFire.IsClear(high, new GridCoordinate(0, 0), new GridCoordinate(4, 0)), Is.True,
                "From high ground the shot passes over the obstacle.");
        }

        [Test]
        public void FallFromThreeUnitsOrMoreCostsFivePercentPerExtraUnit()
        {
            Assert.That(FallRules.DamageForDrop(100, 2), Is.Zero);
            Assert.That(FallRules.DamageForDrop(100, 3), Is.EqualTo(5d));
            Assert.That(FallRules.DamageForDrop(100, 4), Is.EqualTo(10d));
            Assert.That(FallRules.DamageForDrop(200, 22), Is.EqualTo(200d), "A 22-unit fall kills anything.");
        }
    }
}
