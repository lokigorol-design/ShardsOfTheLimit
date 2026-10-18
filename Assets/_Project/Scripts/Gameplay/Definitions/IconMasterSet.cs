using System.Collections.Generic;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// The grayscale ability icon masters (Art/Icons/Abilities_v3), the shared gold frame and the shared palettes.
    /// Sprites in this list are colored at runtime by the owner's palette; every other icon is drawn as it is.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Icon Master Set", fileName = "IconMasters")]
    public sealed class IconMasterSet : ScriptableObject
    {
        public List<Sprite> masters = new List<Sprite>();
        public Texture2D frame;
        public ResonantPalette background;
        public ResonantPalette neutral;
    }
}