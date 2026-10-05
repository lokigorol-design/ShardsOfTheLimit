using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Apply Burn", fileName = "ApplyBurnEffect")]
    public sealed class ApplyBurnEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private Sprite icon = null;
        [SerializeField, Range(0, 100)] private int damagePercentage = 10;
        [SerializeField, Min(1)] private int duration = 3;

        public Sprite Icon => icon;
        public int DamagePercentage => damagePercentage;
        public int Duration => duration;
    }
}
