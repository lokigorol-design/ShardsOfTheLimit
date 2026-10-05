using System;
using System.IO;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Explicit, one-shot content import. Existing data is never regenerated on load.</summary>
public static class BlightgulletContentSetup
{
    public static void SavePreview(Texture2D texture, string path) => File.WriteAllBytes(path,texture.EncodeToPNG());
    private const string Root = "Assets/_Project/Resources/Prototype/";
    private const string Art = "Assets/_Project/Art/Models/Blightgullet/";
    private const string Icons = "Assets/_Project/Art/Icons/Blightgullet/";
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<UnitDefinition>(Root + "Units/Blightgullet.asset") != null)
            throw new InvalidOperationException("Blightgullet already exists. Edit its assets in Inspector; no overwrite performed.");
        AssetDatabase.Refresh();
        foreach (string path in Directory.GetFiles(Icons, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = 512; importer.mipmapEnabled = false; importer.SaveAndReimport();
        }
        Texture2D albedo = Texture("BaseColor.png", false, false);
        Texture2D normal = Texture("Normal.png", true, true);
        Texture2D metallic = Texture("Metallic.png", false, true, true);
        Texture2D roughness = Texture("Roughness.png", false, true, true);
        var packed = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, false, true);
        Color[] pixels = metallic.GetPixels();
        for (int y = 0; y < packed.height; y++) for (int x = 0; x < packed.width; x++)
        {
            int index = y * packed.width + x;
            pixels[index] = new Color(pixels[index].r, 0, 0, 1 - roughness.GetPixelBilinear((x + .5f)/packed.width, (y + .5f)/packed.height).r);
        }
        packed.SetPixels(pixels); packed.Apply();
        File.WriteAllBytes(Art + "MetallicSmoothness.png", packed.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(packed); AssetDatabase.Refresh();
        Texture2D combined = Texture("MetallicSmoothness.png", false, true);
        var material = new Material(Shader.Find("Standard")) { name = "Blightgullet" };
        material.mainTexture = albedo; material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_MetallicGlossMap", combined); material.EnableKeyword("_METALLICGLOSSMAP");
        material.SetFloat("_GlossMapScale", .7f);
        AssetDatabase.CreateAsset(material, Art + "Blightgullet.mat");
        var modelImporter = (ModelImporter)AssetImporter.GetAtPath(Art + "Blightgullet.fbx");
        modelImporter.importAnimation = false; modelImporter.importCameras = false; modelImporter.importLights = false;
        modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        modelImporter.bakeAxisConversion = true; modelImporter.SaveAndReimport();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Blightgullet.fbx");
        var root = new GameObject("Blightgullet Miniature");
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.transform.SetParent(root.transform, false);
        var renderers = root.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) { bounds.Encapsulate(r.bounds); r.sharedMaterial = material; }
        Debug.Log($"Blightgullet imported bounds: {bounds}, root rotation: {visual.transform.localEulerAngles}");
        // Presentation-only uniform scale: one-cell base; preserve imported upright axis.
        float scale = .9f / Mathf.Max(bounds.size.x, bounds.size.z);
        visual.transform.localScale *= scale;
        visual.transform.position = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Art + "Blightgullet.prefab");
        UnityEngine.Object.DestroyImmediate(root);

        var single = Asset<RadiusPatternDefinition>("Patterns/BlightSingle", p => { Set(p,"radius",0); Set(p,"includeCenter",true); });
        var cross = Asset<RadiusPatternDefinition>("Patterns/BlightCross", p => { Set(p,"radius",1); Set(p,"includeCenter",true); });
        var range = Asset<RadiusPatternDefinition>("Patterns/BlightRange", p => { Set(p,"radius",4); Set(p,"includeCenter",true); });
        var poison = Status("Poison", "POISON", false);
        var corrosion = Status("Corrosion", "CORROSION", false);
        var oil = Status("Oil", "OIL", false);
        var immobilized = Status("Immobilized", "IMMOBILIZED", true);
        var pc = Cloud("PoisonCloud", "Poison Cloud", poison, cross, new Color(.45f,.9f,.08f, .4f));
        var cc = Cloud("CorrosionCloud", "Corrosion Cloud", corrosion, cross, new Color(1f,.65f,.02f, .4f));
        var oc = Cloud("OilCloud", "Oil Cloud", oil, cross, new Color(.22f,.12f,.38f, .4f));
        var small = Cloud("VenomousMudCloud", "Venomous Mud Cloud", poison, single, pc.color);
        var damage = Asset<DamageEffectDefinition>("Effects/MudDamage", d => { Set(d,"attackMultiplier",0.8f); Set(d,"amount",0); });
        var immobilize = Asset<ApplyStatusEffectDefinition>("Effects/MudImmobilize", d => d.status = immobilized);
        var pull = Asset<PullEffectDefinition>("Effects/TonguePull", d => d.distance = 3);
        var pe = Asset<CreateCloudEffectDefinition>("Effects/CreatePoisonCloud", d => d.cloud = pc);
        var ce = Asset<CreateCloudEffectDefinition>("Effects/CreateCorrosionCloud", d => d.cloud = cc);
        var oe = Asset<CreateCloudEffectDefinition>("Effects/CreateOilCloud", d => d.cloud = oc);
        var mud = Ability("MudSpit", "Mud Spit", range, single, 1, true, damage, immobilize);
        var poisonAbility = Ability("PoisonCloud", "Poison Cloud", range, cross, 1, false, pe);
        var corrosionAbility = Ability("CorrosionCloud", "Corrosion Cloud", range, cross, 1, false, ce);
        var tongue = Ability("TonguePull", "Tongue Pull", range, single, 1, true, pull);
        var oilAbility = Ability("OilCloud", "Oil Cloud", range, cross, 1, false, oe);
        var passive = Asset<PassiveModifierDefinition>("Passives/BlightgulletBranches", p =>
        { p.modifiedAbility = mud; p.stickyMudStatus = immobilized; });
        Asset<UnitDefinition>("Units/Blightgullet", u =>
        {
            Set(u,"stableId","blightgullet"); Set(u,"displayName","Blightgullet");
            Set(u,"portrait",AssetDatabase.LoadAssetAtPath<Sprite>(Icons + "Blightgullet IV.png"));
            Set(u,"visualPrefab",prefab); Set(u,"visualMaterial",material);
            Set(u,"maximumHealth",110); Set(u,"maximumShield",50); Set(u,"attack",14); Set(u,"defense",3); Set(u,"speed",9); Set(u,"movementRange",3);
            Set(u,"passiveModifiers",passive);
            SetArray(u,"abilities",mud,poisonAbility,corrosionAbility,tongue,oilAbility);
        });
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var controller = new GameObject("Blightgullet Battle").AddComponent<BattlePrototypeController>();
        Set(controller,"blightgulletShowcase",true);
        const string scenePath = "Assets/_Project/Scenes/BlightgulletBattle.unity";
        EditorSceneManager.SaveScene(scene,scenePath);
        EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(scenePath,true) }).ToArray();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("BLIGHTGULLET content created. BALANCE_DEFAULT values require playtesting.");
    }
    private static Texture2D Texture(string name, bool normal, bool linear, bool readable = false)
    {
        var i = (TextureImporter)AssetImporter.GetAtPath(Art + name);
        i.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        i.sRGBTexture = !linear; i.isReadable = readable; i.maxTextureSize = 2048; i.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(Art + name);
    }
    private static StatusDefinition Status(string file, string id, bool immobilized) => Asset<StatusDefinition>("Statuses/" + file, s =>
    { s.stableId = id; s.maximumStacks = immobilized ? 1 : 20; s.durationTicks = immobilized ? 100 : 300; s.preventsVoluntaryMovement = immobilized; });
    private static CloudDefinition Cloud(string file, string id, StatusDefinition status, PatternDefinition pattern, Color color)
        => Asset<CloudDefinition>("Clouds/" + file, c => { c.stableId = id; c.status = status; c.areaShape = pattern; c.color = color; });
    private static AbilityDefinition Ability(string file, string name, PatternDefinition range, PatternDefinition impact, int cd,
        bool unit, params AbilityEffectDefinition[] effects) => Asset<AbilityDefinition>("Abilities/" + file, a =>
    {
        Set(a,"stableId",file); Set(a,"displayName",name); Set(a,"description","BALANCE_DEFAULT — tune in Inspector. Global status duration is in timeline ticks.");
        Set(a,"icon",AssetDatabase.LoadAssetAtPath<Sprite>(Icons + name + ".png")); Set(a,"actionCost",1); Set(a,"cooldown",cd);
        Set(a,"targetType",unit ? 1 : 0); Set(a,"targetTeam",unit ? 2 : 0); Set(a,"rangeType",1);
        Set(a,"targetingPattern",range); Set(a,"impactPattern",impact); SetArray(a,"effects",effects);
    });
    private static T Asset<T>(string path, Action<T> configure) where T : ScriptableObject
    {
        string full = Root + path + ".asset";
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        var asset = ScriptableObject.CreateInstance<T>(); configure(asset); AssetDatabase.CreateAsset(asset,full); return asset;
    }
    private static void Set(UnityEngine.Object target, string name, object value)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(name);
        if (value is string s) p.stringValue = s;
        else if (value is bool b) p.boolValue = b;
        else if (value is int n) p.intValue = n;
        else if (value is float f) p.floatValue = f;
        else p.objectReferenceValue = (UnityEngine.Object)value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(UnityEngine.Object target, string name, params UnityEngine.Object[] values)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(name); p.arraySize = values.Length;
        for (int n = 0; n < values.Length; n++) p.GetArrayElementAtIndex(n).objectReferenceValue = values[n];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
