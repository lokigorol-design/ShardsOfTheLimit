using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    public enum TimelineEventKind
    {
        Activation,
        StatusPulse
    }

    public readonly struct TimelineEvent
    {
        public TimelineEvent(long tick, TimelineEventKind kind, CombatUnit unit, string statusId = null)
        {
            Tick = tick;
            Kind = kind;
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            StatusId = statusId;
        }

        public long Tick { get; }
        public TimelineEventKind Kind { get; }
        public CombatUnit Unit { get; }
        public string StatusId { get; }
    }

    /// <summary>Design 2.5 speed (16.10.3): interval = 1000 / Speed ticks, bounded to 30..200 ticks.</summary>
    public static class SpeedTimeline
    {
        public const int TicksPerAverageTurn = 100;
        public const int SpeedNumerator = 1000;
        public const int MinimumInterval = 30;
        public const int MaximumInterval = 200;

        public static int GetInterval(double speed)
        {
            if (speed <= 0) return MaximumInterval;
            int interval = (int)Math.Round(SpeedNumerator / speed, MidpointRounding.AwayFromZero);
            return Math.Max(MinimumInterval, Math.Min(MaximumInterval, interval));
        }
    }

    /// <summary>Continuous event timeline shared by player and AI combatants.</summary>
    public sealed class TimelineCoordinator
    {
        private readonly List<CombatUnit> units;
        private readonly Dictionary<CombatUnit, int> stableOrder;
        private readonly Dictionary<CombatUnit, long> nextActivationTicks;
        private readonly int actionPointsPerActivation;
        private bool started;
        public AreaEffectWorld AreaEffects { get; set; }

        public TimelineCoordinator(IEnumerable<CombatUnit> units, int actionPointsPerActivation)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (actionPointsPerActivation <= 0) throw new ArgumentOutOfRangeException(nameof(actionPointsPerActivation));

            this.units = units.ToList();
            if (this.units.Count == 0) throw new ArgumentException("Timeline requires at least one unit.", nameof(units));
            if (this.units.Any(unit => unit == null)) throw new ArgumentException("Timeline cannot contain null units.", nameof(units));

            this.actionPointsPerActivation = actionPointsPerActivation;
            stableOrder = this.units.Select((unit, index) => new { unit, index })
                .ToDictionary(entry => entry.unit, entry => entry.index);
            nextActivationTicks = this.units.ToDictionary(
                unit => unit,
                unit => (long)SpeedTimeline.GetInterval(unit.EffectiveSpeed));
        }

        /// <summary>A resonant summoned onto the field joins the timeline; its first turn comes one interval later.</summary>
        public void AddUnit(CombatUnit unit, long firstActivationTick)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (!units.Contains(unit)) units.Add(unit);
            if (!stableOrder.ContainsKey(unit)) stableOrder[unit] = stableOrder.Count == 0 ? 0 : stableOrder.Values.Max() + 1;
            nextActivationTicks[unit] = firstActivationTick;
        }

        /// <summary>A recalled resonant leaves the timeline (never the unit whose turn it is).</summary>
        public void RemoveUnit(CombatUnit unit)
        {
            if (unit == null || unit == CurrentUnit) return;
            units.Remove(unit);
            nextActivationTicks.Remove(unit);
        }

        public void SetNextActivation(CombatUnit unit, long tick)
        {
            if (unit != null && nextActivationTicks.ContainsKey(unit)) nextActivationTicks[unit] = tick;
        }

        public bool Contains(CombatUnit unit) => units.Contains(unit);

        /// <summary>
        /// Swap: the resonant whose turn it is leaves the timeline and the one replacing it continues this activation;
        /// its own next turn is scheduled from now when the activation ends.
        /// </summary>
        public void ReplaceCurrent(CombatUnit outgoing, CombatUnit incoming)
        {
            if (outgoing == null || incoming == null) throw new ArgumentNullException(outgoing == null ? nameof(outgoing) : nameof(incoming));
            if (outgoing != CurrentUnit) throw new InvalidOperationException("Only the active resonant can be swapped out.");
            units.Remove(outgoing);
            nextActivationTicks.Remove(outgoing);
            AddUnit(incoming, CurrentTick);
            CurrentUnit = incoming;
        }

        public long CurrentTick { get; private set; }
        public CombatUnit CurrentUnit { get; private set; }
        public IReadOnlyList<CombatUnit> LivingUnits => units.Where(unit => !unit.IsDefeated).ToArray();
        public IReadOnlyList<ICombatEffectResult> LastTimelineResults { get; private set; } =
            Array.Empty<ICombatEffectResult>();

        public CombatUnit StartBattle()
        {
            if (started) throw new InvalidOperationException("Battle timeline has already started.");
            started = true;
            LastTimelineResults = Array.Empty<ICombatEffectResult>();
            return ActivateNextUnit();
        }

        public CombatUnit Advance()
        {
            if (!started || CurrentUnit == null)
                throw new InvalidOperationException("StartBattle must be called before advancing the timeline.");

            nextActivationTicks[CurrentUnit] = CurrentTick + SpeedTimeline.GetInterval(CurrentUnit.EffectiveSpeed);
            CurrentUnit = null;
            var statusResults = new List<ICombatEffectResult>();

            while (units.Any(unit => !unit.IsDefeated))
            {
                CombatUnit nextUnit = GetNextActivationUnit();
                if (nextUnit == null) break;
                long activationTick = nextActivationTicks[nextUnit];
                long statusTick = units
                    .Where(unit => !unit.IsDefeated && unit.IsBurning)
                    .Select(unit => unit.BurningState.NextPulseTick)
                    .DefaultIfEmpty(long.MaxValue)
                    .Min();
                statusTick = Math.Min(statusTick, AreaEffects?.NextEventTick ?? long.MaxValue);

                if (statusTick <= activationTick)
                {
                    CurrentTick = statusTick;
                    if (AreaEffects != null && AreaEffects.NextEventTick == statusTick)
                        statusResults.AddRange(AreaEffects.ProcessTick(statusTick));
                    foreach (CombatUnit unit in units
                                 .Where(unit => !unit.IsDefeated && unit.IsBurning &&
                                                unit.BurningState.NextPulseTick == statusTick)
                                 .OrderBy(unit => stableOrder[unit])
                                 .ToArray())
                    {
                        statusResults.Add(unit.PulseBurn(statusTick));
                    }

                    continue;
                }

                LastTimelineResults = statusResults;
                CurrentUnit = nextUnit;
                CurrentTick = activationTick;
                CurrentUnit.BeginActivation(actionPointsPerActivation);
                if (AreaEffects != null) statusResults.AddRange(AreaEffects.OnActivation(CurrentUnit));
                return CurrentUnit;
            }

            LastTimelineResults = statusResults;
            return null;
        }

        public IReadOnlyList<TimelineEvent> GetUpcomingEvents(
            int maximumCount,
            bool includeCurrent = true,
            bool includeStatusEvents = true)
        {
            if (maximumCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
            if (maximumCount == 0 || !started) return Array.Empty<TimelineEvent>();

            var result = new List<TimelineEvent>(maximumCount);
            if (includeCurrent && CurrentUnit != null && !CurrentUnit.IsDefeated)
            {
                result.Add(new TimelineEvent(CurrentTick, TimelineEventKind.Activation, CurrentUnit));
            }

            var activations = nextActivationTicks
                .Where(entry => !entry.Key.IsDefeated)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            if (CurrentUnit != null && !CurrentUnit.IsDefeated)
            {
                activations[CurrentUnit] = CurrentTick + SpeedTimeline.GetInterval(CurrentUnit.EffectiveSpeed);
            }

            var statusTicks = includeStatusEvents
                ? units.Where(unit => !unit.IsDefeated && unit.IsBurning)
                    .ToDictionary(unit => unit, unit => unit.BurningState.NextPulseTick)
                : new Dictionary<CombatUnit, long>();

            while (result.Count < maximumCount && activations.Count > 0)
            {
                CombatUnit activationUnit = activations.Keys
                    .OrderBy(unit => activations[unit])
                    .ThenByDescending(unit => unit.EffectiveSpeed)
                    .ThenBy(unit => stableOrder[unit])
                    .First();
                long activationTick = activations[activationUnit];

                CombatUnit statusUnit = statusTicks.Count == 0
                    ? null
                    : statusTicks.Keys
                        .OrderBy(unit => statusTicks[unit])
                        .ThenBy(unit => stableOrder[unit])
                        .First();
                long statusTick = statusUnit == null ? long.MaxValue : statusTicks[statusUnit];

                if (statusTick <= activationTick)
                {
                    result.Add(new TimelineEvent(statusTick, TimelineEventKind.StatusPulse, statusUnit, "BURN"));
                    long followingPulse = statusTick + BurningCalculator.DefaultPulseInterval;
                    if (followingPulse <= statusUnit.BurningState.ExpireTick)
                        statusTicks[statusUnit] = followingPulse;
                    else
                        statusTicks.Remove(statusUnit);
                    continue;
                }

                result.Add(new TimelineEvent(activationTick, TimelineEventKind.Activation, activationUnit));
                activations[activationUnit] = activationTick + SpeedTimeline.GetInterval(activationUnit.EffectiveSpeed);
            }

            if (includeStatusEvents && AreaEffects != null)
                return result.Concat(AreaEffects.PreviewEvents(CurrentTick, maximumCount))
                    .OrderBy(e => e.Tick).ThenBy(e => e.Kind == TimelineEventKind.StatusPulse ? 0 : 1)
                    .Take(maximumCount).ToArray();
            return result;
        }

        private CombatUnit ActivateNextUnit()
        {
            CurrentUnit = GetNextActivationUnit();
            if (CurrentUnit == null) return null;
            CurrentTick = nextActivationTicks[CurrentUnit];
            CurrentUnit.BeginActivation(actionPointsPerActivation);
            if (AreaEffects != null) LastTimelineResults = AreaEffects.OnActivation(CurrentUnit);
            return CurrentUnit;
        }

        private CombatUnit GetNextActivationUnit()
        {
            return units
                .Where(unit => !unit.IsDefeated)
                .OrderBy(unit => nextActivationTicks[unit])
                .ThenByDescending(unit => unit.EffectiveSpeed)
                .ThenBy(unit => stableOrder[unit])
                .FirstOrDefault();
        }
    }
}
