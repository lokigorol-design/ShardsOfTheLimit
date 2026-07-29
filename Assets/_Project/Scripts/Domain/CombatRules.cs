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

    /// <summary>Mutable battle state shared by player and opposing units.</summary>
    public sealed class CombatUnit
    {
        private readonly Dictionary<string, int> cooldowns = new Dictionary<string, int>();

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
        public string CurrentAction { get; set; }
        public int CurrentActionPoints { get; private set; }
        public bool IsDefeated => CurrentHealth <= 0;

        public void MoveTo(GridCoordinate cell) => Cell = cell;
        public int GetCooldown(string abilityId) => cooldowns.TryGetValue(abilityId, out int value) ? value : 0;
        public void SetCooldown(string abilityId, int turns) => cooldowns[abilityId] = Math.Max(0, turns);

        public void BeginTurn(int actionPoints)
        {
            CurrentActionPoints = Math.Max(0, actionPoints);
            TickCooldowns();
            CurrentAction = "Ready";
        }

        public bool SpendActionPoints(int cost)
        {
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
            if (cost > CurrentActionPoints) return false;
            CurrentActionPoints -= cost;
            return true;
        }

        public void TickCooldowns()
        {
            foreach (string abilityId in cooldowns.Keys.ToArray())
            {
                cooldowns[abilityId] = Math.Max(0, cooldowns[abilityId] - 1);
            }
        }

        internal DamageResult ApplyDamage(int amount)
        {
            int applied = Math.Max(0, amount);
            int shieldDamage = Math.Min(CurrentShield, applied);
            CurrentShield -= shieldDamage;
            int healthDamage = Math.Min(CurrentHealth, applied - shieldDamage);
            CurrentHealth -= healthDamage;
            return new DamageResult(this, shieldDamage, healthDamage, IsDefeated);
        }
    }

    public sealed class DamageResult
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
        public static IReadOnlyList<DamageResult> Apply(
            IEnumerable<CombatUnit> units,
            IReadOnlyCollection<GridCoordinate> affectedCells,
            int damage)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (affectedCells == null) throw new ArgumentNullException(nameof(affectedCells));

            var affected = new HashSet<GridCoordinate>(affectedCells);
            return units
                .Where(unit => !unit.IsDefeated && affected.Contains(unit.Cell))
                .Select(unit => unit.ApplyDamage(damage))
                .ToArray();
        }
    }
}
