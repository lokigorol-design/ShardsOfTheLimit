using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// The Archon's panel on the right: his powers are not a unit but are always at hand on a player turn.
    /// Round icons belong to the Archon, square ones to the resonant. Replacement works; recall, divine intervention
    /// and buffs are reserved slots until their rules are set.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private const float ArchonIconSize = 62f;
        private static Texture2D archonDisk;
        private static Texture2D archonRing;
        private string archonTooltipTitle;
        private string archonTooltipBody;

        private static void EnsureArchonTextures()
        {
            if (archonDisk != null) return;
            archonDisk = CircleTexture(false);
            archonRing = CircleTexture(true);
        }

        /// <summary>Anti-aliased white disk or ring for round IMGUI icons.</summary>
        private static Texture2D CircleTexture(bool ring)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = ring ? "Archon Ring" : "Archon Disk", wrapMode = TextureWrapMode.Clamp };
            float radius = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                float outer = Mathf.Clamp01(radius - distance + 0.5f);
                float alpha = ring ? outer * Mathf.Clamp01(distance - (radius - 6f) + 0.5f) : outer;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            texture.Apply();
            return texture;
        }

        private void DrawArchonPanel(CombatUnit active)
        {
            if (blightgulletShowcase || active == null) return;
            EnsureArchonTextures();
            archonTooltipTitle = null;
            bool playerTurn = !showOnboarding && !battleEnded && active.ControlType == ControlType.Player;

            int slots = reserve.Count + 3;
            float height = 34f + slots * (ArchonIconSize + 24f);
            var panel = new Rect(Screen.width - ArchonIconSize - 40f, (Screen.height - height) * 0.5f + 20f, ArchonIconSize + 28f, height);
            Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + 6f, panel.width, 20f), "Архонт", hudTitleStyle.Center());
            float x = panel.x + 14f;
            float y = panel.y + 32f;

            string blocked = playerTurn ? CallBlockReason(active) : "Сейчас ход противника.";
            for (int index = 0; index < reserve.Count; index++)
            {
                ReserveSlot slot = reserve[index];
                bool available = blocked == null && slot.IsAvailable(turns.CurrentTick);
                string state = slot.Unit.IsDefeated ? "ПАЛ" : slot.IsCalled ? "ИДЁТ"
                    : !slot.IsAvailable(turns.CurrentTick) ? $"{slot.AvailableTick}т" : null;
                string label = Initials(slot.Unit.DisplayName);
                if (DrawRoundButton(new Rect(x, y, ArchonIconSize, ArchonIconSize), label, state, index == 0 ? "Z" : index == 1 ? "X" : null,
                        new Color(0.85f, 0.68f, 0.3f), available, slot.Unit.CurrentHealth / (float)slot.Unit.MaximumHealth))
                    RequestCall(index);
                if (new Rect(x, y, ArchonIconSize, ArchonIconSize).Contains(Event.current.mousePosition))
                {
                    archonTooltipTitle = "Замена: " + slot.Unit.DisplayName;
                    archonTooltipBody =
                        $"{ReserveRules.ActionCost} ОД — весь ход активного резонанта. {ReserveRules.SwapTicks} тиков он получает " +
                        "+100% урона, затем уходит в камень (эффект отзыва, отрицательные эффекты снимаются), а вызванный " +
                        "выходит на его место (эффект входа). Если активный падёт раньше, замены не будет. " +
                        $"Здоровье {slot.Unit.CurrentHealth}/{slot.Unit.MaximumHealth}." +
                        (blocked != null && playerTurn ? "\n" + blocked : string.Empty);
                }

                GUI.Label(new Rect(panel.x, y + ArchonIconSize + 1f, panel.width, 18f), "Замена", hudSmallStyle.Center());
                y += ArchonIconSize + 24f;
            }

            DrawReservedArchonSlot(panel, x, ref y, "ОТЗ", "Отзыв",
                "Вернуть резонанта в камень без замены. Правила ещё не заданы.");
            DrawReservedArchonSlot(panel, x, ref y, "БОЖ", "Божественное",
                "Божественное вмешательство: сильная способность покровителя. Правила ещё не заданы.");
            DrawReservedArchonSlot(panel, x, ref y, "БАФ", "Бафы",
                "Усиления Архонта для команды. Правила ещё не заданы.");

            if (archonTooltipTitle != null)
            {
                float width = 320f;
                float tipHeight = hudTextStyle.CalcHeight(new GUIContent(archonTooltipBody), width - 24f) + 42f;
                var tip = new Rect(panel.x - width - 10f, Mathf.Clamp(Event.current.mousePosition.y - 20f, 12f, Screen.height - tipHeight - 12f),
                    width, tipHeight);
                Panel(tip, false);
                GUI.Label(new Rect(tip.x + 12f, tip.y + 8f, tip.width - 24f, 24f), archonTooltipTitle, hudTitleStyle);
                GUI.Label(new Rect(tip.x + 12f, tip.y + 36f, tip.width - 24f, tipHeight - 40f), archonTooltipBody, hudTextStyle);
            }
        }

        private void DrawReservedArchonSlot(Rect panel, float x, ref float y, string label, string caption, string body)
        {
            var rect = new Rect(x, y, ArchonIconSize, ArchonIconSize);
            DrawRoundButton(rect, label, null, null, new Color(0.45f, 0.45f, 0.5f), false, -1f);
            if (rect.Contains(Event.current.mousePosition))
            {
                archonTooltipTitle = caption;
                archonTooltipBody = body;
            }

            GUI.Label(new Rect(panel.x, y + ArchonIconSize + 1f, panel.width, 18f), caption, hudSmallStyle.Center());
            y += ArchonIconSize + 24f;
        }

        /// <summary>Round Archon icon: tinted disk, gold ring, text, optional state, hotkey badge and health arc.</summary>
        private bool DrawRoundButton(Rect rect, string label, string state, string hotkey, Color tint, bool enabled, float health)
        {
            Color previous = GUI.color;
            GUI.color = enabled ? tint * 0.7f + new Color(0f, 0f, 0f, 0.3f) : new Color(0.18f, 0.18f, 0.2f, 0.95f);
            GUI.DrawTexture(rect, archonDisk);
            GUI.color = enabled ? new Color(1f, 0.85f, 0.4f) : new Color(0.45f, 0.42f, 0.36f);
            GUI.DrawTexture(rect, archonRing);
            GUI.color = previous;
            if (state == null) GUI.Label(rect, label, hudTitleStyle.Center());
            else
            {
                GUI.Label(new Rect(rect.x, rect.y + 8f, rect.width, rect.height * 0.45f), label, hudTitleStyle.Center());
                GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.52f, rect.width, 16f), state, hudBadgeStyle);
            }
            if (hotkey != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(rect.xMax - 18f, rect.y, 18f, 18f), archonDisk);
                GUI.color = previous;
                GUI.Label(new Rect(rect.xMax - 18f, rect.y, 18f, 18f), hotkey, hudBadgeStyle);
            }

            if (health >= 0f)
            {
                var bar = new Rect(rect.x + 10f, rect.yMax - 9f, rect.width - 20f, 4f);
                Fill(bar, new Color(0f, 0f, 0f, 0.7f));
                Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(health), bar.height), HealthColor);
            }

            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            return clicked && enabled;
        }

        private static string Initials(string name) =>
            string.Concat(name.Split(' ').Where(part => part.Length > 0).Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }
}
