using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Wide Swipe (shared by Mist Panther and Blazing Giant): rows of 3 cells in front of the resonant towards the chosen
    /// neighbouring cell. Each +1 to melee range adds another row. The ability's damage applies to everyone in them.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Sweep", fileName = "SweepEffect")]
    public sealed class SweepEffectDefinition : AbilityEffectDefinition
    {
        [Min(1)] public int rows = 1;
    }
}