using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Reserve and swap without an Archon: summon cells, return lock and the timeline handing a turn over.</summary>
    public sealed class ReserveTests
    {
        private static GridCoordinate C(int x, int y) => new GridCoordinate(x, y);

        private static CombatUnit Unit(string name, GridCoordinate cell, int speed = 10) =>
            new CombatUnit(name, name, 100, 0, speed, 3, Team.Player, ControlType.Player, cell);

        [Test]
        public void SummonCellsAreFreeWalkableCellsWithinTwoOfTheActiveResonant()
        {
            var grid = new TacticalGrid(Enumerable.Range(0, 6).SelectMany(x =>
                Enumerable.Range(0, 6).Select(y => new GridCell(C(x, y), 0, !(x == 3 && y == 2)))));
            var cells = ReserveRules.SummonCells(grid, C(2, 2), cell => cell == C(2, 3));
            Assert.That(cells, Has.No.Member(C(2, 2)), "Not the resonant's own cell.");
            Assert.That(cells, Has.No.Member(C(2, 3)), "Occupied.");
            Assert.That(cells, Has.No.Member(C(3, 2)), "Obstacle.");
            Assert.That(cells, Has.Member(C(4, 2)));
            Assert.That(cells, Has.No.Member(C(4, 3)), "Three cells away.");
            Assert.That(cells.Count, Is.EqualTo(10), "12 cells within 2, minus the occupied one and the obstacle.");
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

        [Test]
        public void SwapHandsTheActiveTurnToTheNewcomer()
        {
            CombatUnit first = Unit("first", C(0, 0));
            CombatUnit other = Unit("other", C(1, 0), 5);
            CombatUnit incoming = Unit("incoming", C(5, 5));
            var timeline = new TimelineCoordinator(new[] { first, other }, 2);
            Assert.That(timeline.StartBattle(), Is.SameAs(first));
            timeline.ReplaceCurrent(first, incoming);
            Assert.That(timeline.CurrentUnit, Is.SameAs(incoming));
            Assert.That(timeline.Contains(first), Is.False);
            Assert.That(timeline.Advance(), Is.SameAs(incoming), "Its next turn comes one interval after this one (tick 200).");
            Assert.That(timeline.CurrentTick, Is.EqualTo(200));
        }
    }
}
