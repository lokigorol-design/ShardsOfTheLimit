using System;
using System.Collections.Generic;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    [CreateAssetMenu(menuName = "Shards of the Limit/Ability", fileName = "Ability")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string stableId = "ability";
        [SerializeField] private string displayName = "Ability";
        [SerializeField, TextArea] private string description = string.Empty;
        [SerializeField] private Sprite icon = null;

        [Header("Rules")]
        [SerializeField, Min(0)] private int actionCost = 1;
        [SerializeField, Min(0)] private int cooldown = 1;
        [SerializeField] private AbilityTargetType targetType = AbilityTargetType.Cell;
        [SerializeField] private AbilityTargetTeam targetTeam = AbilityTargetTeam.Any;
        [SerializeField] private AbilityRangeType rangeType = AbilityRangeType.Ranged;
        [SerializeField] private PatternDefinition targetingPattern = null;
        [SerializeField] private PatternDefinition impactPattern = null;
        [SerializeField] private List<AbilityEffectDefinition> effects = new List<AbilityEffectDefinition>();

        [Header("Optional presentation")]
        [SerializeField] private GameObject visualEffectPrefab = null;
        [SerializeField] private AudioClip sound = null;
        [SerializeField] private string animationKey = string.Empty;

        public string StableId => stableId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public int ActionCost => actionCost;
        public int Cooldown => cooldown;
        public AbilityTargetType TargetType => targetType;
        public AbilityTargetTeam TargetTeam => targetTeam;
        public AbilityRangeType RangeType => rangeType;
        public PatternDefinition TargetingPattern => targetingPattern;
        public PatternDefinition ImpactPattern => impactPattern;
        public IReadOnlyList<AbilityEffectDefinition> Effects => effects != null
            ? effects
            : Array.Empty<AbilityEffectDefinition>();
        public GameObject VisualEffectPrefab => visualEffectPrefab;
        public AudioClip Sound => sound;
        public string AnimationKey => animationKey;

        public IEnumerable<string> GetValidationErrors()
        {
            if (string.IsNullOrWhiteSpace(stableId)) yield return "Stable Id is empty.";
            if (targetingPattern == null) yield return "Targeting Pattern is missing.";
            if (impactPattern == null) yield return "Impact Pattern is missing.";
            if (effects == null || effects.Count == 0) yield return "At least one effect is required.";
            else if (effects.Exists(effect => effect == null)) yield return "Effects list contains a missing reference.";
            if (effects != null)
                foreach (var effect in effects)
                {
                    if (effect is CreateCloudEffectDefinition create &&
                        (create.cloud == null || create.cloud.status == null || create.cloud.areaShape == null))
                        yield return "Cloud effect requires a cloud, status and area shape.";
                    if (effect is ApplyStatusEffectDefinition status && status.status == null)
                        yield return "Apply Status requires a global status definition.";
                }
        }
    }
}
