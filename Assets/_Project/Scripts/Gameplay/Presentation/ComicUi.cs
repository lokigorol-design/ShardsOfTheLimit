using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Comic-book IMGUI kit for the battle HUD: paper panels with a thick ink outline, a hard offset shadow and halftone
    /// dots; tilted yellow caption boxes; ink-outlined lettering; speech-bubble tooltips; starburst banners; chunky bars.
    /// All textures are generated at runtime, nothing is imported.
    /// </summary>
    public static class ComicUi
    {
        public static readonly Color Ink = new Color(0.07f, 0.06f, 0.08f, 1f);
        public static readonly Color Paper = new Color(0.97f, 0.93f, 0.82f, 0.97f);
        public static readonly Color PaperShade = new Color(0.86f, 0.8f, 0.66f, 0.97f);
        public static readonly Color Night = new Color(0.12f, 0.14f, 0.22f, 0.95f);
        public static readonly Color Yellow = new Color(1f, 0.83f, 0.2f, 1f);
        public static readonly Color Red = new Color(0.9f, 0.22f, 0.2f, 1f);
        public static readonly Color Cyan = new Color(0.25f, 0.76f, 0.92f, 1f);
        public static readonly Color Green = new Color(0.36f, 0.82f, 0.3f, 1f);
        public static readonly Color Muted = new Color(0.55f, 0.53f, 0.5f, 1f);

        private const int Corner = 10;
        private static Texture2D fillTexture;
        private static Texture2D outlineTexture;
        private static Texture2D dotTexture;
        private static Texture2D starTexture;
        private static Texture2D tailTexture;
        private static GUIStyle fillStyle;
        private static GUIStyle outlineStyle;
        private static GUIStyle captionStyle;

        public static void Ensure()
        {
            if (fillTexture != null) return;
            fillTexture = RoundedTexture(false);
            outlineTexture = RoundedTexture(true);
            dotTexture = DotTexture();
            starTexture = StarTexture();
            tailTexture = TailTexture();
            fillStyle = new GUIStyle { normal = { background = fillTexture }, border = new RectOffset(Corner, Corner, Corner, Corner) };
            outlineStyle = new GUIStyle { normal = { background = outlineTexture }, border = new RectOffset(Corner, Corner, Corner, Corner) };
            captionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow
            };
        }

        private static bool Repaint => Event.current.type == EventType.Repaint;

        /// <summary>Paper panel: hard ink shadow, fill, halftone dots and a thick ink outline.</summary>
        public static void Panel(Rect rect, Color fill, bool halftone = true, float shadow = 5f)
        {
            Ensure();
            if (!Repaint) return;
            Color previous = GUI.color;
            if (shadow > 0f)
            {
                GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.85f * fill.a);
                fillStyle.Draw(new Rect(rect.x + shadow, rect.y + shadow, rect.width, rect.height), false, false, false, false);
            }

            GUI.color = fill;
            fillStyle.Draw(rect, false, false, false, false);
            if (halftone && rect.width > 24f && rect.height > 24f)
            {
                // Halftone dots fading towards the top: printed shading of a comic panel.
                GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.09f * fill.a);
                Rect inner = new Rect(rect.x + 5f, rect.y + rect.height * 0.45f, rect.width - 10f, rect.height * 0.55f - 5f);
                GUI.DrawTextureWithTexCoords(inner, dotTexture, new Rect(0f, 0f, inner.width / 9f, inner.height / 9f));
            }

            GUI.color = new Color(Ink.r, Ink.g, Ink.b, fill.a);
            outlineStyle.Draw(rect, false, false, false, false);
            GUI.color = previous;
        }

        /// <summary>Speech bubble: a paper panel with a tail pointing down-left (tooltips).</summary>
        public static void Bubble(Rect rect, bool tailBelow = true)
        {
            Ensure();
            Panel(rect, Paper, false, 4f);
            if (!Repaint) return;
            Color previous = GUI.color;
            var tail = tailBelow
                ? new Rect(rect.x + 22f, rect.yMax - 4f, 26f, 22f)
                : new Rect(rect.x + 22f, rect.y - 18f, 26f, 22f);
            GUI.color = Color.white;
            if (tailBelow) GUI.DrawTexture(tail, tailTexture);
            else GUI.DrawTextureWithTexCoords(tail, tailTexture, new Rect(0f, 1f, 1f, -1f));
            GUI.color = previous;
        }

        /// <summary>Tilted caption box (yellow by default) with an ink outline and uppercase lettering.</summary>
        public static void Caption(Rect rect, string text, Color fill, float tilt = -2.5f, int fontSize = 15)
        {
            Ensure();
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(tilt, rect.center);
            Panel(rect, fill, false, 3f);
            captionStyle.fontSize = fontSize;
            captionStyle.normal.textColor = Ink;
            GUI.Label(rect, text.ToUpperInvariant(), captionStyle);
            GUI.matrix = matrix;
        }

        /// <summary>Comic lettering: the text in <paramref name="fill"/> with a thick ink stroke.</summary>
        public static void OutlinedText(Rect rect, string text, GUIStyle style, Color fill, Color? stroke = null, float thickness = 2f)
        {
            Color previousColor = style.normal.textColor;
            Color outline = stroke ?? Ink;
            style.normal.textColor = outline;
            for (int index = 0; index < 8; index++)
            {
                float angle = index * Mathf.PI / 4f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(angle) * thickness, rect.y + Mathf.Sin(angle) * thickness, rect.width, rect.height),
                    text, style);
            }

            style.normal.textColor = fill;
            GUI.Label(rect, text, style);
            style.normal.textColor = previousColor;
        }

        /// <summary>Chunky bar: ink frame, colored fill with a light top band, tick marks every 25%.</summary>
        public static void Bar(Rect rect, float fraction, Color color)
        {
            Ensure();
            if (!Repaint) return;
            Color previous = GUI.color;
            GUI.color = Ink;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            var inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            GUI.color = new Color(0.22f, 0.2f, 0.22f, 1f);
            GUI.DrawTexture(inner, Texture2D.whiteTexture);
            var filled = new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(fraction), inner.height);
            GUI.color = color;
            GUI.DrawTexture(filled, Texture2D.whiteTexture);
            if (inner.height >= 5f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(filled.x, filled.y, filled.width, Mathf.Max(1f, inner.height * 0.3f)), Texture2D.whiteTexture);
            }

            GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.55f);
            for (int tick = 1; tick < 4; tick++)
                GUI.DrawTexture(new Rect(inner.x + inner.width * tick / 4f, inner.y, 1f, inner.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>Starburst behind a shout: ink star under a colored star.</summary>
        public static void Burst(Rect rect, Color fill, float alpha = 1f)
        {
            Ensure();
            if (!Repaint) return;
            Color previous = GUI.color;
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
            GUI.DrawTexture(new Rect(rect.x - 6f + 5f, rect.y - 6f + 5f, rect.width + 12f, rect.height + 12f), starTexture);
            GUI.DrawTexture(new Rect(rect.x - 6f, rect.y - 6f, rect.width + 12f, rect.height + 12f), starTexture);
            GUI.color = new Color(fill.r, fill.g, fill.b, alpha);
            GUI.DrawTexture(rect, starTexture);
            GUI.color = previous;
        }

        /// <summary>Round coin (action points, badges): ink rim around a colored disk.</summary>
        public static void Coin(Rect rect, Color fill)
        {
            Ensure();
            if (!Repaint) return;
            Color previous = GUI.color;
            GUI.color = Ink;
            GUI.DrawTexture(rect, dotTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x + 2.5f, rect.y + 2.5f, rect.width - 5f, rect.height - 5f), dotTexture);
            GUI.color = previous;
        }

        /// <summary>
        /// Comic button: paper card (yellow when selected or hovered, grey when disabled) with an ink outline and shadow
        /// that drops when pressed. Returns true on click when enabled.
        /// </summary>
        public static bool Button(Rect rect, string text, GUIStyle textStyle, bool enabled, bool selected = false, Color? baseColor = null)
        {
            Ensure();
            bool hover = enabled && rect.Contains(Event.current.mousePosition);
            bool pressed = hover && Input.GetMouseButton(0);
            Color fill = !enabled ? new Color(0.62f, 0.6f, 0.57f, 0.95f)
                : selected ? Yellow
                : hover ? Color.Lerp(baseColor ?? Paper, Yellow, 0.45f)
                : baseColor ?? Paper;
            Rect face = pressed ? new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height) : rect;
            Panel(face, fill, false, pressed ? 2f : 4f);
            if (!string.IsNullOrEmpty(text))
            {
                Color previous = textStyle.normal.textColor;
                textStyle.normal.textColor = enabled ? Ink : new Color(0.25f, 0.24f, 0.24f, 1f);
                GUI.Label(face, text, textStyle);
                textStyle.normal.textColor = previous;
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none) && enabled;
        }

        /// <summary>Thin ink frame around an icon or portrait.</summary>
        public static void Frame(Rect rect, Color color, float thickness = 3f)
        {
            if (!Repaint) return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x - thickness, rect.y - thickness, rect.width + thickness * 2f, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x - thickness, rect.yMax, rect.width + thickness * 2f, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Texture2D RoundedTexture(bool outline)
        {
            const int size = Corner * 2 + 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            const float radius = Corner - 1f;
            const float stroke = 3f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius + 0.5f, size - radius - 0.5f);
                float cy = Mathf.Clamp(py, radius + 0.5f, size - radius - 0.5f);
                float distance = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                float inside = Mathf.Clamp01(radius - distance + 0.5f);
                float alpha = outline ? inside * Mathf.Clamp01(distance - (radius - stroke) + 0.5f) : inside;
                // Straight edges: the distance to the clamped point is the distance to the edge.
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D DotTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size * 0.42f - distance + 0.5f)));
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D StarTexture()
        {
            const int size = 256;
            const int spikes = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float angle = Mathf.Atan2(v, u);
                // Irregular spikes, like a hand-drawn shout balloon.
                float k = Mathf.Abs(Mathf.Repeat(angle / (Mathf.PI * 2f) * spikes + 0.5f, 1f) - 0.5f) * 2f;
                float jitter = 0.06f * Mathf.Sin(angle * 5f + 1.3f);
                float radius = Mathf.Lerp(0.98f + jitter, 0.66f, k);
                float distance = Mathf.Sqrt(u * u + v * v);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((radius - distance) * size * 0.5f)));
            }

            texture.Apply();
            return texture;
        }

        /// <summary>Bubble tail: paper triangle with ink edges, pointing down-left.</summary>
        private static Texture2D TailTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Triangle from the top edge (y = size) down to the tip at bottom-left.
                float t = (y + 0.5f) / size;               // 0 bottom .. 1 top
                float left = (1f - t) * 0.05f * size + t * 0.1f * size;
                float right = (1f - t) * 0.12f * size + t * 0.9f * size;
                float px = x + 0.5f;
                float edge = Mathf.Min(px - left, right - px);
                Color color;
                if (edge < 0f || t > 0.97f) color = new Color(0f, 0f, 0f, 0f);
                else if (edge < 4f) color = Ink;
                else color = Paper;
                if (t > 0.88f && edge >= 4f) color = Paper;
                texture.SetPixel(x, y, color);
            }

            texture.Apply();
            return texture;
        }
    }
}
