using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>Minimal initiative-ordered turn loop that skips defeated units.</summary>
    public sealed class TurnCoordinator
    {
        private readonly List<CombatUnit> turnOrder;
        private readonly int actionPointsPerTurn;
        private int currentIndex = -1;

        public TurnCoordinator(IEnumerable<CombatUnit> units, int actionPointsPerTurn)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (actionPointsPerTurn <= 0) throw new ArgumentOutOfRangeException(nameof(actionPointsPerTurn));

            List<CombatUnit> providedUnits = units.ToList();
            if (providedUnits.Count == 0) throw new ArgumentException("Turn order requires at least one unit.", nameof(units));
            if (providedUnits.Any(unit => unit == null)) throw new ArgumentException("Turn order cannot contain null units.", nameof(units));

            turnOrder = providedUnits
                .Select((unit, index) => new { Unit = unit, Index = index })
                .OrderByDescending(entry => entry.Unit.Initiative)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Unit)
                .ToList();
            this.actionPointsPerTurn = actionPointsPerTurn;
        }

        public int Round { get; private set; }
        public CombatUnit CurrentUnit => currentIndex >= 0 ? turnOrder[currentIndex] : null;
        public IReadOnlyList<CombatUnit> LivingTurnOrder => turnOrder.Where(unit => !unit.IsDefeated).ToArray();

        public CombatUnit StartBattle()
        {
            if (Round > 0) throw new InvalidOperationException("Battle turn order has already started.");
            currentIndex = FindNextLivingIndex(-1);
            if (currentIndex < 0) return null;
            Round = 1;
            BeginCurrentTurn();
            return CurrentUnit;
        }

        public CombatUnit Advance()
        {
            if (currentIndex < 0) throw new InvalidOperationException("StartBattle must be called first.");

            int previousIndex = currentIndex;
            currentIndex = FindNextLivingIndex(currentIndex);
            if (currentIndex < 0) return null;
            if (currentIndex <= previousIndex) Round++;
            BeginCurrentTurn();
            return CurrentUnit;
        }

        private int FindNextLivingIndex(int afterIndex)
        {
            for (int offset = 1; offset <= turnOrder.Count; offset++)
            {
                int candidate = (afterIndex + offset + turnOrder.Count) % turnOrder.Count;
                if (!turnOrder[candidate].IsDefeated) return candidate;
            }

            return -1;
        }

        private void BeginCurrentTurn()
        {
            CurrentUnit.BeginTurn(actionPointsPerTurn);
        }
    }
}
