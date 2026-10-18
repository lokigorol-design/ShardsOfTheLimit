using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    public sealed class CloudRules
    {
        public readonly string Id;
        public readonly StatusRules Status;
        public readonly int Duration, PulseInterval, MovementInterval, MovementSpeed, StacksPerTick, DamagePerTick;
        public readonly float VerticalHeight;
        public CloudRules(string id, StatusRules status, int duration = 300, int pulseInterval = 100,
            int movementInterval = 100, int movementSpeed = 1, int stacksPerTick = 1, int damagePerTick = 0,
            float verticalHeight = 1)
        {
            if (status == null || duration < 1 || pulseInterval < 1 || movementInterval < 1 ||
                movementSpeed < 0 || stacksPerTick < 1 || damagePerTick < 0 || verticalHeight <= 0)
                throw new ArgumentException("Invalid cloud rules.");
            Id = id; Status = status; Duration = duration; PulseInterval = pulseInterval;
            MovementInterval = movementInterval; MovementSpeed = movementSpeed; StacksPerTick = stacksPerTick;
            DamagePerTick = damagePerTick; VerticalHeight = verticalHeight;
        }
    }

    public sealed class CloudAreaEffect
    {
        public CombatUnit Owner { get; }
        public string OwnerId => Owner.Id;
        public string SourceAbilityId { get; }
        public CloudRules Rules { get; }
        public IReadOnlyCollection<GridCoordinate> OccupiedCells { get; private set; }
        public GridCoordinate MovementDirection { get; }
        public float LowerHeight { get; }
        public long ExpireTick { get; }
        public long NextPulseTick { get; internal set; }
        public long NextMoveTick { get; internal set; }
        public int StacksPerTick { get; }
        public CloudAreaEffect(CombatUnit owner, string sourceAbilityId, CloudRules rules,
            IEnumerable<GridCoordinate> cells, GridCoordinate direction, float lowerHeight, long tick, int stacksPerTick)
        {
            if (Math.Abs(direction.X) + Math.Abs(direction.Y) > 1) throw new ArgumentException("Cloud direction must be cardinal.");
            Owner = owner ?? throw new ArgumentNullException(nameof(owner)); SourceAbilityId = sourceAbilityId;
            Rules = rules; OccupiedCells = cells.Distinct().ToArray(); MovementDirection = direction;
            LowerHeight = lowerHeight; ExpireTick = tick + rules.Duration;
            NextPulseTick = tick + rules.PulseInterval; NextMoveTick = tick + rules.MovementInterval;
            StacksPerTick = stacksPerTick;
        }
        public bool Contains(TacticalGrid grid, GridCoordinate cell, float unitHeight = 1)
        {
            if (!OccupiedCells.Contains(cell) || !grid.Contains(cell)) return false;
            float bottom = grid.GetCell(cell).Height;
            return bottom < LowerHeight + Rules.VerticalHeight && bottom + unitHeight > LowerHeight;
        }
        public IReadOnlyCollection<GridCoordinate> PredictMovement(TacticalGrid grid)
        {
            var cells = new HashSet<GridCoordinate>(OccupiedCells);
            for (int step = 0; step < Rules.MovementSpeed; step++)
                cells = new HashSet<GridCoordinate>(cells.Select(cell =>
                {
                    var next = new GridCoordinate(cell.X + MovementDirection.X, cell.Y + MovementDirection.Y);
                    // Blocked parts stay; unblocked parts advance. Units never obstruct an area.
                    return grid.Contains(next) && grid.GetCell(next).IsWalkable &&
                           grid.GetCell(next).Height < LowerHeight + Rules.VerticalHeight ? next : cell;
                }));
            return cells.ToArray();
        }
        internal void Move(TacticalGrid grid) => OccupiedCells = PredictMovement(grid);
        public static GridCoordinate DirectionFrom(GridCoordinate source, GridCoordinate target)
        {
            int x = target.X - source.X, y = target.Y - source.Y;
            return Math.Abs(x) >= Math.Abs(y) && x != 0
                ? new GridCoordinate(Math.Sign(x), 0) : new GridCoordinate(0, y == 0 ? 1 : Math.Sign(y));
        }
    }

    public sealed class PassiveModifiers
    {
        public float CloudStacksMultiplier { get; }
        public int CloudStacksBonus { get; }
        /// <summary>Native Mire (2.5): immune to negative effects of any cloud; heals at turn start while inside one.</summary>
        public bool NativeMire { get; }
        public int HealPercentPerActivation { get; }
        /// <summary>Blood of the Deep (Magma Worm IV): heal this % of maximum health at turn start while standing on Magma.</summary>
        public int MagmaHealPercent { get; }
        public PassiveModifiers(float multiplier = 1, int bonus = 0, bool nativeMire = false, int healPercent = 0, int magmaHealPercent = 0)
        { CloudStacksMultiplier = Math.Max(0, multiplier); CloudStacksBonus = bonus; NativeMire = nativeMire;
          HealPercentPerActivation = Math.Max(0, healPercent); MagmaHealPercent = Math.Max(0, magmaHealPercent); }
        public int ApplyStacks(int amount) => Math.Max(1, (int)Math.Ceiling(amount * CloudStacksMultiplier) + CloudStacksBonus);
    }

    /// <summary>
    /// Delayed ability (tag «Отложенное», e.g. Fire Geyser): a visible mark that strikes its cells after a delay.
    /// Damage uses the creator's ATK captured at cast time.
    /// </summary>
    public sealed class DelayedStrike
    {
        public DelayedStrike(CombatUnit owner, string sourceAbilityId, IEnumerable<GridCoordinate> cells, long strikeTick,
            double rawDamage, int burnPower, int burnDurationPulses)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            SourceAbilityId = sourceAbilityId;
            Cells = cells.Distinct().ToArray();
            StrikeTick = strikeTick;
            RawDamage = rawDamage;
            BurnPower = burnPower;
            BurnDurationPulses = Math.Max(1, burnDurationPulses);
        }

        public CombatUnit Owner { get; }
        public string SourceAbilityId { get; }
        public IReadOnlyCollection<GridCoordinate> Cells { get; }
        public long StrikeTick { get; }
        public double RawDamage { get; }
        public int BurnPower { get; }
        public int BurnDurationPulses { get; }
    }

    /// <summary>Trap object (tag «Ловушка», e.g. Lift Trap): fires on the first resonant entering its cell, allies included.</summary>
    public sealed class TrapObject
    {
        public TrapObject(CombatUnit owner, string sourceAbilityId, GridCoordinate cell, long expireTick, int fallHealthPercent,
            GridCoordinate? pushDirection = null, int pushDistance = 0)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            SourceAbilityId = sourceAbilityId;
            Cell = cell;
            ExpireTick = expireTick;
            FallHealthPercent = Math.Max(0, fallHealthPercent);
            PushDirection = pushDirection;
            PushDistance = Math.Max(0, pushDistance);
        }

        /// <summary>Wall trap: pushes the victim away from the wall instead of throwing it up.</summary>
        public GridCoordinate? PushDirection { get; }
        public int PushDistance { get; }

        public CombatUnit Owner { get; }
        public string SourceAbilityId { get; }
        public GridCoordinate Cell { get; }
        public long ExpireTick { get; }
        public int FallHealthPercent { get; }
    }

    /// <summary>
    /// Magma (16.10.8): a field object dealing 0,25 × creator ATK on entry and every 100 ticks on its cell for 3 turns;
    /// at most 12 cells per creator, the oldest disappear first. Periodic damage, reduced by Magma resistance.
    /// </summary>
    public sealed class MagmaCell
    {
        public MagmaCell(CombatUnit owner, GridCoordinate cell, long createdTick, int lifetime, int power)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Cell = cell;
            CreatedTick = createdTick;
            ExpireTick = createdTick + lifetime;
            NextPulseTick = createdTick + 100;
            Power = Math.Max(0, power);
        }

        public CombatUnit Owner { get; }
        public GridCoordinate Cell { get; }
        public long CreatedTick { get; }
        public long ExpireTick { get; }
        public long NextPulseTick { get; internal set; }
        public int Power { get; }
    }

    public sealed class TrapTrigger
    {
        public TrapTrigger(TrapObject trap) => Trap = trap;
        public TrapObject Trap { get; }
        public PushResult Push { get; internal set; }
        public List<DamageResult> Damage { get; } = new List<DamageResult>();
    }

    /// <summary>Battle-owned areas and global stack statuses. Scheduled by the existing timeline.</summary>
    public sealed class AreaEffectWorld
    {
        private readonly TacticalGrid grid;
        private readonly IReadOnlyList<CombatUnit> units;
        private readonly List<CloudAreaEffect> clouds = new List<CloudAreaEffect>();
        private readonly Dictionary<CombatUnit, PassiveModifiers> passives = new Dictionary<CombatUnit, PassiveModifiers>();
        private readonly List<DelayedStrike> strikes = new List<DelayedStrike>();
        private readonly List<TrapObject> traps = new List<TrapObject>();
        private readonly List<DelayedStrike> firedStrikes = new List<DelayedStrike>();
        public IReadOnlyList<CloudAreaEffect> Clouds => clouds;
        public IReadOnlyList<DelayedStrike> PendingStrikes => strikes;
        public IReadOnlyList<TrapObject> Traps => traps;
        public void AddStrike(DelayedStrike strike) => strikes.Add(strike);
        public void AddTrap(TrapObject trap)
        {
            traps.RemoveAll(existing => existing.Cell == trap.Cell);
            traps.Add(trap);
        }

        public TrapObject TrapAt(GridCoordinate cell) => traps.FirstOrDefault(trap => trap.Cell == cell);

        public const string MagmaResistanceId = "MAGMA";
        public const int MaximumMagmaPerCreator = 12;
        private readonly List<MagmaCell> magma = new List<MagmaCell>();
        public IReadOnlyList<MagmaCell> Magma => magma;
        public MagmaCell MagmaAt(GridCoordinate cell) => magma.FirstOrDefault(m => m.Cell == cell);

        /// <summary>Creates (or refreshes) Magma on a cell; a creator keeps at most 12 cells.</summary>
        public MagmaCell AddMagma(CombatUnit owner, GridCoordinate cell, long tick, int power, int lifetime = 300)
        {
            magma.RemoveAll(existing => existing.Cell == cell);
            var created = new MagmaCell(owner, cell, tick, lifetime, power);
            magma.Add(created);
            List<MagmaCell> own = magma.Where(m => m.Owner == owner).OrderBy(m => m.CreatedTick).ToList();
            for (int index = 0; index < own.Count - MaximumMagmaPerCreator; index++) magma.Remove(own[index]);
            return created;
        }

        /// <summary>A resonant enters a Magma cell (walking, forced or landing).</summary>
        public DamageResult EnterMagma(CombatUnit unit, GridCoordinate cell)
        {
            MagmaCell hot = MagmaAt(cell);
            if (hot == null || unit == null || unit.IsDefeated || unit.IsInMist) return null;
            DamageResult burn = CombatEffectCalculator.PeriodicDamage(unit, unit.ResourceState, hot.Power, MagmaResistanceId);
            CombatEffectResolver.Apply(new[] { burn });
            return burn;
        }

        /// <summary>Strikes that fired since the last call, for presentation.</summary>
        public IReadOnlyList<DelayedStrike> TakeFiredStrikes()
        {
            DelayedStrike[] fired = firedStrikes.ToArray();
            firedStrikes.Clear();
            return fired;
        }

        /// <summary>
        /// A resonant entered a trapped cell: the trap is spent. A floor Lift Trap throws it up (the fall costs
        /// <see cref="TrapObject.FallHealthPercent"/>% of maximum health, Defense does not apply); a wall trap pushes it
        /// away from the wall, with collision damage if something stops it.
        /// </summary>
        public TrapTrigger TriggerTrap(CombatUnit unit, GridCoordinate cell)
        {
            TrapObject trap = TrapAt(cell);
            if (trap == null || unit == null || unit.IsDefeated || unit.IsInMist) return null;
            traps.Remove(trap);
            var trigger = new TrapTrigger(trap);
            if (trap.PushDirection.HasValue)
            {
                GridCoordinate direction = trap.PushDirection.Value;
                var from = new GridCoordinate(cell.X - direction.X, cell.Y - direction.Y);
                PushResult push = ForcedMovementCalculator.Push(grid, unit, from, cell, trap.PushDistance,
                    next => units.Any(other => other != unit && !other.IsDefeated && other.Cell == next));
                CombatEffectResolver.Apply(new ICombatEffectResult[] { push });
                trigger.Push = push;
                if (push.Collided)
                {
                    double raw = trap.Owner.Attack * ForcedMovementCalculator.CollisionAttackMultiplier;
                    CombatUnit struck = units.FirstOrDefault(other => other != unit && !other.IsDefeated && other.Cell == push.CollisionCell.Value);
                    foreach (CombatUnit victim in new[] { unit, struck })
                    {
                        if (victim == null || raw <= 0) continue;
                        DamageResult hit = CombatEffectCalculator.Damage(victim, victim.ResourceState, raw);
                        CombatEffectResolver.Apply(new[] { hit });
                        trigger.Damage.Add(hit);
                    }
                }

                return trigger;
            }

            DamageResult fall = CombatEffectCalculator.PeriodicDamage(unit, unit.ResourceState,
                unit.MaximumHealth * trap.FallHealthPercent / 100d, FallResistanceId);
            CombatEffectResolver.Apply(new[] { fall });
            trigger.Damage.Add(fall);
            return trigger;
        }

        public const string FallResistanceId = "FALL";
        public AreaEffectWorld(TacticalGrid grid, IReadOnlyList<CombatUnit> units) { this.grid = grid; this.units = units; }
        public void Register(CombatUnit unit, PassiveModifiers modifiers) => passives[unit] = modifiers;
        public PassiveModifiers GetModifiers(CombatUnit unit) => passives.TryGetValue(unit, out var value) ? value : new PassiveModifiers();
        public bool IsInsideOwnCloud(CombatUnit unit, GridCoordinate cell) => clouds.Any(c => c.OwnerId == unit.Id && c.Contains(grid, cell));
        public bool IsInsideAnyCloud(GridCoordinate cell) => clouds.Any(c => c.Contains(grid, cell));
        public bool IsImmuneToClouds(CombatUnit unit) => GetModifiers(unit).NativeMire;
        public CloudAreaEffect PreviewCloud(CombatUnit owner, string abilityId, CloudRules rules,
            IEnumerable<GridCoordinate> cells, GridCoordinate direction, float height, long tick)
            => new CloudAreaEffect(owner, abilityId, rules,
                cells.Where(c => grid.Contains(c) && grid.GetCell(c).IsWalkable && grid.GetCell(c).Height < height + rules.VerticalHeight),
                direction, height, tick, GetModifiers(owner).ApplyStacks(rules.StacksPerTick));
        public void Add(CloudAreaEffect cloud) { clouds.Add(cloud); }
        public long NextEventTick => clouds.SelectMany(c => new[] { c.NextPulseTick, c.NextMoveTick, c.ExpireTick })
            .Concat(strikes.Select(s => s.StrikeTick))
            .Concat(magma.SelectMany(m => new[] { m.NextPulseTick, m.ExpireTick }))
            .Concat(traps.Select(trap => trap.ExpireTick))
            .Concat(units.Where(u => !u.IsDefeated).SelectMany(u => u.Statuses.Values.SelectMany(s =>
                s.Rules.Pulses ? new[] { s.NextPulseTick, s.ExpireTick } : new[] { s.ExpireTick })))
            .DefaultIfEmpty(long.MaxValue).Min();
        public IReadOnlyList<ICombatEffectResult> ProcessTick(long tick)
        {
            var results = new List<ICombatEffectResult>();
            foreach (var unit in units.Where(u => !u.IsDefeated))
            {
                // Status expiry is exclusive; clouds have an inclusive final pulse.
                unit.ExpireStatuses(tick);
                foreach (var status in unit.Statuses.Values.Where(s => s.Rules.Pulses && s.NextPulseTick == tick).ToArray())
                {
                    if (unit.IsDefeated) break;
                    var damage = StatusCalculator.Pulse(unit, status);
                    CombatEffectResolver.Apply(new[] { damage }); results.Add(damage);
                    status.NextPulseTick += status.Rules.PulseInterval;
                }
            }
            foreach (var cloud in clouds.ToArray())
            {
                if (cloud.NextPulseTick == tick)
                {
                    foreach (var unit in units.Where(u => !u.IsDefeated && !u.IsInMist && !IsImmuneToClouds(u) && cloud.Contains(grid, u.Cell)))
                    {
                        unit.Statuses.TryGetValue(cloud.Rules.Status.Id, out var previous);
                        var status = StatusCalculator.Apply(unit, cloud.Rules.Status, cloud.StacksPerTick, tick, cloud.OwnerId, previous);
                        unit.ApplyStatus(status); results.Add(status);
                        if (cloud.Rules.DamagePerTick > 0)
                        {
                            var damage = CombatEffectCalculator.Damage(unit, unit.ResourceState, cloud.Rules.DamagePerTick);
                            CombatEffectResolver.Apply(new[] { damage }); results.Add(damage);
                        }
                    }
                    cloud.NextPulseTick += cloud.Rules.PulseInterval;
                }
                if (cloud.NextMoveTick == tick) { cloud.Move(grid); cloud.NextMoveTick += cloud.Rules.MovementInterval; }
                if (cloud.ExpireTick <= tick) clouds.Remove(cloud);
            }
            foreach (DelayedStrike strike in strikes.Where(s => s.StrikeTick <= tick).ToArray())
            {
                strikes.Remove(strike);
                firedStrikes.Add(strike);
                foreach (CombatUnit unit in units.Where(u => !u.IsDefeated && !u.IsInMist && strike.Cells.Contains(u.Cell)).ToArray())
                {
                    DamageResult hit = CombatEffectCalculator.Damage(unit, unit.ResourceState, strike.RawDamage);
                    CombatEffectResolver.Apply(new[] { hit });
                    results.Add(hit);
                    if (unit.IsDefeated || strike.BurnPower <= 0) continue;
                    BurnAppliedResult burn = BurningCalculator.Apply(unit, unit.BurningState, strike.BurnPower,
                        strike.BurnDurationPulses, tick, unit.GetMechanicStacks(StatusMechanic.Oil));
                    CombatEffectResolver.Apply(new ICombatEffectResult[] { burn });
                    results.Add(burn);
                }
            }
            traps.RemoveAll(trap => trap.ExpireTick <= tick);
            foreach (MagmaCell hot in magma.ToArray())
            {
                if (hot.NextPulseTick == tick && hot.NextPulseTick <= hot.ExpireTick)
                {
                    foreach (CombatUnit unit in units.Where(u => !u.IsDefeated && !u.IsInMist && u.Cell == hot.Cell).ToArray())
                    {
                        DamageResult burn = CombatEffectCalculator.PeriodicDamage(unit, unit.ResourceState, hot.Power, MagmaResistanceId);
                        CombatEffectResolver.Apply(new[] { burn });
                        results.Add(burn);
                    }

                    hot.NextPulseTick += 100;
                }

                if (hot.ExpireTick <= tick) magma.Remove(hot);
            }

            return results;
        }
        /// <summary>Start of a resonant's own turn: Purifying Flame cleanse, Native Mire and Blood of the Deep healing.</summary>
        public IReadOnlyList<ICombatEffectResult> OnActivation(CombatUnit unit)
        {
            var results = new List<ICombatEffectResult>();
            if (unit.IsDefeated) return results;
            if (unit.GetMechanicStacks(StatusMechanic.PurifyingFlame) > 0) unit.Cleanse();
            var modifier = GetModifiers(unit);
            int percent = 0;
            if (modifier.NativeMire && IsInsideAnyCloud(unit.Cell)) percent += modifier.HealPercentPerActivation;
            if (modifier.MagmaHealPercent > 0 && MagmaAt(unit.Cell) != null) percent += modifier.MagmaHealPercent;
            if (percent <= 0) return results;
            int amount = (int)Math.Ceiling(unit.MaximumHealth * percent / 100d);
            var heal = CombatEffectCalculator.Heal(unit, unit.ResourceState, amount);
            CombatEffectResolver.Apply(new[] { heal });
            results.Add(heal);
            return results;
        }
        public IEnumerable<TimelineEvent> PreviewEvents(long after, int count)
        {
            foreach (DelayedStrike strike in strikes)
                if (strike.StrikeTick > after) yield return new TimelineEvent(strike.StrikeTick, TimelineEventKind.StatusPulse, strike.Owner, "GEYSER");
            foreach (var cloud in clouds)
            {
                for (long tick = cloud.NextPulseTick, n = 0; tick <= cloud.ExpireTick && n < count; tick += cloud.Rules.PulseInterval, n++)
                    if (tick > after) yield return new TimelineEvent(tick, TimelineEventKind.StatusPulse, cloud.Owner, cloud.Rules.Id);
                for (long tick = cloud.NextMoveTick, n = 0; tick < cloud.ExpireTick && n < count; tick += cloud.Rules.MovementInterval, n++)
                    if (tick > after) yield return new TimelineEvent(tick, TimelineEventKind.StatusPulse, cloud.Owner, "MOVE CLOUD");
            }
            foreach (var unit in units.Where(u => !u.IsDefeated))
                foreach (var status in unit.Statuses.Values)
                {
                    if (status.Rules.Pulses)
                        for (long tick = status.NextPulseTick, n = 0; tick < status.ExpireTick && n < count; tick += status.Rules.PulseInterval, n++)
                            if (tick > after) yield return new TimelineEvent(tick, TimelineEventKind.StatusPulse, unit, status.Rules.Id);
                    yield return new TimelineEvent(status.ExpireTick, TimelineEventKind.StatusPulse, unit, "EXPIRE " + status.Rules.Id);
                }
        }
    }
}
