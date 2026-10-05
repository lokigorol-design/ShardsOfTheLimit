using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    public enum Team
    {
        Player,
        Opponent
    }

    public enum ControlType
    {
        Player,
        Passive,
        BasicAI
    }

    public enum CombatEffectKind
    {
        Damage,
        Heal,
        Shield,
        Push,
        Burning,
        Status
    }

    public readonly struct CombatResourceState
    {
        public CombatResourceState(int health, int shield)
        {
            Health = health;
            Shield = shield;
        }

        public int Health { get; }
        public int Shield { get; }
    }

    public readonly struct BurningState
    {
        public BurningState(int damagePerPulse, long nextPulseTick, long expireTick)
        {
            DamagePerPulse = Math.Max(0, damagePerPulse);
            NextPulseTick = Math.Max(0, nextPulseTick);
            ExpireTick = Math.Max(0, expireTick);
        }

        public int DamagePerPulse { get; }
        public long NextPulseTick { get; }
        public long ExpireTick { get; }
        public bool IsActive => DamagePerPulse > 0 && NextPulseTick > 0 && NextPulseTick <= ExpireTick;

        public int RemainingPulses(int pulseInterval = BurningCalculator.DefaultPulseInterval)
        {
            if (!IsActive || pulseInterval <= 0) return 0;
            return (int)((ExpireTick - NextPulseTick) / pulseInterval) + 1;
        }
    }

    public interface ICombatEffectResult
    {
        CombatUnit Target { get; }
        CombatEffectKind Kind { get; }
        int HealthDelta { get; }
        int ShieldDelta { get; }
    }

    /// <summary>Mutable battle state shared by player and opposing units.</summary>
    public sealed partial class CombatUnit
    {
        private readonly Dictionary<string, int> cooldownReadyActivations = new Dictionary<string, int>();

        public CombatUnit(
            string definitionId,
            string displayName,
            int maximumHealth,
            int maximumShield,
            int initiative,
            int movementRange,
            Team team,
            ControlType controlType,
            GridCoordinate cell)
        {
            if (maximumHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (maximumShield < 0) throw new ArgumentOutOfRangeException(nameof(maximumShield));
            if (movementRange < 0) throw new ArgumentOutOfRangeException(nameof(movementRange));

            DefinitionId = definitionId ?? throw new ArgumentNullException(nameof(definitionId));
            DisplayName = displayName ?? definitionId;
            MaximumHealth = maximumHealth;
            MaximumShield = maximumShield;
            Initiative = initiative;
            MovementRange = movementRange;
            Team = team;
            ControlType = controlType;
            Cell = cell;
            CurrentHealth = maximumHealth;
            CurrentShield = maximumShield;
            CurrentAction = "Ready";
        }

        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int MaximumHealth { get; }
        public int MaximumShield { get; }
        public int Initiative { get; }
        public int MovementRange { get; }
        public Team Team { get; }
        public ControlType ControlType { get; }
        public GridCoordinate Cell { get; private set; }
        public int CurrentHealth { get; private set; }
        public int CurrentShield { get; private set; }
        public CombatResourceState ResourceState => new CombatResourceState(CurrentHealth, CurrentShield);
        public BurningState BurningState { get; private set; }
        public bool IsBurning => BurningState.IsActive;
        public string CurrentAction { get; set; }
        public int CurrentActionPoints { get; private set; }
        public int ActivationCount { get; private set; }
        public bool IsDefeated => CurrentHealth <= 0;

        public void MoveTo(GridCoordinate cell) => Cell = cell;
        public int GetCooldown(string abilityId) => cooldownReadyActivations.TryGetValue(abilityId, out int readyAt)
            ? Math.Max(0, readyAt - ActivationCount)
            : 0;

        public void SetCooldown(string abilityId, int cooldown)
        {
            if (abilityId == null) throw new ArgumentNullException(nameof(abilityId));
            cooldownReadyActivations[abilityId] = ActivationCount + Math.Max(0, cooldown);
        }

        public void BeginActivation(int actionPoints)
        {
            ActivationCount++;
            CurrentActionPoints = Math.Max(0, actionPoints);
            CurrentAction = "Ready";
        }

        public bool SpendActionPoints(int cost)
        {
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
            if (cost > CurrentActionPoints) return false;
            CurrentActionPoints -= cost;
            return true;
        }

        public BurnTickResult PulseBurn(long timelineTick)
        {
            BurnTickResult tick = BurningCalculator.Tick(this, ResourceState, BurningState, timelineTick);
            ApplyResourceState(CombatEffectCalculator.Apply(ResourceState, tick));
            BurningState = tick.NextPulseTick <= tick.ExpireTickAfterTick && !IsDefeated
                ? new BurningState(BurningState.DamagePerPulse, tick.NextPulseTick, tick.ExpireTickAfterTick)
                : default;
            return tick;
        }

        internal void ApplyResourceState(CombatResourceState state)
        {
            if (state.Health < 0 || state.Health > MaximumHealth)
            {
                throw new ArgumentOutOfRangeException(nameof(state), "Health is outside the unit limits.");
            }

            if (state.Shield < 0 || state.Shield > MaximumShield)
            {
                throw new ArgumentOutOfRangeException(nameof(state), "Shield is outside the unit limits.");
            }

            CurrentHealth = state.Health;
            CurrentShield = state.Shield;
        }

        internal void ApplyBurn(BurnAppliedResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.Target != this) throw new InvalidOperationException("Burn result targets another unit.");
            BurningState = new BurningState(result.NewDamagePerPulse, result.NewNextPulseTick, result.NewExpireTick);
        }
    }

    public sealed class DamageResult : ICombatEffectResult
    {
        public DamageResult(CombatUnit target, int shieldDamage, int healthDamage, bool defeated)
        {
            Target = target;
            ShieldDamage = shieldDamage;
            HealthDamage = healthDamage;
            Defeated = defeated;
        }

        public CombatUnit Target { get; }
        public int ShieldDamage { get; }
        public int HealthDamage { get; }
        public bool Defeated { get; }
        public CombatEffectKind Kind => CombatEffectKind.Damage;
        public int HealthDelta => -HealthDamage;
        public int ShieldDelta => -ShieldDamage;
    }

    public sealed class HealResult : ICombatEffectResult
    {
        public HealResult(CombatUnit target, int healthRestored)
        {
            Target = target;
            HealthRestored = healthRestored;
        }

        public CombatUnit Target { get; }
        public int HealthRestored { get; }
        public CombatEffectKind Kind => CombatEffectKind.Heal;
        public int HealthDelta => HealthRestored;
        public int ShieldDelta => 0;
    }

    public sealed class ShieldResult : ICombatEffectResult
    {
        public ShieldResult(CombatUnit target, int shieldGranted)
        {
            Target = target;
            ShieldGranted = shieldGranted;
        }

        public CombatUnit Target { get; }
        public int ShieldGranted { get; }
        public CombatEffectKind Kind => CombatEffectKind.Shield;
        public int HealthDelta => 0;
        public int ShieldDelta => ShieldGranted;
    }

    public sealed class PushResult : ICombatEffectResult
    {
        public PushResult(CombatUnit target, IReadOnlyList<GridCoordinate> path)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            if (path.Count == 0) throw new ArgumentException("Push path must contain its origin.", nameof(path));
        }

        public CombatUnit Target { get; }
        public IReadOnlyList<GridCoordinate> Path { get; }
        public GridCoordinate Origin => Path[0];
        public GridCoordinate Destination => Path[Path.Count - 1];
        public int StepsMoved => Math.Max(0, Path.Count - 1);
        public CombatEffectKind Kind => CombatEffectKind.Push;
        public int HealthDelta => 0;
        public int ShieldDelta => 0;
    }

    public sealed class BurnAppliedResult : ICombatEffectResult
    {
        public BurnAppliedResult(
            CombatUnit target,
            int addedDamagePerPulse,
            int previousDamagePerPulse,
            int newDamagePerPulse,
            long newNextPulseTick,
            long newExpireTick)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            AddedDamagePerPulse = addedDamagePerPulse;
            PreviousDamagePerPulse = previousDamagePerPulse;
            NewDamagePerPulse = newDamagePerPulse;
            NewNextPulseTick = newNextPulseTick;
            NewExpireTick = newExpireTick;
        }

        public CombatUnit Target { get; }
        public int AddedDamagePerPulse { get; }
        public int PreviousDamagePerPulse { get; }
        public int NewDamagePerPulse { get; }
        public long NewNextPulseTick { get; }
        public long NewExpireTick { get; }
        public CombatEffectKind Kind => CombatEffectKind.Burning;
        public int HealthDelta => 0;
        public int ShieldDelta => 0;
    }

    public sealed class BurnTickResult : ICombatEffectResult
    {
        public BurnTickResult(
            CombatUnit target,
            int shieldDamage,
            int healthDamage,
            long nextPulseTick,
            long expireTickAfterTick,
            bool defeated)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ShieldDamage = shieldDamage;
            HealthDamage = healthDamage;
            NextPulseTick = nextPulseTick;
            ExpireTickAfterTick = expireTickAfterTick;
            Defeated = defeated;
        }

        public CombatUnit Target { get; }
        public int ShieldDamage { get; }
        public int HealthDamage { get; }
        public long NextPulseTick { get; }
        public long ExpireTickAfterTick { get; }
        public bool Defeated { get; }
        public CombatEffectKind Kind => CombatEffectKind.Burning;
        public int HealthDelta => -HealthDamage;
        public int ShieldDelta => -ShieldDamage;
    }

    public static class PatternCalculator
    {
        public static IReadOnlyCollection<GridCoordinate> Diamond(
            TacticalGrid grid,
            GridCoordinate center,
            int radius,
            bool includeCenter)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));

            return grid.Cells
                .Select(cell => cell.Coordinate)
                .Where(coordinate => coordinate.ManhattanDistance(center) <= radius)
                .Where(coordinate => includeCenter || coordinate != center)
                .OrderBy(coordinate => coordinate.X)
                .ThenBy(coordinate => coordinate.Y)
                .ToArray();
        }
    }

    public static class DamageResolver
    {
        public static IReadOnlyList<DamageResult> Preview(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int damage)
        {
            return Resolve(units, affectedCells, damage, false);
        }

        public static IReadOnlyList<DamageResult> Apply(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int damage)
        {
            return Resolve(units, affectedCells, damage, true);
        }

        private static IReadOnlyList<DamageResult> Resolve(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int damage,
            bool apply)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (affectedCells == null) throw new ArgumentNullException(nameof(affectedCells));

            var affected = new HashSet<GridCoordinate>(affectedCells);
            DamageResult[] results = units
                .Where(unit => !unit.IsDefeated && affected.Contains(unit.Cell))
                .Select(unit => CombatEffectCalculator.Damage(unit, unit.ResourceState, damage))
                .ToArray();
            if (apply) CombatEffectResolver.Apply(results);
            return results;
        }
    }

    public static class HealResolver
    {
        public static IReadOnlyList<HealResult> Preview(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int healing)
        {
            return Resolve(units, affectedCells, healing, false);
        }

        public static IReadOnlyList<HealResult> Apply(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int healing)
        {
            return Resolve(units, affectedCells, healing, true);
        }

        private static IReadOnlyList<HealResult> Resolve(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int healing,
            bool apply)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (affectedCells == null) throw new ArgumentNullException(nameof(affectedCells));

            var affected = new HashSet<GridCoordinate>(affectedCells);
            HealResult[] results = units
                .Where(unit => !unit.IsDefeated && affected.Contains(unit.Cell))
                .Select(unit => CombatEffectCalculator.Heal(unit, unit.ResourceState, healing))
                .ToArray();
            if (apply) CombatEffectResolver.Apply(results);
            return results;
        }
    }

    public static class ShieldResolver
    {
        public static IReadOnlyList<ShieldResult> Preview(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int shield)
        {
            return Resolve(units, affectedCells, shield, false);
        }

        public static IReadOnlyList<ShieldResult> Apply(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int shield)
        {
            return Resolve(units, affectedCells, shield, true);
        }

        private static IReadOnlyList<ShieldResult> Resolve(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int shield,
            bool apply)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (affectedCells == null) throw new ArgumentNullException(nameof(affectedCells));

            var affected = new HashSet<GridCoordinate>(affectedCells);
            ShieldResult[] results = units
                .Where(unit => !unit.IsDefeated && affected.Contains(unit.Cell))
                .Select(unit => CombatEffectCalculator.GrantShield(unit, unit.ResourceState, shield))
                .ToArray();
            if (apply) CombatEffectResolver.Apply(results);
            return results;
        }
    }

    /// <summary>Pure resource math shared by ability preview and execution.</summary>
    public static class CombatEffectCalculator
    {
        public static DamageResult Damage(CombatUnit target, CombatResourceState state, int amount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            int applied = Math.Max(0, amount - target.DefenseBonus);
            int shieldDamage = Math.Min(state.Shield, applied);
            int healthDamage = Math.Min(state.Health, applied - shieldDamage);
            return new DamageResult(target, shieldDamage, healthDamage, state.Health - healthDamage <= 0);
        }

        public static HealResult Heal(CombatUnit target, CombatResourceState state, int amount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            int restored = Math.Min(Math.Max(0, amount), target.MaximumHealth - state.Health);
            return new HealResult(target, restored);
        }

        public static ShieldResult GrantShield(CombatUnit target, CombatResourceState state, int amount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            int granted = Math.Min(Math.Max(0, amount), target.MaximumShield - state.Shield);
            return new ShieldResult(target, granted);
        }

        public static CombatResourceState Apply(CombatResourceState state, ICombatEffectResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new CombatResourceState(
                state.Health + result.HealthDelta,
                state.Shield + result.ShieldDelta);
        }
    }

    /// <summary>Pure stacking and ticking rules for the first content status.</summary>
    public static class BurningCalculator
    {
        public const int DefaultPulseInterval = 100;

        public static BurnAppliedResult Apply(
            CombatUnit target,
            BurningState current,
            int sourceDamage,
            int percentage,
            int durationInPulses,
            long currentTick,
            int pulseInterval = DefaultPulseInterval)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (sourceDamage < 0) throw new ArgumentOutOfRangeException(nameof(sourceDamage));
            if (percentage < 0) throw new ArgumentOutOfRangeException(nameof(percentage));
            if (durationInPulses <= 0) throw new ArgumentOutOfRangeException(nameof(durationInPulses));
            if (currentTick < 0) throw new ArgumentOutOfRangeException(nameof(currentTick));
            if (pulseInterval <= 0) throw new ArgumentOutOfRangeException(nameof(pulseInterval));

            int addedDamage = sourceDamage == 0 || percentage == 0
                ? 0
                : Math.Max(1, (int)Math.Ceiling(sourceDamage * percentage / 100d));
            return new BurnAppliedResult(
                target,
                addedDamage,
                current.DamagePerPulse,
                current.DamagePerPulse + addedDamage,
                current.IsActive && current.NextPulseTick > currentTick
                    ? current.NextPulseTick
                    : currentTick + pulseInterval,
                currentTick + (long)durationInPulses * pulseInterval);
        }

        public static BurnTickResult Tick(
            CombatUnit target,
            CombatResourceState resources,
            BurningState burning,
            long timelineTick,
            int pulseInterval = DefaultPulseInterval)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!burning.IsActive) throw new InvalidOperationException("Cannot tick an inactive burning state.");
            if (timelineTick != burning.NextPulseTick)
                throw new InvalidOperationException("Burn can only pulse at its scheduled timeline tick.");
            if (pulseInterval <= 0) throw new ArgumentOutOfRangeException(nameof(pulseInterval));
            DamageResult damage = CombatEffectCalculator.Damage(target, resources, burning.DamagePerPulse);
            return new BurnTickResult(
                target,
                damage.ShieldDamage,
                damage.HealthDamage,
                burning.NextPulseTick + pulseInterval,
                burning.ExpireTick,
                damage.Defeated);
        }
    }

    public static class CombatEffectResolver
    {
        public static void Apply(IEnumerable<ICombatEffectResult> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            foreach (ICombatEffectResult result in results)
            {
                if (result == null) throw new ArgumentException("Effect result list contains null.", nameof(results));
                if (result is PushResult push)
                {
                    push.Target.MoveTo(push.Destination);
                    continue;
                }

                if (result is BurnAppliedResult burn)
                {
                    burn.Target.ApplyBurn(burn);
                    continue;
                }

                if (result is StatusAppliedResult status)
                {
                    status.Target.ApplyStatus(status);
                    continue;
                }

                result.Target.ApplyResourceState(CombatEffectCalculator.Apply(result.Target.ResourceState, result));
            }
        }
    }
}
