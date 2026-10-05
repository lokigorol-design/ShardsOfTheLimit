using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Apply Status")]
    public sealed class ApplyStatusEffectDefinition : AbilityEffectDefinition
    {
        public StatusDefinition status;
        [Min(1)] public int stacks = 1;
    }
}
