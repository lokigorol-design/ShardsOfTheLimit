using ShardsOfTheLimit.Domain;
using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Blight Gullet IV second passive (design 2.5, 10.10.3). Enable one; no runtime state in this asset.</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Passive Modifiers")]
    public sealed class PassiveModifierDefinition : ScriptableObject
    {
        [Header("Native Mire — immune to negative effects of any cloud; heal % max HP at turn start in a cloud")]
        public bool nativeMire;
        [Min(0)] public int nativeMireHealPercent = 5;
        [Header("Toxic Cocktail — own clouds apply more stacks")]
        public bool toxicCocktail;
        [Min(0)] public float cloudStacksMultiplier = 2;
        public int cloudStacksBonus;
        [Header("Sticky Mud — Modified Ability applies Sticky Mud Status instead of Slow")]
        public bool stickyMud;
        public AbilityDefinition modifiedAbility;
        public StatusDefinition stickyMudStatus;
        public PassiveModifiers Snapshot() => new PassiveModifiers(toxicCocktail ? cloudStacksMultiplier : 1,
            toxicCocktail ? cloudStacksBonus : 0, nativeMire, nativeMireHealPercent);
    }
}
