using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Scene, input and placeholder presentation adapter for the fixed stage 2 battle.</summary>
    public sealed partial class BattlePrototypeController : MonoBehaviour
    {
        private enum InteractionMode
        {
            Movement,
            AbilityTargeting,
            /// <summary>End-of-turn step: the player chooses where the resonant faces.</summary>
            Facing
        }

        private sealed class AiAbilityPlan
        {
            public AbilityDefinition Ability;
            public GridCoordinate Target;
            public int DamageValue;
            public int HealingValue;
            public int UtilityValue;
        }

        private sealed class EnemyIntentPreview
        {
            public CombatUnit Unit;
            public BattleIntentKind First;
            public BattleIntentKind Second;

            public string Codes =>
                $"[{BattleReadabilityRules.GetIntentCode(First)}][{BattleReadabilityRules.GetIntentCode(Second)}]";
        }

        private const int GridWidth = 10;
        private const int GridHeight = 10;
        private const int MinimumTerrainHeight = -4;
        private const int MaximumTerrainHeight = 5;
        private const float CellSpacing = 1.05f;
        private const float HeightStep = 0.55f;
        private const int ActionsPerTurn = 2;
        private const float DefaultCameraZoom = 9.2f;
        private const float MinimumCameraZoom = 4.25f;
        private const float MaximumCameraZoom = 11.5f;
        private const float CameraZoomStep = 0.8f;
        private const float CameraZoomSpeed = 10f;
        private const float MinimumCameraPitch = 30f;
        private const float MaximumCameraPitch = 78f;
        private const float CameraKeyboardPanSpeed = 0.85f;

        private static readonly Color ReachableColor = new Color(0.18f, 0.48f, 0.92f);
        private static readonly Color PathColor = new Color(0.15f, 0.9f, 1f);
        private static readonly Color TargetableColor = new Color(0.25f, 0.75f, 0.35f);
        private static readonly Color TargetingRangeColor = new Color(0.18f, 0.38f, 0.22f);
        private static readonly Color ImpactColor = new Color(1f, 0.38f, 0.12f);
        private static readonly Color ForcedPathColor = new Color(0.68f, 0.28f, 0.92f);
        private static readonly Color ForcedDestinationColor = new Color(1f, 0.2f, 0.92f);
        private static readonly Color SelectedCellColor = new Color(1f, 0.9f, 0.2f);
        private static readonly Color FacingOptionColor = new Color(0.85f, 0.55f, 0.25f);
        private static readonly Vector3 DefaultCameraPivot = new Vector3(0f, 0.2f, 0f);
        private static readonly Vector3 DefaultCameraOffset = new Vector3(10.5f, 11.5f, -11.2f);
        // Tabletop camera: a perspective lens looking down at the board like a player leaning over a table.
        // Zoom is the dolly distance (zoom × CameraDistancePerZoom world units), not an orthographic size.
        private const float CameraFieldOfView = 34f;
        private const float CameraDistancePerZoom = 2.2f;
        private const float DefaultCameraPitch = 52f;

        private readonly Dictionary<GridCoordinate, CellView> cellViews = new Dictionary<GridCoordinate, CellView>();
        private readonly Dictionary<CombatUnit, CombatUnitView> unitViews = new Dictionary<CombatUnit, CombatUnitView>();
        private readonly Dictionary<CombatUnit, UnitDefinition> definitionsByUnit = new Dictionary<CombatUnit, UnitDefinition>();
        private readonly Dictionary<CombatUnit, float> aiDecisionRolls = new Dictionary<CombatUnit, float>();
        private readonly List<CombatUnit> units = new List<CombatUnit>();

        private TacticalGrid grid;
        private Camera battleCamera;
        private float cameraRotation;
        private float targetCameraRotation;
        private float cameraPitch = DefaultCameraPitch;
        private float targetCameraPitch = DefaultCameraPitch;
        private float cameraZoom = DefaultCameraZoom;
        private float targetCameraZoom = DefaultCameraZoom;
        private Vector2 previousCameraPointer;
        private Vector3 cameraPivot = DefaultCameraPivot;
        private GUIStyle movementCostStyle;
        private GUIStyle abilityPreviewStyle;
        private GUIStyle statusEffectStyle;
        private GUIStyle timelineStyle;
        private GUIStyle timelineActiveStyle;
        private GUIStyle onboardingTitleStyle;
        private GUIStyle onboardingBodyStyle;
        private GUIStyle resultTitleStyle;
        private IReadOnlyList<UnitDefinition> playerDefinitions = Array.Empty<UnitDefinition>();
        private IReadOnlyList<UnitDefinition> opponentDefinitions = Array.Empty<UnitDefinition>();
        private CombatUnit selectedUnit;
        private int selectedAbilityIndex;
        private TimelineCoordinator turns;
        private BattleFeedbackView feedback;
        private InteractionMode mode;
        private MovementMap movementMap;
        private IReadOnlyDictionary<GridCoordinate, int> reachable = new Dictionary<GridCoordinate, int>();
        private IReadOnlyList<GridCoordinate> selectedPath = Array.Empty<GridCoordinate>();
        private IReadOnlyCollection<GridCoordinate> targetable = Array.Empty<GridCoordinate>();
        private IReadOnlyCollection<GridCoordinate> targetingRange = Array.Empty<GridCoordinate>();
        private IReadOnlyCollection<GridCoordinate> previewImpact = Array.Empty<GridCoordinate>();
        private AbilityResolution abilityPreview;
        private GridCoordinate? selectedDestination;
        private GridCoordinate? selectedAbilityTarget;
        private bool isBusy;
        private bool battleEnded;
        private bool showOnboarding = true;
        private bool showStatusTimelineEvents = true;
        private int onboardingPage;
        private EnemyIntentPreview nextEnemyIntent;
        private BattleOutcome outcome = BattleOutcome.Ongoing;
        private Sprite burningIcon;
        private float battleStartedAt;
        private int resolvedActions;
        private string status = "Select the blue Echo.";

        public CombatUnit ActiveUnit => turns?.CurrentUnit;
        public long CurrentTimelineTick => turns?.CurrentTick ?? 0;
        public IReadOnlyList<CombatUnit> Units => units;
        public IReadOnlyDictionary<GridCoordinate, int> CurrentReachableCells => reachable;
        public float CameraRotation => cameraRotation;
        public float CameraPitch => cameraPitch;
        public float CameraZoom => cameraZoom;
        /// <summary>Distance from the camera to the board pivot it orbits.</summary>
        public float CameraDistance => cameraZoom * CameraDistancePerZoom;
        public Vector3 CameraPivotPosition => cameraPivot;
        public bool BattleEnded => battleEnded;
        public BattleOutcome Outcome => outcome;
        public CombatUnit NextIntentUnit => nextEnemyIntent?.Unit;
        public string NextEnemyIntentCodes => nextEnemyIntent?.Codes ?? string.Empty;

        private AbilityDefinition SelectedAbility
        {
            get
            {
                if (selectedUnit == null || !definitionsByUnit.TryGetValue(selectedUnit, out UnitDefinition definition))
                {
                    return null;
                }

                return selectedAbilityIndex >= 0 && selectedAbilityIndex < definition.Abilities.Count
                    ? definition.Abilities[selectedAbilityIndex]
                    : null;
            }
        }

        public void RequestEndTurn()
        {
            EndPlayerTurn();
        }

        public bool RequestMove(GridCoordinate destination)
        {
            if (battleEnded || isBusy || mode != InteractionMode.Movement || selectedUnit == null ||
                turns?.CurrentUnit != selectedUnit || selectedUnit.ControlType != ControlType.Player || selectedUnit.IsImmobilized ||
                !reachable.ContainsKey(destination))
            {
                return false;
            }

            IReadOnlyList<GridCoordinate> path = StopAtTrap(movementMap?.GetPath(destination) ?? Array.Empty<GridCoordinate>());
            if (path.Count <= 1) return false;

            selectedDestination = destination;
            selectedPath = path;
            StartCoroutine(MoveSelectedUnit(path));
            return true;
        }

        private void Start()
        {
            LoadDefinitions();
            BuildGrid();
            ConfigureCameraAndLight();
            feedback = gameObject.AddComponent<BattleFeedbackView>();
            LoadStatusIcons();

            SpawnUnits();
            InitializeAreaEffects();
            turns = new TimelineCoordinator(units, ActionsPerTurn) { AreaEffects = areaEffects };
            battleStartedAt = Time.unscaledTime;
            BeginTurn(turns.StartBattle());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                Restart();
                return;
            }

            UpdateCameraControls();
            EmitFieldMarks();

            if (showOnboarding)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                    Input.GetKeyDown(KeyCode.Space))
                {
                    AdvanceOnboarding();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseOnboarding();
                }

                return;
            }

            if (isBusy || battleEnded || turns?.CurrentUnit?.ControlType != ControlType.Player)
            {
                return;
            }

            for (int index = 0; index < 6; index++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)))
                {
                    // Pressing the selected ability's key again returns to movement.
                    if (mode == InteractionMode.AbilityTargeting && selectedAbilityIndex == index) CancelAbility();
                    else BeginAbilityTargeting(index);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (mode == InteractionMode.Facing) CancelFacing();
                else CancelAbility();
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (mode == InteractionMode.Facing) EndPlayerTurn();
                else ConfirmCurrentChoice();
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                EndPlayerTurn();
            }

            UpdatePointer();
        }

        private float fieldMarkBudget;

        /// <summary>Pending Fire Geyser marks and Lift Traps stay visible to everyone until they fire or expire.</summary>
        private void EmitFieldMarks()
        {
            if (areaEffects == null) return;
            fieldMarkBudget += Time.deltaTime * 12f;
            if (fieldMarkBudget < 1f) return;
            fieldMarkBudget -= 1f;
            foreach (DelayedStrike strike in areaEffects.PendingStrikes)
                foreach (GridCoordinate cell in strike.Cells) AbilityVfx.GeyserMark(EffectWorldPosition(cell));
            foreach (TrapObject trap in areaEffects.Traps) AbilityVfx.TrapMark(EffectWorldPosition(trap.Cell));
        }

        private void LoadDefinitions()
        {
            playerDefinitions = new[]
            {
                Resources.Load<UnitDefinition>(blightgulletShowcase ? "Prototype/Units/Blightgullet" : "Prototype/Units/BlazeWorm"),
                // The main battle fields both authored resonants: the worm and the toad.
                Resources.Load<UnitDefinition>(blightgulletShowcase ? "Prototype/Units/RangerEcho" : "Prototype/Units/Blightgullet"),
                // The third authored resonant: Primal Idol replaces the technical skirmisher in the main battle.
                Resources.Load<UnitDefinition>(blightgulletShowcase ? "Prototype/Units/SkirmisherEcho" : "Prototype/Units/PrimalIdol")
            };
            opponentDefinitions = new[]
            {
                Resources.Load<UnitDefinition>("Prototype/Units/TargetDummy"),
                Resources.Load<UnitDefinition>("Prototype/Units/RaiderEnemy")
            };

            UnitDefinition[] allDefinitions = playerDefinitions.Concat(opponentDefinitions).ToArray();
            if (allDefinitions.Any(definition => definition == null))
            {
                throw new InvalidOperationException(
                    "Prototype definitions are missing under Assets/_Project/Resources/Prototype.");
            }

            AbilityDefinition[] abilities = allDefinitions
                .SelectMany(definition => definition.Abilities)
                .Distinct()
                .ToArray();
            string error = allDefinitions.SelectMany(definition => definition.GetValidationErrors())
                .Concat(abilities.SelectMany(ability => ability.GetValidationErrors()))
                .FirstOrDefault(message => !message.Contains("primitive fallback"));
            if (error != null)
            {
                throw new InvalidOperationException($"Invalid prototype content: {error}");
            }

            if (abilities.Length < 7)
            {
                throw new InvalidOperationException("Prototype content must expose seven to sixteen distinct abilities.");
            }

            ApplyBurnEffectDefinition burn =
                Resources.Load<ApplyBurnEffectDefinition>("Prototype/Effects/ApplyBurnTenPercent");
            if (burn == null || burn.Icon == null)
            {
                throw new InvalidOperationException("Burn effect definition or its status icon is missing.");
            }

            burningIcon = burn.Icon;
        }

        private void SpawnUnits()
        {
            SpawnUnit(playerDefinitions[0], Team.Player, blightgulletShowcase ? blightgulletControl : ControlType.Player, new GridCoordinate(0, 0), new Color(1f, 0.26f, 0.03f));
            SpawnUnit(playerDefinitions[1], Team.Player, ControlType.Player, new GridCoordinate(1, 4), new Color(0.18f, 0.82f, 1f));
            SpawnUnit(playerDefinitions[2], Team.Player, ControlType.Player, new GridCoordinate(1, 1), new Color(0.1f, 0.86f, 0.68f));
            SpawnUnit(opponentDefinitions[0], Team.Opponent, ControlType.BasicAI, blightgulletShowcase ? new GridCoordinate(4, 0) : new GridCoordinate(7, 7), new Color(0.95f, 0.2f, 0.18f));
            SpawnUnit(opponentDefinitions[1], Team.Opponent, ControlType.BasicAI, new GridCoordinate(7, 3), new Color(1f, 0.52f, 0.12f));
            SpawnUnit(opponentDefinitions[1], Team.Opponent, ControlType.BasicAI, new GridCoordinate(5, 6), new Color(0.95f, 0.72f, 0.12f));
        }

        private void SpawnUnit(
            UnitDefinition definition,
            Team team,
            ControlType controlType,
            GridCoordinate coordinate,
            Color fallbackColor)
        {
            var unit = new CombatUnit(
                definition.StableId,
                definition.DisplayName,
                definition.MaximumHealth,
                definition.MaximumShield,
                definition.Speed,
                definition.MovementRange,
                team,
                controlType,
                coordinate,
                definition.Attack,
                definition.Defense);
            unit.ConfigureMovement(definition.MovementMode, definition.MovementActionCost);
            foreach (PeriodicResistance resistance in definition.PeriodicResistances)
                if (!string.IsNullOrWhiteSpace(resistance.sourceId))
                    unit.SetPeriodicResistance(resistance.sourceId, resistance.percent);
            units.Add(unit);
            definitionsByUnit.Add(unit, definition);

            GameObject body = definition.VisualPrefab != null
                ? CreateAuthoredUnitView(definition)
                : PlaceholderModelFactory.Create(definition, team, fallbackColor);
            body.name = $"{definition.DisplayName} [{team}]";
            body.transform.SetParent(transform, false);
            body.transform.position = UnitWorldPosition(coordinate);
            // Teams start facing each other along X; the player re-chooses facing at the end of every turn.
            unit.SetFacing(team == Team.Player ? new GridCoordinate(1, 0) : new GridCoordinate(-1, 0));
            body.transform.rotation = Quaternion.LookRotation(new Vector3(unit.Facing.X, 0f, unit.Facing.Y));
            body.transform.localScale = Vector3.one;
            CombatUnitView view = body.AddComponent<CombatUnitView>();
            view.Initialize(unit, fallbackColor);
            body.AddComponent<StatusAuraView>().Initialize(unit);
            CreateFacingWedge(body.transform, team);
            unitViews.Add(unit, view);
        }

        private static Material facingWedgeMaterial;

        /// <summary>Flat arrow on the ground in front of a resonant: shows its front at a glance.</summary>
        private static void CreateFacingWedge(Transform unitRoot, Team team)
        {
            if (facingWedgeMaterial == null) facingWedgeMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Facing Wedge" };
            var wedge = new GameObject("Facing Wedge");
            wedge.transform.SetParent(unitRoot, false);
            wedge.transform.localPosition = new Vector3(0f, -0.58f, 0.36f);
            Color color = team == Team.Player ? new Color(0.35f, 0.75f, 1f, 0.85f) : new Color(1f, 0.38f, 0.28f, 0.85f);
            var mesh = new Mesh { name = "Facing Wedge" };
            mesh.vertices = new[] { new Vector3(0f, 0f, 0.2f), new Vector3(0.17f, 0f, -0.02f), new Vector3(-0.17f, 0f, -0.02f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            mesh.colors = new[] { color, color * 0.8f, color * 0.8f };
            mesh.RecalculateBounds();
            wedge.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = wedge.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = facingWedgeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static GameObject CreateAuthoredUnitView(UnitDefinition definition)
        {
            var root = new GameObject($"{definition.DisplayName} Authored Model Root");
            GameObject visual = Instantiate(definition.VisualPrefab, root.transform);
            visual.name = $"{definition.DisplayName} Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(definition.VisualRotationOffset);
            visual.transform.localScale = Vector3.one;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Destroy(root);
                throw new InvalidOperationException($"Authored model for {definition.DisplayName} has no renderers.");
            }

            if (definition.VisualMaterial != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        materials[index] = definition.VisualMaterial;
                    }

                    renderer.sharedMaterials = materials;
                }
            }

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Vector3 alignment = new Vector3(-bounds.center.x, -0.62f - bounds.min.y, -bounds.center.z);
            visual.transform.localPosition += alignment;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center + alignment;
            collider.size = new Vector3(
                Mathf.Clamp(bounds.size.x, 0.28f, 0.86f),
                Mathf.Max(0.35f, bounds.size.y),
                Mathf.Clamp(bounds.size.z, 0.28f, 0.86f));
            return root;
        }

        private void HandleCellChoice(GridCoordinate coordinate)
        {
            if (selectedUnit == null || turns.CurrentUnit != selectedUnit || selectedUnit.CurrentActionPoints <= 0)
            {
                status = "No action points remain. End the turn with Space.";
                return;
            }

            if (mode == InteractionMode.AbilityTargeting)
            {
                AbilityDefinition ability = SelectedAbility;
                if (ability == null)
                {
                    status = "The selected unit has no ability in that slot.";
                    return;
                }

                if (!targetable.Contains(coordinate))
                {
                    status = "That cell is outside the green targeting area.";
                    return;
                }

                selectedAbilityTarget = coordinate;
                abilityPreview = AbilityRuntime.Preview(
                    ability, grid, selectedUnit, coordinate, units, turns.CurrentTick, areaEffects, definitionsByUnit[selectedUnit].PassiveModifiers);
                previewImpact = abilityPreview.ImpactCells;
                selectedUnit.CurrentAction = $"Previewing {ability.DisplayName} at {coordinate}";
                status = $"Orange cells show impact. {BuildAbilitySummary(abilityPreview, true)} Confirm with Enter.";

                RefreshHighlights();
                return;
            }

            if (!reachable.ContainsKey(coordinate))
            {
                status = "That cell is not reachable.";
                return;
            }

            IReadOnlyList<GridCoordinate> path = movementMap?.GetPath(coordinate) ?? Array.Empty<GridCoordinate>();
            if (path.Count == 0)
            {
                status = "No legal path found.";
                return;
            }

            if (selectedDestination == coordinate)
            {
                RequestMove(coordinate);
                return;
            }

            selectedDestination = coordinate;
            selectedPath = path;
            int movementCost = reachable[coordinate];
            selectedUnit.CurrentAction = $"Path to {coordinate} ({movementCost}/{MovementBudget(selectedUnit)} movement)";
            status = $"Cyan path costs {movementCost}/{MovementBudget(selectedUnit)} movement. Click again or confirm.";
            RefreshHighlights();
        }

        private void SelectUnit(CombatUnit unit)
        {
            if (unit == null || unit.IsDefeated || unit.ControlType != ControlType.Player || turns?.CurrentUnit != unit)
            {
                return;
            }

            selectedUnit = unit;
            selectedAbilityIndex = 0;
            mode = InteractionMode.Movement;
            selectedDestination = null;
            selectedPath = Array.Empty<GridCoordinate>();
            targetable = Array.Empty<GridCoordinate>();
            targetingRange = Array.Empty<GridCoordinate>();
            previewImpact = Array.Empty<GridCoordinate>();
            abilityPreview = null;
            selectedAbilityTarget = null;
            selectedUnit.CurrentAction = "Ready for player input";
            RefreshMovementOptions(unit);
            status = unit.CurrentActionPoints > 0
                ? $"Blue cells fit {MovementBudget(unit)} movement: uphill +1/+2 costs extra, +3 is blocked; orange cones are obstacles."
                : "No action points remain. End the turn with Space.";
            RefreshHighlights();
        }

        private void BeginAbilityTargeting(int abilityIndex)
        {
            if (selectedUnit == null || selectedUnit.IsDefeated || turns.CurrentUnit != selectedUnit)
            {
                return;
            }

            if (!definitionsByUnit.TryGetValue(selectedUnit, out UnitDefinition definition) ||
                abilityIndex < 0 || abilityIndex >= definition.Abilities.Count)
            {
                status = $"{selectedUnit.DisplayName} has no ability in slot {abilityIndex + 1}.";
                return;
            }

            selectedAbilityIndex = abilityIndex;
            AbilityDefinition ability = SelectedAbility;
            if (selectedUnit.GetCooldown(ability.StableId) > 0)
            {
                status = $"{ability.DisplayName} is on cooldown for {selectedUnit.GetCooldown(ability.StableId)} turn(s).";
                return;
            }

            if (selectedUnit.CurrentActionPoints < ability.ActionCost)
            {
                status = $"{ability.DisplayName} needs {ability.ActionCost} action point(s).";
                return;
            }

            mode = InteractionMode.AbilityTargeting;
            selectedDestination = null;
            selectedPath = Array.Empty<GridCoordinate>();
            selectedAbilityTarget = null;
            previewImpact = Array.Empty<GridCoordinate>();
            abilityPreview = null;
            targetable = AbilityRuntime.GetTargetableCells(ability, grid, selectedUnit, units);
            targetingRange = AbilityRuntime.GetTargetingHighlightCells(ability, grid, selectedUnit, units);
            selectedUnit.CurrentAction = $"Choosing target for {ability.DisplayName}";
            status = $"{ability.RangeType}: dark green shows full range; bright green cells are valid targets.";
            RefreshHighlights();
        }

        private void ConfirmCurrentChoice()
        {
            if (mode == InteractionMode.Movement && selectedDestination.HasValue && selectedPath.Count > 0)
            {
                RequestMove(selectedDestination.Value);
            }
            else if (mode == InteractionMode.AbilityTargeting && selectedAbilityTarget.HasValue)
            {
                StartCoroutine(ConfirmAbility());
            }
        }

        private IEnumerator MoveSelectedUnit(IReadOnlyList<GridCoordinate> path)
        {
            if (!selectedUnit.SpendActionPoints(selectedUnit.MovementActionCost))
            {
                status = "Not enough action points to move.";
                yield break;
            }

            isBusy = true;
            GridCoordinate start = selectedUnit.Cell;
            int movementCost = MovementRules.PathCost(grid, selectedUnit, path);
            selectedUnit.CurrentAction = "Moving";
            status = "Moving along the validated path...";
            feedback.AddLog(
                $"{selectedUnit.DisplayName} moves {start} → {path[path.Count - 1]} " +
                $"({movementCost}/{MovementBudget(selectedUnit)} movement, -{selectedUnit.MovementActionCost} AP)");
            yield return AnimateMovement(selectedUnit, path);
            yield return TriggerTrapIfAny(selectedUnit, new[] { selectedUnit.Cell });
            if (EvaluateBattleEnd()) yield break;

            selectedUnit.CurrentAction = "Move complete";
            resolvedActions++;
            feedback.AddLog($"{selectedUnit.DisplayName} reached {selectedUnit.Cell}");
            selectedDestination = null;
            selectedPath = Array.Empty<GridCoordinate>();
            RefreshMovementOptions(selectedUnit);
            status = selectedUnit.CurrentActionPoints > 0
                ? "Move complete. Choose an ability or spend the remaining action."
                : "Move complete. No actions remain; ending turn...";
            isBusy = false;
            RefreshNextEnemyIntent();
            RefreshHighlights();
            if (selectedUnit.CurrentActionPoints == 0)
            {
                yield return new WaitForSeconds(0.45f);
                EndPlayerTurn();
            }
        }

        private IEnumerator ConfirmAbility()
        {
            AbilityDefinition ability = SelectedAbility;
            if (ability == null)
            {
                status = "No ability is selected.";
                yield break;
            }

            if (!selectedUnit.SpendActionPoints(ability.ActionCost))
            {
                status = "Not enough action points for this ability.";
                yield break;
            }

            GridCoordinate target = selectedAbilityTarget.Value;
            IReadOnlyCollection<GridCoordinate> actualImpact = AbilityRuntime.GetImpactCells(ability, grid, target);
            if (!new HashSet<GridCoordinate>(previewImpact).SetEquals(actualImpact))
            {
                throw new InvalidOperationException("Ability preview diverged from execution impact cells.");
            }

            isBusy = true;
            selectedUnit.CurrentAction = $"Casting {ability.DisplayName}";
            status = $"{selectedUnit.DisplayName} launches {ability.DisplayName}...";
            feedback.AddLog($"{selectedUnit.DisplayName} casts {ability.DisplayName} at {target} (-{ability.ActionCost} AP)");
            FaceTowards(selectedUnit, selectedUnit.Cell, target);
            yield return PlayAbilityVisual(selectedUnit, ability, target, actualImpact);

            AbilityResolution resolution = ResolveAbility(selectedUnit, ability, target);
            EnsurePreviewMatchesExecution(abilityPreview, resolution);
            yield return PresentAbilityResults(selectedUnit, resolution);
            resolvedActions++;
            isBusy = false;
            status = BuildAbilitySummary(resolution, false);
            ClearInteractionState();
            if (EvaluateBattleEnd()) yield break;

            RefreshNextEnemyIntent();
            RefreshMovementOptions(selectedUnit);
            RefreshHighlights();
            if (selectedUnit.CurrentActionPoints == 0)
            {
                status += " No actions remain; ending turn...";
                StartCoroutine(EndTurnAfterDelay());
            }
        }

        private AbilityResolution ResolveAbility(
            CombatUnit source,
            AbilityDefinition ability,
            GridCoordinate target)
        {
            AbilityResolution resolution = AbilityRuntime.Execute(
                ability, grid, source, target, units, turns.CurrentTick, areaEffects, definitionsByUnit[source].PassiveModifiers);
            source.SetCooldown(ability.StableId, ability.Cooldown);
            source.CurrentAction = $"Used {ability.DisplayName}";
            feedback.AddLog($"{ability.DisplayName} cooldown set to {ability.Cooldown}");
            return resolution;
        }

        private bool EvaluateBattleEnd()
        {
            outcome = BattleReadabilityRules.EvaluateOutcome(units);
            if (outcome == BattleOutcome.Ongoing) return false;

            battleEnded = true;
            isBusy = false;
            nextEnemyIntent = null;
            ClearInteractionState();
            bool playerWon = outcome == BattleOutcome.PlayerVictory;
            status = playerWon
                ? "Victory! The opponent is defeated. Press R to restart."
                : "Defeat. Your Echo is defeated. Press R to restart.";
            feedback.AddLog(status);
            feedback.AddLog(
                $"Battle finished at tick {turns.CurrentTick}, {resolvedActions} resolved actions, " +
                $"{Time.unscaledTime - battleStartedAt:0.0}s");
            feedback.ShowTurnBanner(playerWon ? "VICTORY" : "DEFEAT", playerWon ? Color.green : Color.red, 2f);
            RefreshHighlights();
            return true;
        }

        private void ClearInteractionState()
        {
            mode = InteractionMode.Movement;
            selectedDestination = null;
            selectedPath = Array.Empty<GridCoordinate>();
            selectedAbilityTarget = null;
            targetable = Array.Empty<GridCoordinate>();
            targetingRange = Array.Empty<GridCoordinate>();
            previewImpact = Array.Empty<GridCoordinate>();
            abilityPreview = null;
            movementMap = null;
            reachable = new Dictionary<GridCoordinate, int>();
        }

        private void CancelAbility()
        {
            if (mode != InteractionMode.AbilityTargeting)
            {
                return;
            }

            int abilityIndex = selectedAbilityIndex;
            SelectUnit(selectedUnit);
            selectedAbilityIndex = abilityIndex;
            status = "Ability cancelled.";
        }

        private void RefreshHighlights()
        {
            foreach (CellView view in cellViews.Values)
            {
                view.SetColor(view.BaseColor);
            }

            if (mode == InteractionMode.Facing && selectedUnit != null)
            {
                foreach (GridCoordinate direction in new[] { new GridCoordinate(1, 0), new GridCoordinate(-1, 0), new GridCoordinate(0, 1), new GridCoordinate(0, -1) })
                {
                    var neighbour = new GridCoordinate(selectedUnit.Cell.X + direction.X, selectedUnit.Cell.Y + direction.Y);
                    SetColor(new[] { neighbour }, facingPreview == direction ? SelectedCellColor : FacingOptionColor);
                }
            }
            else if (mode == InteractionMode.Movement)
            {
                SetColor(reachable.Keys, ReachableColor);
                SetColor(selectedPath, PathColor);
                if (selectedDestination.HasValue) SetColor(new[] { selectedDestination.Value }, SelectedCellColor);
            }
            else
            {
                SetColor(targetingRange, TargetingRangeColor);
                SetColor(targetable, TargetableColor);
                SetColor(previewImpact, ImpactColor);
                if (abilityPreview != null)
                {
                    foreach (PushResult push in abilityPreview.Results.OfType<PushResult>())
                    {
                        SetColor(push.Path.Skip(1), ForcedPathColor);
                        if (push.StepsMoved > 0)
                        {
                            SetColor(new[] { push.Destination }, ForcedDestinationColor);
                        }
                    }
                }
                if (selectedAbilityTarget.HasValue) SetColor(new[] { selectedAbilityTarget.Value }, SelectedCellColor);
            }
        }

        private void SetColor(IEnumerable<GridCoordinate> coordinates, Color color)
        {
            foreach (GridCoordinate coordinate in coordinates)
            {
                if (cellViews.TryGetValue(coordinate, out CellView view))
                {
                    // Tint the stone instead of painting over it, so the texture stays readable.
                    view.SetColor(Color.Lerp(view.BaseColor, color, 0.72f) * 1.15f);
                }
            }
        }

        private bool IsOccupied(GridCoordinate coordinate)
        {
            return units.Any(unit => !unit.IsDefeated && unit.Cell == coordinate);
        }

        private static int MovementBudget(CombatUnit unit) => unit.MovementMode == MovementMode.Burrow
            ? unit.AvailableMovementRange * MovementRules.BurrowRangeMultiplier
            : unit.AvailableMovementRange;

        private void RefreshMovementOptions(CombatUnit unit)
        {
            if (unit != null && !unit.IsDefeated && unit.CurrentActionPoints >= unit.MovementActionCost)
            {
                movementMap = MovementRules.BuildMap(grid, unit, IsOccupied);
                reachable = movementMap.ReachableCosts;
                return;
            }

            movementMap = null;
            reachable = new Dictionary<GridCoordinate, int>();
        }

        private void Restart()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(string.IsNullOrEmpty(activeScene.path) ? activeScene.name : activeScene.path);
        }

    }
}
