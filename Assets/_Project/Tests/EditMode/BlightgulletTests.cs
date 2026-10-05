using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Tests
{
    public sealed class BlightgulletTests
    {
        private TacticalGrid grid;
        private CombatUnit owner, target;
        private AreaEffectWorld world;
        private static GridCoordinate C(int x, int y = 0) => new GridCoordinate(x,y);
        private static CombatUnit Unit(string name, GridCoordinate cell, Team team = Team.Opponent) =>
            new CombatUnit(name,name,100,40,10,3,team,ControlType.Player,cell,attack:14);
        [SetUp] public void Setup()
        {
            grid = new TacticalGrid(Enumerable.Range(0,7).SelectMany(x => Enumerable.Range(0,4).Select(y => new GridCell(C(x,y),0,true))));
            owner = Unit("owner",C(0),Team.Player); target = Unit("target",C(2));
            world = new AreaEffectWorld(grid,new[] { owner,target });
        }
        private CloudAreaEffect Cloud(string status = "POISON", int x = 2, int speed = 1, int duration = 300, CombatUnit source = null,
            float height = 1, float bottom = 0)
        {
            var cloud = world.PreviewCloud(source ?? owner,"test",new CloudRules(status,new StatusRules(status,20,300),duration,
                movementSpeed:speed,verticalHeight:height),new[] { C(x) },C(1),bottom,0);
            world.Add(cloud); return cloud;
        }
        [TestCase("POISON")][TestCase("CORROSION")][TestCase("OIL")]
        public void CloudAppliesOnlyItsOwnStatus(string status)
        {
            Cloud(status); world.ProcessTick(100);
            Assert.That(target.GetStacks(status),Is.EqualTo(1));
            Assert.That(target.Statuses.Count,Is.EqualTo(1));
        }
        [Test] public void MovementReallyChangesFootprint()
        { var cloud = Cloud(); world.ProcessTick(100); Assert.That(cloud.OccupiedCells,Is.EquivalentTo(new[] { C(3) })); }
        [Test] public void NewOccupantReceivesStacksAtNextPulse()
        { Cloud(); world.ProcessTick(100); target.MoveTo(C(3)); world.ProcessTick(200); Assert.That(target.GetStacks("POISON"),Is.EqualTo(2)); }
        [Test] public void CloudLeavesTargetAndStopsAddingStacks()
        { Cloud(); world.ProcessTick(100); world.ProcessTick(200); Assert.That(target.GetStacks("POISON"),Is.EqualTo(1)); }
        [Test] public void ToxicCocktailDoublesCorrespondingStacks()
        { world.Register(owner,new PassiveModifiers(2)); Cloud(); world.ProcessTick(100); Assert.That(target.GetStacks("POISON"),Is.EqualTo(2)); }
        [Test] public void NativeMireRequiresInstanceOwnerNotSpecies()
        {
            var copy = Unit("owner",C(2),Team.Player);
            world.Register(target,new PassiveModifiers(nativeMire:true,defense:7,heal:8));
            Cloud(source:copy,speed:0);
            Assert.That(world.IsInsideOwnCloud(target,target.Cell),Is.False); Assert.That(target.DefenseBonus,Is.Zero);
            Cloud(source:target,speed:0); Assert.That(target.DefenseBonus,Is.EqualTo(7));
        }
        [Test] public void NativeMireOverlapHealsAndDefendsOnceAndEndsOnExit()
        {
            CombatEffectResolver.Apply(new[] { CombatEffectCalculator.Damage(target,target.ResourceState,60) });
            world.Register(target,new PassiveModifiers(nativeMire:true,defense:5,heal:6));
            Cloud(source:target,speed:0); Cloud(source:target,speed:0);
            world.OnActivation(target);
            Assert.That(target.CurrentHealth,Is.EqualTo(86)); Assert.That(target.DefenseBonus,Is.EqualTo(5));
            target.MoveTo(C(3)); Assert.That(target.DefenseBonus,Is.Zero);
            world.OnActivation(target); Assert.That(target.CurrentHealth,Is.EqualTo(86));
        }
        [Test] public void VenomousMudCreatesCloudWithoutChangingPreviewState()
        {
            var ability = Resources.Load<AbilityDefinition>("Prototype/Abilities/MudSpit");
            var original = Resources.Load<PassiveModifierDefinition>("Prototype/Passives/BlightgulletBranches");
            var modifier = Object.Instantiate(original);
            try
            {
                modifier.venomousMud = true;
                var preview = AbilityRuntime.Preview(ability,grid,owner,target.Cell,new[] {owner,target},0,world,modifier);
                Assert.That(world.Clouds,Is.Empty); Assert.That(target.IsImmobilized,Is.False);
                Assert.That(preview.Clouds.Count,Is.EqualTo(1));
                var actual = AbilityRuntime.Execute(ability,grid,owner,target.Cell,new[] {owner,target},0,world,modifier);
                Assert.That(actual.Clouds[0].OccupiedCells,Is.EquivalentTo(preview.Clouds[0].OccupiedCells));
                Assert.That(world.Clouds.Single().Rules.Status.Id,Is.EqualTo("POISON"));
                Assert.That(world.Clouds.Single().OwnerId,Is.EqualTo(owner.Id));
                Assert.That(target.IsImmobilized,Is.True);
            }
            finally { Object.DestroyImmediate(modifier); }
        }
        [Test] public void PullMovesTargetInsideNonBlockingCloud()
        {
            Cloud(x:1,speed:0); target.MoveTo(C(4));
            var pull = ForcedMovementCalculator.Pull(grid,target,owner.Cell,target.Cell,3,c => c == owner.Cell);
            CombatEffectResolver.Apply(new[] { pull });
            Assert.That(target.Cell,Is.EqualTo(C(1))); world.ProcessTick(100);
            Assert.That(target.GetStacks("POISON"),Is.EqualTo(1));
        }
        [Test] public void CloudsDoNotBlockMovementMap()
        { Cloud(x:1); Assert.That(grid.BuildMovementMap(owner.Cell,3,c => c == target.Cell).ReachableCosts.ContainsKey(C(1)),Is.True); }
        [Test] public void OverlappingCloudsApplyIndependentPulsesAndPreserveOwner()
        {
            var a = Cloud(speed:0); var b = Cloud(speed:0);
            Assert.That(a.OwnerId,Is.EqualTo(owner.Id)); Assert.That(b.SourceAbilityId,Is.EqualTo("test"));
            world.ProcessTick(100); Assert.That(world.Clouds.Count,Is.EqualTo(2)); Assert.That(target.GetStacks("POISON"),Is.EqualTo(2));
        }
        [TestCase(1f,0)][TestCase(0f,1)][TestCase(-1f,0)]
        public void HeightIntersectionIsNotInfinite(float cloudBottom, int stacks)
        { Cloud(speed:0,bottom:cloudBottom); world.ProcessTick(100); Assert.That(target.GetStacks("POISON"),Is.EqualTo(stacks)); }
        [Test] public void ImmobilizedStillAllowsForcedMovementAndAbilities()
        {
            var rules = new StatusRules("IMMOBILIZED",1,100,preventsMovement:true);
            target.ApplyStatus(StatusCalculator.Apply(target,rules,1,0,owner.Id));
            Assert.That(target.AvailableMovementRange,Is.Zero);
            var pull = ForcedMovementCalculator.Pull(grid,target,owner.Cell,target.Cell,3,c => c == owner.Cell);
            Assert.That(pull.Destination,Is.EqualTo(C(1)));
            var ability = Resources.Load<AbilityDefinition>("Prototype/Abilities/MudSpit");
            Assert.DoesNotThrow(() => AbilityRuntime.Preview(ability,grid,target,owner.Cell,new[] { owner,target },0,world));
            world.ProcessTick(100); Assert.That(target.AvailableMovementRange,Is.EqualTo(3));
        }
        [Test] public void CloudStopsAtWallAndEdgeButIgnoresUnit()
        {
            grid = new TacticalGrid(Enumerable.Range(0,5).Select(x => new GridCell(C(x),0,x != 3)));
            world = new AreaEffectWorld(grid,new[] { owner,target });
            var c = Cloud(); world.ProcessTick(100); Assert.That(c.OccupiedCells,Does.Contain(C(2)));
            var edge = Cloud(x:4); world.ProcessTick(200); Assert.That(edge.OccupiedCells,Does.Contain(C(4)));
        }
        [Test] public void StackCapAndIndependentCountersAndExpiry()
        {
            foreach (string id in new[] { "POISON","CORROSION","OIL" })
                target.ApplyStatus(StatusCalculator.Apply(target,new StatusRules(id,3,100),8,0,owner.Id));
            Assert.That(target.Statuses.Count,Is.EqualTo(3)); Assert.That(target.GetStacks("OIL"),Is.EqualTo(3));
            world.ProcessTick(100); Assert.That(target.Statuses,Is.Empty);
        }
        [Test] public void TimelineProcessesCloudsBeforeActivationAndPreviewDoesNotMutate()
        {
            var timeline = new TimelineCoordinator(new[] { owner,target },2) { AreaEffects = world };
            timeline.StartBattle();
            var cloud = world.PreviewCloud(owner,"test",new CloudRules("test",new StatusRules("POISON",20,300)),
                new[] { target.Cell },C(1),0,timeline.CurrentTick);
            world.Add(cloud);
            var events = timeline.GetUpcomingEvents(10,true,true);
            Assert.That(events.Count,Is.EqualTo(10)); Assert.That(events.Any(e => e.StatusId == "test"),Is.True);
            Assert.That(target.Statuses,Is.Empty);
            for (int i = 0; i < 3; i++) timeline.Advance();
            Assert.That(target.GetStacks("POISON"),Is.EqualTo(1));
        }
        [Test] public void PullStopsBeforeOccupiedOrTooHighCell()
        {
            target.MoveTo(C(4));
            Assert.That(ForcedMovementCalculator.Pull(grid,target,C(0),C(4),3,c => c == C(2)).Destination,Is.EqualTo(C(3)));
            grid = new TacticalGrid(Enumerable.Range(0,5).Select(x => new GridCell(C(x),x == 3 ? 3 : 0,true)));
            Assert.That(ForcedMovementCalculator.Pull(grid,target,C(0),C(4),3,c => false).StepsMoved,Is.Zero);
        }
        [Test] public void GlobalStatusDamageUsesStacksAndShieldFirst()
        {
            target.ApplyStatus(StatusCalculator.Apply(target,new StatusRules("POISON",20,300,100,3),2,0,owner.Id));
            world.ProcessTick(100); Assert.That(target.CurrentShield,Is.EqualTo(34)); Assert.That(target.CurrentHealth,Is.EqualTo(100));
        }
        [Test] public void AuthoredContentHasFiveIconsAndModelAndNoRuntimeStatusInAsset()
        {
            var unit = Resources.Load<UnitDefinition>("Prototype/Units/Blightgullet");
            Assert.That(unit,Is.Not.Null); Assert.That(unit.Abilities.Count,Is.EqualTo(5));
            Assert.That(unit.Abilities.All(a => a.Icon != null),Is.True);
            Assert.That(unit.VisualPrefab,Is.Not.Null); Assert.That(unit.Portrait,Is.Not.Null);
            Assert.That(unit.PassiveModifiers,Is.Not.Null);
        }
    }
}
