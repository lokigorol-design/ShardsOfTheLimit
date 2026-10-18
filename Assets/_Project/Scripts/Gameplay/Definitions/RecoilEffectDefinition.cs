using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Parting Strike: after the hit the resonant itself jumps Distance cells straight back, away from the target.</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Recoil", fileName = "RecoilEffect")]
    public sealed class RecoilEffectDefinition : AbilityEffectDefinition
    {
        [Min(1)] public int distance = 1;
    }
}