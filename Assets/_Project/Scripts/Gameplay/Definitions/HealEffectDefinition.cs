using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Heal", fileName = "HealEffect")]
    public sealed class HealEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField, Min(0)] private int amount = 1;

        public int Amount => amount;
    }
}
