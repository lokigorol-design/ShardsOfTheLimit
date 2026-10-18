using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Ability icon coloring at runtime: a grayscale master (Art/Icons/Abilities_v3) is rendered once per palette through
    /// the Shards/IconGradientMap shader into a 256×256 render texture with the gold frame and transparent corners, and
    /// cached. The soft background/effect mask depends only on the master, so it is computed once per master on the CPU
    /// (min filter, blur, smoothstep, max filter, blur — as colorize_icon.py). Icons that are not masters are drawn as they are.
    /// </summary>
    public static class IconTintCache
    {
        public const int Size = 256;
        private const int MaskSize = 256;

        private static readonly Dictionary<(Sprite, ResonantPalette), RenderTexture> Rendered =
            new Dictionary<(Sprite, ResonantPalette), RenderTexture>();
        private static readonly Dictionary<(Sprite, float, float), Texture2D> Masks = new Dictionary<(Sprite, float, float), Texture2D>();
        private static readonly Dictionary<ResonantPalette, Texture2D> EffectRamps = new Dictionary<ResonantPalette, Texture2D>();
        private static readonly Dictionary<ResonantPalette, Texture2D> BackgroundRamps = new Dictionary<ResonantPalette, Texture2D>();
        private static IconMasterSet masterSet;
        private static HashSet<Sprite> masters;
        private static Material material;

        public static IconMasterSet MasterSet
        {
            get
            {
                if (masterSet == null)
                {
                    masterSet = Resources.Load<IconMasterSet>("Prototype/Palettes/IconMasters");
                    masters = masterSet != null ? new HashSet<Sprite>(masterSet.masters.Where(sprite => sprite != null)) : new HashSet<Sprite>();
                }

                return masterSet;
            }
        }

        public static ResonantPalette Neutral => MasterSet != null ? MasterSet.neutral : null;

        /// <summary>True for a grayscale master that must be colored by a palette.</summary>
        public static bool IsMaster(Sprite sprite) => sprite != null && MasterSet != null && masters.Contains(sprite);

        /// <summary>The master colored by <paramref name="palette"/> (neutral when null), 256×256 with frame. Null if not a master.</summary>
        public static Texture Get(Sprite master, ResonantPalette palette)
        {
            if (!IsMaster(master)) return null;
            palette = palette != null ? palette : Neutral;
            if (Rendered.TryGetValue((master, palette), out RenderTexture cached) && cached != null && cached.IsCreated())
                return cached;

            if (material == null)
            {
                Shader shader = Shader.Find("Shards/IconGradientMap");
                if (shader == null) return null;
                material = new Material(shader) { name = "Icon Gradient Map", hideFlags = HideFlags.HideAndDontSave };
            }

            float low = palette != null ? palette.maskLow : 0.16f;
            float high = palette != null ? palette.maskHigh : 0.42f;
            material.SetTexture("_MaskTex", Mask(master, low, high));
            material.SetTexture("_FgRamp", Ramp(EffectRamps, palette, palette != null ? palette.effectStops : null));
            List<ResonantPalette.Stop> backgroundStops = palette != null && palette.backgroundStops.Count > 0
                ? palette.backgroundStops
                : MasterSet.background != null ? MasterSet.background.effectStops : null;
            material.SetTexture("_BgRamp", Ramp(BackgroundRamps, palette, backgroundStops));
            material.SetTexture("_FrameTex", MasterSet.frame != null ? (Texture)MasterSet.frame : Texture2D.blackTexture);
            Color32 tint = palette != null ? palette.tintColor : new Color32(0, 0, 0, 255);
            material.SetVector("_TintColor", new Vector4(tint.r / 255f, tint.g / 255f, tint.b / 255f, 1f));
            material.SetFloat("_TintStrength", palette != null ? palette.tintStrength : 0f);

            var target = cached != null ? cached : new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = $"{master.name} · {(palette != null ? palette.paletteName : "neutral")}",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.IsCreated()) target.Create();
            Graphics.Blit(master.texture, target, material);
            Rendered[(master, palette)] = target;
            return target;
        }

        private static Texture2D Ramp(Dictionary<ResonantPalette, Texture2D> cache, ResonantPalette palette, List<ResonantPalette.Stop> stops)
        {
            ResonantPalette key = palette != null ? palette : Neutral;
            if (key != null && cache.TryGetValue(key, out Texture2D existing) && existing != null) return existing;
            // Linear (non-sRGB) texture: the authored sRGB bytes are sampled as they are.
            var ramp = new Texture2D(256, 1, TextureFormat.RGBA32, false, true)
            {
                name = "Icon Ramp", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            for (int x = 0; x < 256; x++) ramp.SetPixel(x, 0, ResonantPalette.Evaluate(stops, x / 255f));
            ramp.Apply(false, true);
            if (key != null) cache[key] = ramp;
            return ramp;
        }

        /// <summary>Soft mask of the bright effect: large bright regions; thin lines and halftone blur away.</summary>
        private static Texture2D Mask(Sprite master, float low, float high)
        {
            if (Masks.TryGetValue((master, low, high), out Texture2D existing) && existing != null) return existing;
            float[] lum = ReadLuminance(master.texture, MaskSize);
            float[] a = MinFilter(lum, MaskSize, 1);
            a = GaussianBlur(a, MaskSize, 5f);
            for (int index = 0; index < a.Length; index++)
            {
                float m = Mathf.Clamp01((a[index] - low) / Mathf.Max(1e-4f, high - low));
                a[index] = m * m * (3f - 2f * m);
            }

            a = MaxFilter(a, MaskSize, 2);
            a = GaussianBlur(a, MaskSize, 3f);
            var mask = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true)
            {
                name = master.name + " Mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[a.Length];
            for (int index = 0; index < a.Length; index++)
            {
                byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(a[index]) * 255f);
                pixels[index] = new Color32(value, value, value, 255);
            }

            mask.SetPixels32(pixels);
            mask.Apply(false, true);
            Masks[(master, low, high)] = mask;
            return mask;
        }

        /// <summary>Box-downsampled grayscale of a readable texture (raw sRGB bytes, as the reference script reads them).</summary>
        private static float[] ReadLuminance(Texture2D texture, int size)
        {
            Color32[] source = texture.GetPixels32();
            int width = texture.width, height = texture.height;
            var result = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int x0 = x * width / size, x1 = Mathf.Max(x0 + 1, (x + 1) * width / size);
                int y0 = y * height / size, y1 = Mathf.Max(y0 + 1, (y + 1) * height / size);
                float sum = 0f;
                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                    sum += source[sy * width + sx].r;
                result[y * size + x] = sum / ((x1 - x0) * (y1 - y0) * 255f);
            }

            return result;
        }

        /// <summary>Releases every cached render texture and helper texture (tests, editor tools).</summary>
        public static void Clear()
        {
            foreach (RenderTexture texture in Rendered.Values)
                if (texture != null)
                {
                    texture.Release();
                    Object.DestroyImmediate(texture);
                }

            foreach (Texture2D texture in Masks.Values.Concat(EffectRamps.Values).Concat(BackgroundRamps.Values))
                if (texture != null) Object.DestroyImmediate(texture);
            Rendered.Clear();
            Masks.Clear();
            EffectRamps.Clear();
            BackgroundRamps.Clear();
            if (material != null) Object.DestroyImmediate(material);
            material = null;
        }

        private static float[] MinFilter(float[] source, int size, int radius) => RankFilter(source, size, radius, true);
        private static float[] MaxFilter(float[] source, int size, int radius) => RankFilter(source, size, radius, false);

        private static float[] RankFilter(float[] source, int size, int radius, bool minimum)
        {
            var horizontal = new float[source.Length];
            var result = new float[source.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                float[] input = pass == 0 ? source : horizontal;
                float[] output = pass == 0 ? horizontal : result;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float value = minimum ? float.MaxValue : float.MinValue;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = pass == 0 ? Mathf.Clamp(x + k, 0, size - 1) : x;
                        int sy = pass == 0 ? y : Mathf.Clamp(y + k, 0, size - 1);
                        float sample = input[sy * size + sx];
                        value = minimum ? Mathf.Min(value, sample) : Mathf.Max(value, sample);
                    }

                    output[y * size + x] = value;
                }
            }

            return result;
        }

        private static float[] GaussianBlur(float[] source, int size, float sigma)
        {
            int radius = Mathf.CeilToInt(sigma * 3f);
            var kernel = new float[radius * 2 + 1];
            float total = 0f;
            for (int k = -radius; k <= radius; k++) total += kernel[k + radius] = Mathf.Exp(-k * k / (2f * sigma * sigma));
            for (int k = 0; k < kernel.Length; k++) kernel[k] /= total;
            var horizontal = new float[source.Length];
            var result = new float[source.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                float[] input = pass == 0 ? source : horizontal;
                float[] output = pass == 0 ? horizontal : result;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = pass == 0 ? Mathf.Clamp(x + k, 0, size - 1) : x;
                        int sy = pass == 0 ? y : Mathf.Clamp(y + k, 0, size - 1);
                        sum += input[sy * size + sx] * kernel[k + radius];
                    }

                    output[y * size + x] = sum;
                }
            }

            return result;
        }
    }
}
