using ShardsOfTheLimit.Domain;
using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Cloud")]
    public sealed class CloudDefinition : ScriptableObject
    {
        [Tooltip("BALANCE_DEFAULT: duration and cadence are GLOBAL ticks, not owner activations.")]
        public string stableId = "Poison Cloud";
        public StatusDefinition status;
        public PatternDefinition areaShape;
        [Min(1)] public int durationTicks = 300;
        [Min(1)] public int pulseInterval = 100;
        [Min(1)] public int movementInterval = 100;
        [Min(0)] public int movementSpeed = 1;
        [Min(1)] public int baseStacksPerTick = 1;
        [Min(0)] public int damagePerTick = 0;
        [Min(0.1f)] public float verticalHeight = 1;
        public Color color = Color.green;
        public CloudRules Snapshot() => new CloudRules(stableId, status.Snapshot(), durationTicks, pulseInterval,
            movementInterval, movementSpeed, baseStacksPerTick, damagePerTick, verticalHeight);
    }
}
