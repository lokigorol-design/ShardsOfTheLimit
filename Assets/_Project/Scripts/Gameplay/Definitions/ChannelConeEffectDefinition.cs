using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Tag «Поддерживающее» (Flame Burst): starts for the ability's AP cost, each next own turn can be extended for
    /// Extend Cost AP up to the last stage; the cone grows by stage and each stage hits everything inside.
    /// The cooldown starts when the channel ends.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Channel Cone", fileName = "ChannelConeEffect")]
    public sealed class ChannelConeEffectDefinition : AbilityEffectDefinition
    {
        public float[] stageAttackMultipliers = { 0.8f, 1.0f, 1.2f };
        [Min(1)] public int extendCost = 2;
        [Min(0)] public int cooldownAfterEnd = 3;
        [Min(0)] public float burnAttackMultiplier = 0.25f;
        [Min(1)] public int burnDurationPulses = 3;

        public int MaximumStage => stageAttackMultipliers.Length;
    }
}