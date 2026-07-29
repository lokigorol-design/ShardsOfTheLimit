using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Temporary combat readability: log, banners, projectile, impacts and damage reactions.</summary>
    public sealed class BattleFeedbackView : MonoBehaviour
    {
        private sealed class FloatingEntry
        {
            public Vector3 WorldPosition;
            public string Text;
            public Color Color;
            public float StartedAt;
            public float Duration;
        }

        private const int MaximumLogEntries = 9;
        private readonly List<string> logEntries = new List<string>();
        private readonly List<FloatingEntry> floatingEntries = new List<FloatingEntry>();
        private string bannerText = string.Empty;
        private Color bannerColor = Color.white;
        private float bannerStartedAt;
        private float bannerDuration;
        private GUIStyle logStyle;
        private GUIStyle bannerStyle;
        private GUIStyle floatingStyle;

        public IReadOnlyList<string> RecentEntries => logEntries;

        public void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            logEntries.Add(message);
            while (logEntries.Count > MaximumLogEntries)
            {
                logEntries.RemoveAt(0);
            }
        }

        public void ShowTurnBanner(string message, Color color, float duration = 1.1f)
        {
            bannerText = message;
            bannerColor = color;
            bannerStartedAt = Time.unscaledTime;
            bannerDuration = duration;
        }

        public void AddFloatingText(Vector3 worldPosition, string text, Color color, float duration = 1.25f)
        {
            floatingEntries.Add(new FloatingEntry
            {
                WorldPosition = worldPosition,
                Text = text,
                Color = color,
                StartedAt = Time.unscaledTime,
                Duration = duration
            });
        }

        public IEnumerator PlayAbility(
            Vector3 sourcePosition,
            Vector3 targetPosition,
            IReadOnlyList<Vector3> impactPositions)
        {
            GameObject projectile = CreateMarker("Ability Projectile", PrimitiveType.Sphere, new Color(1f, 0.42f, 0.08f));
            projectile.transform.localScale = Vector3.one * 0.22f;
            float elapsed = 0f;
            const float travelDuration = 0.42f;
            while (elapsed < travelDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / travelDuration);
                projectile.transform.position = Vector3.Lerp(sourcePosition, targetPosition, progress) +
                                                Vector3.up * Mathf.Sin(progress * Mathf.PI) * 0.7f;
                yield return null;
            }

            Destroy(projectile);
            List<GameObject> impacts = impactPositions
                .Select(position => CreateMarker("Impact Pulse", PrimitiveType.Sphere, new Color(1f, 0.2f, 0.04f)))
                .ToList();
            for (int index = 0; index < impacts.Count; index++)
            {
                impacts[index].transform.position = impactPositions[index];
            }

            elapsed = 0f;
            const float impactDuration = 0.38f;
            while (elapsed < impactDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / impactDuration);
                float size = Mathf.Sin(progress * Mathf.PI) * 0.95f;
                foreach (GameObject impact in impacts)
                {
                    impact.transform.localScale = new Vector3(size, 0.09f, size);
                }

                yield return null;
            }

            foreach (GameObject impact in impacts) Destroy(impact);
        }

        public IEnumerator PlayHitReactions(IReadOnlyList<CombatUnitView> targets)
        {
            if (targets.Count == 0) yield break;

            Vector3[] origins = targets.Select(view => view.transform.position).ToArray();
            float elapsed = 0f;
            const float duration = 0.3f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float kick = Mathf.Sin(progress * Mathf.PI * 4f) * (1f - progress) * 0.08f;
                for (int index = 0; index < targets.Count; index++)
                {
                    targets[index].SetColor(Color.Lerp(Color.white, targets[index].BaseColor, progress));
                    targets[index].transform.position = origins[index] + Vector3.right * kick;
                }

                yield return null;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                targets[index].SetColor(targets[index].BaseColor);
                targets[index].transform.position = origins[index];
            }
        }

        private static GameObject CreateMarker(string objectName, PrimitiveType primitiveType, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(primitiveType);
            marker.name = objectName;
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            marker.GetComponent<Renderer>().material.color = color;
            return marker;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawLog();
            DrawBanner();
            DrawFloatingText();
        }

        private void EnsureStyles()
        {
            if (logStyle != null) return;
            logStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            logStyle.normal.textColor = new Color(0.9f, 0.93f, 1f);
            bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            floatingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private void DrawLog()
        {
            float width = Mathf.Min(390f, Screen.width * 0.38f);
            var box = new Rect(Screen.width - width - 12f, 12f, width, 184f);
            GUI.Box(box, "ACTION LOG");
            string text = string.Join("\n", logEntries.Select(entry => $"› {entry}"));
            GUI.Label(new Rect(box.x + 12f, box.y + 25f, box.width - 24f, box.height - 32f), text, logStyle);
        }

        private void DrawBanner()
        {
            float elapsed = Time.unscaledTime - bannerStartedAt;
            if (string.IsNullOrEmpty(bannerText) || elapsed >= bannerDuration) return;
            float alpha = Mathf.Min(1f, elapsed / 0.15f, (bannerDuration - elapsed) / 0.25f);
            Color previous = GUI.color;
            GUI.color = new Color(bannerColor.r, bannerColor.g, bannerColor.b, alpha);
            GUI.Box(new Rect(Screen.width * 0.5f - 230f, 164f, 460f, 52f), string.Empty);
            GUI.Label(new Rect(Screen.width * 0.5f - 225f, 166f, 450f, 48f), bannerText, bannerStyle);
            GUI.color = previous;
        }

        private void DrawFloatingText()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            float now = Time.unscaledTime;
            for (int index = floatingEntries.Count - 1; index >= 0; index--)
            {
                FloatingEntry entry = floatingEntries[index];
                float progress = (now - entry.StartedAt) / entry.Duration;
                if (progress >= 1f)
                {
                    floatingEntries.RemoveAt(index);
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(entry.WorldPosition + Vector3.up * progress * 0.65f);
                if (screen.z <= 0f) continue;
                Color previous = GUI.color;
                GUI.color = new Color(entry.Color.r, entry.Color.g, entry.Color.b, 1f - progress);
                GUI.Label(new Rect(screen.x - 75f, Screen.height - screen.y - 18f, 150f, 36f), entry.Text, floatingStyle);
                GUI.color = previous;
            }
        }
    }
}
