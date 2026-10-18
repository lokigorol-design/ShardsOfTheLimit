using System;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>
    /// Calling a replacement (author's rule, 18.10.2026): on its turn any own resonant may call a resonant from the
    /// reserve. For 200 ticks the caller takes +100% damage; then the newcomer appears in its place and the caller returns
    /// to the reserve. If the caller falls first, nobody comes — a fallen resonant's place cannot be refilled.
    /// The battle is lost when no own resonant is left on the field.
    /// </summary>
    public static class ReserveRules
    {
        /// <summary>Resonants on the field at the start of a battle.</summary>
        public const int FieldLimit = 3;
        public const int SwapTicks = 200;
        public const double SummoningDamageMultiplier = 2d;
        /// <summary>A resonant that went back to the reserve cannot be called again for 3 average turns.</summary>
        public const int ReturnLockTicks = 300;
        public const int ActionCost = 1;
        public const string SummoningId = "SUMMONING";

        public static StatusRules Summoning => new StatusRules(SummoningId, 1, SwapTicks, 100, 0, false, StatusMechanic.Summoning);
    }

    public sealed partial class CombatUnit
    {
        /// <summary>The call is over (the caller left the field).</summary>
        public void RemoveSummoning() => RemoveMechanic(StatusMechanic.Summoning);
        public bool IsSummoning => GetMechanicStacks(StatusMechanic.Summoning) > 0;
    }

    /// <summary>One resonant waiting in the reserve.</summary>
    public sealed class ReserveSlot
    {
        public ReserveSlot(CombatUnit unit, long availableTick)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            AvailableTick = availableTick;
        }

        public CombatUnit Unit { get; }
        /// <summary>Resonants that went back to the reserve are locked for <see cref="ReserveRules.ReturnLockTicks"/>.</summary>
        public long AvailableTick { get; }
        /// <summary>Already being called by someone on the field.</summary>
        public bool IsCalled { get; set; }
        public bool IsAvailable(long tick) => !Unit.IsDefeated && !IsCalled && tick >= AvailableTick;
    }

    /// <summary>A call in progress: the caller waits <see cref="ReserveRules.SwapTicks"/> ticks, vulnerable.</summary>
    public sealed class PendingSwap
    {
        public PendingSwap(CombatUnit caller, ReserveSlot incoming, long arrivalTick)
        {
            Caller = caller ?? throw new ArgumentNullException(nameof(caller));
            Incoming = incoming ?? throw new ArgumentNullException(nameof(incoming));
            ArrivalTick = arrivalTick;
        }

        public CombatUnit Caller { get; }
        public ReserveSlot Incoming { get; }
        public long ArrivalTick { get; }
        public bool IsDue(long tick) => tick >= ArrivalTick;
    }
}
