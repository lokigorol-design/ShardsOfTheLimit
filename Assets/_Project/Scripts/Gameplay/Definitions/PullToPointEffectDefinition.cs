using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Storm Magnet: the first resonant on a straight line from the chosen point within Reach cells is pulled
    /// onto the point. The ability's other effects (damage, Immobilize) apply to that resonant only.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Pull To Point", fileName = "PullToPointEffect")]
    public sealed class PullToPointEffectDefinition : AbilityEffectDefinition
    {
        [Min(1)] public int reach = 2;
    }
}