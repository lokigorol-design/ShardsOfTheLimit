using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
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

        [Header("Direct prototype stats")]
        [SerializeField, Min(1)] private int maximumHealth = 10;
        [SerializeField, Min(0)] private int maximumShield = 0;
        [SerializeField] private int initiative = 10;
        [SerializeField, Min(0)] private int movementRange = 3;
        [SerializeField] private List<AbilityDefinition> abilities = new List<AbilityDefinition>();
        [SerializeField] private PassiveModifierDefinition passiveModifiers;
        public PassiveModifierDefinition PassiveModifiers => passiveModifiers;

        public string StableId => stableId;
        public string DisplayName => displayName;
        public Sprite Portrait => portrait;
        public GameObject VisualPrefab => visualPrefab;
        public Material VisualMaterial => visualMaterial;
        public Vector3 VisualRotationOffset => visualRotationOffset;
        public int MaximumHealth => maximumHealth;
        public int MaximumShield => maximumShield;
        public int Initiative => initiative;
        public int MovementRange => movementRange;
        public IReadOnlyList<AbilityDefinition> Abilities => abilities != null
            ? abilities
            : Array.Empty<AbilityDefinition>();

        public IEnumerable<string> GetValidationErrors()
        {
            if (string.IsNullOrWhiteSpace(stableId)) yield return "Stable Id is empty.";
            if (maximumHealth <= 0) yield return "Maximum Health must be positive.";
            if (maximumShield < 0) yield return "Maximum Shield cannot be negative.";
            if (movementRange < 0) yield return "Movement Range cannot be negative.";
            if (abilities == null) yield return "Abilities list is missing.";
            else if (abilities.Exists(ability => ability == null)) yield return "Abilities list contains a missing reference.";
            if (visualPrefab == null) yield return "Visual Prefab is missing; the prototype will use a primitive fallback.";
        }
    }
}
