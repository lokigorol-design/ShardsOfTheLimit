using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>Immutable snapshot of authored global status rules. No Unity dependency.</summary>
    public sealed class StatusRules
    {
        public readonly string Id;
        public readonly int MaximumStacks, Duration, PulseInterval, DamagePerStack;
        public readonly bool PreventsMovement;
        public StatusRules(string id, int maximumStacks, int duration, int pulseInterval = 100,
            int damagePerStack = 0, bool preventsMovement = false)
        {
            if (string.IsNullOrWhiteSpace(id) || maximumStacks < 1 || duration < 1 || pulseInterval < 1 || damagePerStack < 0)
                throw new ArgumentException("Invalid status rules.");
            Id = id; MaximumStacks = maximumStacks; Duration = duration; PulseInterval = pulseInterval;
            DamagePerStack = damagePerStack; PreventsMovement = preventsMovement;
        }
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
    }

    public sealed partial class CombatUnit
    {
        // Instance identity, not the definition id: two copies of a species are different owners.
        public string Id { get; } = Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, StackStatusState> statuses = new Dictionary<string, StackStatusState>();
        public IReadOnlyDictionary<string, StackStatusState> Statuses => statuses;
        public bool IsImmobilized => statuses.Values.Any(s => s.Rules.PreventsMovement);
        public int AvailableMovementRange => IsImmobilized ? 0 : MovementRange;
        public Func<int> DefenseProvider { private get; set; }
        public int DefenseBonus => Math.Max(0, DefenseProvider?.Invoke() ?? 0);
        public int GetStacks(string id) => statuses.TryGetValue(id, out var value) ? value.Stacks : 0;
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
    }
}
