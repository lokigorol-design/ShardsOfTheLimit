using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Grant Shield", fileName = "GrantShieldEffect")]
    public sealed class GrantShieldEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField, Min(0)] private int amount = 1;

        public int Amount => amount;
    }
}
