using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>Invisibility, Mist and Bleed (16.10.8) with the Mist Panther numbers (10.12).</summary>
    public static class StealthRules
    {
        public const string InvisibilityId = "INVISIBILITY";
        public const string MistId = "MIST";
        public const string VulnerabilityId = "VULNERABILITY";
        /// <summary>«2 хода»: the turn it is applied and the next one.</summary>
        public const int DurationTicks = 200;
        public const int VulnerabilityTicks = 200;
        /// <summary>First attack from Invisibility: +40% damage.</summary>
        public const double InvisibilityBonus = 0.4d;
        /// <summary>Silent Executioner: from Invisibility at a target below 50% health, another +40%.</summary>
        public const double ExecutionerBonus = 0.4d;

        public static StatusRules Invisibility => new StatusRules(InvisibilityId, 1, DurationTicks, 100, 0, false, StatusMechanic.Invisibility);
        public static StatusRules Mist => new StatusRules(MistId, 1, DurationTicks, 100, 0, false, StatusMechanic.Mist);
        public static StatusRules Vulnerability =>
            new StatusRules(VulnerabilityId, 1, VulnerabilityTicks, 100, 0, false, StatusMechanic.Vulnerability);

        public static StatusRules Bleed(string id, int sourceAttack, int duration, bool stacks) => new StatusRules(id,
            stacks ? 99 : 1, duration, 100,
            System.Math.Max(1, (int)System.Math.Round(sourceAttack * StatusMechanicRules.BleedAttackFraction,
                System.MidpointRounding.AwayFromZero)), false, StatusMechanic.Bleed);
    }

    public sealed partial class CombatUnit
    {
        private readonly List<DamageResult> pendingBleedHits = new List<DamageResult>();

        /// <summary>Bleed hits since the last call (for presentation).</summary>
        public IReadOnlyList<DamageResult> TakeBleedHits()
        {
            DamageResult[] hits = pendingBleedHits.ToArray();
            pendingBleedHits.Clear();
            return hits;
        }

        public bool IsInvisible => GetMechanicStacks(StatusMechanic.Invisibility) > 0;
        public bool IsInMist => GetMechanicStacks(StatusMechanic.Mist) > 0;
        /// <summary>Enemies cannot choose this unit as a target.</summary>
        public bool IsHiddenFromEnemies => IsInvisible || IsInMist;

        /// <summary>The first attack ends Invisibility and Mist.</summary>
        public void RevealFromStealth()
        {
            RemoveMechanic(StatusMechanic.Invisibility);
            RemoveMechanic(StatusMechanic.Mist);
        }

        /// <summary>Bleed: the bearer spent AP — each Bleed deals its stacks × damage (periodic, BLEED resistance).</summary>
        public IReadOnlyList<DamageResult> TriggerBleed()
        {
            var results = new List<DamageResult>();
            foreach (StackStatusState bleed in statuses.Values.Where(s => s.Rules.Mechanic == StatusMechanic.Bleed).ToArray())
            {
                if (IsDefeated) break;
                DamageResult hit = CombatEffectCalculator.PeriodicDamage(this, ResourceState,
                    bleed.Stacks * bleed.Rules.DamagePerStack, StatusMechanicRules.BleedResistanceId);
                CombatEffectResolver.Apply(new[] { hit });
                results.Add(hit);
            }

            return results;
        }
    }
}
