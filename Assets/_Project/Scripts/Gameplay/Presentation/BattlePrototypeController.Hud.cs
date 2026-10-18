using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Mouse-first battle HUD: icon ability bar with tooltips, active unit card, portrait timeline,
    /// health plates above units and a hover card. Input that reaches the field is filtered by HUD rects.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private const float AbilityButtonSize = 74f;
        private const float PlateWidth = 86f;

        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.82f);
        private static readonly Color PanelEdgeColor = new Color(0.85f, 0.72f, 0.45f, 0.55f);
        private static readonly Color HealthColor = new Color(0.36f, 0.85f, 0.32f);
        private static readonly Color EnemyHealthColor = new Color(0.92f, 0.3f, 0.24f);
        private static readonly Color ShieldColor = new Color(0.35f, 0.75f, 1f);

        private readonly List<Rect> hudRects = new List<Rect>();
        private readonly Dictionary<string, Sprite> statusIcons = new Dictionary<string, Sprite>();
        private Texture2D whiteTexture;
        private GUIStyle hudTitleStyle;
        private GUIStyle hudTextStyle;
        private GUIStyle hudSmallStyle;
        private GUIStyle hudCenterStyle;
        private GUIStyle hudBadgeStyle;
        private GUIStyle hudButtonStyle;
        private int hoveredAbilityIndex = -1;
        private CombatUnit hoveredUnit;
        private GridCoordinate? hoveredCell;

        /// <summary>True when the pointer is over a HUD element drawn last frame (screen coordinates, y up).</summary>
        private bool IsPointerOverHud(Vector3 mousePosition)
        {
            var guiPoint = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            return hudRects.Any(rect => rect.Contains(guiPoint)) ||
                   (feedback != null && feedback.LogRect.Contains(guiPoint)) ||
                   showOnboarding || battleEnded;
        }

        private void LoadStatusIcons()
        {
            foreach (StatusDefinition definition in Resources.LoadAll<StatusDefinition>("Prototype/Statuses"))
            {
                if (definition.icon != null) statusIcons[definition.stableId] = definition.icon;
                if (definition.paletteOverride != null) statusPalettes[definition.stableId] = definition.paletteOverride;
            }

            if (burningIcon != null) statusIcons[BurningCalculator.ResistanceId] = burningIcon;
            // Burning is not a status asset: its master icon is colored with the fire palette.
            ResonantPalette fire = Resources.Load<ResonantPalette>("Prototype/Palettes/fire");
            if (fire != null) statusPalettes[BurningCalculator.ResistanceId] = fire;
            Sprite geyser = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireGeyser")?.Icon;
            if (geyser != null) statusIcons["GEYSER"] = geyser;
        }

        private void OnGUI()
        {
            EnsureInterfaceStyles();
            EnsureHudStyles();
            if (Event.current.type == EventType.Layout) hudRects.Clear();

            DrawMovementCostLabels();
            DrawAbilityPreviewLabels();
            DrawUnitPlates();
            DrawCloudInformation();

            CombatUnit active = turns?.CurrentUnit;
            DrawTopBar(active);
            DrawActiveUnitCard(active);
            DrawAbilityBar(active);
            DrawArchonPanel(active);
            DrawHoverCard();

            if (showOnboarding) DrawOnboarding();
            if (battleEnded) DrawBattleResult();
        }

        private void EnsureHudStyles()
        {
            if (hudTitleStyle != null) return;
            whiteTexture = Texture2D.whiteTexture;
            hudTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            hudTitleStyle.normal.textColor = new Color(1f, 0.93f, 0.78f);
            hudTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            hudTextStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
            hudSmallStyle = new GUIStyle(hudTextStyle) { fontSize = 11 };
            hudCenterStyle = new GUIStyle(hudTextStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            hudBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            hudBadgeStyle.normal.textColor = Color.white;
            hudButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true };
        }

        private void Panel(Rect rect, bool register = true)
        {
            if (register) hudRects.Add(rect);
            Fill(rect, PanelColor);
            Fill(new Rect(rect.x, rect.y, rect.width, 1f), PanelEdgeColor);
            Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), PanelEdgeColor * 0.6f);
        }

        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = previous;
        }

        private void Bar(Rect rect, float fraction, Color color)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.65f));
            Fill(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * Mathf.Clamp01(fraction), rect.height - 2f), color);
        }

        /// <summary>
        /// Draws an icon. A grayscale master from Abilities_v3 is colored by <paramref name="palette"/> (neutral when null)
        /// through <see cref="IconTintCache"/>; any other sprite is drawn as it is.
        /// </summary>
        private void Icon(Rect rect, Sprite sprite, ResonantPalette palette = null)
        {
            if (sprite == null) return;
            Texture tinted = IconTintCache.IsMaster(sprite) ? IconTintCache.Get(sprite, palette) : null;
            GUI.DrawTexture(rect, tinted != null ? tinted : sprite.texture, ScaleMode.ScaleAndCrop, true);
        }

        /// <summary>An ability is colored by its owner's palette unless the ability overrides it (e.g. blood for Blood Dash).</summary>
        private ResonantPalette PaletteFor(CombatUnit owner, AbilityDefinition ability)
        {
            if (ability != null && ability.PaletteOverride != null) return ability.PaletteOverride;
            return owner != null && definitionsByUnit.TryGetValue(owner, out UnitDefinition definition) ? definition.Palette : null;
        }

        private ResonantPalette PaletteOf(CombatUnit owner) =>
            owner != null && definitionsByUnit.TryGetValue(owner, out UnitDefinition definition) ? definition.Palette : null;

        private readonly Dictionary<string, ResonantPalette> statusPalettes = new Dictionary<string, ResonantPalette>();

        private void DrawTopBar(CombatUnit active)
        {
            if (turns == null) return;
            IReadOnlyList<TimelineEvent> upcoming = turns.GetUpcomingEvents(10, true, showStatusTimelineEvents);
            const float tile = 54f;
            const float gap = 6f;
            float width = upcoming.Count * (tile + gap) + 16f;
            float maximumWidth = Screen.width - 440f;
            float scale = width > maximumWidth ? maximumWidth / width : 1f;
            float tileSize = tile * scale;
            var panel = new Rect(12f, 12f, Mathf.Max(260f, width * scale), tileSize + 46f);
            Panel(panel);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 4f, panel.width - 20f, 20f),
                $"Очередь ходов · тик {turns.CurrentTick}", hudSmallStyle);
            float x = panel.x + 8f;
            foreach (TimelineEvent timelineEvent in upcoming)
            {
                CombatUnit unit = timelineEvent.Unit;
                var rect = new Rect(x, panel.y + 24f, tileSize, tileSize);
                bool isCurrent = timelineEvent.Kind == TimelineEventKind.Activation && unit == active &&
                                 timelineEvent.Tick == turns.CurrentTick;
                Color edge = unit.Team == Team.Player ? new Color(0.3f, 0.65f, 1f) : new Color(1f, 0.38f, 0.28f);
                if (isCurrent) edge = new Color(1f, 0.88f, 0.3f);
                Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), edge);
                Fill(rect, new Color(0.1f, 0.1f, 0.12f));
                if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                    Icon(rect, definition.Portrait);
                else
                    GUI.Label(rect, GetTimelineLabel(unit), hudBadgeStyle);

                if (timelineEvent.Kind == TimelineEventKind.StatusPulse)
                {
                    Fill(rect, new Color(0f, 0f, 0f, 0.55f));
                    string id = ResolveTimelineStatusId(timelineEvent.StatusId ?? string.Empty);
                    if (statusIcons.TryGetValue(id, out Sprite statusIcon))
                        Icon(new Rect(rect.x + rect.width * 0.25f, rect.y + rect.height * 0.25f, rect.width * 0.5f, rect.height * 0.5f), statusIcon,
                            statusPalettes.TryGetValue(id, out ResonantPalette timelinePalette) ? timelinePalette : null);
                    else
                        GUI.Label(rect, id == "MOVE CLOUD" ? "≈>" : id.Length > 5 ? id.Substring(0, 5) : id, hudBadgeStyle);
                }

                if (nextEnemyIntent != null && nextEnemyIntent.Unit == unit && timelineEvent.Kind == TimelineEventKind.Activation)
                {
                    Fill(new Rect(rect.x, rect.yMax - 16f, rect.width, 16f), new Color(0f, 0f, 0f, 0.7f));
                    GUI.Label(new Rect(rect.x, rect.yMax - 17f, rect.width, 18f), nextEnemyIntent.Codes, hudBadgeStyle);
                }

                x += tileSize + gap;
            }

            var toggleRect = new Rect(panel.x + panel.width - 150f, panel.y + 3f, 145f, 18f);
            showStatusTimelineEvents = GUI.Toggle(toggleRect, showStatusTimelineEvents, " события статусов");

            // Utility buttons under the action log.
            float buttonY = (feedback != null ? feedback.LogRect.yMax : 196f) + 6f;
            var utility = new Rect(Screen.width - 260f, buttonY, 248f, 34f);
            hudRects.Add(utility);
            if (GUI.Button(new Rect(utility.x, utility.y, 58f, 30f), "< Q")) RequestCameraRotation(-90f);
            if (GUI.Button(new Rect(utility.x + 62f, utility.y, 58f, 30f), "E >")) RequestCameraRotation(90f);
            if (GUI.Button(new Rect(utility.x + 124f, utility.y, 34f, 30f), "?")) { onboardingPage = 0; showOnboarding = true; }
            if (GUI.Button(new Rect(utility.x + 162f, utility.y, 86f, 30f), "Заново (R)")) Restart();
        }

        /// <summary>Timeline status ids: BURN, a status id, a cloud id (mapped to its status) or MOVE CLOUD.</summary>
        private string ResolveTimelineStatusId(string id)
        {
            if (id == "BURN") return BurningCalculator.ResistanceId;
            if (id.StartsWith("EXPIRE ")) return id.Substring(7);
            return cloudDefinitions.TryGetValue(id, out CloudDefinition cloud) && cloud.status != null ? cloud.status.stableId : id;
        }

        private void DrawActiveUnitCard(CombatUnit active)
        {
            if (active == null) return;
            var panel = new Rect(12f, Screen.height - 150f, 300f, 138f);
            Panel(panel);
            var portrait = new Rect(panel.x + 10f, panel.y + 10f, 84f, 84f);
            Fill(new Rect(portrait.x - 2f, portrait.y - 2f, portrait.width + 4f, portrait.height + 4f),
                active.Team == Team.Player ? new Color(0.3f, 0.65f, 1f) : new Color(1f, 0.38f, 0.28f));
            Fill(portrait, new Color(0.1f, 0.1f, 0.12f));
            if (definitionsByUnit.TryGetValue(active, out UnitDefinition definition)) Icon(portrait, definition.Portrait);

            float x = portrait.xMax + 10f;
            float width = panel.xMax - x - 10f;
            GUI.Label(new Rect(x, panel.y + 6f, width, 22f), active.DisplayName, hudTitleStyle);
            Bar(new Rect(x, panel.y + 32f, width, 12f), active.CurrentHealth / (float)active.MaximumHealth,
                active.Team == Team.Player ? HealthColor : EnemyHealthColor);
            GUI.Label(new Rect(x, panel.y + 29f, width, 16f), $"{active.CurrentHealth}/{active.MaximumHealth}", hudCenterStyle);
            if (active.MaximumShield > 0)
            {
                Bar(new Rect(x, panel.y + 47f, width, 8f), active.CurrentShield / (float)active.MaximumShield, ShieldColor);
            }

            GUI.Label(new Rect(x, panel.y + 58f, width, 18f),
                $"АТК {active.Attack}  ЗАЩ {active.EffectiveDefense:0.#}  СКОР {active.EffectiveSpeed:0.#}", hudSmallStyle);
            GUI.Label(new Rect(x, panel.y + 74f, 30f, 18f), "ОД", hudSmallStyle);
            // Banked Heat can start a turn with 3 AP.
            for (int index = 0; index < Mathf.Max(ActionsPerTurn, active.CurrentActionPoints); index++)
            {
                var pip = new Rect(x + 26f + index * 20f, panel.y + 78f, 14f, 14f);
                Fill(pip, new Color(0f, 0f, 0f, 0.7f));
                if (index < active.CurrentActionPoints) Fill(new Rect(pip.x + 2f, pip.y + 2f, 10f, 10f), new Color(1f, 0.82f, 0.25f));
            }

            // Branch passive (the part after the species name), e.g. "Twin Eruption"; B cycles it and restarts.
            string branch = BranchVariantName(active);
            if (branch.Contains("·")) branch = branch.Substring(branch.IndexOf('·') + 1).Trim();
            float branchX = x + 72f;
            PassiveModifierDefinition branchModifiers = ModifiersOf(active);
            Rect branchArea = new Rect(branchX, panel.y + 72f, width - 72f, 22f);
            if (branchModifiers != null && branchModifiers.icon != null)
            {
                // Branch passive icons (square: they belong to the resonant), colored like its abilities.
                Icon(new Rect(branchX, panel.y + 73f, 20f, 20f), branchModifiers.icon,
                    branchModifiers.paletteOverride != null ? branchModifiers.paletteOverride : PaletteOf(active));
                branchX += 22f;
                if (branchModifiers.secondaryIcon != null)
                {
                    Icon(new Rect(branchX, panel.y + 73f, 20f, 20f), branchModifiers.secondaryIcon,
                        branchModifiers.secondaryPaletteOverride != null ? branchModifiers.secondaryPaletteOverride : PaletteOf(active));
                    branchX += 22f;
                }
            }

            if (branch.Length > 0)
                GUI.Label(new Rect(branchX + 2f, panel.y + 74f, panel.xMax - branchX - 12f, 18f),
                    definition != null && definition.BranchVariants.Count > 1 && active.ControlType == ControlType.Player
                        ? $"{branch} [B]" : branch, hudSmallStyle);
            if (branchModifiers != null && branchArea.Contains(Event.current.mousePosition))
                DrawTextTooltip(new Rect(panel.x, panel.y, panel.width, 0f), BranchVariantName(active),
                    "Ветка резонанта: её пассивки и числа из карточки. B — сменить ветку (бой начнётся заново).",
                    branchModifiers.icon, branchModifiers.paletteOverride != null ? branchModifiers.paletteOverride : PaletteOf(active));
            DrawStatusIcons(active, new Rect(panel.x + 10f, panel.y + 102f, panel.width - 20f, 28f), 26f);
        }

        private void DrawStatusIcons(CombatUnit unit, Rect area, float size)
        {
            float x = area.x;
            foreach ((string id, int stacks, string tooltip) in GetStatusEntries(unit))
            {
                var rect = new Rect(x, area.y, size, size);
                Fill(rect, new Color(0f, 0f, 0f, 0.6f));
                if (statusIcons.TryGetValue(id, out Sprite icon))
                    Icon(rect, icon, statusPalettes.TryGetValue(id, out ResonantPalette statusPalette) ? statusPalette : null);
                else GUI.Label(rect, id.Substring(0, Mathf.Min(3, id.Length)), hudBadgeStyle);
                if (stacks > 1)
                {
                    GUI.Label(new Rect(rect.xMax - 16f, rect.yMax - 14f, 18f, 14f), stacks.ToString(), hudBadgeStyle);
                }

                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
                x += size + 4f;
                if (x > area.xMax - size) break;
            }
        }

        private IEnumerable<(string id, int stacks, string tooltip)> GetStatusEntries(CombatUnit unit)
        {
            if (unit.IsBurning)
                yield return (BurningCalculator.ResistanceId, 1,
                    $"Горение {unit.BurningState.DamagePerPulse}/100 тиков, ещё {unit.BurningState.RemainingPulses()}");
            foreach (StackStatusState state in unit.Statuses.Values)
                yield return (state.Rules.Id, state.Stacks, $"{state.Rules.Id} ×{state.Stacks} до тика {state.ExpireTick}");
            MagmaCell magma = areaEffects?.MagmaAt(unit.Cell);
            if (magma != null)
                yield return (AreaEffectWorld.MagmaResistanceId, 1, $"Стоит на Магме: {magma.Power} каждые 100 тиков, до тика {magma.ExpireTick}");
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
            float width = count * (AbilityButtonSize + 8f) + 150f;
            var panel = new Rect((Screen.width - width) * 0.5f, Screen.height - AbilityButtonSize - 46f, width, AbilityButtonSize + 34f);
            if (panel.x < 324f) panel.x = 324f;
            Panel(panel);

            GUI.Label(new Rect(panel.x + 10f, panel.y + 3f, panel.width - 20f, 20f), GetContextHint(), hudSmallStyle);
            float x = panel.x + 8f;
            float y = panel.y + 24f;

            // Move button: returns to movement mode (the default after every action).
            var moveRect = new Rect(x, y, AbilityButtonSize, AbilityButtonSize);
            bool moving = mode == InteractionMode.Movement;
            GUI.enabled = canAct;
            Fill(new Rect(moveRect.x - 2f, moveRect.y - 2f, moveRect.width + 4f, moveRect.height + 4f),
                moving && canAct ? new Color(0.35f, 0.75f, 1f) : new Color(0.25f, 0.25f, 0.28f));
            Sprite movementIcon = definition != null && active.MovementMode == MovementMode.Burrow ? definition.MovementIcon : null;
            if (GUI.Button(moveRect, movementIcon != null ? GUIContent.none
                    : new GUIContent(active.MovementMode == MovementMode.Burrow ? "ПОД\nЗЕМЛЁЙ\n2 ОД" : "ХОД\n1 ОД"), hudButtonStyle))
            {
                CancelAbility();
            }

            if (movementIcon != null)
            {
                // Deep Burrow: the worm's movement has its own icon.
                Icon(new Rect(moveRect.x + 4f, moveRect.y + 4f, moveRect.width - 8f, moveRect.height - 8f), movementIcon, PaletteOf(active));
                for (int ap = 0; ap < active.MovementActionCost; ap++)
                    Fill(new Rect(moveRect.xMax - 12f - ap * 12f, moveRect.y + 4f, 8f, 8f), new Color(1f, 0.82f, 0.25f));
            }

            if (moveRect.Contains(Event.current.mousePosition)) hoveredAbilityIndex = -2;
            x += AbilityButtonSize + 8f;

            for (int index = 0; index < abilities.Count; index++)
            {
                AbilityDefinition ability = abilities[index];
                var rect = new Rect(x, y, AbilityButtonSize, AbilityButtonSize);
                int cooldown = active.GetCooldown(ability.StableId);
                bool usable = canAct && CanUseAbility(active, ability);
                bool extending = IsExtending(active, ability);
                bool selected = mode == InteractionMode.AbilityTargeting && selectedAbilityIndex == index;
                Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f),
                    selected ? new Color(1f, 0.85f, 0.3f) : usable ? new Color(0.55f, 0.48f, 0.35f) : new Color(0.2f, 0.2f, 0.22f));
                GUI.enabled = usable;
                if (GUI.Button(rect, ability.Icon != null ? GUIContent.none : new GUIContent(ability.DisplayName), hudButtonStyle))
                {
                    if (selected) CancelAbility();
                    else BeginAbilityTargeting(index);
                }

                GUI.enabled = true;
                if (ability.Icon != null)
                    Icon(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f), ability.Icon, PaletteFor(active, ability));
                if (!usable) Fill(rect, new Color(0f, 0f, 0f, 0.55f));
                if (cooldown > 0) GUI.Label(rect, $"КД {cooldown}", hudTitleStyle.Center());
                Fill(new Rect(rect.x, rect.y, 18f, 18f), new Color(0f, 0f, 0f, 0.75f));
                GUI.Label(new Rect(rect.x, rect.y, 18f, 18f), (index + 1).ToString(), hudBadgeStyle);
                for (int ap = 0; ap < AbilityCost(active, ability); ap++)
                    Fill(new Rect(rect.xMax - 12f - ap * 12f, rect.y + 4f, 8f, 8f), new Color(1f, 0.82f, 0.25f));
                if (extending)
                {
                    Fill(new Rect(rect.x, rect.yMax - 18f, rect.width, 18f), new Color(0.6f, 0.15f, 0.02f, 0.85f));
                    GUI.Label(new Rect(rect.x, rect.yMax - 18f, rect.width, 18f), $"ПРОДЛ. {active.Channel.Stage}→", hudBadgeStyle);
                }
                if (rect.Contains(Event.current.mousePosition)) hoveredAbilityIndex = index;
                x += AbilityButtonSize + 8f;
            }

            if (abilities.Any(AbilityRuntime.IsSurfaceAbility))
            {
                // Ritual Surfaces: floor or wall for Surface abilities (Tab).
                var surface = new Rect(x, y, AbilityButtonSize, AbilityButtonSize);
                GUI.enabled = canAct;
                Fill(new Rect(surface.x - 2f, surface.y - 2f, surface.width + 4f, surface.height + 4f),
                    wallMode ? new Color(0.95f, 0.78f, 0.35f) : new Color(0.3f, 0.3f, 0.33f));
                Sprite surfaceIcon = definition != null ? definition.PassiveIcon : null;
                if (GUI.Button(surface, surfaceIcon != null ? GUIContent.none : new GUIContent(wallMode ? "СТЕНА\n(Tab)" : "ПОЛ\n(Tab)"),
                        hudButtonStyle)) ToggleWallMode();
                if (surfaceIcon != null)
                {
                    // Ritual Surfaces icon with the current mode written over it.
                    Icon(new Rect(surface.x + 4f, surface.y + 4f, surface.width - 8f, surface.height - 8f), surfaceIcon, PaletteOf(active));
                    Fill(new Rect(surface.x, surface.yMax - 18f, surface.width, 18f), new Color(0f, 0f, 0f, 0.7f));
                    GUI.Label(new Rect(surface.x, surface.yMax - 18f, surface.width, 18f), wallMode ? "СТЕНА · Tab" : "ПОЛ · Tab", hudBadgeStyle);
                }

                if (surface.Contains(Event.current.mousePosition)) hoveredAbilityIndex = -3;
                GUI.enabled = true;
            }

            GUI.enabled = canAct || facingStep && !isBusy;
            var endTurn = new Rect(panel.xMax - 140f, y + 6f, 130f, AbilityButtonSize - 12f);
            if (facingStep) Fill(new Rect(endTurn.x - 2f, endTurn.y - 2f, endTurn.width + 4f, endTurn.height + 4f), new Color(1f, 0.85f, 0.3f));
            string endLabel = facingStep ? "Подтвердить\nнаправление" : "Конец хода\n(Space)";
            if (GUI.Button(endTurn, endLabel, hudButtonStyle)) EndPlayerTurn();
            GUI.enabled = true;

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

        private void DrawAbilityTooltip(AbilityDefinition ability, CombatUnit active, Rect anchor)
        {
            string range = ability.TargetingPattern is RadiusPatternDefinition radius
                ? $"дальность {radius.Radius}"
                : ability.TargetingPattern is LinePatternDefinition line ? $"линия {line.Length}, первая цель" : "особая";
            string kind = ability.RangeType == AbilityRangeType.Melee ? "Ближний бой" : "Дальний бой";
            int cooldown = active.GetCooldown(ability.StableId);
            string body = $"{kind} · {range} · {ability.ActionCost} ОД · КД {ability.Cooldown}" +
                          (cooldown > 0 ? $" (ещё {cooldown})" : string.Empty) + "\n" + ability.Description;
            DrawTextTooltip(anchor, ability.DisplayName, body, ability.Icon, PaletteFor(active, ability));
        }

        private void DrawTextTooltip(Rect anchor, string title, string body, Sprite icon = null, ResonantPalette palette = null)
        {
            float width = 380f;
            float height = hudTextStyle.CalcHeight(new GUIContent(body), width - 24f) + 42f;
            var rect = new Rect(Mathf.Clamp(anchor.x, 12f, Screen.width - width - 12f), anchor.y - height - 8f, width, height);
            Panel(rect, false);
            float textX = rect.x + 12f;
            if (icon != null)
            {
                Icon(new Rect(rect.x + 10f, rect.y + 8f, 26f, 26f), icon, palette);
                textX += 32f;
            }

            GUI.Label(new Rect(textX, rect.y + 8f, rect.width - (textX - rect.x) - 10f, 24f), title, hudTitleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 36f, rect.width - 24f, height - 40f), body, hudTextStyle);
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
                float y = Screen.height - screen.y - 16f;
                bool isActive = unit == turns?.CurrentUnit;
                Fill(new Rect(x - 2f, y - 2f, PlateWidth + 4f, 15f),
                    isActive ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(0f, 0f, 0f, 0.6f));
                Bar(new Rect(x, y, PlateWidth, 7f), unit.CurrentHealth / (float)unit.MaximumHealth,
                    unit.Team == Team.Player ? HealthColor : EnemyHealthColor);
                if (unit.MaximumShield > 0)
                    Bar(new Rect(x, y + 7f, PlateWidth, 4f), unit.CurrentShield / (float)unit.MaximumShield, ShieldColor);
                DrawStatusIcons(unit, new Rect(x, y - 21f, PlateWidth + 40f, 18f), 18f);
            }
        }

        private void DrawHoverCard()
        {
            if (hoveredUnit == null || hoveredUnit.IsDefeated || showOnboarding) return;
            CombatUnit unit = hoveredUnit;
            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(Mathf.Min(mouse.x + 18f, Screen.width - 262f), Mathf.Min(mouse.y + 18f, Screen.height - 160f), 250f, 128f);
            Panel(rect, false);
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                Icon(new Rect(rect.x + 8f, rect.y + 8f, 44f, 44f), definition.Portrait);
            GUI.Label(new Rect(rect.x + 58f, rect.y + 6f, rect.width - 64f, 22f), unit.DisplayName, hudTitleStyle);
            GUI.Label(new Rect(rect.x + 58f, rect.y + 28f, rect.width - 64f, 18f),
                unit.Team == Team.Player ? "союзник" : "противник", hudSmallStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 56f, rect.width - 20f, 40f),
                $"Здоровье {unit.CurrentHealth}/{unit.MaximumHealth} · Щит {unit.CurrentShield}/{unit.MaximumShield}\n" +
                $"АТК {unit.Attack} · ЗАЩ {unit.EffectiveDefense:0.#} · СКОР {unit.Speed} · ход {MovementBudget(unit)}",
                hudSmallStyle);
            DrawStatusIcons(unit, new Rect(rect.x + 10f, rect.y + 96f, rect.width - 20f, 24f), 22f);
        }

        /// <summary>Per-frame pointer handling: hover previews, left click acts, right click cancels.</summary>
        private void UpdatePointer()
        {
            Vector3 mouse = Input.mousePosition;
            bool overHud = IsPointerOverHud(mouse);
            hoveredUnit = null;
            GridCoordinate? cell = null;
            if (!overHud && battleCamera != null &&
                Physics.Raycast(battleCamera.ScreenPointToRay(mouse), out RaycastHit hit, 200f))
            {
                CombatUnitView unitView = hit.collider.GetComponentInParent<CombatUnitView>();
                if (unitView != null && !unitView.Unit.IsDefeated)
                {
                    hoveredUnit = unitView.Unit;
                    cell = unitView.Unit.Cell;
                }
                else if (hit.collider.GetComponent<CellView>() is CellView cellView)
                {
                    cell = cellView.Coordinate;
                    hoveredUnit = units.FirstOrDefault(unit => !unit.IsDefeated && unit.Cell == cell.Value);
                }
            }

            bool canAct = !isBusy && !battleEnded && !showOnboarding && turns?.CurrentUnit?.ControlType == ControlType.Player &&
                          selectedUnit == turns.CurrentUnit;
            if (canAct && mode == InteractionMode.Facing)
            {
                UpdateFacingPointer(mouse, overHud);
                return;
            }
            if (cell != hoveredCell)
            {
                hoveredCell = cell;
                if (canAct) PreviewHoveredCell();
            }

            if (Input.GetMouseButtonDown(1)) rightPressPosition = mouse;
            if (Input.GetMouseButtonUp(1) && (mouse - rightPressPosition).sqrMagnitude < 36f && canAct)
            {
                if (mode == InteractionMode.AbilityTargeting) CancelAbility();
                else ClearMovementPreview();
            }

            if (!canAct || overHud || !Input.GetMouseButtonDown(0) || !hoveredCell.HasValue) return;
            GridCoordinate target = hoveredCell.Value;

            if (mode == InteractionMode.AbilityTargeting)
            {
                if (!targetable.Contains(target))
                {
                    status = "Эта клетка вне зелёной зоны целей.";
                    return;
                }

                if (selectedAbilityTarget != target) HandleCellChoice(target);
                ConfirmCurrentChoice();
                return;
            }

            if (reachable.ContainsKey(target)) RequestMove(target);
        }

        private Vector3 rightPressPosition;
        private Vector3 lastFacingPointer;

        /// <summary>Facing step: the side the pointer is on (around the unit) becomes the front; a click confirms.</summary>
        private void UpdateFacingPointer(Vector3 mouse, bool overHud)
        {
            if (overHud || battleCamera == null || selectedUnit == null) return;
            // Only a moving pointer re-aims; a resting cursor must not override a keyboard or button choice.
            bool pointerMoved = (mouse - lastFacingPointer).sqrMagnitude > 4f;
            lastFacingPointer = mouse;
            var ground = new Plane(Vector3.up, UnitWorldPosition(selectedUnit.Cell) - Vector3.up * 0.6f);
            Ray ray = battleCamera.ScreenPointToRay(mouse);
            if (pointerMoved && ground.Raycast(ray, out float distance))
            {
                Vector3 offset = ray.GetPoint(distance) - UnitWorldPosition(selectedUnit.Cell);
                if (new Vector2(offset.x, offset.z).sqrMagnitude > 0.04f)
                {
                    GridCoordinate direction = Mathf.Abs(offset.x) >= Mathf.Abs(offset.z)
                        ? new GridCoordinate(offset.x > 0 ? 1 : -1, 0)
                        : new GridCoordinate(0, offset.z > 0 ? 1 : -1);
                    if (direction != facingPreview) PreviewFacing(direction);
                }
            }

            if (Input.GetMouseButtonDown(0)) EndPlayerTurn();
            if (Input.GetMouseButtonDown(1)) rightPressPosition = mouse;
            if (Input.GetMouseButtonUp(1) && (mouse - rightPressPosition).sqrMagnitude < 36f) CancelFacing();
        }

        private void PreviewHoveredCell()
        {
            if (!hoveredCell.HasValue)
            {
                if (mode == InteractionMode.Movement) ClearMovementPreview();
                return;
            }

            GridCoordinate cell = hoveredCell.Value;
            if (mode == InteractionMode.AbilityTargeting)
            {
                if (targetable.Contains(cell) && selectedAbilityTarget != cell) HandleCellChoice(cell);
                return;
            }

            if (reachable.ContainsKey(cell))
            {
                if (selectedDestination != cell) HandleCellChoice(cell);
            }
            else
            {
                ClearMovementPreview();
            }
        }

        private void ClearMovementPreview()
        {
            if (mode != InteractionMode.Movement || !selectedDestination.HasValue) return;
            selectedDestination = null;
            selectedPath = System.Array.Empty<GridCoordinate>();
            RefreshHighlights();
        }
    }

    internal static class GuiStyleExtensions
    {
        public static GUIStyle Center(this GUIStyle style) => new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };
    }
}
