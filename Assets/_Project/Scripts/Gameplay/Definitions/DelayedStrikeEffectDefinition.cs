using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Tag «Отложенное»: marks the impact cells now and strikes them after Delay Ticks (Fire Geyser).</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Delayed Strike", fileName = "DelayedStrikeEffect")]
    public sealed class DelayedStrikeEffectDefinition : AbilityEffectDefinition
    {
        [Min(1)] public int delayTicks = 100;
        [Min(0)] public float attackMultiplier = 0.8f;
        [Min(0), Tooltip("Burning power as a multiple of the creator's ATK; 0 = no Burning.")] public float burnAttackMultiplier = 0.25f;
        [Min(1)] public int burnDurationPulses = 3;
    }
}