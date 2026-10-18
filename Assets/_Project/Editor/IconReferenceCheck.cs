using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Presentation;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Compares runtime-colored icons with the reference renders of the icon pipeline (colored/&lt;palette&gt;/*_256.png,
/// outside the project) and writes side-by-side sheets to Logs/Screenshots.
/// Run: Unity -batchmode -projectPath ... -executeMethod IconReferenceCheck.Run -quit
/// </summary>
public static class IconReferenceCheck
{
    private const string ReferenceRoot = "D:/GameDev/ShardsOfTheLimit/Echo/_AbilityIcons_v3/colored/";

    public static void Run()
    {
        var set = Resources.Load<IconMasterSet>("Prototype/Palettes/IconMasters");
        Directory.CreateDirectory("Logs/Screenshots");
        var report = new List<string>();
        foreach (string paletteFolder in Directory.Exists(ReferenceRoot) ? Directory.GetDirectories(ReferenceRoot) : new string[0])
        {
            string paletteName = Path.GetFileName(paletteFolder);
            var palette = Resources.Load<ResonantPalette>("Prototype/Palettes/" + paletteName);
            if (palette == null) continue;
            var pairs = new List<(Texture2D ours, Texture2D reference)>();
            foreach (string file in Directory.GetFiles(paletteFolder, "*_256.png").OrderBy(f => f))
            {
                string iconName = Path.GetFileName(file).Replace("_256.png", string.Empty);
                Sprite master = set.masters.FirstOrDefault(sprite => sprite.name == iconName);
                if (master == null) continue;
                var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                reference.LoadImage(File.ReadAllBytes(file));
                Texture2D ours = ReadBack(IconTintCache.Get(master, palette));
                pairs.Add((ours, reference));
                report.Add($"{paletteName}/{iconName}: mean |Δ| {MeanDifference(ours, reference):0.0} / 255");
            }

            if (pairs.Count > 0) SaveSheet(pairs, $"Logs/Screenshots/icons-{paletteName}-ours-vs-reference.png");
        }

        IconTintCache.Clear();
        File.WriteAllLines("Logs/Screenshots/icons-reference-diff.txt", report);
        foreach (string line in report) Debug.Log("[IconReferenceCheck] " + line);
    }

    public static Texture2D ReadBack(Texture texture)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = (RenderTexture)texture;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        return copy;
    }

    private static float MeanDifference(Texture2D ours, Texture2D reference)
    {
        Color32[] a = ours.GetPixels32();
        Color32[] b = reference.GetPixels32();
        if (a.Length != b.Length) return 255f;
        double sum = 0;
        int count = 0;
        for (int index = 0; index < a.Length; index++)
        {
            if (a[index].a < 128 && b[index].a < 128) continue;
            sum += (System.Math.Abs(a[index].r - b[index].r) + System.Math.Abs(a[index].g - b[index].g) +
                    System.Math.Abs(a[index].b - b[index].b)) / 3.0;
            count++;
        }

        return count == 0 ? 0f : (float)(sum / count);
    }

    private static void SaveSheet(List<(Texture2D ours, Texture2D reference)> pairs, string path)
    {
        const int size = 256;
        var sheet = new Texture2D(size * 2 + 8, (size + 8) * pairs.Count, TextureFormat.RGBA32, false);
        var clear = Enumerable.Repeat(new Color32(30, 30, 34, 255), sheet.width * sheet.height).ToArray();
        sheet.SetPixels32(clear);
        for (int row = 0; row < pairs.Count; row++)
        {
            int y = (pairs.Count - 1 - row) * (size + 8);
            Blend(sheet, pairs[row].ours, 0, y);
            Blend(sheet, pairs[row].reference, size + 8, y);
        }

        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
    }

    private static void Blend(Texture2D sheet, Texture2D icon, int x0, int y0)
    {
        for (int y = 0; y < icon.height; y++)
        for (int x = 0; x < icon.width; x++)
        {
            Color c = icon.GetPixel(x, y);
            Color under = sheet.GetPixel(x0 + x, y0 + y);
            sheet.SetPixel(x0 + x, y0 + y, Color.Lerp(under, new Color(c.r, c.g, c.b, 1f), c.a));
        }
    }
}
