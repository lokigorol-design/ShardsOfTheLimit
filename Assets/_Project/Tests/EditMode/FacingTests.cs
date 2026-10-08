using System;
using NUnit.Framework;
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
    }
}
