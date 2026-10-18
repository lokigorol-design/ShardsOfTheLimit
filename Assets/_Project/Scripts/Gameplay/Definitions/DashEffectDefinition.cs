using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Dash (16.10.8, e.g. Blood Dash): the resonant runs straight at an enemy on one of its four lines, up to
    /// Max Distance cells over a free path, and stops next to it. The ability's other effects then hit that enemy.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Dash", fileName = "DashEffect")]
    public sealed class DashEffectDefinition : AbilityEffectDefinition
    {
        [Min(1)] public int maxDistance = 3;
    }
}