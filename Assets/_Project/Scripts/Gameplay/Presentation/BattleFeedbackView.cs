using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    public enum AbilityVisualKind
    {
        Attack,
        Healing,
        Shield,
        Special
    }

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
            IReadOnlyList<Vector3> impactPositions,
            Color effectColor,
            AbilityVisualKind visualKind)
        {
            switch (visualKind)
            {
                case AbilityVisualKind.Healing:
                    yield return PlayHealing(impactPositions, effectColor);
                    break;
                case AbilityVisualKind.Shield:
                    yield return PlayShield(impactPositions, effectColor);
                    break;
                case AbilityVisualKind.Special:
                    yield return PlaySpecial(sourcePosition, targetPosition, impactPositions, effectColor);
                    break;
                default:
                    yield return PlayAttack(sourcePosition, targetPosition, impactPositions, effectColor);
                    break;
            }
        }

        private static IEnumerator PlayAttack(
            Vector3 sourcePosition,
            Vector3 targetPosition,
            IReadOnlyList<Vector3> impactPositions,
            Color effectColor)
        {
            GameObject projectile = CreateMarker("Ability Projectile", PrimitiveType.Sphere, effectColor);
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
                .Select(position => CreateMarker("Impact Pulse", PrimitiveType.Sphere, effectColor))
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

        private static IEnumerator PlayHealing(IReadOnlyList<Vector3> impactPositions, Color effectColor)
        {
            var motes = new List<GameObject>();
            foreach (Vector3 position in impactPositions)
            {
                for (int index = 0; index < 3; index++)
                {
                    GameObject mote = CreateMarker("Healing Mote", PrimitiveType.Sphere, effectColor);
                    float angle = index * Mathf.PI * 2f / 3f;
                    mote.transform.position = position + new Vector3(Mathf.Cos(angle), 0.12f, Mathf.Sin(angle)) * 0.28f;
                    mote.transform.localScale = Vector3.one * 0.12f;
                    motes.Add(mote);
                }
            }

            float elapsed = 0f;
            const float duration = 0.58f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                for (int index = 0; index < motes.Count; index++)
                {
                    motes[index].transform.position += Vector3.up * Time.unscaledDeltaTime * 0.9f;
                    float size = Mathf.Max(0.01f, Mathf.Sin(progress * Mathf.PI) * 0.18f);
                    motes[index].transform.localScale = Vector3.one * size;
                }

                yield return null;
            }

            foreach (GameObject mote in motes) Destroy(mote);
        }

        private static IEnumerator PlayShield(IReadOnlyList<Vector3> impactPositions, Color effectColor)
        {
            List<GameObject> shells = impactPositions.Select(position =>
            {
                GameObject shell = CreateMarker("Shield Shell", PrimitiveType.Sphere, effectColor);
                shell.transform.position = position + Vector3.up * 0.48f;
                shell.transform.localScale = Vector3.one * 0.08f;
                return shell;
            }).ToList();

            float elapsed = 0f;
            const float duration = 0.52f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float size = 0.08f + Mathf.Sin(progress * Mathf.PI) * 0.86f;
                foreach (GameObject shell in shells)
                {
                    shell.transform.localScale = Vector3.one * size;
                }

                yield return null;
            }

            foreach (GameObject shell in shells) Destroy(shell);
        }

        private static IEnumerator PlaySpecial(
            Vector3 sourcePosition,
            Vector3 targetPosition,
            IReadOnlyList<Vector3> impactPositions,
            Color effectColor)
        {
            GameObject core = CreateMarker("Special Core", PrimitiveType.Cube, effectColor);
            core.transform.position = sourcePosition;
            core.transform.localScale = Vector3.one * 0.18f;
            float elapsed = 0f;
            const float travelDuration = 0.34f;
            while (elapsed < travelDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / travelDuration);
                core.transform.position = Vector3.Lerp(sourcePosition, targetPosition, progress) +
                                          Vector3.up * Mathf.Sin(progress * Mathf.PI) * 0.38f;
                core.transform.Rotate(Vector3.one, Time.unscaledDeltaTime * 420f, Space.World);
                yield return null;
            }

            Destroy(core);
            List<GameObject> pillars = impactPositions.Select(position =>
            {
                GameObject pillar = CreateMarker("Special Pillar", PrimitiveType.Cube, effectColor);
                pillar.transform.position = position;
                pillar.transform.localScale = new Vector3(0.16f, 0.02f, 0.16f);
                return pillar;
            }).ToList();
            elapsed = 0f;
            const float pulseDuration = 0.46f;
            while (elapsed < pulseDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / pulseDuration);
                float height = Mathf.Sin(progress * Mathf.PI) * 0.9f;
                foreach (GameObject pillar in pillars)
                {
                    pillar.transform.localScale = new Vector3(0.16f, Mathf.Max(0.02f, height), 0.16f);
                    pillar.transform.Rotate(Vector3.up, Time.unscaledDeltaTime * 180f, Space.World);
                }

                yield return null;
            }

            foreach (GameObject pillar in pillars) Destroy(pillar);
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
                    targets[index].SetHitFlash(1f - progress);
                    targets[index].transform.position = origins[index] + Vector3.right * kick;
                }

                yield return null;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                targets[index].RestoreColors();
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
