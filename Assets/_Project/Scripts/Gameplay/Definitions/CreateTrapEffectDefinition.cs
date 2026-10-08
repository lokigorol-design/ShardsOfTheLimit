using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Tag «Ловушка»: a trap on the chosen floor cell (Lift Trap). Fires on the first resonant entering it.</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Create Trap", fileName = "CreateTrapEffect")]
    public sealed class CreateTrapEffectDefinition : AbilityEffectDefinition
    {
        [Min(1)] public int lifetimeTicks = 300;
        [Tooltip("Floor Lift Trap throws the victim 4 units up; the fall costs this % of maximum health.")]
        [Min(0)] public int fallHealthPercent = 10;
    }
}