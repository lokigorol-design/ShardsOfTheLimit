using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Damage", fileName = "DamageEffect")]
    public sealed class DamageEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField, Min(0)] private int amount = 1;

        public int Amount => amount;
    }
}
