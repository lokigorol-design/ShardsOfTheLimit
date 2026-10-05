using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>Raw hit = Attack Multiplier × source ATK + Flat Amount, then the target's Defense (design 2.5, 16.10.5).</summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Effects/Damage", fileName = "DamageEffect")]
    public sealed class DamageEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField, Min(0), Tooltip("Written in cards as \"1,1 × АТК\".")] private float attackMultiplier = 1f;
        [SerializeField, Min(0), Tooltip("Optional fixed part of the raw hit, independent of ATK.")] private int amount = 0;

        public float AttackMultiplier => attackMultiplier;
        public int Amount => amount;

        public double RawDamage(int sourceAttack) => attackMultiplier * (double)sourceAttack + amount;
    }
}