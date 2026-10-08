using System.IO;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Completes the three resonants to their design 2.5 cards: Fire Worm II/IV abilities (Purifying Flame, Flame Burst)
/// and the branch variants (II/IV passives with their stat numbers) of the worm, the toad and Primal Idol.
/// Run explicitly: Unity -batchmode -projectPath ... -executeMethod ResonantBranchesSetup.Create -quit
/// Existing assets are kept; unit ability lists and variant lists are (re)assigned.
/// </summary>
public static class ResonantBranchesSetup
{
    private const string Root = "Assets/_Project/Resources/Prototype/";
    private const string WormIcons = "Assets/_Project/Art/Icons/BlazeWorm/";

    public static void Create()
    {
        CreatePlaceholderIcons();
        AssetDatabase.Refresh();
        foreach (string icon in new[] { "Icon_FireWorm_PurifyingFlame", "Icon_FireWorm_FlameBurst" })
            if (AssetImporter.GetAtPath(WormIcons + icon + ".png") is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }

        var single = AssetDatabase.LoadAssetAtPath<PatternDefinition>(Root + "Patterns/SingleImpact.asset");
        var self = Asset<RadiusPatternDefinition>("Patterns/SelfTargeting", pattern =>
        {
            Set(pattern, "radius", 0);
            Set(pattern, "includeCenter", true);
        });
        var adjacent = Asset<RadiusPatternDefinition>("Patterns/AdjacentTargeting", pattern =>
        {
            Set(pattern, "radius", 1);
            Set(pattern, "includeCenter", false);
        });

        var purifyingStatus = Asset<StatusDefinition>("Statuses/PurifyingFlame", status =>
        {
            status.stableId = "PURIFYING_FLAME";
            status.mechanic = StatusMechanic.PurifyingFlame;
            status.maximumStacks = 1;
            status.durationTicks = 300;
            status.pulseInterval = 100;
            status.icon = AssetDatabase.LoadAssetAtPath<Sprite>(WormIcons + "Icon_FireWorm_PurifyingFlame.png");
        });
        var purifyingEffect = Asset<ApplyStatusEffectDefinition>("Effects/PurifyingFlameApply", effect =>
        {
            effect.status = purifyingStatus;
            effect.stacks = 1;
        });
        var cone = Asset<ChannelConeEffectDefinition>("Effects/FlameBurstCone", effect => { });

        var purifyingFlame = Ability("PurifyingFlame", "Purifying Flame",
            "Buff, self. For 3 turns: at the start of each own turn the worm cleanses every negative effect; " +
            "whoever hits it with a Melee ability catches fire (0,25 × worm ATK).",
            "Icon_FireWorm_PurifyingFlame", 1, 5, AbilityTargetType.Unit, AbilityTargetTeam.Allies, AbilityRangeType.Melee,
            self, single, purifyingEffect);
        var flameBurst = Ability("FlameBurst", "Flame Burst",
            "Ranged, Channelled. Launch 1 AP towards a neighbouring cell; extend for 2 AP on each next turn. Cone 1-3 → 1-3-3 → 1-3-3-5-5; " +
            "everything in the cone and everything entering it takes 0,8 → 1,0 → 1,2 × ATK and Burning. While it lasts the worm " +
            "neither moves nor uses other abilities. Cooldown 3 after it ends.",
            "Icon_FireWorm_FlameBurst", 1, 0, AbilityTargetType.Cell, AbilityTargetTeam.Any, AbilityRangeType.Ranged,
            adjacent, single, cone);

        var worm = AssetDatabase.LoadAssetAtPath<UnitDefinition>(Root + "Units/BlazeWorm.asset");
        var bite = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Root + "Abilities/BlazeLongBite.asset");
        var spit = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Root + "Abilities/BlazeFierySpit.asset");
        SetArray(worm, "abilities", bite, spit, purifyingFlame, flameBurst);
        SetArray(worm, "branchVariants",
            Variant("FireWorm_BankedHeat", "Fire Worm · Banked Heat", v => { v.attackMultiplier = 1.15f; v.defenseMultiplier = 0.9f; v.bankedHeat = true; }),
            Variant("FireWorm_RoaringFurnace", "Fire Worm · Roaring Furnace", v => { v.healthMultiplier = 1.1f; v.speedBonus = -0.5f; v.roaringFurnace = true; }),
            Variant("MagmaWorm_BankedHeat", "Magma Worm · Banked Heat", v =>
            {
                MagmaBase(v);
                v.attackMultiplier = 1.1f * 1.1f;
                v.speedBonus = 0.5f;
                v.bankedHeat = true;
            }),
            Variant("MagmaWorm_BloodOfTheDeep", "Magma Worm · Blood of the Deep", v =>
            {
                MagmaBase(v);
                v.healthMultiplier = 1.1f * 1.15f;
                v.attackMultiplier = 1.1f * 0.9f;
                v.bloodOfTheDeep = true;
            }));

        var idol = AssetDatabase.LoadAssetAtPath<UnitDefinition>(Root + "Units/PrimalIdol.asset");
        SetArray(idol, "branchVariants",
            Variant("PrimalIdol_TwinEruption", "Primal Idol · Twin Eruption", v => { v.attackMultiplier = 1.1f; v.healthMultiplier = 0.95f; v.twinEruption = true; }),
            Variant("PrimalIdol_StormOvercharge", "Primal Idol · Storm Overcharge", v => { v.speedBonus = 0.5f; v.defenseMultiplier = 0.9f; v.stormOvercharge = true; }),
            Variant("PrimalIdol_TitanPillar", "Primal Idol · Titan Pillar", v => { v.healthMultiplier = 1.1f; v.defenseMultiplier = 1.1f; v.titanPillar = true; }),
            Variant("PrimalIdol_RagingTornado", "Primal Idol · Raging Tornado", v => { v.movementBonus = 1; v.healthMultiplier = 0.95f; v.ragingTornado = true; }));

        var toad = AssetDatabase.LoadAssetAtPath<UnitDefinition>(Root + "Units/Blightgullet.asset");
        var toadBase = AssetDatabase.LoadAssetAtPath<PassiveModifierDefinition>(Root + "Passives/BlightgulletBranches.asset");
        SetArray(toad, "branchVariants",
            Variant("Blightgullet_NativeMire", "Blightgullet · Native Mire", v => { v.healthMultiplier = 1.15f; v.defenseMultiplier = 1.1f; v.nativeMire = true; }),
            Variant("Blightgullet_ToxicCocktail", "Blightgullet · Toxic Cocktail", v => { v.attackMultiplier = 1.1f; v.healthMultiplier = 0.95f; v.toxicCocktail = true; }),
            Variant("Blightgullet_StickyMud", "Blightgullet · Sticky Mud", v =>
            {
                v.speedBonus = 0.5f;
                v.defenseMultiplier = 0.9f;
                v.stickyMud = true;
                v.modifiedAbility = toadBase != null ? toadBase.modifiedAbility : null;
                v.stickyMudStatus = toadBase != null ? toadBase.stickyMudStatus : null;
            }));

        AssetDatabase.SaveAssets();
        Debug.Log("[ResonantBranchesSetup] Worm abilities and branch variants of worm, toad and idol ready.");
    }

    /// <summary>Magma Worm II: Magma Trail, HP ×1,1, ATK ×1,1, Defense ×0,9, Movement +1.</summary>
    private static void MagmaBase(PassiveModifierDefinition v)
    {
        v.magmaWorm = true;
        v.healthMultiplier = 1.1f;
        v.attackMultiplier = 1.1f;
        v.defenseMultiplier = 0.9f;
        v.movementBonus = 1;
    }

    private static PassiveModifierDefinition Variant(string id, string name, System.Action<PassiveModifierDefinition> configure) =>
        Asset<PassiveModifierDefinition>("Passives/" + id, variant =>
        {
            variant.variantName = name;
            configure(variant);
        });

    private static void CreatePlaceholderIcons()
    {
        Directory.CreateDirectory(WormIcons);
        Icon("Icon_FireWorm_PurifyingFlame", new Color(1f, 0.85f, 0.45f), (u, v) =>
        {
            float radius = Mathf.Sqrt(u * u + v * v);
            return radius > 0.55f && radius < 0.72f || Mathf.Abs(u) < 0.12f + (0.5f - v) * 0.12f && v > -0.45f && v < 0.5f;
        });
        Icon("Icon_FireWorm_FlameBurst", new Color(1f, 0.38f, 0.08f), (u, v) =>
            u > -0.7f && u < 0.75f && Mathf.Abs(v) < 0.1f + (u + 0.7f) * 0.38f && Mathf.Repeat((u + 0.7f) * 4f, 1f) < 0.8f);
    }

    private static void Icon(string name, Color color, System.Func<float, float, bool> glyph)
    {
        string path = WormIcons + name + ".png";
        if (File.Exists(path)) return;
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size * 2f - 1f;
            float v = y / (float)size * 2f - 1f;
            float vignette = Mathf.Clamp01(1f - (u * u + v * v) * 0.6f);
            Color background = Color.Lerp(new Color(0.07f, 0.05f, 0.04f), color * 0.35f, vignette * 0.8f);
            bool on = glyph(u, v);
            bool glow = !on && (glyph(u + 0.04f, v) || glyph(u - 0.04f, v) || glyph(u, v + 0.04f) || glyph(u, v - 0.04f));
            Color pixel = on ? Color.Lerp(color, Color.white, 0.25f) : glow ? Color.Lerp(background, color, 0.6f) : background;
            pixel.a = 1f;
            texture.SetPixel(x, y, pixel);
        }

        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    private static T Asset<T>(string relative, System.Action<T> configure) where T : ScriptableObject
    {
        string path = Root + relative + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        configure(asset);
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static AbilityDefinition Ability(string id, string displayName, string description, string icon, int actionCost,
        int cooldown, AbilityTargetType targetType, AbilityTargetTeam team, AbilityRangeType rangeType,
        PatternDefinition targeting, PatternDefinition impact, params AbilityEffectDefinition[] effects)
    {
        return Asset<AbilityDefinition>("Abilities/" + id, ability =>
        {
            Set(ability, "stableId", id);
            Set(ability, "displayName", displayName);
            Set(ability, "description", description);
            Set(ability, "icon", AssetDatabase.LoadAssetAtPath<Sprite>(WormIcons + icon + ".png"));
            Set(ability, "actionCost", actionCost);
            Set(ability, "cooldown", cooldown);
            Set(ability, "targetType", (int)targetType);
            Set(ability, "targetTeam", (int)team);
            Set(ability, "rangeType", (int)rangeType);
            Set(ability, "targetingPattern", targeting);
            Set(ability, "impactPattern", impact);
            SetArray(ability, "effects", effects);
        });
    }

    private static void Set(Object target, string name, object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
        {
            Debug.LogWarning($"[ResonantBranchesSetup] {target.name}: no property {name}");
            return;
        }

        switch (value)
        {
            case string text: property.stringValue = text; break;
            case bool flag: property.boolValue = flag; break;
            case int number when property.propertyType == SerializedPropertyType.Enum: property.enumValueIndex = number; break;
            case int number: property.intValue = number; break;
            case float real: property.floatValue = real; break;
            default: property.objectReferenceValue = (Object)value; break;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Object target, string name, params Object[] values)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
            property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
