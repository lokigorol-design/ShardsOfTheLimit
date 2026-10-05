using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Modify Terrain", fileName = "ModifyTerrainEffect")]
    public sealed class ModifyTerrainEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private int heightDelta = 1;
        [SerializeField, Min(0)] private int minimumHeight = 0;
        [SerializeField, Min(0)] private int maximumHeight = 3;

        public int HeightDelta => heightDelta;
        public int MinimumHeight => minimumHeight;
        public int MaximumHeight => maximumHeight;
    }
}
