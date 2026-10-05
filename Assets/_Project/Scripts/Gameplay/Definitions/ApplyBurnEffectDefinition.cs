using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Design 2.5 Burning: power = Attack Multiplier × source ATK every 100 ticks; strongest wins.</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Apply Burn", fileName = "ApplyBurnEffect")]
    public sealed class ApplyBurnEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private Sprite icon = null;
        [SerializeField, Min(0)] private float attackMultiplier = 0.25f;
        [SerializeField, Min(1), Tooltip("Pulses of 100 ticks; the default lifetime is 3 turns.")] private int duration = 3;

        public Sprite Icon => icon;
        public float AttackMultiplier => attackMultiplier;
        public int Duration => duration;
    }
}