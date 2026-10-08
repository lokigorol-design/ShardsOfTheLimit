using ShardsOfTheLimit.Domain;
using UnityEngine;
namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Branch choices and passives of one resonant (design 2.5 cards). Enable the options of the chosen branch;
    /// no runtime state in this asset. Stat multipliers are the branch numbers from the card.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Passive Modifiers")]
    public sealed class PassiveModifierDefinition : ScriptableObject
    {
        [Tooltip("Shown in the HUD, e.g. \"Magma Worm · Blood of the Deep\".")]
        public string variantName;

        [Header("Branch stat changes (e.g. HP ×1,15)")]
        [Min(0.1f)] public float healthMultiplier = 1f;
        [Min(0.1f)] public float attackMultiplier = 1f;
        [Min(0.1f)] public float defenseMultiplier = 1f;
        public float speedBonus;
        public int movementBonus;

        [Header("Blight Gullet IV — Native Mire: immune to negative effects of any cloud; heal % at turn start in a cloud")]
        public bool nativeMire;
        [Min(0)] public int nativeMireHealPercent = 5;
        [Header("Blight Gullet IV — Toxic Cocktail: own clouds apply more stacks")]
        public bool toxicCocktail;
        [Min(0)] public float cloudStacksMultiplier = 2;
        public int cloudStacksBonus;
        [Header("Blight Gullet IV — Sticky Mud: Modified Ability applies Sticky Mud Status instead of Slow")]
        public bool stickyMud;
        public AbilityDefinition modifiedAbility;
        public StatusDefinition stickyMudStatus;

        [Header("Fire Worm II — Magma Worm branch: Magma Trail replaces Deep Burrow, Burning becomes Magma")]
        public bool magmaWorm;
        [Header("Fire Worm IV — Banked Heat: unused AP at turn end gives +1 AP next turn")]
        public bool bankedHeat;
        [Header("Fire Worm IV — Roaring Furnace: Flame Burst jumps to full power on its second turn")]
        public bool roaringFurnace;
        [Header("Magma Worm IV — Blood of the Deep: heal % at turn start on Magma")]
        public bool bloodOfTheDeep;
        [Min(0)] public int bloodOfTheDeepHealPercent = 8;

        [Header("Primal Idol IV — Twin Eruption: Fire Geyser erupts again 2 turns later")]
        public bool twinEruption;
        [Header("Primal Idol IV — Storm Overcharge: Storm Magnet reaches 3 cells, Immobilize 2 turns")]
        public bool stormOvercharge;
        [Header("Primal Idol IV — Titan Pillar: Earth Pillar +3 instead of +2, ledge pushes 2 cells")]
        public bool titanPillar;
        [Header("Primal Idol IV — Raging Tornado: Lift Trap throws 6 units instead of 4, pushes 3 cells instead of 2")]
        public bool ragingTornado;

        public PassiveModifiers Snapshot() => new PassiveModifiers(toxicCocktail ? cloudStacksMultiplier : 1,
            toxicCocktail ? cloudStacksBonus : 0, nativeMire, nativeMireHealPercent,
            bloodOfTheDeep ? bloodOfTheDeepHealPercent : 0);
    }
}