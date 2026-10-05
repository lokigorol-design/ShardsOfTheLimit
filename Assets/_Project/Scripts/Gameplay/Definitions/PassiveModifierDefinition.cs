using ShardsOfTheLimit.Domain;
using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Passive Modifiers")]
    public sealed class PassiveModifierDefinition : ScriptableObject
    {
        [Header("BALANCE_DEFAULT — enable any branch; no runtime state in this asset")]
        public bool nativeMire;
        [Min(0)] public int defenseBonus = 5;
        [Min(0)] public int healPerActivation = 5;
        public bool toxicCocktail;
        [Min(0)] public float cloudStacksMultiplier = 2;
        public int cloudStacksBonus;
        [Header("On-hit AbilityModifier (Venomous Mud)")]
        public bool venomousMud;
        public AbilityDefinition modifiedAbility;
        public CloudDefinition onHitCloud;
        public PassiveModifiers Snapshot() => new PassiveModifiers(toxicCocktail ? cloudStacksMultiplier : 1,
            toxicCocktail ? cloudStacksBonus : 0, nativeMire, defenseBonus, healPerActivation);
    }
}
