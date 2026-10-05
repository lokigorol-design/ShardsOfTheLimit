using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Push", fileName = "PushEffect")]
    public sealed class PushEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField, Min(0)] private int distance = 1;

        public int Distance => distance;
    }
}
