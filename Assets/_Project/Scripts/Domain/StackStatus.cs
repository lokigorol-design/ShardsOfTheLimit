using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>Shared design 2.5 status behaviour (16.10.8). A status asset picks one; numbers live in <see cref="StatusMechanicRules"/>.</summary>
    public enum StatusMechanic
    {
        None,
        /// <summary>Every pulse: 3% × stacks^0.52 of maximum health, reduced by Poison resistance.</summary>
        Poison,
        /// <summary>Per stack: −10% of base Defense and −10 p.p. periodic resistance; both may go negative.</summary>
        Corrosion,
        /// <summary>Per stack: −1 movement point. Burning consumes all Oil for +50% power per stack.</summary>
        Oil,
        /// <summary>Movement points −50%.</summary>
        Slow,
        /// <summary>Incoming damage +20%.</summary>
        Vulnerability,
        /// <summary>Purifying Flame (positive): cleanses negative effects at the start of each own turn;
        /// melee attackers catch fire.</summary>
        PurifyingFlame
    }

    public static class StatusMechanicRules
    {
        public const double PoisonBaseFraction = 0.03d;
        public const double PoisonStackExponent = 0.52d;
        public const string PoisonResistanceId = "POISON";
        public const double CorrosionDefenseFraction = 0.1d;
        public const int CorrosionResistancePenalty = 10;
        public const int OilMovementPenalty = 1;
        public const double SlowMovementFraction = 0.5d;
        public const double VulnerabilityMultiplier = 1.2d;

        /// <summary>Negative effects removed by cleansing: the damaging and hindering mechanics and Immobilize.</summary>
        public static bool IsNegative(StatusRules rules) =>
            rules.PreventsMovement || rules.Mechanic == StatusMechanic.Poison || rules.Mechanic == StatusMechanic.Corrosion ||
            rules.Mechanic == StatusMechanic.Oil || rules.Mechanic == StatusMechanic.Slow ||
            rules.Mechanic == StatusMechanic.Vulnerability || rules.DamagePerStack > 0;

        public static double PoisonPulse(int maximumHealth, int stacks) =>
            stacks <= 0 ? 0 : maximumHealth * PoisonBaseFraction * Math.Pow(stacks, PoisonStackExponent);
    }

    /// <summary>Immutable snapshot of authored global status rules. No Unity dependency.</summary>
    public sealed class StatusRules
    {
        public readonly string Id;
        public readonly int MaximumStacks, Duration, PulseInterval, DamagePerStack;
        public readonly bool PreventsMovement;
        public readonly StatusMechanic Mechanic;
        public StatusRules(string id, int maximumStacks, int duration, int pulseInterval = 100,
            int damagePerStack = 0, bool preventsMovement = false, StatusMechanic mechanic = StatusMechanic.None)
        {
            if (string.IsNullOrWhiteSpace(id) || maximumStacks < 1 || duration < 1 || pulseInterval < 1 || damagePerStack < 0)
                throw new ArgumentException("Invalid status rules.");
            Id = id; MaximumStacks = maximumStacks; Duration = duration; PulseInterval = pulseInterval;
            DamagePerStack = damagePerStack; PreventsMovement = preventsMovement; Mechanic = mechanic;
        }

        /// <summary>True when the status deals damage on its own pulses.</summary>
        public bool Pulses => DamagePerStack > 0 || Mechanic == StatusMechanic.Poison;
    }

    public sealed class StackStatusState
    {
        public StatusRules Rules { get; }
        public int Stacks { get; }
        public long ExpireTick { get; }
        public long NextPulseTick { get; internal set; }
        public string SourceUnitId { get; }
        public StackStatusState(StatusRules rules, int stacks, long expireTick, long nextPulseTick, string sourceUnitId)
        { Rules = rules; Stacks = stacks; ExpireTick = expireTick; NextPulseTick = nextPulseTick; SourceUnitId = sourceUnitId; }
    }

    public sealed class StatusAppliedResult : ICombatEffectResult
    {
        public CombatUnit Target { get; }
        public StackStatusState State { get; }
        public CombatEffectKind Kind => CombatEffectKind.Status;
        public int HealthDelta => 0;
        public int ShieldDelta => 0;
        public StatusAppliedResult(CombatUnit target, StackStatusState state) { Target = target; State = state; }
    }

    public static class StatusCalculator
    {
        public static StatusAppliedResult Apply(CombatUnit target, StatusRules rules, int stacks,
            long tick, string sourceUnitId, StackStatusState previous = null)
        {
            if (stacks < 1) throw new ArgumentOutOfRangeException(nameof(stacks));
            bool active = previous != null && previous.ExpireTick > tick;
            return new StatusAppliedResult(target, new StackStatusState(rules,
                Math.Min(rules.MaximumStacks, (active ? previous.Stacks : 0) + stacks), tick + rules.Duration,
                active ? previous.NextPulseTick : tick + rules.PulseInterval, sourceUnitId));
        }

        /// <summary>Damage of one status pulse; Poison ignores Defense and uses its resistance.</summary>
        public static DamageResult Pulse(CombatUnit unit, StackStatusState status)
        {
            if (status.Rules.Mechanic == StatusMechanic.Poison)
                return CombatEffectCalculator.PeriodicDamage(unit, unit.ResourceState,
                    StatusMechanicRules.PoisonPulse(unit.MaximumHealth, status.Stacks), StatusMechanicRules.PoisonResistanceId);
            return CombatEffectCalculator.PeriodicDamage(unit, unit.ResourceState,
                status.Stacks * status.Rules.DamagePerStack, status.Rules.Id);
        }
    }

    public sealed partial class CombatUnit
    {
        // Instance identity, not the definition id: two copies of a species are different owners.
        public string Id { get; } = Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, StackStatusState> statuses = new Dictionary<string, StackStatusState>();
        public IReadOnlyDictionary<string, StackStatusState> Statuses => statuses;
        public bool IsImmobilized => statuses.Values.Any(s => s.Rules.PreventsMovement);

        /// <summary>Movement points after Immobilize, Oil (−1 per stack) and Slow (−50%, rounded down).</summary>
        public int AvailableMovementRange
        {
            get
            {
                if (IsImmobilized) return 0;
                int range = Math.Max(0, MovementRange - StatusMechanicRules.OilMovementPenalty * GetMechanicStacks(StatusMechanic.Oil));
                if (GetMechanicStacks(StatusMechanic.Slow) > 0)
                    range = (int)Math.Floor(range * (1d - StatusMechanicRules.SlowMovementFraction));
                return range;
            }
        }

        public Func<int> DefenseProvider { private get; set; }
        public int DefenseBonus => Math.Max(0, DefenseProvider?.Invoke() ?? 0);
        public int GetStacks(string id) => statuses.TryGetValue(id, out var value) ? value.Stacks : 0;
        public int GetMechanicStacks(StatusMechanic mechanic) =>
            mechanic == StatusMechanic.None ? 0 : statuses.Values.Where(s => s.Rules.Mechanic == mechanic).Sum(s => s.Stacks);
        public void ApplyStatus(StatusAppliedResult result)
        {
            if (result.Target != this) throw new InvalidOperationException("Status targets another unit.");
            statuses[result.State.Rules.Id] = result.State;
        }
        public void ExpireStatuses(long tick)
        {
            foreach (string id in statuses.Where(s => s.Value.ExpireTick <= tick).Select(s => s.Key).ToArray())
                statuses.Remove(id);
        }
        internal void RemoveMechanic(StatusMechanic mechanic)
        {
            foreach (string id in statuses.Where(s => s.Value.Rules.Mechanic == mechanic).Select(s => s.Key).ToArray())
                statuses.Remove(id);
        }
    }
}
