using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Pull")]
    public sealed class PullEffectDefinition : AbilityEffectDefinition
    { [Min(0)] public int distance = 3; }
}
