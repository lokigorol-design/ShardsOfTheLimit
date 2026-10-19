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

        /// <summary>Timeline status ids: BURN, a status id, a cloud id (mapped to its status) or MOVE CLOUD.</summary>
        private string ResolveTimelineStatusId(string id)
        {
            if (id == "BURN") return BurningCalculator.ResistanceId;
            if (id.StartsWith("EXPIRE ")) return id.Substring(7);
            return cloudDefinitions.TryGetValue(id, out CloudDefinition cloud) && cloud.status != null ? cloud.status.stableId : id;
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
