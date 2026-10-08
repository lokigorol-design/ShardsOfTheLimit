using System;
using System.Collections.Generic;
using ShardsOfTheLimit.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace ShardsOfTheLimit.Definitions
{
    [Serializable]
    public struct PeriodicResistance
    {
        [Tooltip("Status id of the periodic source: BURNING, POISON, MAGMA...")] public string sourceId;
        [Tooltip("Percent; 100 = immune.")] public int percent;
    }

    [CreateAssetMenu(menuName = "Shards of the Limit/Unit", fileName = "Unit")]
    public sealed class UnitDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string stableId = "unit";
        [SerializeField] private string displayName = "Echo";
        [SerializeField] private Sprite portrait = null;
        [SerializeField] private GameObject visualPrefab = null;
        [SerializeField] private Material visualMaterial = null;
        [SerializeField] private Vector3 visualRotationOffset = Vector3.zero;

        [Header("Stats (design 2.5, rank I.1)")]
        [SerializeField, Min(1)] private int maximumHealth = 100;
        [SerializeField, Min(0)] private int maximumShield = 50;
        [SerializeField, Min(0), Tooltip("ATK. Ability damage is a multiplier of it.")] private int attack = 17;
        [SerializeField, Tooltip("Defense against direct hits: A² / (A + Defense).")] private int defense = 3;
        [SerializeField, Min(1), Tooltip("Turn interval = 1000 / Speed ticks (30..200). Speed 10 = every 100 ticks.")]
        [FormerlySerializedAs("initiative")] private int speed = 10;
        [SerializeField, Min(0)] private int movementRange = 3;
        [SerializeField, Tooltip("Walk, or Burrow (Deep Burrow: emerge within Movement × 3 cells).")]
        private MovementMode movementMode = MovementMode.Walk;
        [SerializeField, Min(1), Tooltip("AP one move costs; Deep Burrow costs 2.")] private int movementActionCost = 1;
        [SerializeField, Tooltip("\"Защита от периодического урона\" per source, e.g. BURNING 50.")]
        private List<PeriodicResistance> periodicResistances = new List<PeriodicResistance>();
        [SerializeField] private List<AbilityDefinition> abilities = new List<AbilityDefinition>();
        [SerializeField] private PassiveModifierDefinition passiveModifiers;
        public PassiveModifierDefinition PassiveModifiers => passiveModifiers;
        [SerializeField, Tooltip("Branch variants (II/IV choices) selectable before a battle; the first is the default.")]
        private List<PassiveModifierDefinition> branchVariants = new List<PassiveModifierDefinition>();
        public IReadOnlyList<PassiveModifierDefinition> BranchVariants => branchVariants != null
            ? branchVariants
            : (IReadOnlyList<PassiveModifierDefinition>)Array.Empty<PassiveModifierDefinition>();

        public string StableId => stableId;
        public string DisplayName => displayName;
        public Sprite Portrait => portrait;
        public GameObject VisualPrefab => visualPrefab;
        public Material VisualMaterial => visualMaterial;
        public Vector3 VisualRotationOffset => visualRotationOffset;
        public int MaximumHealth => maximumHealth;
        public int MaximumShield => maximumShield;
        public int Attack => attack;
        public int Defense => defense;
        public int Speed => speed;
        public int MovementRange => movementRange;
        public MovementMode MovementMode => movementMode;
        public int MovementActionCost => Mathf.Max(1, movementActionCost);
        public IReadOnlyList<PeriodicResistance> PeriodicResistances => periodicResistances != null
            ? periodicResistances
            : (IReadOnlyList<PeriodicResistance>)Array.Empty<PeriodicResistance>();
        public IReadOnlyList<AbilityDefinition> Abilities => abilities != null
            ? abilities
            : Array.Empty<AbilityDefinition>();

        public IEnumerable<string> GetValidationErrors()
        {
            if (string.IsNullOrWhiteSpace(stableId)) yield return "Stable Id is empty.";
            if (maximumHealth <= 0) yield return "Maximum Health must be positive.";
            if (maximumShield < 0) yield return "Maximum Shield cannot be negative.";
            if (movementRange < 0) yield return "Movement Range cannot be negative.";
            if (attack < 0) yield return "Attack cannot be negative.";
            if (speed <= 0) yield return "Speed must be positive.";
            if (abilities == null) yield return "Abilities list is missing.";
            else if (abilities.Exists(ability => ability == null)) yield return "Abilities list contains a missing reference.";
            if (visualPrefab == null) yield return "Visual Prefab is missing; the prototype will use a primitive fallback.";
        }
    }
}
