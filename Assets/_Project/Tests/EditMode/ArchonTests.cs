using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Archon on the field: summon cells, link radius, Link Break and the timeline joining/leaving.</summary>
    public sealed class ArchonTests
    {
        private static GridCoordinate C(int x, int y) => new GridCoordinate(x, y);

        private static CombatUnit Unit(string name, GridCoordinate cell, int speed = 10) =>
            new CombatUnit(name, name, 100, 0, speed, 3, Team.Player, ControlType.Player, cell);

        [Test]
        public void SummonCellsAreFreeWalkableCellsWithinTwoOfTheArchon()
        {
            var grid = new TacticalGrid(Enumerable.Range(0, 6).SelectMany(x =>
                Enumerable.Range(0, 6).Select(y => new GridCell(C(x, y), 0, !(x == 3 && y == 2)))));
            var cells = ArchonRules.SummonCells(grid, C(2, 2), cell => cell == C(2, 3));
            Assert.That(cells, Has.No.Member(C(2, 2)), "Not the Archon's own cell.");
            Assert.That(cells, Has.No.Member(C(2, 3)), "Occupied.");
            Assert.That(cells, Has.No.Member(C(3, 2)), "Obstacle.");
            Assert.That(cells, Has.Member(C(4, 2)));
            Assert.That(cells, Has.No.Member(C(4, 3)), "Three cells away.");
            Assert.That(cells.Count, Is.EqualTo(10), "12 cells within 2, minus the occupied one and the obstacle.");
        }

        [Test]
        public void LinkRadiusIsFourCells()
        {
            Assert.That(ArchonRules.InLink(C(0, 0), C(2, 2)), Is.True);
            Assert.That(ArchonRules.InLink(C(0, 0), C(3, 2)), Is.False);
        }

        [Test]
        public void LinkBreakRevivesWithThirtyPercentHealth()
        {
            CombatUnit archon = Unit("archon", C(0, 0));
            CombatEffectResolver.Apply(new[] { CombatEffectCalculator.Damage(archon, archon.ResourceState, 500) });
            Assert.That(archon.IsDefeated, Is.True);
            archon.Revive(ArchonRules.ReviveHealth(archon.MaximumHealth));
            Assert.That(archon.IsDefeated, Is.False);
            Assert.That(archon.CurrentHealth, Is.EqualTo(30));
        }

        [Test]
        public void ReserveSlotIsLockedAfterARecall()
        {
            var slot = new ReserveSlot(Unit("toad", C(0, 0)), 400);
            Assert.That(slot.IsAvailable(399), Is.False);
            Assert.That(slot.IsAvailable(400), Is.True);
        }

        [Test]
        public void SummonedUnitJoinsTheTimelineAndRecalledUnitLeavesIt()
        {
            CombatUnit first = Unit("first", C(0, 0));
            CombatUnit second = Unit("second", C(1, 0));
            CombatUnit summoned = Unit("summoned", C(2, 0));
            var timeline = new TimelineCoordinator(new[] { first, second }, 2);
            Assert.That(timeline.StartBattle(), Is.SameAs(first));
            timeline.AddUnit(summoned, 150);
            timeline.RemoveUnit(second);
            Assert.That(timeline.Contains(second), Is.False);
            Assert.That(timeline.Advance(), Is.SameAs(summoned), "Summoned at 150, before the first's turn at 200.");
            Assert.That(timeline.CurrentTick, Is.EqualTo(150));
            Assert.That(timeline.Advance(), Is.SameAs(first));
        }
    }
}
