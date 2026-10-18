using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Domain;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Calling a replacement: the caller's double damage, the 200-tick wait, the return lock and turn hand-over.</summary>
    public sealed class ReserveTests
    {
        private static GridCoordinate C(int x, int y) => new GridCoordinate(x, y);

        private static CombatUnit Unit(string name, GridCoordinate cell, int speed = 10) =>
            new CombatUnit(name, name, 100, 0, speed, 3, Team.Player, ControlType.Player, cell);

        [Test]
        public void CallerTakesDoubleDamageUntilTheReplacementArrives()
        {
            CombatUnit caller = Unit("caller", C(0, 0));
            Assert.That(CombatEffectCalculator.Damage(caller, caller.ResourceState, 10).HealthDamage, Is.EqualTo(10));
            caller.ApplyStatus(StatusCalculator.Apply(caller, ReserveRules.Summoning, 1, 100, caller.Id));
            Assert.That(caller.IsSummoning, Is.True);
            Assert.That(CombatEffectCalculator.Damage(caller, caller.ResourceState, 10).HealthDamage, Is.EqualTo(20), "+100%.");
            Assert.That(CombatEffectCalculator.PeriodicDamage(caller, caller.ResourceState, 5, "BURNING").HealthDamage, Is.EqualTo(10));
            Assert.That(caller.Cleanse(), Is.Zero, "The call is not a negative effect: cleansing does not cancel it.");
            caller.ExpireStatuses(100 + ReserveRules.SwapTicks);
            Assert.That(caller.IsSummoning, Is.False, "It lasts 200 ticks.");
        }

        [Test]
        public void PendingSwapIsDueAfterTwoHundredTicksAndHoldsItsReserveSlot()
        {
            var slot = new ReserveSlot(Unit("panther", C(0, 0)), 0);
            var swap = new PendingSwap(Unit("toad", C(1, 1)), slot, 100 + ReserveRules.SwapTicks);
            slot.IsCalled = true;
            Assert.That(slot.IsAvailable(150), Is.False, "Already on its way.");
            Assert.That(swap.IsDue(299), Is.False);
            Assert.That(swap.IsDue(300), Is.True);
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
