using ShardsOfTheLimit.Domain;
using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Status")]
    public sealed class StatusDefinition : ScriptableObject
    {
        [Tooltip("BALANCE_DEFAULT: tune stacking, expiry and optional global damage here.")]
        public string stableId = "POISON";
        public Sprite icon;
        [Tooltip("Palette for a grayscale icon master; empty draws the icon as it is.")]
        public ResonantPalette paletteOverride;
        [Tooltip("Shared design 2.5 behaviour from 16.10.8 (Poison, Corrosion, Oil, Slow, Vulnerability).")]
        public StatusMechanic mechanic = StatusMechanic.None;
        [Min(1)] public int maximumStacks = 999;
        [Min(1)] public int durationTicks = 300;
        [Min(1)] public int pulseInterval = 100;
        [Min(0)] public int damagePerStack = 0;
        public bool preventsVoluntaryMovement;
        public StatusRules Snapshot() => new StatusRules(stableId, maximumStacks, durationTicks,
            pulseInterval, damagePerStack, preventsVoluntaryMovement, mechanic);
    }
}