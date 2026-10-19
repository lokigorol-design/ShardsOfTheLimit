using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Comic-book look of the battle HUD (see <see cref="ComicUi"/>): paper panels with ink outlines and halftone,
    /// tilted caption boxes, coin badges, chunky bars and speech-bubble tooltips. Square icons belong to the resonant.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private GUIStyle hudShoutStyle;

        private void EnsureHudStyles()
        {
            if (hudTitleStyle != null) return;
            ComicUi.Ensure();
            whiteTexture = Texture2D.whiteTexture;
            hudTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            hudTitleStyle.normal.textColor = ComicUi.Ink;
            hudTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            hudTextStyle.normal.textColor = ComicUi.Ink;
            hudSmallStyle = new GUIStyle(hudTextStyle) { fontSize = 11, fontStyle = FontStyle.Bold };
            hudCenterStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hudCenterStyle.normal.textColor = Color.white;
            hudBadgeStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hudBadgeStyle.normal.textColor = Color.white;
            hudButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true
            };
            hudButtonStyle.normal.textColor = ComicUi.Ink;
            hudShoutStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        private void Panel(Rect rect, bool register = true)
        {
            if (register) hudRects.Add(rect);
            ComicUi.Panel(rect, ComicUi.Paper);
        }

        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = previous;
        }

        private void Bar(Rect rect, float fraction, Color color) => ComicUi.Bar(rect, fraction, color);

        private static Color TeamColor(CombatUnit unit) => unit.Team == Team.Player ? ComicUi.Cyan : ComicUi.Red;

        private void DrawTopBar(CombatUnit active)
        {
            if (turns == null) return;
            IReadOnlyList<TimelineEvent> upcoming = turns.GetUpcomingEvents(10, true, showStatusTimelineEvents);
            const float tile = 54f;
            const float gap = 8f;
            float width = upcoming.Count * (tile + gap) + 20f;
            float maximumWidth = Screen.width - 460f;
            float scale = width > maximumWidth ? maximumWidth / width : 1f;
            float tileSize = tile * scale;
            var panel = new Rect(14f, 24f, Mathf.Max(300f, width * scale), tileSize + 40f);
            Panel(panel);
            ComicUi.Caption(new Rect(panel.x + 12f, panel.y - 14f, 168f, 26f), $"Очередь · тик {turns.CurrentTick}", ComicUi.Yellow);
            float x = panel.x + 12f;
            foreach (TimelineEvent timelineEvent in upcoming)
            {
                CombatUnit unit = timelineEvent.Unit;
                bool isCurrent = timelineEvent.Kind == TimelineEventKind.Activation && unit == active &&
                                 timelineEvent.Tick == turns.CurrentTick;
                float grow = isCurrent ? 4f : 0f;
                var rect = new Rect(x - grow, panel.y + 18f - grow, tileSize + grow * 2f, tileSize + grow * 2f);
                Fill(rect, ComicUi.Night);
                if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                    Icon(rect, definition.Portrait);
                else
                    ComicUi.OutlinedText(rect, GetTimelineLabel(unit), hudBadgeStyle, Color.white);

                if (timelineEvent.Kind == TimelineEventKind.StatusPulse)
                {
                    Fill(rect, new Color(0f, 0f, 0f, 0.55f));
                    string id = ResolveTimelineStatusId(timelineEvent.StatusId ?? string.Empty);
                    if (statusIcons.TryGetValue(id, out Sprite statusIcon))
                        Icon(new Rect(rect.x + rect.width * 0.22f, rect.y + rect.height * 0.22f, rect.width * 0.56f, rect.height * 0.56f), statusIcon,
                            statusPalettes.TryGetValue(id, out ResonantPalette timelinePalette) ? timelinePalette : null);
                    else
                        ComicUi.OutlinedText(rect, id == "MOVE CLOUD" ? "≈>" : id.Length > 5 ? id.Substring(0, 5) : id, hudBadgeStyle, Color.white);
                }

                // Team strip under the tile, ink frame around it; the current turn is framed in yellow.
                Fill(new Rect(rect.x, rect.yMax - 5f, rect.width, 5f), TeamColor(unit));
                ComicUi.Frame(rect, isCurrent ? ComicUi.Yellow : ComicUi.Ink, isCurrent ? 4f : 3f);
                if (isCurrent) ComicUi.Frame(new Rect(rect.x - 4f, rect.y - 4f, rect.width + 8f, rect.height + 8f), ComicUi.Ink, 2f);

                if (nextEnemyIntent != null && nextEnemyIntent.Unit == unit && timelineEvent.Kind == TimelineEventKind.Activation)
                {
                    var tag = new Rect(rect.x - 2f, rect.yMax - 2f, rect.width + 4f, 16f);
                    ComicUi.Panel(tag, ComicUi.Red, false, 2f);
                    ComicUi.OutlinedText(tag, nextEnemyIntent.Codes, hudBadgeStyle, Color.white, null, 1f);
                }

                x += tileSize + gap;
            }

            var toggleRect = new Rect(panel.xMax - 132f, panel.y - 14f, 120f, 24f);
            if (ComicUi.Button(toggleRect, showStatusTimelineEvents ? "статусы: да" : "статусы: нет", hudButtonStyle, true,
                    showStatusTimelineEvents))
                showStatusTimelineEvents = !showStatusTimelineEvents;
            hudRects.Add(toggleRect);

            // Utility buttons under the action log.
            float buttonY = (feedback != null ? feedback.LogRect.yMax : 196f) + 12f;
            var utility = new Rect(Screen.width - 262f, buttonY, 250f, 34f);
            hudRects.Add(utility);
            if (ComicUi.Button(new Rect(utility.x, utility.y, 56f, 30f), "< Q", hudButtonStyle, true)) RequestCameraRotation(-90f);
            if (ComicUi.Button(new Rect(utility.x + 62f, utility.y, 56f, 30f), "E >", hudButtonStyle, true)) RequestCameraRotation(90f);
            if (ComicUi.Button(new Rect(utility.x + 124f, utility.y, 32f, 30f), "?", hudButtonStyle, true))
            {
                onboardingPage = 0;
                showOnboarding = true;
            }

            if (ComicUi.Button(new Rect(utility.x + 162f, utility.y, 88f, 30f), "Заново (R)", hudButtonStyle, true)) Restart();
        }

        private void DrawActiveUnitCard(CombatUnit active)
        {
            if (active == null) return;
            var panel = new Rect(14f, Screen.height - 156f, 310f, 142f);
            Panel(panel);
            var portrait = new Rect(panel.x + 12f, panel.y + 22f, 82f, 82f);
            Fill(portrait, ComicUi.Night);
            definitionsByUnit.TryGetValue(active, out UnitDefinition definition);
            if (definition != null) Icon(portrait, definition.Portrait);
            Fill(new Rect(portrait.x, portrait.yMax - 6f, portrait.width, 6f), TeamColor(active));
            ComicUi.Frame(portrait, ComicUi.Ink, 3f);
            ComicUi.Caption(new Rect(panel.x + 8f, panel.y - 14f, Mathf.Min(220f, 40f + active.DisplayName.Length * 9f), 28f),
                active.DisplayName, active.Team == Team.Player ? ComicUi.Yellow : new Color(1f, 0.6f, 0.5f), -3f, 16);

            float x = portrait.xMax + 12f;
            float width = panel.xMax - x - 12f;
            var health = new Rect(x, panel.y + 22f, width, 16f);
            Bar(health, active.CurrentHealth / (float)active.MaximumHealth, active.Team == Team.Player ? HealthColor : EnemyHealthColor);
            ComicUi.OutlinedText(health, $"{active.CurrentHealth}/{active.MaximumHealth}", hudCenterStyle, Color.white, null, 1.5f);
            if (active.MaximumShield > 0)
            {
                var shield = new Rect(x, panel.y + 40f, width, 11f);
                Bar(shield, active.CurrentShield / (float)active.MaximumShield, ShieldColor);
            }

            GUI.Label(new Rect(x, panel.y + 54f, width, 18f),
                $"АТК {active.Attack} · ЗАЩ {active.EffectiveDefense:0.#} · СКОР {active.EffectiveSpeed:0.#}", hudSmallStyle);
            GUI.Label(new Rect(x, panel.y + 76f, 30f, 18f), "ОД", hudSmallStyle);
            // Banked Heat can start a turn with 3 AP.
            for (int index = 0; index < Mathf.Max(ActionsPerTurn, active.CurrentActionPoints); index++)
                ComicUi.Coin(new Rect(x + 24f + index * 20f, panel.y + 76f, 17f, 17f),
                    index < active.CurrentActionPoints ? ComicUi.Yellow : new Color(0.4f, 0.38f, 0.36f, 1f));

            // Branch passive (the part after the species name); B cycles it and restarts.
            string branch = BranchVariantName(active);
            if (branch.Contains("·")) branch = branch.Substring(branch.IndexOf('·') + 1).Trim();
            float branchX = x + 72f;
            PassiveModifierDefinition branchModifiers = ModifiersOf(active);
            var branchArea = new Rect(branchX, panel.y + 74f, panel.xMax - branchX - 8f, 22f);
            if (branchModifiers != null && branchModifiers.icon != null)
            {
                Icon(new Rect(branchX, panel.y + 74f, 20f, 20f), branchModifiers.icon,
                    branchModifiers.paletteOverride != null ? branchModifiers.paletteOverride : PaletteOf(active));
                branchX += 22f;
                if (branchModifiers.secondaryIcon != null)
                {
                    Icon(new Rect(branchX, panel.y + 74f, 20f, 20f), branchModifiers.secondaryIcon,
                        branchModifiers.secondaryPaletteOverride != null ? branchModifiers.secondaryPaletteOverride : PaletteOf(active));
                    branchX += 22f;
                }
            }

            if (branch.Length > 0)
                GUI.Label(new Rect(branchX + 2f, panel.y + 76f, panel.xMax - branchX - 12f, 18f),
                    definition != null && definition.BranchVariants.Count > 1 && active.ControlType == ControlType.Player
                        ? $"{branch} [B]" : branch, hudSmallStyle);
            DrawStatusIcons(active, new Rect(panel.x + 12f, panel.y + 110f, panel.width - 24f, 24f), 24f);
            if (branchModifiers != null && branchArea.Contains(Event.current.mousePosition))
                DrawTextTooltip(new Rect(panel.x, panel.y - 10f, panel.width, 0f), BranchVariantName(active),
                    "Ветка резонанта: её пассивки и числа из карточки. B — сменить ветку (бой начнётся заново).",
                    branchModifiers.icon, branchModifiers.paletteOverride != null ? branchModifiers.paletteOverride : PaletteOf(active));
        }

        private void DrawStatusIcons(CombatUnit unit, Rect area, float size)
        {
            float x = area.x;
            foreach ((string id, int stacks, string tooltip) in GetStatusEntries(unit))
            {
                var rect = new Rect(x, area.y, size, size);
                Fill(rect, ComicUi.Night);
                if (statusIcons.TryGetValue(id, out Sprite icon))
                    Icon(rect, icon, statusPalettes.TryGetValue(id, out ResonantPalette statusPalette) ? statusPalette : null);
                else ComicUi.OutlinedText(rect, id.Substring(0, Mathf.Min(3, id.Length)), hudBadgeStyle, Color.white, null, 1f);
                ComicUi.Frame(rect, ComicUi.Ink, 2f);
                if (stacks > 1)
                {
                    var coin = new Rect(rect.xMax - 11f, rect.yMax - 11f, 15f, 15f);
                    ComicUi.Coin(coin, ComicUi.Yellow);
                    hudButtonStyle.fontSize = 10;
                    GUI.Label(coin, stacks.ToString(), hudButtonStyle);
                    hudButtonStyle.fontSize = 12;
                }

                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
                x += size + 6f;
                if (x > area.xMax - size) break;
            }
        }

        private void DrawAbilityBar(CombatUnit active)
        {
            hoveredAbilityIndex = -1;
            if (active == null) return;
            IReadOnlyList<AbilityDefinition> abilities = definitionsByUnit.TryGetValue(active, out UnitDefinition definition)
                ? definition.Abilities
                : new AbilityDefinition[0];
            bool facingStep = mode == InteractionMode.Facing && active.ControlType == ControlType.Player;
            bool canAct = !showOnboarding && !battleEnded && !isBusy && active.ControlType == ControlType.Player && !facingStep;
            float count = abilities.Count + 1 + (abilities.Any(AbilityRuntime.IsSurfaceAbility) ? 1 : 0);
            float width = count * (AbilityButtonSize + 10f) + 168f;
            var panel = new Rect((Screen.width - width) * 0.5f, Screen.height - AbilityButtonSize - 50f, width, AbilityButtonSize + 38f);
            if (panel.x < 340f) panel.x = 340f;
            Panel(panel);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 4f, panel.width - 24f, 20f), GetContextHint(), hudSmallStyle);
            float x = panel.x + 12f;
            float y = panel.y + 26f;

            // Move button: returns to movement mode (the default after every action).
            var moveRect = new Rect(x, y, AbilityButtonSize, AbilityButtonSize);
            bool moving = mode == InteractionMode.Movement;
            Sprite movementIcon = definition != null && active.MovementMode == MovementMode.Burrow ? definition.MovementIcon : null;
            if (ComicUi.Button(moveRect, movementIcon != null ? null
                    : active.MovementMode == MovementMode.Burrow ? "ПОД\nЗЕМЛЁЙ\n2 ОД" : "ХОД\n1 ОД", hudButtonStyle, canAct, moving && canAct))
                CancelAbility();
            if (movementIcon != null)
            {
                // Deep Burrow: the worm's movement has its own icon.
                Icon(new Rect(moveRect.x + 5f, moveRect.y + 5f, moveRect.width - 10f, moveRect.height - 10f), movementIcon, PaletteOf(active));
                for (int ap = 0; ap < active.MovementActionCost; ap++)
                    ComicUi.Coin(new Rect(moveRect.xMax - 16f - ap * 13f, moveRect.y - 4f, 13f, 13f), ComicUi.Yellow);
            }

            if (moveRect.Contains(Event.current.mousePosition)) hoveredAbilityIndex = -2;
            x += AbilityButtonSize + 10f;

            for (int index = 0; index < abilities.Count; index++)
            {
                AbilityDefinition ability = abilities[index];
                var rect = new Rect(x, y, AbilityButtonSize, AbilityButtonSize);
                int cooldown = active.GetCooldown(ability.StableId);
                bool usable = canAct && CanUseAbility(active, ability);
                bool extending = IsExtending(active, ability);
                bool selected = mode == InteractionMode.AbilityTargeting && selectedAbilityIndex == index;
                if (ComicUi.Button(rect, ability.Icon != null ? null : ability.DisplayName, hudButtonStyle, usable, selected))
                {
                    if (selected) CancelAbility();
                    else BeginAbilityTargeting(index);
                }

                if (ability.Icon != null)
                    Icon(new Rect(rect.x + 5f, rect.y + 5f, rect.width - 10f, rect.height - 10f), ability.Icon, PaletteFor(active, ability));
                if (!usable) Fill(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f), new Color(0.1f, 0.1f, 0.12f, 0.55f));
                if (cooldown > 0) ComicUi.OutlinedText(rect, $"КД {cooldown}", hudShoutStyle, Color.white);
                var key = new Rect(rect.x - 6f, rect.y - 6f, 20f, 20f);
                ComicUi.Coin(key, ComicUi.Ink);
                GUI.Label(key, (index + 1).ToString(), hudBadgeStyle);
                for (int ap = 0; ap < AbilityCost(active, ability); ap++)
                    ComicUi.Coin(new Rect(rect.xMax - 16f - ap * 13f, rect.y - 4f, 13f, 13f), ComicUi.Yellow);
                if (extending)
                {
                    var strip = new Rect(rect.x - 2f, rect.yMax - 18f, rect.width + 4f, 18f);
                    ComicUi.Panel(strip, ComicUi.Red, false, 2f);
                    ComicUi.OutlinedText(strip, $"ПРОДЛ. {active.Channel.Stage}→", hudBadgeStyle, Color.white, null, 1f);
                }

                if (rect.Contains(Event.current.mousePosition)) hoveredAbilityIndex = index;
                x += AbilityButtonSize + 10f;
            }

            if (abilities.Any(AbilityRuntime.IsSurfaceAbility))
            {
                // Ritual Surfaces: floor or wall for Surface abilities (Tab).
                var surface = new Rect(x, y, AbilityButtonSize, AbilityButtonSize);
                Sprite surfaceIcon = definition != null ? definition.PassiveIcon : null;
                if (ComicUi.Button(surface, surfaceIcon != null ? null : wallMode ? "СТЕНА\n(Tab)" : "ПОЛ\n(Tab)", hudButtonStyle, canAct, wallMode))
                    ToggleWallMode();
                if (surfaceIcon != null)
                {
                    Icon(new Rect(surface.x + 5f, surface.y + 5f, surface.width - 10f, surface.height - 10f), surfaceIcon, PaletteOf(active));
                    var strip = new Rect(surface.x - 2f, surface.yMax - 18f, surface.width + 4f, 18f);
                    ComicUi.Panel(strip, wallMode ? ComicUi.Yellow : ComicUi.Paper, false, 2f);
                    GUI.Label(strip, wallMode ? "СТЕНА · Tab" : "ПОЛ · Tab", hudButtonStyle);
                }

                if (surface.Contains(Event.current.mousePosition)) hoveredAbilityIndex = -3;
            }

            var endTurn = new Rect(panel.xMax - 150f, y + 4f, 136f, AbilityButtonSize - 8f);
            bool endEnabled = canAct || facingStep && !isBusy;
            if (ComicUi.Button(endTurn, null, hudButtonStyle, endEnabled, false, facingStep ? ComicUi.Yellow : ComicUi.Red))
                EndPlayerTurn();
            hudShoutStyle.fontSize = 17;
            ComicUi.OutlinedText(new Rect(endTurn.x, endTurn.y - 8f, endTurn.width, endTurn.height),
                facingStep ? "ГОТОВО!" : "КОНЕЦ ХОДА!", hudShoutStyle, endEnabled ? Color.white : new Color(0.8f, 0.8f, 0.8f));
            hudShoutStyle.fontSize = 20;
            GUI.Label(new Rect(endTurn.x, endTurn.yMax - 22f, endTurn.width, 18f), facingStep ? "направление · Enter" : "Space", hudSmallStyle.Center());

            if (hoveredAbilityIndex >= 0) DrawAbilityTooltip(abilities[hoveredAbilityIndex], active, panel);
            else if (hoveredAbilityIndex == -3) DrawTextTooltip(panel, "Ритуальные поверхности",
                "Способности «Поверхность» применяются к полу или к стене. Стена — столб на 2+ единицы выше соседней клетки " +
                "или препятствие, обращённое к тотему; эффект выходит из неё перпендикулярно в клетку перед ней.",
                definition != null ? definition.PassiveIcon : null, PaletteOf(active));
            else if (hoveredAbilityIndex == -2) DrawTextTooltip(panel, active.MovementMode == MovementMode.Burrow ? "Подземный ход" : "Перемещение",
                active.MovementMode == MovementMode.Burrow
                    ? $"Подземный ход: 2 ОД, выныривает в любой свободной клетке в радиусе {MovementBudget(active)}. Высота и стены не мешают."
                    : $"Шаг по клеткам: {MovementBudget(active)} очков передвижения за 1 ОД. Подъём стоит дороже.",
                movementIcon, PaletteOf(active));
        }

        /// <summary>Speech-bubble tooltip above the anchor: icon, title caption and text.</summary>
        private void DrawTextTooltip(Rect anchor, string title, string body, Sprite icon = null, ResonantPalette palette = null)
        {
            float width = 390f;
            float height = hudTextStyle.CalcHeight(new GUIContent(body), width - 28f) + 50f;
            var rect = new Rect(Mathf.Clamp(anchor.x, 14f, Screen.width - width - 14f), anchor.y - height - 26f, width, height);
            ComicUi.Bubble(rect);
            float textX = rect.x + 14f;
            if (icon != null)
            {
                var iconRect = new Rect(rect.x + 12f, rect.y + 10f, 30f, 30f);
                Icon(iconRect, icon, palette);
                textX += 38f;
            }

            GUI.Label(new Rect(textX, rect.y + 12f, rect.width - (textX - rect.x) - 12f, 24f), title.ToUpperInvariant(), hudTitleStyle);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 44f, rect.width - 28f, height - 48f), body, hudTextStyle);
        }

        /// <summary>Health, shield and status icons floating over every living resonant.</summary>
        private void DrawUnitPlates()
        {
            if (battleCamera == null) return;
            foreach (CombatUnit unit in units)
            {
                if (unit.IsDefeated || !unitViews.TryGetValue(unit, out CombatUnitView view) || !view.gameObject.activeInHierarchy)
                    continue;
                Vector3 screen = battleCamera.WorldToScreenPoint(view.transform.position + Vector3.up * 1.05f);
                if (screen.z <= 0f) continue;
                float x = screen.x - PlateWidth * 0.5f;
                float y = Screen.height - screen.y - 18f;
                bool isActive = unit == turns?.CurrentUnit;
                if (isActive) ComicUi.Frame(new Rect(x - 1f, y - 1f, PlateWidth + 2f, unit.MaximumShield > 0 ? 16f : 12f), ComicUi.Yellow, 3f);
                Bar(new Rect(x, y, PlateWidth, 10f), unit.CurrentHealth / (float)unit.MaximumHealth,
                    unit.Team == Team.Player ? HealthColor : EnemyHealthColor);
                if (unit.MaximumShield > 0)
                    Bar(new Rect(x, y + 9f, PlateWidth, 6f), unit.CurrentShield / (float)unit.MaximumShield, ShieldColor);
                DrawStatusIcons(unit, new Rect(x, y - 23f, PlateWidth + 40f, 19f), 19f);
            }
        }

        private void DrawHoverCard()
        {
            if (hoveredUnit == null || hoveredUnit.IsDefeated || showOnboarding) return;
            CombatUnit unit = hoveredUnit;
            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(Mathf.Min(mouse.x + 22f, Screen.width - 270f), Mathf.Min(mouse.y + 28f, Screen.height - 170f), 256f, 130f);
            Panel(rect, false);
            var portrait = new Rect(rect.x + 10f, rect.y + 18f, 44f, 44f);
            Fill(portrait, ComicUi.Night);
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null) Icon(portrait, definition.Portrait);
            Fill(new Rect(portrait.x, portrait.yMax - 4f, portrait.width, 4f), TeamColor(unit));
            ComicUi.Frame(portrait, ComicUi.Ink, 2f);
            ComicUi.Caption(new Rect(rect.x + 6f, rect.y - 12f, Mathf.Min(220f, 40f + unit.DisplayName.Length * 8.5f), 24f), unit.DisplayName,
                unit.Team == Team.Player ? ComicUi.Yellow : new Color(1f, 0.6f, 0.5f), -2f, 14);
            GUI.Label(new Rect(rect.x + 62f, rect.y + 18f, rect.width - 70f, 18f), unit.Team == Team.Player ? "союзник" : "противник", hudSmallStyle);
            GUI.Label(new Rect(rect.x + 62f, rect.y + 36f, rect.width - 70f, 40f),
                $"HP {unit.CurrentHealth}/{unit.MaximumHealth} · Щит {unit.CurrentShield}/{unit.MaximumShield}\n" +
                $"АТК {unit.Attack} · ЗАЩ {unit.EffectiveDefense:0.#} · СКОР {unit.Speed} · ход {MovementBudget(unit)}",
                hudSmallStyle);
            DrawStatusIcons(unit, new Rect(rect.x + 10f, rect.y + 96f, rect.width - 20f, 24f), 22f);
        }
    }
}
