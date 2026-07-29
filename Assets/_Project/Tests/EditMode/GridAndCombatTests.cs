using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Tests
{
    public sealed class GridAndCombatTests
    {
        [Test]
        public void GridRejectsDuplicateCoordinates()
        {
            var duplicate = new GridCoordinate(1, 2);
            Assert.Throws<ArgumentException>(() => new TacticalGrid(new[]
            {
                new GridCell(duplicate, 0),
                new GridCell(duplicate, 2)
            }));
        }

        [Test]
        public void ReachableCellsRespectRangeWalkabilityAndOccupancy()
        {
            TacticalGrid grid = CreateGrid(4, 3, new GridCoordinate(1, 1));
            var occupied = new GridCoordinate(2, 0);

            IReadOnlyDictionary<GridCoordinate, int> result = grid.GetReachable(
                new GridCoordinate(0, 0),
                3,
                coordinate => coordinate == occupied);

            Assert.That(result.ContainsKey(new GridCoordinate(0, 2)), Is.True);
            Assert.That(result.ContainsKey(new GridCoordinate(1, 1)), Is.False, "Blocked cell was included.");
            Assert.That(result.ContainsKey(occupied), Is.False, "Occupied cell was included.");
            Assert.That(result.Keys.All(cell => cell.ManhattanDistance(new GridCoordinate(0, 0)) <= 3), Is.True);
        }

        [Test]
        public void PathUsesShortestLegalDetour()
        {
            TacticalGrid grid = CreateGrid(4, 3);
            var occupied = new GridCoordinate(1, 0);

            IReadOnlyList<GridCoordinate> path = grid.FindPath(
                new GridCoordinate(0, 0),
                new GridCoordinate(3, 0),
                5,
                coordinate => coordinate == occupied);

            CollectionAssert.AreEqual(new[]
            {
                new GridCoordinate(0, 0),
                new GridCoordinate(0, 1),
                new GridCoordinate(1, 1),
                new GridCoordinate(2, 1),
                new GridCoordinate(3, 1),
                new GridCoordinate(3, 0)
            }, path);
        }

        [Test]
        public void TargetingAndImpactPatternsAreIndependent()
        {
            TacticalGrid grid = CreateGrid(7, 7);
            var source = new GridCoordinate(1, 1);
            var target = new GridCoordinate(4, 1);

            IReadOnlyCollection<GridCoordinate> targeting = PatternCalculator.Diamond(grid, source, 4, false);
            IReadOnlyCollection<GridCoordinate> impact = PatternCalculator.Diamond(grid, target, 1, true);

            CollectionAssert.Contains(targeting.ToArray(), target);
            CollectionAssert.DoesNotContain(targeting.ToArray(), source);
            Assert.That(impact.Count, Is.EqualTo(5));
            CollectionAssert.Contains(impact.ToArray(), target);
            CollectionAssert.DoesNotContain(impact.ToArray(), source);
        }

        [Test]
        public void DamageConsumesShieldBeforeHealthWithoutChangingMaximums()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(2, 2), 10, 4);

            DamageResult result = DamageResolver.Apply(
                new[] { target },
                new[] { new GridCoordinate(2, 2) },
                7).Single();

            Assert.That(result.ShieldDamage, Is.EqualTo(4));
            Assert.That(result.HealthDamage, Is.EqualTo(3));
            Assert.That(target.CurrentShield, Is.Zero);
            Assert.That(target.CurrentHealth, Is.EqualTo(7));
            Assert.That(target.MaximumShield, Is.EqualTo(4));
            Assert.That(target.MaximumHealth, Is.EqualTo(10));
        }

        [Test]
        public void DamageExecutionUsesThePreviewedCells()
        {
            TacticalGrid grid = CreateGrid(6, 6);
            GridCoordinate selectedTarget = new GridCoordinate(3, 3);
            IReadOnlyCollection<GridCoordinate> preview = PatternCalculator.Diamond(grid, selectedTarget, 1, true);
            CombatUnit inside = CreateUnit(new GridCoordinate(4, 3));
            CombatUnit outside = CreateUnit(new GridCoordinate(5, 3));

            IReadOnlyList<DamageResult> results = DamageResolver.Apply(new[] { inside, outside }, preview, 3);

            Assert.That(results.Select(result => result.Target), Is.EquivalentTo(new[] { inside }));
            Assert.That(inside.CurrentHealth, Is.EqualTo(7));
            Assert.That(outside.CurrentHealth, Is.EqualTo(10));
        }

        [Test]
        public void TurnOrderUsesInitiativeAndSkipsDefeatedUnits()
        {
            CombatUnit slow = CreateUnit(new GridCoordinate(0, 0), initiative: 5);
            CombatUnit fast = CreateUnit(new GridCoordinate(1, 0), initiative: 12);
            CombatUnit middle = CreateUnit(new GridCoordinate(2, 0), initiative: 8);
            var turns = new TurnCoordinator(new[] { slow, fast, middle }, 2);

            Assert.That(turns.StartBattle(), Is.SameAs(fast));
            DamageResolver.Apply(new[] { middle }, new[] { middle.Cell }, 99);

            Assert.That(turns.Advance(), Is.SameAs(slow));
            Assert.That(turns.Advance(), Is.SameAs(fast));
            Assert.That(turns.Round, Is.EqualTo(2));
            Assert.That(turns.LivingTurnOrder, Is.EquivalentTo(new[] { fast, slow }));
        }

        [Test]
        public void ActionBudgetRejectsOverspendingAndResetsNextTurn()
        {
            CombatUnit unit = CreateUnit(new GridCoordinate(0, 0));
            unit.BeginTurn(2);

            Assert.That(unit.SpendActionPoints(1), Is.True);
            Assert.That(unit.CurrentActionPoints, Is.EqualTo(1));
            Assert.That(unit.SpendActionPoints(2), Is.False);
            Assert.That(unit.CurrentActionPoints, Is.EqualTo(1));

            unit.BeginTurn(2);
            Assert.That(unit.CurrentActionPoints, Is.EqualTo(2));
        }

        [Test]
        public void CooldownTicksAtStartOfOwningUnitsTurn()
        {
            CombatUnit unit = CreateUnit(new GridCoordinate(0, 0));
            unit.SetCooldown("fire-burst", 2);

            unit.BeginTurn(2);
            Assert.That(unit.GetCooldown("fire-burst"), Is.EqualTo(1));

            unit.BeginTurn(2);
            Assert.That(unit.GetCooldown("fire-burst"), Is.Zero);
        }

        [Test]
        public void SixUnitTurnOrderKeepsInitiativeTiesStable()
        {
            CombatUnit skirmisher = CreateUnit(new GridCoordinate(0, 0), initiative: 14);
            CombatUnit vanguard = CreateUnit(new GridCoordinate(0, 1), initiative: 12);
            CombatUnit firstRaider = CreateUnit(new GridCoordinate(5, 1), initiative: 11);
            CombatUnit secondRaider = CreateUnit(new GridCoordinate(5, 2), initiative: 11);
            CombatUnit ranger = CreateUnit(new GridCoordinate(0, 2), initiative: 10);
            CombatUnit brute = CreateUnit(new GridCoordinate(5, 3), initiative: 8);
            var turns = new TurnCoordinator(
                new[] { vanguard, ranger, skirmisher, brute, firstRaider, secondRaider },
                2);

            Assert.That(turns.StartBattle(), Is.SameAs(skirmisher));
            Assert.That(turns.Advance(), Is.SameAs(vanguard));
            Assert.That(turns.Advance(), Is.SameAs(firstRaider));
            Assert.That(turns.Advance(), Is.SameAs(secondRaider));
            Assert.That(turns.Advance(), Is.SameAs(ranger));
            Assert.That(turns.Advance(), Is.SameAs(brute));
            Assert.That(turns.Advance(), Is.SameAs(skirmisher));
            Assert.That(turns.Round, Is.EqualTo(2));
        }

        private static TacticalGrid CreateGrid(
            int width,
            int height,
            params GridCoordinate[] blocked)
        {
            var blockedSet = new HashSet<GridCoordinate>(blocked);
            var cells = new List<GridCell>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var coordinate = new GridCoordinate(x, y);
                    cells.Add(new GridCell(coordinate, (x + y) % 3, !blockedSet.Contains(coordinate)));
                }
            }

            return new TacticalGrid(cells);
        }

        private static CombatUnit CreateUnit(
            GridCoordinate coordinate,
            int maximumHealth = 10,
            int maximumShield = 0,
            int initiative = 10)
        {
            return new CombatUnit(
                "test-unit",
                "Test Unit",
                maximumHealth,
                maximumShield,
                initiative,
                3,
                Team.Opponent,
                ControlType.Passive,
                coordinate);
        }
    }
}
