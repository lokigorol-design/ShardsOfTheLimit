using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ability icons v3 (grayscale masters colored per resonant at runtime) and the Mist Panther model.
/// Run: Unity -batchmode -projectPath ... -executeMethod IconAndPantherSetup.Create -quit
/// Imports masters and the frame, creates the palettes (values from palettes.json of the icon pipeline), assigns icons
/// and palettes to units, abilities, branch variants and statuses, and builds the Mist Panther prefab.
/// Old icons are kept; statuses without a new master keep their icons.
/// </summary>
public static class IconAndPantherSetup
{
    private const string Masters = "Assets/_Project/Art/Icons/Abilities_v3/";
    private const string FramePath = "Assets/_Project/Art/Icons/Frame/frame_256.png";
    private const string Root = "Assets/_Project/Resources/Prototype/";
    private const string Palettes = Root + "Palettes/";
    private const string PantherModels = "Assets/_Project/Art/Models/MistPanther/";
    private const string PantherPortrait = "Assets/_Project/Art/Portraits/MistPanther/MistPanther_Portrait.png";

    public static readonly List<string> Report = new List<string>();

    public static void Create()
    {
        Report.Clear();
        ImportTextures();
        Dictionary<string, ResonantPalette> palettes = CreatePalettes();
        CreateMasterSet(palettes);
        AssignUnits(palettes);
        AssignAbilities(palettes);
        AssignBranches(palettes);
        AssignStatuses(palettes);
        CreatePantherModel();
        AssetDatabase.SaveAssets();
        foreach (string line in Report) Debug.Log("[IconAndPantherSetup] " + line);
        Debug.Log("[IconAndPantherSetup] Done.");
    }

    private static void ImportTextures()
    {
        foreach (string file in Directory.GetFiles(Masters, "*.png"))
            Configure(file.Replace('\\', '/'), readable: true);
        Configure(FramePath, readable: false);
        Configure("Assets/_Project/Art/Icons/Frame/frame_128.png", readable: false);
        if (AssetImporter.GetAtPath(PantherPortrait) is TextureImporter portrait)
        {
            portrait.textureType = TextureImporterType.Sprite;
            portrait.spriteImportMode = SpriteImportMode.Single;
            portrait.maxTextureSize = 512;
            portrait.SaveAndReimport();
        }
    }

    /// <summary>Sprite (2D and UI), Single, no mipmaps, max 512, lossless; masters stay readable for the soft mask.</summary>
    private static void Configure(string path, bool readable)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 512;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = true;
        importer.isReadable = readable;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    private static Sprite Master(string name)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Masters + name + ".png");
        if (sprite == null) Report.Add($"missing master {name}");
        return sprite;
    }

    private static Dictionary<string, ResonantPalette> CreatePalettes()
    {
        Directory.CreateDirectory(Palettes);
        var definitions = new Dictionary<string, (string[] stops, string tint, float strength)>
        {
            ["_background"] = (new[] { "0.0 #03100f", "0.2 #0b3433", "0.45 #145a58", "0.7 #228580", "1.0 #8fe0d4" }, "#000000", 0f),
            ["neutral"] = (new[] { "0.0 #060606", "0.3 #2a2a2e", "0.65 #8c8a86", "1.0 #fbfaf6" }, "#202024", 0f),
            ["fire"] = (new[] { "0.0 #0a0302", "0.2 #3a0c06", "0.4 #8a1a0c", "0.58 #d8401a", "0.74 #ff7a22", "0.88 #ffc24a", "1.0 #fff4d0" }, "#0f3d3c", 0.12f),
            ["plant"] = (new[] { "0.0 #070502", "0.2 #2a1c0c", "0.38 #5a3c18", "0.56 #4e7a1c", "0.74 #86c034", "0.88 #c8ec6a", "1.0 #f4ffd8" }, "#0f3d3c", 0.12f),
            ["poison"] = (new[] { "0.0 #040806", "0.2 #14281a", "0.4 #2e5420", "0.6 #6aa81a", "0.78 #b4e830", "0.9 #eaff6a", "1.0 #fbffd8" }, "#0f3d3c", 0.12f),
            ["earth"] = (new[] { "0.0 #070503", "0.2 #2e1c0a", "0.42 #704616", "0.62 #b8802a", "0.8 #e4b452", "0.92 #f4dc9a", "1.0 #fff8e4" }, "#0f3d3c", 0.12f),
            ["mist"] = (new[] { "0.0 #06050a", "0.2 #1e1830", "0.42 #4a3e6e", "0.64 #8a82b2", "0.84 #d2cfea", "1.0 #fbfbff" }, "#0f3d3c", 0.12f),
            ["blood"] = (new[] { "0.0 #080204", "0.2 #2a0810", "0.42 #68101e", "0.6 #a81a2c", "0.8 #dc3a48", "0.93 #f8a6aa", "1.0 #fff0f0" }, "#0f3d3c", 0.12f),
            ["lightning"] = (new[] { "0.0 #03040c", "0.2 #101848", "0.4 #2a2e9a", "0.6 #5a52e6", "0.78 #9a92ff", "0.9 #d6d4ff", "1.0 #ffffff" }, "#0f3d3c", 0.12f),
            ["water"] = (new[] { "0.0 #02060a", "0.2 #08243a", "0.42 #14507a", "0.62 #2a8ac0", "0.8 #6ac4f0", "0.92 #b8ecff", "1.0 #f4fdff" }, "#0f3d3c", 0.12f)
        };
        var result = new Dictionary<string, ResonantPalette>();
        foreach (var pair in definitions)
        {
            string path = Palettes + pair.Key + ".asset";
            var palette = AssetDatabase.LoadAssetAtPath<ResonantPalette>(path);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<ResonantPalette>();
                AssetDatabase.CreateAsset(palette, path);
            }

            palette.paletteName = pair.Key;
            palette.effectStops = pair.Value.stops.Select(ParseStop).ToList();
            palette.backgroundStops = new List<ResonantPalette.Stop>();
            palette.tintColor = ParseHex(pair.Value.tint);
            palette.tintStrength = pair.Value.strength;
            palette.maskLow = 0.16f;
            palette.maskHigh = 0.42f;
            EditorUtility.SetDirty(palette);
            result[pair.Key] = palette;
        }

        return result;
    }

    private static ResonantPalette.Stop ParseStop(string text)
    {
        string[] parts = text.Split(' ');
        return new ResonantPalette.Stop(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), ParseHex(parts[1]));
    }

    private static Color32 ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return new Color32(System.Convert.ToByte(hex.Substring(0, 2), 16), System.Convert.ToByte(hex.Substring(2, 2), 16),
            System.Convert.ToByte(hex.Substring(4, 2), 16), 255);
    }

    private static void CreateMasterSet(Dictionary<string, ResonantPalette> palettes)
    {
        string path = Palettes + "IconMasters.asset";
        var set = AssetDatabase.LoadAssetAtPath<IconMasterSet>(path);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<IconMasterSet>();
            AssetDatabase.CreateAsset(set, path);
        }

        set.masters = Directory.GetFiles(Masters, "*.png").OrderBy(file => file)
            .Select(file => AssetDatabase.LoadAssetAtPath<Sprite>(file.Replace('\\', '/'))).Where(sprite => sprite != null).ToList();
        set.frame = AssetDatabase.LoadAssetAtPath<Texture2D>(FramePath);
        set.background = palettes["_background"];
        set.neutral = palettes["neutral"];
        EditorUtility.SetDirty(set);
        Report.Add($"masters: {set.masters.Count}, frame: {(set.frame != null ? "ok" : "MISSING")}");
    }

    private static void AssignUnits(Dictionary<string, ResonantPalette> palettes)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:UnitDefinition", new[] { Root + "Units" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var unit = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);
            string palette = unit.StableId switch
            {
                "blaze-worm" => "fire",
                "mist-panther" => "mist",
                "primal-idol" => "earth",
                "blightgullet" => "poison",
                _ => "neutral"
            };
            Set(unit, "palette", palettes[palette]);
            if (unit.StableId == "blaze-worm") Set(unit, "movementIcon", Master("deep_burrow"));
            if (unit.StableId == "primal-idol") Set(unit, "passiveIcon", Master("ritual_surfaces"));
            Report.Add($"unit {Path.GetFileNameWithoutExtension(path)} → palette {palette}");
        }
    }

    private static void AssignAbilities(Dictionary<string, ResonantPalette> palettes)
    {
        var map = new Dictionary<string, string>
        {
            ["BlazeFierySpit"] = "fire_spit",
            ["BlazeFieryVolley"] = "fiery_volley",
            ["BlazeLongBite"] = "long_bite",
            ["FlameBurst"] = "flame_burst",
            ["PurifyingFlame"] = "purifying_flame",
            ["WideSwipe"] = "wide_swipe",
            ["VanishingStep"] = "vanishing_step",
            ["PartingStrike"] = "parting_strike",
            ["BloodDash"] = "blood_dash",
            ["EarthPillar"] = "earth_pillar",
            ["FireGeyser"] = "fire_geyser",
            ["LiftTrap"] = "lift_trap",
            ["StormMagnet"] = "storm_magnet"
        };
        foreach (var pair in map)
        {
            var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Root + "Abilities/" + pair.Key + ".asset");
            if (ability == null)
            {
                Report.Add($"missing ability {pair.Key}");
                continue;
            }

            Set(ability, "icon", Master(pair.Value));
            Set(ability, "paletteOverride", pair.Key == "BloodDash" ? palettes["blood"] : null);
            Report.Add($"ability {pair.Key} ← {pair.Value}" + (pair.Key == "BloodDash" ? " (blood)" : string.Empty));
        }
    }

    private static void AssignBranches(Dictionary<string, ResonantPalette> palettes)
    {
        void Branch(string asset, string icon, string secondary = null, string secondaryPalette = null)
        {
            var variant = AssetDatabase.LoadAssetAtPath<PassiveModifierDefinition>(Root + "Passives/" + asset + ".asset");
            if (variant == null)
            {
                Report.Add($"missing branch {asset}");
                return;
            }

            variant.icon = Master(icon);
            variant.secondaryIcon = secondary != null ? Master(secondary) : null;
            variant.paletteOverride = null;
            variant.secondaryPaletteOverride = secondaryPalette != null ? palettes[secondaryPalette] : null;
            EditorUtility.SetDirty(variant);
            Report.Add($"branch {asset} ← {icon}" + (secondary != null ? $" + {secondary}" : string.Empty) +
                       (secondaryPalette != null ? $" ({secondaryPalette})" : string.Empty));
        }

        Branch("FireWorm_BankedHeat", "banked_heat");
        Branch("FireWorm_RoaringFurnace", "roaring_furnace");
        Branch("MagmaWorm_BankedHeat", "banked_heat", "magma_trail");
        Branch("MagmaWorm_BloodOfTheDeep", "blood_of_the_deep", "magma_trail");
        Branch("MistPanther_Mist", "mist_veil", "deep_wounds", "blood");
        Branch("MistPanther_Shadow", "return_to_shadow", "silent_executioner");
        Branch("PrimalIdol_TwinEruption", "twin_eruption");
        Branch("PrimalIdol_StormOvercharge", "storm_overcharge");
        Branch("PrimalIdol_TitanPillar", "titan_pillar");
        Branch("PrimalIdol_RagingTornado", "raging_tornado");
    }

    private static void AssignStatuses(Dictionary<string, ResonantPalette> palettes)
    {
        void Status(string asset, string id, StatusMechanic mechanic, string icon, string palette)
        {
            string path = Root + "Statuses/" + asset + ".asset";
            var status = AssetDatabase.LoadAssetAtPath<StatusDefinition>(path);
            if (status == null)
            {
                // Icon carrier for a status that the rules create in code (Mist, Vulnerability, Magma).
                status = ScriptableObject.CreateInstance<StatusDefinition>();
                status.stableId = id;
                status.mechanic = mechanic;
                status.maximumStacks = 1;
                status.durationTicks = 200;
                AssetDatabase.CreateAsset(status, path);
            }

            status.icon = Master(icon);
            status.paletteOverride = palettes[palette];
            EditorUtility.SetDirty(status);
            Report.Add($"status {asset} ← {icon} ({palette})");
        }

        Status("Bleed", "BLEED", StatusMechanic.Bleed, "status_bleed", "blood");
        Status("Invisibility", StealthRules.InvisibilityId, StatusMechanic.Invisibility, "status_invisibility", "mist");
        Status("Mist", StealthRules.MistId, StatusMechanic.Mist, "status_mist", "mist");
        Status("Vulnerability", StealthRules.VulnerabilityId, StatusMechanic.Vulnerability, "status_vulnerability", "blood");
        Status("Magma", AreaEffectWorld.MagmaResistanceId, StatusMechanic.None, "status_magma", "fire");

        var burn = AssetDatabase.LoadAssetAtPath<ApplyBurnEffectDefinition>(Root + "Effects/ApplyBurnTenPercent.asset");
        if (burn != null)
        {
            Set(burn, "icon", Master("status_burning"));
            Report.Add("burning (ApplyBurnTenPercent.icon) ← status_burning (fire)");
        }
    }

    /// <summary>Mist Panther: URP Lit material from the Meshy maps and a prefab scaled to a 0.95-cell footprint.</summary>
    private static void CreatePantherModel()
    {
        SetTexture(PantherModels + "MistPanther_Normal.png", importer => importer.textureType = TextureImporterType.NormalMap);
        SetTexture(PantherModels + "MistPanther_Metallic.png", importer => { importer.isReadable = true; importer.sRGBTexture = false; });
        SetTexture(PantherModels + "MistPanther_Roughness.png", importer => { importer.isReadable = true; importer.sRGBTexture = false; });
        string packed = PantherModels + "MistPanther_MetallicSmoothness.png";
        if (!File.Exists(packed))
        {
            var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(PantherModels + "MistPanther_Metallic.png");
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(PantherModels + "MistPanther_Roughness.png");
            var output = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, false, true);
            Color[] m = metallic.GetPixels();
            Color[] r = roughness.width == metallic.width && roughness.height == metallic.height ? roughness.GetPixels() : m;
            var pixels = new Color[m.Length];
            for (int index = 0; index < m.Length; index++)
                pixels[index] = new Color(m[index].r, m[index].r, m[index].r, 1f - r[index].r);
            output.SetPixels(pixels);
            File.WriteAllBytes(packed, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(packed);
            SetTexture(packed, importer => importer.sRGBTexture = false);
        }

        string materialPath = PantherModels + "MistPanther.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "MistPanther" };
            var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(PantherModels + "MistPanther_BaseColor.png");
            material.SetTexture("_BaseMap", baseColor);
            material.SetTexture("_MainTex", baseColor);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PantherModels + "MistPanther_Normal.png"));
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packed));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        string prefabPath = PantherModels + "MistPanther.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PantherModels + "MistPanther.fbx");
            // The battle resets the prefab root's scale, so the scaled model lives under a neutral wrapper.
            var wrapper = new GameObject("MistPanther");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
            Bounds bounds = instance.GetComponentsInChildren<Renderer>().Select(renderer => renderer.bounds)
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });
            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = Mathf.Min(0.95f / Mathf.Max(0.001f, footprint), 1.2f / Mathf.Max(0.001f, bounds.size.y));
            instance.transform.localScale *= scale;
            prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
            Object.DestroyImmediate(wrapper);
            Report.Add($"panther model bounds {bounds.size}, scale {scale}");
        }

        var unit = AssetDatabase.LoadAssetAtPath<UnitDefinition>(Root + "Units/MistPanther.asset");
        Set(unit, "visualPrefab", prefab);
        Set(unit, "visualMaterial", material);
        Set(unit, "portrait", AssetDatabase.LoadAssetAtPath<Sprite>(PantherPortrait));
    }

    private static void SetTexture(string path, System.Action<TextureImporter> configure)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        configure(importer);
        importer.SaveAndReimport();
    }

    private static void Set(Object target, string name, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
        {
            Report.Add($"{target.name}: no property {name}");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
