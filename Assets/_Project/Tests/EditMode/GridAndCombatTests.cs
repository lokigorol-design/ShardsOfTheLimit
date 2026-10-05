using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

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

            Assert.That(path.Count, Is.EqualTo(6));
            Assert.That(path[0], Is.EqualTo(new GridCoordinate(0, 0)));
            Assert.That(path[path.Count - 1], Is.EqualTo(new GridCoordinate(3, 0)));
            CollectionAssert.DoesNotContain(path, occupied);
            Assert.That(path.Zip(path.Skip(1), (from, to) => from.ManhattanDistance(to))
                .All(distance => distance == 1), Is.True);
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
        public void HealingAndShieldPreviewUseExecutionMathAndRespectMaximums()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(1, 1), maximumHealth: 10, maximumShield: 4);
            DamageResolver.Apply(new[] { target }, new[] { target.Cell }, 6);
            Assert.That(target.CurrentHealth, Is.EqualTo(8));
            Assert.That(target.CurrentShield, Is.Zero);

            HealResult healPreview = HealResolver.Preview(new[] { target }, new[] { target.Cell }, 6).Single();
            ShieldResult shieldPreview = ShieldResolver.Preview(new[] { target }, new[] { target.Cell }, 8).Single();
            Assert.That(healPreview.HealthRestored, Is.EqualTo(2));
            Assert.That(shieldPreview.ShieldGranted, Is.EqualTo(4));
            Assert.That(target.CurrentHealth, Is.EqualTo(8), "Preview mutated health.");
            Assert.That(target.CurrentShield, Is.Zero, "Preview mutated shield.");

            HealResult healExecution = HealResolver.Apply(new[] { target }, new[] { target.Cell }, 6).Single();
            ShieldResult shieldExecution = ShieldResolver.Apply(new[] { target }, new[] { target.Cell }, 8).Single();
            Assert.That(healExecution.HealthRestored, Is.EqualTo(healPreview.HealthRestored));
            Assert.That(shieldExecution.ShieldGranted, Is.EqualTo(shieldPreview.ShieldGranted));
            Assert.That(target.CurrentHealth, Is.EqualTo(target.MaximumHealth));
            Assert.That(target.CurrentShield, Is.EqualTo(target.MaximumShield));
        }

        [Test]
        public void AreaHealingCanTargetCellsAndHealsBothTeamsInsideImpact()
        {
            AbilityDefinition repair = Resources.Load<AbilityDefinition>("Prototype/Abilities/RepairPulse");
            Assert.That(repair, Is.Not.Null);
            TacticalGrid grid = CreateGrid(5, 5);
            CombatUnit source = CreateUnit(new GridCoordinate(1, 1), team: Team.Player);
            CombatUnit ally = CreateUnit(new GridCoordinate(2, 1), team: Team.Player);
            CombatUnit enemy = CreateUnit(new GridCoordinate(2, 2), team: Team.Opponent);
            CombatUnit[] units = { source, ally, enemy };
            DamageResolver.Apply(new[] { ally }, new[] { ally.Cell }, 7);
            DamageResolver.Apply(new[] { enemy }, new[] { enemy.Cell }, 4);

            IReadOnlyCollection<GridCoordinate> targetable =
                AbilityRuntime.GetTargetableCells(repair, grid, source, units);
            CollectionAssert.Contains(targetable.ToArray(), source.Cell);
            CollectionAssert.Contains(targetable.ToArray(), ally.Cell);
            CollectionAssert.Contains(targetable.ToArray(), enemy.Cell);

            int healthBefore = ally.CurrentHealth;
            int enemyHealthBefore = enemy.CurrentHealth;
            AbilityResolution preview = AbilityRuntime.Preview(repair, grid, source, ally.Cell, units);
            Assert.That(preview.Results.Single(result => result.Target == ally).HealthDelta, Is.EqualTo(7));
            Assert.That(preview.Results.Single(result => result.Target == enemy).HealthDelta, Is.EqualTo(4));
            Assert.That(ally.CurrentHealth, Is.EqualTo(healthBefore));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(enemyHealthBefore));

            AbilityResolution execution = AbilityRuntime.Execute(repair, grid, source, ally.Cell, units);
            Assert.That(execution.Results.Single(result => result.Target == ally).HealthDelta,
                Is.EqualTo(preview.Results.Single(result => result.Target == ally).HealthDelta));
            Assert.That(ally.CurrentHealth, Is.EqualTo(healthBefore + 7));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(enemy.MaximumHealth));
        }

        [Test]
        public void AreaDamageHitsEnemiesAndAlliesInsideImpact()
        {
            AbilityDefinition fireBurst = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst");
            Assert.That(fireBurst, Is.Not.Null);
            TacticalGrid grid = CreateGrid(5, 5);
            CombatUnit source = CreateUnit(new GridCoordinate(0, 0), team: Team.Player);
            CombatUnit enemy = CreateUnit(new GridCoordinate(2, 0), team: Team.Opponent);
            CombatUnit allyInArea = CreateUnit(new GridCoordinate(2, 1), team: Team.Player);
            CombatUnit[] units = { source, enemy, allyInArea };

            AbilityResolution preview = AbilityRuntime.Preview(fireBurst, grid, source, enemy.Cell, units);
            Assert.That(preview.Results.Select(result => result.Target), Is.EquivalentTo(new[] { enemy, allyInArea }));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(enemy.MaximumHealth));
            Assert.That(allyInArea.CurrentHealth, Is.EqualTo(allyInArea.MaximumHealth));

            AbilityRuntime.Execute(fireBurst, grid, source, enemy.Cell, units);
            Assert.That(enemy.CurrentHealth, Is.LessThan(enemy.MaximumHealth));
            Assert.That(allyInArea.CurrentHealth, Is.LessThan(allyInArea.MaximumHealth));
        }

        [Test]
        public void TimelineUsesSpeedIntervalsAndSkipsDefeatedUnits()
        {
            CombatUnit slow = CreateUnit(new GridCoordinate(0, 0), speed: 5);
            CombatUnit fast = CreateUnit(new GridCoordinate(1, 0), speed: 12);
            CombatUnit middle = CreateUnit(new GridCoordinate(2, 0), speed: 8);
            var turns = new TimelineCoordinator(new[] { slow, fast, middle }, 2);

            Assert.That(turns.StartBattle(), Is.SameAs(fast), "Speed 12 acts at tick 83.");
            DamageResolver.Apply(new[] { middle }, new[] { middle.Cell }, 99);

            Assert.That(turns.Advance(), Is.SameAs(fast), "Second fast turn at 166 precedes slow at 200.");
            Assert.That(turns.Advance(), Is.SameAs(slow));
            Assert.That(turns.LivingUnits, Is.EquivalentTo(new[] { fast, slow }));
        }

        [Test]
        public void SpeedIntervalIsThousandOverSpeedBoundedThirtyToTwoHundred()
        {
            Assert.That(SpeedTimeline.GetInterval(10), Is.EqualTo(100));
            Assert.That(SpeedTimeline.GetInterval(8), Is.EqualTo(125));
            Assert.That(SpeedTimeline.GetInterval(9), Is.EqualTo(111));
            Assert.That(SpeedTimeline.GetInterval(33), Is.EqualTo(30));
            Assert.That(SpeedTimeline.GetInterval(50), Is.EqualTo(30));
            Assert.That(SpeedTimeline.GetInterval(5), Is.EqualTo(200));
            Assert.That(SpeedTimeline.GetInterval(1), Is.EqualTo(200));
        }

        [Test]
        public void DefenseFormulaMatchesDesignExamples()
        {
            Assert.That(DefenseFormula.Apply(5, 5), Is.EqualTo(2.5d).Within(0.001d));
            Assert.That(DefenseFormula.Apply(15, 5), Is.EqualTo(11.25d).Within(0.001d));
            Assert.That(DefenseFormula.Apply(40, 5), Is.EqualTo(35.56d).Within(0.01d));
            Assert.That(DefenseFormula.Apply(15, -5), Is.EqualTo(22.5d).Within(0.001d));
            Assert.That(DefenseFormula.Apply(40, -5), Is.EqualTo(45.71d).Within(0.01d));
            Assert.That(DefenseFormula.Apply(10, -100), Is.EqualTo(40d).Within(0.001d), "Negative Defense caps at ×4.");

            CombatUnit armored = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100, defense: 5);
            Assert.That(CombatEffectCalculator.Damage(armored, armored.ResourceState, 15).HealthDamage, Is.EqualTo(11));
            Assert.That(CombatEffectCalculator.Damage(armored, armored.ResourceState, 1).HealthDamage, Is.EqualTo(1),
                "A hit always deals at least 1.");
        }

        [Test]
        public void AbilityDamageScalesWithSourceAttack()
        {
            AbilityDefinition heavyStrike = Resources.Load<AbilityDefinition>("Prototype/Abilities/HeavyStrike");
            TacticalGrid grid = CreateGrid(4, 4);
            CombatUnit weak = CreateUnit(new GridCoordinate(1, 1), team: Team.Player, attack: 10);
            CombatUnit strong = CreateUnit(new GridCoordinate(1, 2), team: Team.Player, attack: 20);
            CombatUnit target = CreateUnit(new GridCoordinate(2, 1), maximumHealth: 100);
            CombatUnit secondTarget = CreateUnit(new GridCoordinate(2, 2), maximumHealth: 100);

            int weakHit = -AbilityRuntime.Preview(heavyStrike, grid, weak, target.Cell, new[] { weak, target })
                .Results.OfType<DamageResult>().Single().HealthDelta;
            int strongHit = -AbilityRuntime.Preview(heavyStrike, grid, strong, secondTarget.Cell, new[] { strong, secondTarget })
                .Results.OfType<DamageResult>().Single().HealthDelta;
            Assert.That(weakHit, Is.EqualTo(12), "Heavy Strike is 1.2 × ATK.");
            Assert.That(strongHit, Is.EqualTo(24));
        }

        [Test]
        public void PeriodicDamageIgnoresDefenseAndUsesPerSourceResistance()
        {
            CombatUnit worm = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100, defense: 50);
            worm.SetPeriodicResistance(BurningCalculator.ResistanceId, 50);
            Assert.That(CombatEffectCalculator.PeriodicDamage(worm, worm.ResourceState, 10, "BURNING").HealthDamage,
                Is.EqualTo(5));
            Assert.That(CombatEffectCalculator.PeriodicDamage(worm, worm.ResourceState, 10, "POISON").HealthDamage,
                Is.EqualTo(10), "Defense never reduces periodic damage.");
            worm.SetPeriodicResistance("MAGMA", 100);
            Assert.That(CombatEffectCalculator.PeriodicDamage(worm, worm.ResourceState, 10, "MAGMA").HealthDamage,
                Is.Zero);
        }

        [Test]
        public void CorrosionLowersDefenseAndResistanceBelowZero()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100, defense: 10);
            target.SetPeriodicResistance("POISON", 50);
            var corrosion = new StatusRules("CORROSION", 999, 300, mechanic: StatusMechanic.Corrosion);
            target.ApplyStatus(StatusCalculator.Apply(target, corrosion, 12, 0, "source"));

            Assert.That(target.EffectiveDefense, Is.EqualTo(-2d).Within(0.001d));
            Assert.That(target.GetPeriodicResistance("POISON"), Is.EqualTo(-70));
            Assert.That(CombatEffectCalculator.PeriodicDamage(target, target.ResourceState, 10, "POISON").HealthDamage,
                Is.EqualTo(17));
        }

        [Test]
        public void PoisonPulseUsesMaximumHealthPercentFormula()
        {
            Assert.That(StatusMechanicRules.PoisonPulse(100, 1), Is.EqualTo(3d).Within(0.001d));
            Assert.That(StatusMechanicRules.PoisonPulse(100, 3), Is.EqualTo(5.3d).Within(0.05d));
            Assert.That(StatusMechanicRules.PoisonPulse(100, 10), Is.EqualTo(9.93d).Within(0.05d));
            Assert.That(StatusMechanicRules.PoisonPulse(1000, 20), Is.EqualTo(142.5d).Within(0.5d));

            CombatUnit target = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 200, defense: 30);
            var grid = CreateGrid(1, 1);
            var world = new AreaEffectWorld(grid, new[] { target });
            var poison = new StatusRules("POISON", 999, 300, mechanic: StatusMechanic.Poison);
            target.ApplyStatus(StatusCalculator.Apply(target, poison, 3, 0, "source"));
            world.ProcessTick(100);
            Assert.That(target.CurrentHealth, Is.EqualTo(189), "3 stacks: 5.3% of 200 = 11, Defense ignored.");
        }

        [Test]
        public void OilAndSlowReduceMovementPoints()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(0, 0));
            target.ApplyStatus(StatusCalculator.Apply(target,
                new StatusRules("OIL", 999, 300, mechanic: StatusMechanic.Oil), 1, 0, "source"));
            Assert.That(target.AvailableMovementRange, Is.EqualTo(2));
            target.ApplyStatus(StatusCalculator.Apply(target,
                new StatusRules("SLOW", 1, 300, mechanic: StatusMechanic.Slow), 1, 0, "source"));
            Assert.That(target.AvailableMovementRange, Is.EqualTo(1));
        }

        [Test]
        public void VulnerabilityAddsTwentyPercentAfterDefense()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100);
            target.ApplyStatus(StatusCalculator.Apply(target,
                new StatusRules("VULNERABILITY", 1, 200, mechanic: StatusMechanic.Vulnerability), 1, 0, "source"));
            Assert.That(CombatEffectCalculator.Damage(target, target.ResourceState, 10).HealthDamage, Is.EqualTo(12));
        }

        [Test]
        public void PushIntoUnitDealsCollisionDamageToBothSides()
        {
            AbilityDefinition repulse = Resources.Load<AbilityDefinition>("Prototype/Abilities/RepulseStrike");
            TacticalGrid grid = CreateGrid(6, 3);
            CombatUnit source = CreateUnit(new GridCoordinate(1, 1), team: Team.Player, attack: 20);
            CombatUnit target = CreateUnit(new GridCoordinate(2, 1), maximumHealth: 100);
            CombatUnit wall = CreateUnit(new GridCoordinate(4, 1), maximumHealth: 100);
            CombatUnit[] units = { source, target, wall };

            AbilityResolution preview = AbilityRuntime.Preview(repulse, grid, source, target.Cell, units);
            PushResult push = preview.Results.OfType<PushResult>().Single();
            Assert.That(push.Destination, Is.EqualTo(new GridCoordinate(3, 1)));
            Assert.That(push.CollisionCell, Is.EqualTo(new GridCoordinate(4, 1)));

            AbilityRuntime.Execute(repulse, grid, source, target.Cell, units);
            Assert.That(target.CurrentHealth, Is.EqualTo(100 - 16 - 4), "0.8 × ATK hit, then 0.2 × ATK collision.");
            Assert.That(wall.CurrentHealth, Is.EqualTo(96), "The struck unit takes the same collision damage.");
        }

        [Test]
        public void PushStoppedByMapEdgeIsNotACollision()
        {
            var grid = CreateGrid(3, 1);
            CombatUnit target = CreateUnit(new GridCoordinate(1, 0));
            PushResult push = ForcedMovementCalculator.Push(grid, target, new GridCoordinate(0, 0), target.Cell, 3, _ => false);
            Assert.That(push.Destination, Is.EqualTo(new GridCoordinate(2, 0)));
            Assert.That(push.Collided, Is.False);
        }

        [Test]
        public void UpcomingTimelineReturnsTenChronologicalEvents()
        {
            CombatUnit slow = CreateUnit(new GridCoordinate(0, 0), speed: 5);
            CombatUnit fast = CreateUnit(new GridCoordinate(1, 0), speed: 12);
            CombatUnit middle = CreateUnit(new GridCoordinate(2, 0), speed: 8);
            var turns = new TimelineCoordinator(new[] { slow, fast, middle }, 2);

            turns.StartBattle();
            IReadOnlyList<TimelineEvent> events = turns.GetUpcomingEvents(10);
            Assert.That(events.Count, Is.EqualTo(10));
            Assert.That(events[0].Unit, Is.SameAs(fast));
            Assert.That(events.All(entry => entry.Kind == TimelineEventKind.Activation), Is.True);
            Assert.That(events.Select(entry => entry.Tick), Is.Ordered);
        }

        [Test]
        public void BattleOutcomeAndIntentCodesArePureAndUnambiguous()
        {
            CombatUnit player = CreateUnit(new GridCoordinate(0, 0), team: Team.Player);
            CombatUnit opponent = CreateUnit(new GridCoordinate(1, 0), team: Team.Opponent);
            Assert.That(BattleReadabilityRules.EvaluateOutcome(new[] { player, opponent }),
                Is.EqualTo(BattleOutcome.Ongoing));
            Assert.That(BattleReadabilityRules.GetIntentCode(BattleIntentKind.Attack), Is.EqualTo("A"));
            Assert.That(BattleReadabilityRules.GetIntentCode(BattleIntentKind.Movement), Is.EqualTo("M"));
            Assert.That(BattleReadabilityRules.GetIntentCode(BattleIntentKind.Help), Is.EqualTo("H"));
            Assert.That(BattleReadabilityRules.GetIntentCode(BattleIntentKind.Special), Is.EqualTo("S"));

            DamageResolver.Apply(new[] { opponent }, new[] { opponent.Cell }, 99);
            Assert.That(BattleReadabilityRules.EvaluateOutcome(new[] { player, opponent }),
                Is.EqualTo(BattleOutcome.PlayerVictory));
        }

        [Test]
        public void ActionBudgetRejectsOverspendingAndResetsNextActivation()
        {
            CombatUnit unit = CreateUnit(new GridCoordinate(0, 0));
            unit.BeginActivation(2);

            Assert.That(unit.SpendActionPoints(1), Is.True);
            Assert.That(unit.CurrentActionPoints, Is.EqualTo(1));
            Assert.That(unit.SpendActionPoints(2), Is.False);
            Assert.That(unit.CurrentActionPoints, Is.EqualTo(1));

            unit.BeginActivation(2);
            Assert.That(unit.CurrentActionPoints, Is.EqualTo(2));
        }

        [Test]
        public void CooldownUsesOnlyOwnerActivationsAndDistinguishesZeroOneAndTwo()
        {
            CombatUnit unit = CreateUnit(new GridCoordinate(0, 0));
            unit.BeginActivation(2);
            unit.SetCooldown("zero", 0);
            unit.SetCooldown("one", 1);
            unit.SetCooldown("two", 2);

            Assert.That(unit.GetCooldown("zero"), Is.Zero);
            Assert.That(unit.GetCooldown("one"), Is.EqualTo(1));
            Assert.That(unit.GetCooldown("two"), Is.EqualTo(2));

            unit.BeginActivation(2);
            Assert.That(unit.GetCooldown("one"), Is.Zero);
            Assert.That(unit.GetCooldown("two"), Is.EqualTo(1));

            unit.BeginActivation(2);
            Assert.That(unit.GetCooldown("two"), Is.Zero);
        }

        [Test]
        public void SixUnitTimelineKeepsInitiativeTiesStable()
        {
            CombatUnit skirmisher = CreateUnit(new GridCoordinate(0, 0), speed: 14);
            CombatUnit vanguard = CreateUnit(new GridCoordinate(0, 1), speed: 12);
            CombatUnit firstRaider = CreateUnit(new GridCoordinate(5, 1), speed: 11);
            CombatUnit secondRaider = CreateUnit(new GridCoordinate(5, 2), speed: 11);
            CombatUnit ranger = CreateUnit(new GridCoordinate(0, 2), speed: 10);
            CombatUnit brute = CreateUnit(new GridCoordinate(5, 3), speed: 8);
            var turns = new TimelineCoordinator(
                new[] { vanguard, ranger, skirmisher, brute, firstRaider, secondRaider },
                2);

            Assert.That(turns.StartBattle(), Is.SameAs(skirmisher));
            Assert.That(turns.Advance(), Is.SameAs(vanguard));
            Assert.That(turns.Advance(), Is.SameAs(firstRaider));
            Assert.That(turns.Advance(), Is.SameAs(secondRaider));
            Assert.That(turns.Advance(), Is.SameAs(ranger));
            Assert.That(turns.Advance(), Is.SameAs(brute));
            Assert.That(turns.Advance(), Is.SameAs(skirmisher));
        }

        [Test]
        public void MovementStepCostAddsClimbAndRejectsThreeLevelJump()
        {
            var center = new GridCoordinate(1, 1);
            var flat = new GridCoordinate(1, 0);
            var oneHigher = new GridCoordinate(2, 1);
            var twoHigher = new GridCoordinate(0, 1);
            var threeHigher = new GridCoordinate(1, 2);
            var grid = new TacticalGrid(new[]
            {
                new GridCell(center, 0),
                new GridCell(flat, 0),
                new GridCell(oneHigher, 1),
                new GridCell(twoHigher, 2),
                new GridCell(threeHigher, 3)
            });

            Assert.That(grid.TryGetMovementCost(center, flat, out int flatCost), Is.True);
            Assert.That(flatCost, Is.EqualTo(1));
            Assert.That(grid.TryGetMovementCost(center, oneHigher, out int oneLevelCost), Is.True);
            Assert.That(oneLevelCost, Is.EqualTo(2));
            Assert.That(grid.TryGetMovementCost(center, twoHigher, out int twoLevelCost), Is.True);
            Assert.That(twoLevelCost, Is.EqualTo(3));
            Assert.That(grid.TryGetMovementCost(center, threeHigher, out _), Is.False);
            Assert.That(grid.TryGetMovementCost(twoHigher, center, out int descentCost), Is.True);
            Assert.That(descentCost, Is.EqualTo(1));
        }

        [Test]
        public void ReachableCellsSpendMovementBudgetOnClimb()
        {
            var start = new GridCoordinate(0, 0);
            var raised = new GridCoordinate(1, 0);
            var plateau = new GridCoordinate(2, 0);
            var grid = new TacticalGrid(new[]
            {
                new GridCell(start, 0),
                new GridCell(raised, 1),
                new GridCell(plateau, 1)
            });

            IReadOnlyDictionary<GridCoordinate, int> onePoint = grid.GetReachable(start, 1);
            IReadOnlyDictionary<GridCoordinate, int> twoPoints = grid.GetReachable(start, 2);
            IReadOnlyDictionary<GridCoordinate, int> threePoints = grid.GetReachable(start, 3);

            CollectionAssert.DoesNotContain(onePoint.Keys, raised);
            Assert.That(twoPoints[raised], Is.EqualTo(2));
            CollectionAssert.DoesNotContain(twoPoints.Keys, plateau);
            Assert.That(threePoints[plateau], Is.EqualTo(3));
        }

        [Test]
        public void PathChoosesLongerFlatRouteWhenItCostsLessThanRepeatedClimbs()
        {
            var cells = new List<GridCell>();
            for (int x = 0; x < 5; x++)
            {
                cells.Add(new GridCell(new GridCoordinate(x, 0), x % 2 == 0 ? 0 : 2));
                cells.Add(new GridCell(new GridCoordinate(x, 1), 0));
            }

            var grid = new TacticalGrid(cells);
            IReadOnlyList<GridCoordinate> path = grid.FindPath(
                new GridCoordinate(0, 0),
                new GridCoordinate(4, 0),
                6);

            CollectionAssert.AreEqual(new[]
            {
                new GridCoordinate(0, 0),
                new GridCoordinate(0, 1),
                new GridCoordinate(1, 1),
                new GridCoordinate(2, 1),
                new GridCoordinate(3, 1),
                new GridCoordinate(4, 1),
                new GridCoordinate(4, 0)
            }, path);
        }

        [Test]
        public void MovementMapUsesIdenticalCostsForPreviewAndEveryExecutablePath()
        {
            var cells = new List<GridCell>();
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    int height = x == 2 && y == 1 ? 2 : x == 3 && y == 2 ? 3 : 0;
                    cells.Add(new GridCell(new GridCoordinate(x, y), height));
                }
            }

            var grid = new TacticalGrid(cells);
            MovementMap movement = grid.BuildMovementMap(new GridCoordinate(0, 1), 6);

            foreach (KeyValuePair<GridCoordinate, int> preview in movement.ReachableCosts)
            {
                IReadOnlyList<GridCoordinate> path = movement.GetPath(preview.Key);
                Assert.That(path.Count, Is.GreaterThan(1), $"No path for previewed cell {preview.Key}.");
                int actualCost = 0;
                for (int index = 1; index < path.Count; index++)
                {
                    Assert.That(grid.TryGetMovementCost(path[index - 1], path[index], out int stepCost), Is.True);
                    actualCost += stepCost;
                }

                Assert.That(actualCost, Is.EqualTo(preview.Value),
                    $"Preview and path cost differ for {preview.Key}.");
            }
        }

        [Test]
        public void TerrainChangePreviewDoesNotMutateGridAndExecutionImmediatelyChangesMovement()
        {
            var start = new GridCoordinate(0, 0);
            var destination = new GridCoordinate(1, 0);
            var grid = new TacticalGrid(new[]
            {
                new GridCell(start, 0),
                new GridCell(destination, 0)
            });

            TerrainChangeResult preview = TerrainChangeCalculator.ChangeHeight(
                grid.GetCell(destination), 2, 0, 3);
            Assert.That(preview.NewHeight, Is.EqualTo(2));
            Assert.That(grid.GetCell(destination).Height, Is.Zero);
            Assert.That(grid.GetReachable(start, 1).ContainsKey(destination), Is.True);

            grid.ApplyTerrainChange(preview);
            Assert.That(grid.GetCell(destination).Height, Is.EqualTo(2));
            Assert.That(grid.GetReachable(start, 2).ContainsKey(destination), Is.False);
            Assert.That(grid.GetReachable(start, 3)[destination], Is.EqualTo(3));
        }

        [Test]
        public void PushStopsBeforeOccupiedBlockedOrThreeLevelClimbCells()
        {
            CombatUnit source = CreateUnit(new GridCoordinate(0, 0), team: Team.Player);
            CombatUnit target = CreateUnit(new GridCoordinate(1, 0), team: Team.Opponent);
            var heightGrid = new TacticalGrid(new[]
            {
                new GridCell(new GridCoordinate(0, 0), 0),
                new GridCell(new GridCoordinate(1, 0), 0),
                new GridCell(new GridCoordinate(2, 0), 0),
                new GridCell(new GridCoordinate(3, 0), 3)
            });

            PushResult heightResult = ForcedMovementCalculator.Push(
                heightGrid, target, source.Cell, target.Cell, 3, _ => false);
            Assert.That(heightResult.Destination, Is.EqualTo(new GridCoordinate(2, 0)));
            Assert.That(heightResult.StepsMoved, Is.EqualTo(1));

            PushResult occupiedResult = ForcedMovementCalculator.Push(
                heightGrid,
                target,
                source.Cell,
                target.Cell,
                3,
                coordinate => coordinate == new GridCoordinate(2, 0));
            Assert.That(occupiedResult.StepsMoved, Is.Zero);

            var blockedGrid = new TacticalGrid(new[]
            {
                new GridCell(new GridCoordinate(0, 0), 0),
                new GridCell(new GridCoordinate(1, 0), 0),
                new GridCell(new GridCoordinate(2, 0), 0, false)
            });
            PushResult blockedResult = ForcedMovementCalculator.Push(
                blockedGrid, target, source.Cell, target.Cell, 2, _ => false);
            Assert.That(blockedResult.StepsMoved, Is.Zero);
        }

        [Test]
        public void RepulseAbilityPreviewAndExecutionUseTheSameDamageAndPushPath()
        {
            AbilityDefinition repulse = Resources.Load<AbilityDefinition>("Prototype/Abilities/RepulseStrike");
            Assert.That(repulse, Is.Not.Null);
            TacticalGrid grid = CreateGrid(6, 3);
            CombatUnit source = CreateUnit(new GridCoordinate(1, 1), team: Team.Player);
            CombatUnit target = CreateUnit(new GridCoordinate(2, 1), team: Team.Opponent);
            CombatUnit[] units = { source, target };

            AbilityResolution preview = AbilityRuntime.Preview(repulse, grid, source, target.Cell, units);
            DamageResult previewDamage = preview.Results.OfType<DamageResult>().Single();
            PushResult previewPush = preview.Results.OfType<PushResult>().Single();
            Assert.That(previewDamage.HealthDamage, Is.EqualTo(8), "0.8 × ATK 10 against Defense 0.");
            Assert.That(previewPush.Destination, Is.EqualTo(new GridCoordinate(4, 1)));
            Assert.That(target.CurrentHealth, Is.EqualTo(target.MaximumHealth));
            Assert.That(target.Cell, Is.EqualTo(new GridCoordinate(2, 1)));

            AbilityResolution execution = AbilityRuntime.Execute(repulse, grid, source, target.Cell, units);
            PushResult executedPush = execution.Results.OfType<PushResult>().Single();
            Assert.That(executedPush.Path, Is.EqualTo(previewPush.Path));
            Assert.That(target.CurrentHealth, Is.EqualTo(target.MaximumHealth - 8));
            Assert.That(target.Cell, Is.EqualTo(new GridCoordinate(4, 1)));
        }

        [Test]
        public void TerrainAbilityExcludesOccupiedCellsAndExecutesItsPreviewedHeightChange()
        {
            AbilityDefinition lowerGround = Resources.Load<AbilityDefinition>("Prototype/Abilities/LowerGround");
            Assert.That(lowerGround, Is.Not.Null);
            var cells = new List<GridCell>();
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    int height = x == 2 && y == 1 || x == 2 && y == 2 ? 2 : 0;
                    cells.Add(new GridCell(new GridCoordinate(x, y), height));
                }
            }

            var grid = new TacticalGrid(cells);
            CombatUnit source = CreateUnit(new GridCoordinate(1, 1), team: Team.Player);
            CombatUnit occupant = CreateUnit(new GridCoordinate(2, 1), team: Team.Player);
            CombatUnit[] units = { source, occupant };
            var emptyRaisedCell = new GridCoordinate(2, 2);

            IReadOnlyCollection<GridCoordinate> targetable =
                AbilityRuntime.GetTargetableCells(lowerGround, grid, source, units);
            CollectionAssert.DoesNotContain(targetable.ToArray(), occupant.Cell);
            CollectionAssert.Contains(targetable.ToArray(), emptyRaisedCell);
            CollectionAssert.Contains(targetable.ToArray(), source.Cell,
                "Terrain abilities must be usable under the caster.");

            AbilityResolution preview = AbilityRuntime.Preview(
                lowerGround, grid, source, emptyRaisedCell, units);
            Assert.That(preview.TerrainResults.Single().PreviousHeight, Is.EqualTo(2));
            Assert.That(preview.TerrainResults.Single().NewHeight, Is.EqualTo(1));
            Assert.That(grid.GetCell(emptyRaisedCell).Height, Is.EqualTo(2));

            AbilityResolution execution = AbilityRuntime.Execute(
                lowerGround, grid, source, emptyRaisedCell, units);
            Assert.That(execution.TerrainResults.Single().NewHeight,
                Is.EqualTo(preview.TerrainResults.Single().NewHeight));
            Assert.That(grid.GetCell(emptyRaisedCell).Height, Is.EqualTo(1));

            AbilityResolution selfExecution = AbilityRuntime.Execute(
                lowerGround, grid, source, source.Cell, units);
            Assert.That(selfExecution.TerrainResults.Single().NewHeight, Is.EqualTo(-1));
            Assert.That(grid.GetCell(source.Cell).Height, Is.EqualTo(-1));
            Assert.That(source.Cell, Is.EqualTo(new GridCoordinate(1, 1)));
        }

        [Test]
        public void MeleeAndRangedHeightRulesAdjustRangeInOppositeWays()
        {
            Assert.That(AbilityRangeCalculator.GetEffectiveRange(1, AbilityRangeType.Melee, 0, 2), Is.Zero);
            Assert.That(AbilityRangeCalculator.GetEffectiveRange(1, AbilityRangeType.Melee, 2, 0), Is.Zero);
            Assert.That(AbilityRangeCalculator.GetEffectiveRange(4, AbilityRangeType.Ranged, 0, 3), Is.EqualTo(2));
            Assert.That(AbilityRangeCalculator.GetEffectiveRange(4, AbilityRangeType.Ranged, 3, 0), Is.EqualTo(6));
        }

        [Test]
        public void AbilityTargetingUsesHeightAdjustedRangeInsteadOfBaseDiamondOnly()
        {
            AbilityDefinition fireBurst = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst");
            AbilityDefinition heavyStrike = Resources.Load<AbilityDefinition>("Prototype/Abilities/HeavyStrike");
            var cells = new List<GridCell>();
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    int height = x == 0 && y == 1 ? 3 : x == 1 && y == 1 ? 1 : 0;
                    cells.Add(new GridCell(new GridCoordinate(x, y), height));
                }
            }

            var grid = new TacticalGrid(cells);
            CombatUnit source = CreateUnit(new GridCoordinate(0, 1), team: Team.Player);
            IReadOnlyCollection<GridCoordinate> ranged =
                AbilityRuntime.GetTargetingRangeCells(fireBurst, grid, source);
            CollectionAssert.Contains(ranged.ToArray(), new GridCoordinate(6, 1),
                "A height advantage of 3 must add 2 cells to ranged range 4.");

            IReadOnlyCollection<GridCoordinate> melee =
                AbilityRuntime.GetTargetingRangeCells(heavyStrike, grid, source);
            CollectionAssert.DoesNotContain(melee.ToArray(), new GridCoordinate(1, 1),
                "A two-level melee height difference must reduce range 1 to zero.");
        }

        [Test]
        public void AiDamageHealingProbabilityIsSymmetricAndCappedAtNinetyFivePercent()
        {
            Assert.That(AiDecisionMath.CalculateFirstOptionProbability(10, 10), Is.EqualTo(0.5d));
            Assert.That(AiDecisionMath.CalculateFirstOptionProbability(20, 10), Is.EqualTo(0.95d).Within(0.0001d));
            Assert.That(AiDecisionMath.CalculateFirstOptionProbability(10, 20), Is.EqualTo(0.05d).Within(0.0001d));
            Assert.That(AiDecisionMath.CalculateFirstOptionProbability(100, 1), Is.EqualTo(0.95d).Within(0.0001d));
            Assert.That(AiDecisionMath.CalculateFirstOptionProbability(15, 10), Is.EqualTo(0.725d).Within(0.0001d));
        }

        [Test]
        public void TerrainHeightCanMoveBetweenMinusFourAndPlusFive()
        {
            var coordinate = new GridCoordinate(0, 0);
            TerrainChangeResult lowered = TerrainChangeCalculator.ChangeHeight(
                new GridCell(coordinate, -3), -2, -4, 5);
            TerrainChangeResult raised = TerrainChangeCalculator.ChangeHeight(
                new GridCell(coordinate, 4), 2, -4, 5);

            Assert.That(lowered.NewHeight, Is.EqualTo(-4));
            Assert.That(raised.NewHeight, Is.EqualTo(5));
        }

        [Test]
        public void CellTargetingHighlightExcludesTerrainThatExecutionRejects()
        {
            AbilityDefinition raiseGround = Resources.Load<AbilityDefinition>("Prototype/Abilities/RaiseGround");
            var sourceCell = new GridCoordinate(0, 0);
            var cappedCell = new GridCoordinate(1, 0);
            var validCell = new GridCoordinate(0, 1);
            var grid = new TacticalGrid(new[]
            {
                new GridCell(sourceCell, 0),
                new GridCell(cappedCell, 5),
                new GridCell(validCell, 0)
            });
            CombatUnit source = CreateUnit(sourceCell, team: Team.Player);

            IReadOnlyCollection<GridCoordinate> highlights = AbilityRuntime.GetTargetingHighlightCells(
                raiseGround, grid, source, new[] { source });

            CollectionAssert.DoesNotContain(highlights.ToArray(), cappedCell);
            CollectionAssert.Contains(highlights.ToArray(), validCell);
        }

        [Test]
        public void FullTargetingRangeIsAvailableSeparatelyFromSparseValidUnitTargets()
        {
            AbilityDefinition aegis = Resources.Load<AbilityDefinition>("Prototype/Abilities/AegisField");
            TacticalGrid grid = CreateGrid(7, 7);
            CombatUnit source = CreateUnit(new GridCoordinate(2, 2), team: Team.Player);
            CombatUnit enemy = CreateUnit(new GridCoordinate(3, 2), team: Team.Opponent);
            CombatUnit[] units = { source, enemy };

            IReadOnlyCollection<GridCoordinate> range =
                AbilityRuntime.GetTargetingRangeCells(aegis, grid, source);
            IReadOnlyCollection<GridCoordinate> validTargets =
                AbilityRuntime.GetTargetableCells(aegis, grid, source, units);

            CollectionAssert.Contains(range.ToArray(), new GridCoordinate(4, 2));
            CollectionAssert.Contains(range.ToArray(), enemy.Cell);
            CollectionAssert.DoesNotContain(validTargets.ToArray(), enemy.Cell);
            Assert.That(range.Count, Is.GreaterThan(validTargets.Count));
        }

        [Test]
        public void BurningKeepsStrongestPowerAndLatestApplicationResetsDuration()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100);
            BurnAppliedResult first = BurningCalculator.Apply(target, target.BurningState, 4, 3, 0);
            CombatEffectResolver.Apply(new ICombatEffectResult[] { first });

            Assert.That(target.BurningState.DamagePerPulse, Is.EqualTo(4));
            Assert.That(target.BurningState.RemainingPulses(), Is.EqualTo(3));

            BurnAppliedResult weaker = BurningCalculator.Apply(target, target.BurningState, 2, 3, 50);
            CombatEffectResolver.Apply(new ICombatEffectResult[] { weaker });

            Assert.That(weaker.AddedDamagePerPulse, Is.EqualTo(2));
            Assert.That(target.BurningState.DamagePerPulse, Is.EqualTo(4), "Burning does not stack; strongest stays.");
            Assert.That(target.BurningState.NextPulseTick, Is.EqualTo(100));
            Assert.That(target.BurningState.ExpireTick, Is.EqualTo(350));

            BurnAppliedResult stronger = BurningCalculator.Apply(target, target.BurningState, 6, 3, 60);
            CombatEffectResolver.Apply(new ICombatEffectResult[] { stronger });
            Assert.That(target.BurningState.DamagePerPulse, Is.EqualTo(6));
            Assert.That(BurningCalculator.PowerFromAttack(18, 0.25f), Is.EqualTo(5), "0.25 × ATK 18 = 4.5 → 5.");
        }

        [Test]
        public void BurningConsumesOilForFiftyPercentPerStack()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100);
            target.ApplyStatus(StatusCalculator.Apply(target,
                new StatusRules("OIL", 999, 300, mechanic: StatusMechanic.Oil), 2, 0, "source"));
            Assert.That(target.AvailableMovementRange, Is.EqualTo(1));

            BurnAppliedResult burn = BurningCalculator.Apply(target, target.BurningState, 4, 3, 0, target.GetStacks("OIL"));
            CombatEffectResolver.Apply(new ICombatEffectResult[] { burn });

            Assert.That(burn.ConsumedOilStacks, Is.EqualTo(2));
            Assert.That(target.BurningState.DamagePerPulse, Is.EqualTo(8), "4 × (1 + 0.5 × 2).");
            Assert.That(target.GetStacks("OIL"), Is.Zero);
            Assert.That(target.AvailableMovementRange, Is.EqualTo(3));
        }

        [Test]
        public void BurningPulseIgnoresDefenseAndRespectsBurningResistance()
        {
            CombatUnit target = CreateUnit(new GridCoordinate(0, 0), maximumHealth: 100, defense: 40);
            target.SetPeriodicResistance(BurningCalculator.ResistanceId, 50);
            CombatEffectResolver.Apply(new ICombatEffectResult[]
            {
                BurningCalculator.Apply(target, target.BurningState, 10, 3, 0)
            });
            Assert.That(target.PulseBurn(100).HealthDamage, Is.EqualTo(5));
        }

        [Test]
        public void BurningPulsesOnGlobalTimelineUsesShieldFirstAndExpiresAfterThreePulses()
        {
            CombatUnit target = CreateUnit(
                new GridCoordinate(0, 0),
                maximumHealth: 100,
                maximumShield: 5);
            BurnAppliedResult applied = BurningCalculator.Apply(target, target.BurningState, 10, 3, 0);
            CombatEffectResolver.Apply(new ICombatEffectResult[] { applied });

            BurnTickResult first = target.PulseBurn(100);
            BurnTickResult second = target.PulseBurn(200);
            BurnTickResult third = target.PulseBurn(300);

            Assert.That(first.ShieldDamage, Is.EqualTo(5));
            Assert.That(first.HealthDamage, Is.EqualTo(5));
            Assert.That(second.HealthDamage, Is.EqualTo(10));
            Assert.That(third.HealthDamage, Is.EqualTo(10));
            Assert.That(target.CurrentShield, Is.Zero);
            Assert.That(target.CurrentHealth, Is.EqualTo(75));
            Assert.That(target.IsBurning, Is.False);
        }

        [Test]
        public void TimelineCanIncludeOrHideScheduledStatusPulses()
        {
            CombatUnit fast = CreateUnit(new GridCoordinate(0, 0), speed: 50);
            CombatUnit burning = CreateUnit(new GridCoordinate(1, 0), speed: 30);
            BurnAppliedResult applied = BurningCalculator.Apply(
                burning, burning.BurningState, 3, 3, 0);
            CombatEffectResolver.Apply(new ICombatEffectResult[] { applied });
            var timeline = new TimelineCoordinator(new[] { fast, burning }, 2);

            timeline.StartBattle();
            IReadOnlyList<TimelineEvent> withStatuses = timeline.GetUpcomingEvents(10, true, true);
            IReadOnlyList<TimelineEvent> withoutStatuses = timeline.GetUpcomingEvents(10, true, false);

            Assert.That(withStatuses.Any(entry => entry.Kind == TimelineEventKind.StatusPulse), Is.True);
            Assert.That(withoutStatuses.All(entry => entry.Kind == TimelineEventKind.Activation), Is.True);
        }

        [Test]
        public void BlazeWormContentAndBurningAbilityPreviewAreConfigured()
        {
            UnitDefinition blaze = Resources.Load<UnitDefinition>("Prototype/Units/BlazeWorm");
            AbilityDefinition spit = Resources.Load<AbilityDefinition>("Prototype/Abilities/BlazeFierySpit");
            AbilityDefinition volley = Resources.Load<AbilityDefinition>("Prototype/Abilities/BlazeFieryVolley");

            Assert.That(blaze, Is.Not.Null);
            Assert.That(blaze.MaximumHealth, Is.EqualTo(150), "Fire Worm I.1 card, design 2.5.");
            Assert.That(blaze.MaximumShield, Is.EqualTo(15));
            Assert.That(blaze.Attack, Is.EqualTo(18));
            Assert.That(blaze.Defense, Is.EqualTo(4));
            Assert.That(blaze.Speed, Is.EqualTo(8));
            Assert.That(blaze.MovementRange, Is.EqualTo(2));
            Assert.That(blaze.PeriodicResistances.Single().sourceId, Is.EqualTo("BURNING"));
            Assert.That(blaze.PeriodicResistances.Single().percent, Is.EqualTo(50));
            Assert.That(blaze.Abilities.Count, Is.EqualTo(3));
            Assert.That(blaze.Abilities.All(ability => ability.Icon != null), Is.True);
            Assert.That(blaze.Portrait, Is.Not.Null);
            Assert.That(blaze.VisualPrefab, Is.Not.Null);
            Assert.That(blaze.VisualMaterial, Is.Not.Null);
            Assert.That(blaze.VisualRotationOffset, Is.EqualTo(new Vector3(90f, -135f, 0f)),
                "Preserve the facing authored by the user in the Unity Inspector.");
            Assert.That(blaze.VisualMaterial.GetTexture("_MainTex"), Is.Not.Null);
            Assert.That(blaze.VisualMaterial.GetTexture("_BumpMap"), Is.Not.Null);
            Assert.That(blaze.VisualMaterial.GetTexture("_MetallicGlossMap"), Is.Not.Null);
            Assert.That(blaze.VisualMaterial.GetTexture("_EmissionMap"), Is.Not.Null);
            ApplyBurnEffectDefinition burnDefinition =
                Resources.Load<ApplyBurnEffectDefinition>("Prototype/Effects/ApplyBurnTenPercent");
            Assert.That(burnDefinition, Is.Not.Null);
            Assert.That(burnDefinition.Icon, Is.Not.Null);
            Assert.That(spit.Cooldown, Is.Zero);
            Assert.That(volley.Cooldown, Is.EqualTo(2));

            TacticalGrid grid = CreateGrid(7, 7);
            CombatUnit source = CreateUnit(new GridCoordinate(1, 1), team: Team.Player);
            CombatUnit target = CreateUnit(new GridCoordinate(3, 1), maximumHealth: 100);
            AbilityResolution preview = AbilityRuntime.Preview(spit, grid, source, target.Cell, new[] { source, target });
            BurnAppliedResult burnPreview = preview.Results.OfType<BurnAppliedResult>().Single();

            Assert.That(burnPreview.AddedDamagePerPulse, Is.EqualTo(3), "0.25 × ATK 10 = 2.5 → 3.");
            Assert.That(GetPulseCount(burnPreview), Is.EqualTo(3));
            Assert.That(target.IsBurning, Is.False, "Preview must not mutate runtime status.");

            AbilityRuntime.Execute(spit, grid, source, target.Cell, new[] { source, target });
            Assert.That(target.IsBurning, Is.True);
            Assert.That(target.BurningState.DamagePerPulse, Is.EqualTo(3));
            Assert.That(volley.ImpactPattern.Evaluate(grid, new GridCoordinate(3, 3)).Count, Is.EqualTo(5));
        }

        private static int GetPulseCount(BurnAppliedResult burn)
        {
            return burn.NewNextPulseTick <= burn.NewExpireTick
                ? (int)((burn.NewExpireTick - burn.NewNextPulseTick) /
                        BurningCalculator.DefaultPulseInterval) + 1
                : 0;
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
                    cells.Add(new GridCell(coordinate, 0, !blockedSet.Contains(coordinate)));
                }
            }

            return new TacticalGrid(cells);
        }

        private static CombatUnit CreateUnit(
            GridCoordinate coordinate,
            int maximumHealth = 10,
            int maximumShield = 0,
            int speed = 10,
            Team team = Team.Opponent,
            int attack = 10,
            int defense = 0)
        {
            return new CombatUnit(
                "test-unit",
                "Test Unit",
                maximumHealth,
                maximumShield,
                speed,
                3,
                team,
                ControlType.Passive,
                coordinate,
                attack,
                defense);
        }
    }
}
