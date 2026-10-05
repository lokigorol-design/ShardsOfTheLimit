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

        private readonly Dictionary<string, int> periodicResistances = new Dictionary<string, int>();

        public CombatUnit(
            string definitionId,
            string displayName,
            int maximumHealth,
            int maximumShield,
            int speed,
            int movementRange,
            Team team,
            ControlType controlType,
            GridCoordinate cell,
            int attack = 0,
            int defense = 0)
        {
            if (maximumHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (maximumShield < 0) throw new ArgumentOutOfRangeException(nameof(maximumShield));
            if (movementRange < 0) throw new ArgumentOutOfRangeException(nameof(movementRange));
            if (attack < 0) throw new ArgumentOutOfRangeException(nameof(attack));

            DefinitionId = definitionId ?? throw new ArgumentNullException(nameof(definitionId));
            DisplayName = displayName ?? definitionId;
            MaximumHealth = maximumHealth;
            MaximumShield = maximumShield;
            Speed = speed;
            MovementRange = movementRange;
            Attack = attack;
            BaseDefense = defense;
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
        /// <summary>Design 2.5 Speed: activation interval is 1000 / Speed ticks.</summary>
        public int Speed { get; }
        public int MovementRange { get; }
        /// <summary>Design 2.5 ATK: ability damage is written as a multiplier of it.</summary>
        public int Attack { get; }
        /// <summary>Authored Defense before Corrosion and temporary bonuses.</summary>
        public int BaseDefense { get; }
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

        /// <summary>Authored "Защита от периодического урона" for one source, in percent.</summary>
        public void SetPeriodicResistance(string sourceId, int percent)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Source id is empty.", nameof(sourceId));
            periodicResistances[sourceId] = percent;
        }

        public int GetBasePeriodicResistance(string sourceId) =>
            sourceId != null && periodicResistances.TryGetValue(sourceId, out int value) ? value : 0;

        /// <summary>Corrosion removes 10 percentage points per stack and may go negative.</summary>
        public int GetPeriodicResistance(string sourceId) =>
            Math.Min(100, GetBasePeriodicResistance(sourceId) -
                          StatusMechanicRules.CorrosionResistancePenalty * GetMechanicStacks(StatusMechanic.Corrosion));

        /// <summary>Corrosion removes 10% of base Defense per stack and may go negative.</summary>
        public double EffectiveDefense =>
            BaseDefense * (1d - StatusMechanicRules.CorrosionDefenseFraction * GetMechanicStacks(StatusMechanic.Corrosion)) +
            DefenseBonus;
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
            if (result.ConsumedOilStacks > 0) RemoveMechanic(StatusMechanic.Oil);
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
        public PushResult(CombatUnit target, IReadOnlyList<GridCoordinate> path, GridCoordinate? collisionCell = null)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            if (path.Count == 0) throw new ArgumentException("Push path must contain its origin.", nameof(path));
            CollisionCell = collisionCell;
        }

        public CombatUnit Target { get; }
        public IReadOnlyList<GridCoordinate> Path { get; }
        /// <summary>Cell that stopped the displacement early (unit, obstacle or terrain); null when it was not blocked.</summary>
        public GridCoordinate? CollisionCell { get; }
        public bool Collided => CollisionCell.HasValue;
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
            long newExpireTick,
            int consumedOilStacks = 0)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            AddedDamagePerPulse = addedDamagePerPulse;
            PreviousDamagePerPulse = previousDamagePerPulse;
            NewDamagePerPulse = newDamagePerPulse;
            NewNextPulseTick = newNextPulseTick;
            NewExpireTick = newExpireTick;
            ConsumedOilStacks = consumedOilStacks;
        }

        public CombatUnit Target { get; }
        /// <summary>Power of this application (after Oil), before Burning resistance.</summary>
        public int AddedDamagePerPulse { get; }
        /// <summary>Oil stacks burned away by this application (+50% power each).</summary>
        public int ConsumedOilStacks { get; }
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
        /// <summary>A direct hit of raw strength <paramref name="amount"/>: Defense, then Vulnerability, then shield-first.</summary>
        public static DamageResult Damage(CombatUnit target, CombatResourceState state, double amount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            double afterDefense = DefenseFormula.Apply(amount, target.EffectiveDefense);
            if (afterDefense > 0 && target.GetMechanicStacks(StatusMechanic.Vulnerability) > 0)
                afterDefense *= StatusMechanicRules.VulnerabilityMultiplier;
            return Absorb(target, state, afterDefense <= 0 ? 0 : Math.Max(1, DefenseFormula.Round(afterDefense)));
        }

        /// <summary>Periodic damage (Burning, Poison, Magma): ignores Defense, reduced by the per-source resistance.</summary>
        public static DamageResult PeriodicDamage(CombatUnit target, CombatResourceState state, double amount, string sourceId)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (amount <= 0) return Absorb(target, state, 0);
            double scaled = amount * (100 - target.GetPeriodicResistance(sourceId)) / 100d;
            return Absorb(target, state, scaled <= 0 ? 0 : Math.Max(1, DefenseFormula.Round(scaled)));
        }

        private static DamageResult Absorb(CombatUnit target, CombatResourceState state, int applied)
        {
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

    /// <summary>Design 2.5 direct-hit Defense: damage = A² / (A + Defense), denominator at least 0.25 × A.</summary>
    public static class DefenseFormula
    {
        public const double MinimumDenominatorFraction = 0.25d;

        public static double Apply(double rawDamage, double defense)
        {
            if (rawDamage <= 0) return 0;
            double denominator = Math.Max(rawDamage + defense, MinimumDenominatorFraction * rawDamage);
            return rawDamage * rawDamage / denominator;
        }

        public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>Design 2.5 Burning: 0.25 × source ATK every 100 ticks for 3 turns; strongest wins, no stacking.</summary>
    public static class BurningCalculator
    {
        public const int DefaultPulseInterval = 100;
        public const string ResistanceId = "BURNING";
        public const double OilBonusPerStack = 0.5d;

        /// <param name="power">Pulse power of this application before Oil and resistance.</param>
        /// <param name="oilStacks">Oil stacks on the target; all are consumed for +50% power each.</param>
        public static BurnAppliedResult Apply(
            CombatUnit target,
            BurningState current,
            int power,
            int durationInPulses,
            long currentTick,
            int oilStacks = 0,
            int pulseInterval = DefaultPulseInterval)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (power < 0) throw new ArgumentOutOfRangeException(nameof(power));
            if (durationInPulses <= 0) throw new ArgumentOutOfRangeException(nameof(durationInPulses));
            if (currentTick < 0) throw new ArgumentOutOfRangeException(nameof(currentTick));
            if (pulseInterval <= 0) throw new ArgumentOutOfRangeException(nameof(pulseInterval));
            if (oilStacks < 0) throw new ArgumentOutOfRangeException(nameof(oilStacks));

            int consumedOil = power > 0 ? oilStacks : 0;
            int appliedPower = power == 0
                ? 0
                : DefenseFormula.Round(power * (1d + OilBonusPerStack * consumedOil));
            int previous = current.IsActive ? current.DamagePerPulse : 0;
            return new BurnAppliedResult(
                target,
                appliedPower,
                previous,
                Math.Max(previous, appliedPower),
                current.IsActive && current.NextPulseTick > currentTick
                    ? current.NextPulseTick
                    : currentTick + pulseInterval,
                currentTick + (long)durationInPulses * pulseInterval,
                consumedOil);
        }

        public static int PowerFromAttack(int attack, float attackMultiplier) =>
            attack <= 0 || attackMultiplier <= 0 ? 0 : Math.Max(1, DefenseFormula.Round(attack * (double)attackMultiplier));

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
            DamageResult damage = CombatEffectCalculator.PeriodicDamage(
                target, resources, burning.DamagePerPulse, ResistanceId);
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
