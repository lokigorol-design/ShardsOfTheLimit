using System.IO;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time import of Primal Idol (design 2.5, 10.11): model, URP material, prefab, placeholder icons,
/// effects, abilities and the unit. Run explicitly:
/// Unity -batchmode -projectPath ... -executeMethod PrimalIdolContentSetup.Create -quit
/// Existing assets are kept (balance edited in the Inspector is not overwritten). Placeholder icons use the
/// file names from art-prompts/primal-idol-prompts.md, so dropping the generated PNGs over them keeps every reference.
/// </summary>
public static class PrimalIdolContentSetup
{
    private const string Models = "Assets/_Project/Art/Models/PrimalIdol/";
    private const string Icons = "Assets/_Project/Art/Icons/PrimalIdol/";
    private const string Portrait = "Assets/_Project/Art/Portraits/PrimalIdol/PrimalIdol_Portrait.png";
    private const string Root = "Assets/_Project/Resources/Prototype/";

    public static void Create()
    {
        Directory.CreateDirectory(Icons);
        CreatePlaceholderIcons();
        AssetDatabase.Refresh();
        ConfigureTextures();
        Material material = CreateMaterial();
        GameObject prefab = CreatePrefab(material);

        var immobilized = AssetDatabase.LoadAssetAtPath<StatusDefinition>(Root + "Statuses/Immobilized.asset");
        var single = AssetDatabase.LoadAssetAtPath<PatternDefinition>(Root + "Patterns/SingleImpact.asset");
        var range = AssetDatabase.LoadAssetAtPath<PatternDefinition>(Root + "Patterns/Range4.asset");
        var line = AssetDatabase.LoadAssetAtPath<PatternDefinition>(Root + "Patterns/Line2.asset");

        var magnetPull = Asset<PullToPointEffectDefinition>("Effects/StormMagnetPull", e => e.reach = 2);
        var magnetDamage = Asset<DamageEffectDefinition>("Effects/StormMagnetDamage", e => Set(e, "attackMultiplier", 1f));
        var magnetHold = Asset<ApplyStatusEffectDefinition>("Effects/StormMagnetImmobilize", e => { e.status = immobilized; e.stacks = 1; });
        var geyser = Asset<DelayedStrikeEffectDefinition>("Effects/FireGeyserStrike", e =>
        {
            e.delayTicks = 100;
            e.attackMultiplier = 0.8f;
            e.burnAttackMultiplier = 0.25f;
            e.burnDurationPulses = 3;
        });
        var pillar = Asset<ModifyTerrainEffectDefinition>("Effects/EarthPillarRaise", e =>
        {
            Set(e, "heightDelta", 2);
            Set(e, "minimumHeight", -4);
            Set(e, "maximumHeight", 5);
            Set(e, "liftsOccupant", true);
        });
        var trap = Asset<CreateTrapEffectDefinition>("Effects/LiftTrapCreate", e =>
        {
            e.lifetimeTicks = 300;
            e.fallHealthPercent = 10;
        });

        var stormMagnet = Ability("StormMagnet", "Storm Magnet",
            "Ranged, Surface. Point within 4: the first resonant on a straight line from the point within 2 cells is pulled onto it; 1 x ATK and Immobilize for 1 turn.",
            "Icon_PrimalIdol_StormMagnet", 2, range, line, magnetPull, magnetDamage, magnetHold);
        var fireGeyser = Ability("FireGeyser", "Fire Geyser",
            "Surface, Delayed. Marks a point within 4; after 100 ticks a jet of fire bursts up from it: 0,8 x ATK and Burning. The mark is visible to everyone.",
            "Icon_PrimalIdol_FireGeyser", 2, range, single, geyser);
        var earthPillar = Ability("EarthPillar", "Earth Pillar",
            "Surface, Terrain. Raises a floor cell within 4 by 2 levels together with whoever stands on it. Stays until the end of the battle.",
            "Icon_PrimalIdol_EarthPillar", 3, range, single, pillar);
        var liftTrap = Ability("LiftTrap", "Lift Trap",
            "Surface, Trap. A whirlwind trap on a free floor cell within 4. The first resonant to enter it, allies included, is thrown 4 units up and loses 10% of maximum health on landing. Lives 3 turns.",
            "Icon_PrimalIdol_LiftTrap", 3, range, single, trap);

        var idolUnit = Asset<UnitDefinition>("Units/PrimalIdol", unit =>
        {
            Set(unit, "stableId", "primal-idol");
            Set(unit, "displayName", "Primal Idol");
            Set(unit, "portrait", AssetDatabase.LoadAssetAtPath<Sprite>(Portrait));
            Set(unit, "visualPrefab", prefab);
            Set(unit, "visualMaterial", material);
            Set(unit, "maximumHealth", 90);
            Set(unit, "maximumShield", 60);
            Set(unit, "attack", 17);
            Set(unit, "defense", 3);
            Set(unit, "speed", 10);
            Set(unit, "movementRange", 2);
            SetArray(unit, "abilities", stormMagnet, fireGeyser, earthPillar, liftTrap);
        });
        Set(idolUnit, "visualPrefab", prefab);

        AssetDatabase.SaveAssets();
        Debug.Log("[PrimalIdolContentSetup] Primal Idol content ready.");
    }

    public static void Diagnose()
    {
        foreach (string path in new[] { Models + "PrimalIdol.fbx", Models + "PrimalIdol.prefab" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                Debug.Log($"[IdolDiag] {path} {filter.name} mesh={(filter.sharedMesh == null ? "NULL" : filter.sharedMesh.name + " v" + filter.sharedMesh.vertexCount)}");
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                Debug.Log($"[IdolDiag] {path} renderer {renderer.name} {renderer.GetType().Name}");
        }
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(Models + "PrimalIdol.fbx"))
            Debug.Log($"[IdolDiag] sub {sub.GetType().Name} {sub.name}");
    }

    private static void ConfigureTextures()
    {
        SetTexture(Models + "PrimalIdol_Normal.png", importer => importer.textureType = TextureImporterType.NormalMap);
        SetTexture(Models + "PrimalIdol_Metallic.png", importer => { importer.isReadable = true; importer.sRGBTexture = false; });
        SetTexture(Models + "PrimalIdol_Roughness.png", importer => { importer.isReadable = true; importer.sRGBTexture = false; });
        SetTexture(Portrait, AsSprite);
        foreach (string icon in Directory.GetFiles(Icons, "*.png")) SetTexture(icon.Replace('\\', '/'), AsSprite);

        string packed = Models + "PrimalIdol_MetallicSmoothness.png";
        if (!File.Exists(packed))
        {
            var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "PrimalIdol_Metallic.png");
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "PrimalIdol_Roughness.png");
            var output = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, false, true);
            Color[] m = metallic.GetPixels();
            Color[] r = roughness.width == metallic.width ? roughness.GetPixels() : m;
            var pixels = new Color[m.Length];
            for (int index = 0; index < m.Length; index++)
                pixels[index] = new Color(m[index].r, m[index].r, m[index].r, 1f - r[index].r);
            output.SetPixels(pixels);
            File.WriteAllBytes(packed, output.EncodeToPNG());
            AssetDatabase.ImportAsset(packed);
            SetTexture(packed, importer => importer.sRGBTexture = false);
        }
    }

    private static void AsSprite(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.maxTextureSize = 512;
    }

    private static void SetTexture(string path, System.Action<TextureImporter> configure)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        configure(importer);
        importer.SaveAndReimport();
    }

    private static Material CreateMaterial()
    {
        string path = Models + "PrimalIdol.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "PrimalIdol" };
        var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "PrimalIdol_BaseColor.png");
        material.SetTexture("_BaseMap", baseColor);
        material.SetTexture("_MainTex", baseColor);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "PrimalIdol_Normal.png"));
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "PrimalIdol_MetallicSmoothness.png"));
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.SetFloat("_Smoothness", 1f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>Uniformly scales the generated model to a 0.9-cell footprint and ~1.4 cells of height.</summary>
    private static GameObject CreatePrefab(Material material)
    {
        string path = Models + "PrimalIdol.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "PrimalIdol.fbx");
        // The battle resets the prefab root's scale, so the scaled model lives under a neutral wrapper
        // (the FBX root itself carries the file's unit scale).
        var wrapper = new GameObject("PrimalIdol");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
        Bounds bounds = instance.GetComponentsInChildren<Renderer>()
            .Select(renderer => renderer.bounds)
            .Aggregate((a, b) => { a.Encapsulate(b); return a; });
        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        float scale = Mathf.Min(0.9f / Mathf.Max(0.001f, footprint), 1.45f / Mathf.Max(0.001f, bounds.size.y));
        instance.transform.localScale *= scale;
        var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, path);
        Object.DestroyImmediate(wrapper);
        Debug.Log($"[PrimalIdolContentSetup] Model bounds {bounds.size}, scale {scale}.");
        return prefab;
    }

    /// <summary>Simple glyph icons until the painted ones (see primal-idol-prompts.md) replace them in place.</summary>
    private static void CreatePlaceholderIcons()
    {
        Icon("Icon_PrimalIdol_StormMagnet", new Color(0.55f, 0.4f, 1f), Bolt);
        Icon("Icon_PrimalIdol_FireGeyser", new Color(1f, 0.45f, 0.12f), Jet);
        Icon("Icon_PrimalIdol_EarthPillar", new Color(0.85f, 0.6f, 0.25f), Pillar);
        Icon("Icon_PrimalIdol_LiftTrap", new Color(0.25f, 0.9f, 0.85f), Spiral);
        Icon("Icon_PrimalIdol_RitualSurfaces", new Color(0.95f, 0.78f, 0.35f), Corner);
    }

    private static void Icon(string name, Color color, System.Func<float, float, bool> glyph)
    {
        string path = Icons + name + ".png";
        if (File.Exists(path)) return;
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * 2f - 1f;
                float v = y / (float)size * 2f - 1f;
                float vignette = Mathf.Clamp01(1f - (u * u + v * v) * 0.6f);
                Color background = Color.Lerp(new Color(0.06f, 0.06f, 0.08f), color * 0.35f, vignette * 0.8f);
                background.a = 1f;
                bool on = glyph(u, v);
                bool glow = !on && (glyph(u + 0.04f, v) || glyph(u - 0.04f, v) || glyph(u, v + 0.04f) || glyph(u, v - 0.04f));
                Color pixel = on ? Color.Lerp(color, Color.white, 0.25f) : glow ? Color.Lerp(background, color, 0.6f) : background;
                pixel.a = 1f;
                texture.SetPixel(x, y, pixel);
            }
        }

        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    private static bool Bolt(float u, float v)
    {
        // Zig-zag lightning bolt.
        float x = v > 0.25f ? 0.2f - (v - 0.25f) * 0.5f : v > -0.15f ? -0.15f + (v + 0.15f) * 0.9f : 0.1f - (v + 0.15f) * 0.6f;
        return Mathf.Abs(u - x) < 0.11f && Mathf.Abs(v) < 0.8f;
    }

    private static bool Jet(float u, float v)
    {
        float width = 0.12f + (v + 0.8f) * 0.18f;
        return v > -0.7f && v < 0.75f && Mathf.Abs(u + Mathf.Sin(v * 6f) * 0.05f) < width || v < -0.6f && v > -0.75f && Mathf.Abs(u) < 0.55f;
    }

    private static bool Pillar(float u, float v)
    {
        return Mathf.Abs(u) < 0.25f && v > -0.7f && v < 0.6f || v < -0.6f && v > -0.75f && Mathf.Abs(u) < 0.7f;
    }

    private static bool Spiral(float u, float v)
    {
        float radius = Mathf.Sqrt(u * u + v * v);
        float angle = Mathf.Atan2(v, u);
        float arm = Mathf.Repeat(radius * 9f - angle, Mathf.PI * 2f);
        return radius < 0.75f && radius > 0.08f && arm < 1.3f;
    }

    private static bool Corner(float u, float v)
    {
        return u > -0.6f && u < -0.4f && v > -0.6f && v < 0.6f || v > -0.6f && v < -0.4f && u > -0.6f && u < 0.6f;
    }

    private static T Asset<T>(string relative, System.Action<T> configure) where T : ScriptableObject
    {
        string path = Root + relative + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        configure(asset);
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static AbilityDefinition Ability(string id, string displayName, string description, string icon, int cooldown,
        PatternDefinition targeting, PatternDefinition impact, params AbilityEffectDefinition[] effects)
    {
        return Asset<AbilityDefinition>("Abilities/" + id, ability =>
        {
            Set(ability, "stableId", id);
            Set(ability, "displayName", displayName);
            Set(ability, "description", description);
            Set(ability, "icon", AssetDatabase.LoadAssetAtPath<Sprite>(Icons + icon + ".png"));
            Set(ability, "actionCost", 1);
            Set(ability, "cooldown", cooldown);
            Set(ability, "targetType", (int)AbilityTargetType.Cell);
            Set(ability, "targetTeam", (int)AbilityTargetTeam.Any);
            Set(ability, "rangeType", (int)AbilityRangeType.Ranged);
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
            Debug.LogWarning($"[PrimalIdolContentSetup] {target.name}: no property {name}");
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
    }
}
