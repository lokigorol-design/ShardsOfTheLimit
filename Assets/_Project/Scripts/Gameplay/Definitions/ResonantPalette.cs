using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    /// <summary>
    /// Colors of a resonant for grayscale ability icon masters (gradient map): master brightness → effect color,
    /// the same brightness through the background gradient for the dark surroundings, a soft brightness mask between them,
    /// and a tint pulled into dark areas away from the center. Values follow palettes.json of the icon pipeline.
    /// </summary>
    [CreateAssetMenu(menuName = "Shards of the Limit/Resonant Palette", fileName = "Palette")]
    public sealed class ResonantPalette : ScriptableObject
    {
        [Serializable]
        public struct Stop
        {
            [Range(0f, 1f)] public float position;
            public Color32 color;

            public Stop(float position, Color32 color)
            {
                this.position = position;
                this.color = color;
            }
        }

        public string paletteName = "neutral";
        [Tooltip("Effect gradient: master brightness 0..1 → color (sRGB as authored).")]
        public List<Stop> effectStops = new List<Stop>();
        [Tooltip("Background gradient; empty uses the shared _background palette.")]
        public List<Stop> backgroundStops = new List<Stop>();
        public Color32 tintColor = new Color32(15, 61, 60, 255);
        [Range(0f, 1f)] public float tintStrength;
        [Tooltip("Soft mask between background and effect: smoothstep of blurred brightness between these values.")]
        public float maskLow = 0.16f;
        public float maskHigh = 0.42f;

        /// <summary>Linear interpolation of the stops at t (clamped to the first and last stop), in authored sRGB bytes.</summary>
        public static Color32 Evaluate(IReadOnlyList<Stop> stops, float t)
        {
            if (stops == null || stops.Count == 0) return new Color32(0, 0, 0, 255);
            if (t <= stops[0].position) return stops[0].color;
            for (int index = 1; index < stops.Count; index++)
            {
                if (t > stops[index].position) continue;
                Stop a = stops[index - 1], b = stops[index];
                float k = (t - a.position) / Mathf.Max(1e-6f, b.position - a.position);
                return Color32.Lerp(a.color, b.color, k);
            }

            return stops[stops.Count - 1].color;
        }
    }
}