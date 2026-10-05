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
            AbilityTargeting
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
        private const float DefaultCameraZoom = 7.7f;
        private const float MinimumCameraZoom = 4.25f;
        private const float MaximumCameraZoom = 11.5f;
        private const float CameraZoomStep = 0.8f;
        private const float CameraZoomSpeed = 10f;
        private const float MinimumCameraPitch = 20f;
        private const float MaximumCameraPitch = 72f;
        private const float CameraKeyboardPanSpeed = 0.85f;

        private static readonly Color ReachableColor = new Color(0.18f, 0.48f, 0.92f);
        private static readonly Color PathColor = new Color(0.15f, 0.9f, 1f);
        private static readonly Color TargetableColor = new Color(0.25f, 0.75f, 0.35f);
        private static readonly Color TargetingRangeColor = new Color(0.18f, 0.38f, 0.22f);
        private static readonly Color ImpactColor = new Color(1f, 0.38f, 0.12f);
        private static readonly Color ForcedPathColor = new Color(0.68f, 0.28f, 0.92f);
        private static readonly Color ForcedDestinationColor = new Color(1f, 0.2f, 0.92f);
        private static readonly Color SelectedCellColor = new Color(1f, 0.9f, 0.2f);
        private static readonly Vector3 DefaultCameraPivot = new Vector3(0f, 0.2f, 0f);
        private static readonly Vector3 DefaultCameraOffset = new Vector3(10.5f, 11.5f, -11.2f);
        private static readonly float CameraOrbitDistance = DefaultCameraOffset.magnitude;
        private static readonly float DefaultCameraPitch = Mathf.Atan2(
            DefaultCameraOffset.y,
            new Vector2(DefaultCameraOffset.x, DefaultCameraOffset.z).magnitude) * Mathf.Rad2Deg;

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

        public void RequestCameraRotation(float degrees)
        {
            targetCameraRotation += degrees;
        }

        public void RequestCameraZoom(float scrollDelta)
        {
            targetCameraZoom = Mathf.Clamp(
                targetCameraZoom - scrollDelta * CameraZoomStep,
                MinimumCameraZoom,
                MaximumCameraZoom);
        }

        public void RequestCameraTilt(float degrees)
        {
            targetCameraPitch = Mathf.Clamp(
                targetCameraPitch + degrees,
                MinimumCameraPitch,
                MaximumCameraPitch);
        }

        public void RequestCameraPan(Vector3 worldDelta)
        {
            float xLimit = (GridWidth - 1) * CellSpacing * 0.5f;
            float zLimit = (GridHeight - 1) * CellSpacing * 0.5f;
            cameraPivot = new Vector3(
                Mathf.Clamp(cameraPivot.x + worldDelta.x, -xLimit, xLimit),
                DefaultCameraPivot.y,
                Mathf.Clamp(cameraPivot.z + worldDelta.z, -zLimit, zLimit));
            ApplyCameraTransform();
        }

        public void ResetCameraView()
        {
            cameraPivot = DefaultCameraPivot;
            targetCameraRotation = 0f;
            targetCameraPitch = DefaultCameraPitch;
            targetCameraZoom = DefaultCameraZoom;
            ApplyCameraTransform();
        }

        public bool RequestMove(GridCoordinate destination)
        {
            if (battleEnded || isBusy || mode != InteractionMode.Movement || selectedUnit == null ||
                turns?.CurrentUnit != selectedUnit || selectedUnit.ControlType != ControlType.Player || selectedUnit.IsImmobilized ||
                !reachable.ContainsKey(destination))
            {
                return false;
            }

            IReadOnlyList<GridCoordinate> path = movementMap?.GetPath(destination) ?? Array.Empty<GridCoordinate>();
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
                    BeginAbilityTargeting(index);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelAbility();
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                ConfirmCurrentChoice();
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                EndPlayerTurn();
            }

            bool pointerOutsideHud = Input.mousePosition.y > 205f && Input.mousePosition.y < Screen.height - 270f;
            if (Input.GetMouseButtonDown(0) && pointerOutsideHud)
            {
                HandlePointer(Input.mousePosition);
            }
        }

        private void LoadDefinitions()
        {
            playerDefinitions = new[]
            {
                Resources.Load<UnitDefinition>(blightgulletShowcase ? "Prototype/Units/Blightgullet" : "Prototype/Units/BlazeWorm"),
                Resources.Load<UnitDefinition>("Prototype/Units/RangerEcho"),
                Resources.Load<UnitDefinition>("Prototype/Units/SkirmisherEcho")
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

            AbilityDefinition sharedFireBurst = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst");
            if (sharedFireBurst == null || playerDefinitions.Count(definition => definition.Abilities.Contains(sharedFireBurst)) < 2)
            {
                throw new InvalidOperationException("At least two allied Echoes must reuse the shared Fire Burst definition.");
            }

            ApplyBurnEffectDefinition burn =
                Resources.Load<ApplyBurnEffectDefinition>("Prototype/Effects/ApplyBurnTenPercent");
            if (burn == null || burn.Icon == null)
            {
                throw new InvalidOperationException("Burn effect definition or its status icon is missing.");
            }

            burningIcon = burn.Icon;
        }

        private void BuildGrid()
        {
            var logicalCells = new List<GridCell>();
            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    var coordinate = new GridCoordinate(x, y);
                    int height = GetFixedHeight(coordinate);
                    bool walkable = coordinate != new GridCoordinate(2, 4) &&
                                    coordinate != new GridCoordinate(3, 1) &&
                                    coordinate != new GridCoordinate(5, 5) &&
                                    coordinate != new GridCoordinate(6, 3) &&
                                    coordinate != new GridCoordinate(4, 7);
                    logicalCells.Add(new GridCell(coordinate, height, walkable));
                }
            }

            grid = new TacticalGrid(logicalCells);
            foreach (GridCell cell in grid.Cells)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Cell {cell.Coordinate} H{cell.Height}{(cell.IsWalkable ? string.Empty : " Blocked")}";
                block.transform.SetParent(transform, false);
                GetCellColumnGeometry(cell.Height, out float columnCenterY, out float columnHeight);
                block.transform.position = new Vector3(
                    WorldX(cell.Coordinate.X),
                    columnCenterY,
                    WorldZ(cell.Coordinate.Y));
                block.transform.localScale = new Vector3(0.98f, columnHeight, 0.98f);
                Color baseColor = GetCellBaseColor(cell);
                CellView view = block.AddComponent<CellView>();
                view.Initialize(cell.Coordinate, baseColor);
                cellViews.Add(cell.Coordinate, view);
                if (!cell.IsWalkable) CreateObstacleCone(cell);
            }
        }

        private void CreateObstacleCone(GridCell cell)
        {
            var obstacle = new GameObject($"Obstacle Cone {cell.Coordinate}");
            obstacle.transform.SetParent(transform, false);
            obstacle.transform.position = new Vector3(
                WorldX(cell.Coordinate.X),
                cell.Height * HeightStep + 0.02f,
                WorldZ(cell.Coordinate.Y));

            MeshFilter filter = obstacle.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateConeMesh(18, 0.34f, 0.82f);
            MeshRenderer renderer = obstacle.AddComponent<MeshRenderer>();
            renderer.material.color = new Color(1f, 0.34f, 0.06f);
            obstacle.AddComponent<ObstacleView>().Initialize(cell.Coordinate);
        }

        private static Mesh CreateConeMesh(int segments, float radius, float height)
        {
            var vertices = new Vector3[segments + 2];
            vertices[0] = new Vector3(0f, height, 0f);
            vertices[1] = Vector3.zero;
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                vertices[index + 2] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            var triangles = new int[segments * 6];
            for (int index = 0; index < segments; index++)
            {
                int current = index + 2;
                int next = (index + 1) % segments + 2;
                int triangle = index * 6;
                triangles[triangle] = 0;
                triangles[triangle + 1] = next;
                triangles[triangle + 2] = current;
                triangles[triangle + 3] = 1;
                triangles[triangle + 4] = current;
                triangles[triangle + 5] = next;
            }

            var mesh = new Mesh { name = "Prototype Obstacle Cone" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ConfigureCameraAndLight()
        {
            battleCamera = Camera.main;
            if (battleCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                battleCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            battleCamera.orthographic = true;
            cameraZoom = DefaultCameraZoom;
            targetCameraZoom = DefaultCameraZoom;
            cameraPitch = DefaultCameraPitch;
            targetCameraPitch = DefaultCameraPitch;
            cameraPivot = DefaultCameraPivot;
            battleCamera.orthographicSize = cameraZoom;
            battleCamera.backgroundColor = new Color(0.055f, 0.07f, 0.1f);
            ApplyCameraTransform();

            Light light = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (light == null)
            {
                var lightObject = new GameObject("Directional Light");
                light = lightObject.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
        }

        private void UpdateCameraControls()
        {
            if (Input.GetKeyDown(KeyCode.Q)) RequestCameraRotation(-90f);
            if (Input.GetKeyDown(KeyCode.E)) RequestCameraRotation(90f);
            if (Input.GetKeyDown(KeyCode.PageDown)) RequestCameraTilt(-8f);
            if (Input.GetKeyDown(KeyCode.PageUp)) RequestCameraTilt(8f);
            if (Input.GetKeyDown(KeyCode.Home)) ResetCameraView();
            float scrollDelta = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scrollDelta, 0f)) RequestCameraZoom(scrollDelta);

            if (Input.GetMouseButtonDown(1)) previousCameraPointer = Input.mousePosition;
            if (Input.GetMouseButton(1))
            {
                Vector2 currentPointer = Input.mousePosition;
                Vector2 pointerDelta = currentPointer - previousCameraPointer;
                targetCameraRotation += pointerDelta.x * 0.35f;
                RequestCameraTilt(pointerDelta.y * 0.22f);
                previousCameraPointer = currentPointer;
            }

            if (Input.GetMouseButtonDown(2)) previousCameraPointer = Input.mousePosition;
            if (Input.GetMouseButton(2) && battleCamera != null)
            {
                Vector2 currentPointer = Input.mousePosition;
                Vector2 pointerDelta = currentPointer - previousCameraPointer;
                float worldUnitsPerPixel = cameraZoom * 2f / Mathf.Max(1f, Screen.height);
                Vector3 screenRight = Vector3.ProjectOnPlane(battleCamera.transform.right, Vector3.up).normalized;
                Vector3 screenUp = Vector3.ProjectOnPlane(battleCamera.transform.up, Vector3.up).normalized;
                RequestCameraPan(
                    (-screenRight * pointerDelta.x - screenUp * pointerDelta.y) * worldUnitsPerPixel);
                previousCameraPointer = currentPointer;
            }

            UpdateKeyboardCameraPan();

            float updatedRotation = Mathf.MoveTowardsAngle(
                cameraRotation,
                targetCameraRotation,
                240f * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(updatedRotation, cameraRotation))
            {
                cameraRotation = updatedRotation;
                ApplyCameraTransform();
            }

            float updatedPitch = Mathf.MoveTowards(
                cameraPitch,
                targetCameraPitch,
                120f * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(updatedPitch, cameraPitch))
            {
                cameraPitch = updatedPitch;
                ApplyCameraTransform();
            }

            float updatedZoom = Mathf.MoveTowards(
                cameraZoom,
                targetCameraZoom,
                CameraZoomSpeed * Time.unscaledDeltaTime);
            if (Mathf.Approximately(updatedZoom, cameraZoom)) return;
            cameraZoom = updatedZoom;
            if (battleCamera != null) battleCamera.orthographicSize = cameraZoom;
        }

        private void UpdateKeyboardCameraPan()
        {
            if (battleCamera == null) return;
            float horizontal = 0f;
            float vertical = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Mathf.Approximately(horizontal, 0f) && Mathf.Approximately(vertical, 0f)) return;

            Vector3 screenRight = Vector3.ProjectOnPlane(battleCamera.transform.right, Vector3.up).normalized;
            Vector3 screenUp = Vector3.ProjectOnPlane(battleCamera.transform.up, Vector3.up).normalized;
            Vector3 direction = screenRight * horizontal + screenUp * vertical;
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            RequestCameraPan(direction * cameraZoom * CameraKeyboardPanSpeed * Time.unscaledDeltaTime);
        }

        private void ApplyCameraTransform()
        {
            if (battleCamera == null) return;
            Vector3 horizontalDirection = new Vector3(
                DefaultCameraOffset.x,
                0f,
                DefaultCameraOffset.z).normalized;
            horizontalDirection = Quaternion.AngleAxis(cameraRotation, Vector3.up) * horizontalDirection;
            float pitchRadians = cameraPitch * Mathf.Deg2Rad;
            Vector3 orbitOffset = horizontalDirection * (Mathf.Cos(pitchRadians) * CameraOrbitDistance) +
                                  Vector3.up * (Mathf.Sin(pitchRadians) * CameraOrbitDistance);
            battleCamera.transform.position = cameraPivot + orbitOffset;
            battleCamera.transform.LookAt(cameraPivot);
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
            Vector3 facingDirection = team == Team.Player
                ? new Vector3(1f, 0f, 1f)
                : new Vector3(-1f, 0f, -1f);
            body.transform.rotation = Quaternion.LookRotation(facingDirection);
            body.transform.localScale = Vector3.one;
            CombatUnitView view = body.AddComponent<CombatUnitView>();
            view.Initialize(unit, fallbackColor);
            unitViews.Add(unit, view);
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

        private void HandlePointer(Vector3 screenPosition)
        {
            Camera camera = Camera.main;
            if (camera == null || !Physics.Raycast(camera.ScreenPointToRay(screenPosition), out RaycastHit hit, 100f))
            {
                return;
            }

            CombatUnitView unitView = hit.collider.GetComponentInParent<CombatUnitView>();
            if (unitView != null)
            {
                if (mode == InteractionMode.Movement && unitView.Unit.ControlType == ControlType.Player)
                {
                    SelectUnit(unitView.Unit);
                }
                else
                {
                    HandleCellChoice(unitView.Unit.Cell);
                }

                return;
            }

            CellView cellView = hit.collider.GetComponent<CellView>();
            if (cellView != null)
            {
                HandleCellChoice(cellView.Coordinate);
            }
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
                feedback.AddLog(
                    $"Preview: {ability.DisplayName} at {coordinate} — {BuildAbilitySummary(abilityPreview, true)}");
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

        private IEnumerator AnimateMovement(CombatUnit unit, IReadOnlyList<GridCoordinate> path)
        {
            if (unit.MovementMode == MovementMode.Burrow && path.Count > 1)
            {
                yield return AnimateBurrow(unit, path[path.Count - 1]);
                yield break;
            }

            for (int index = 1; index < path.Count; index++)
            {
                GridCoordinate next = path[index];
                Vector3 destination = UnitWorldPosition(next);
                Transform body = unitViews[unit].transform;
                while ((body.position - destination).sqrMagnitude > 0.0004f)
                {
                    body.position = Vector3.MoveTowards(body.position, destination, Time.deltaTime * 3.6f);
                    yield return null;
                }

                unit.MoveTo(next);
            }
        }

        /// <summary>Deep Burrow: sink into the ground, travel unseen, emerge at the destination.</summary>
        private IEnumerator AnimateBurrow(CombatUnit unit, GridCoordinate destinationCell)
        {
            const float depth = 1.4f;
            const float speed = 4.5f;
            Transform body = unitViews[unit].transform;
            Vector3 sunk = body.position + Vector3.down * depth;
            while ((body.position - sunk).sqrMagnitude > 0.0004f)
            {
                body.position = Vector3.MoveTowards(body.position, sunk, Time.deltaTime * speed);
                yield return null;
            }

            Vector3 surface = UnitWorldPosition(destinationCell);
            body.position = surface + Vector3.down * depth;
            yield return new WaitForSeconds(0.15f);
            while ((body.position - surface).sqrMagnitude > 0.0004f)
            {
                body.position = Vector3.MoveTowards(body.position, surface, Time.deltaTime * speed);
                yield return null;
            }

            unit.MoveTo(destinationCell);
        }

        private IEnumerator AnimateForcedMovement(PushResult push)
        {
            if (!unitViews.TryGetValue(push.Target, out CombatUnitView view)) yield break;
            for (int index = 1; index < push.Path.Count; index++)
            {
                Vector3 destination = UnitWorldPosition(push.Path[index]);
                while ((view.transform.position - destination).sqrMagnitude > 0.0004f)
                {
                    view.transform.position = Vector3.MoveTowards(
                        view.transform.position,
                        destination,
                        Time.deltaTime * 5.2f);
                    yield return null;
                }
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
            yield return feedback.PlayAbility(
                unitViews[selectedUnit].transform.position + Vector3.up * 0.4f,
                EffectWorldPosition(target) + Vector3.up * 0.45f,
                actualImpact.Select(EffectWorldPosition).ToArray(),
                GetAbilityColor(ability),
                GetAbilityVisualKind(ability));

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

        private IEnumerator PresentAbilityResults(CombatUnit source, AbilityResolution resolution)
        {
            IReadOnlyList<ICombatEffectResult> results = resolution.Results;
            RefreshCloudViews();
            foreach (var cloud in resolution.Clouds) feedback.AddLog($"{cloud.Rules.Id}: {cloud.OccupiedCells.Count} cells, {cloud.StacksPerTick} stacks/tick, direction {cloud.MovementDirection}");
            var hitViews = new List<CombatUnitView>();
            foreach (ICombatEffectResult result in results)
            {
                if (!unitViews.TryGetValue(result.Target, out CombatUnitView view)) continue;
                Vector3 labelPosition = view.transform.position + Vector3.up * 0.75f;
                if (result.Kind == CombatEffectKind.Damage)
                {
                    if (!hitViews.Contains(view)) hitViews.Add(view);
                    if (result.ShieldDelta < 0)
                    {
                        feedback.AddFloatingText(labelPosition, $"{result.ShieldDelta} SHIELD", new Color(0.2f, 0.75f, 1f));
                    }

                    if (result.HealthDelta < 0)
                    {
                        feedback.AddFloatingText(labelPosition + Vector3.up * 0.22f, $"{result.HealthDelta} HP", new Color(1f, 0.28f, 0.2f));
                    }
                }
                else if (result.Kind == CombatEffectKind.Heal && result.HealthDelta > 0)
                {
                    feedback.AddFloatingText(labelPosition, $"+{result.HealthDelta} HP", new Color(0.28f, 1f, 0.45f));
                }
                else if (result.Kind == CombatEffectKind.Shield && result.ShieldDelta > 0)
                {
                    feedback.AddFloatingText(labelPosition, $"+{result.ShieldDelta} SHIELD", new Color(0.25f, 0.9f, 1f));
                }
                else if (result is PushResult push)
                {
                    string pushText = (push.StepsMoved > 0 ? $"PUSH {push.StepsMoved}" : "PUSH BLOCKED") +
                                      (push.Collided ? " · COLLISION" : string.Empty);
                    feedback.AddFloatingText(labelPosition, pushText, new Color(0.85f, 0.45f, 1f));
                }
                else if (result is BurnAppliedResult burn)
                {
                    feedback.AddFloatingText(
                        labelPosition + Vector3.up * 0.22f,
                        $"BURN {burn.NewDamagePerPulse}/100t · {GetBurnPulseCount(burn)}x" +
                        (burn.ConsumedOilStacks > 0 ? $" · OIL ×{burn.ConsumedOilStacks}" : string.Empty),
                        new Color(1f, 0.42f, 0.05f));
                }
                else if (result is StatusAppliedResult statusResult)
                    feedback.AddFloatingText(labelPosition, $"{statusResult.State.Rules.Id} ×{statusResult.State.Stacks}", Color.yellow);

                feedback.AddLog(DescribeResult(result, false));
                if (result.Kind == CombatEffectKind.Damage && result.Target.Team == source.Team)
                {
                    feedback.AddLog($"Friendly fire: {result.Target.DisplayName} was caught in the impact area");
                }
                else if (result.Kind == CombatEffectKind.Heal && result.Target.Team != source.Team &&
                         result.HealthDelta > 0)
                {
                    feedback.AddLog($"Enemy aided: {result.Target.DisplayName} received area healing");
                }
            }

            yield return feedback.PlayHitReactions(hitViews);
            foreach (DamageResult result in results.OfType<DamageResult>().Where(result => result.Defeated))
            {
                if (unitViews.TryGetValue(result.Target, out CombatUnitView defeatedView))
                {
                    feedback.AddLog($"{result.Target.DisplayName} is defeated");
                    defeatedView.gameObject.SetActive(false);
                }
            }

            foreach (PushResult push in results.OfType<PushResult>().Where(result => result.StepsMoved > 0))
            {
                yield return AnimateForcedMovement(push);
            }

            foreach (TerrainChangeResult terrainResult in resolution.TerrainResults)
            {
                RefreshCellVisual(terrainResult.Coordinate);
                Vector3 labelPosition = EffectWorldPosition(terrainResult.Coordinate) + Vector3.up * 0.35f;
                feedback.AddFloatingText(
                    labelPosition,
                    $"HEIGHT {terrainResult.PreviousHeight}→{terrainResult.NewHeight}",
                    new Color(0.78f, 0.55f, 1f));
                feedback.AddLog(
                    $"Cell {terrainResult.Coordinate}: height {terrainResult.PreviousHeight} → {terrainResult.NewHeight}");
                foreach (CombatUnit unit in units.Where(unit =>
                             !unit.IsDefeated && unit.Cell == terrainResult.Coordinate))
                {
                    yield return AnimateUnitToCurrentCell(unit);
                }
            }
        }

        private IEnumerator AnimateUnitToCurrentCell(CombatUnit unit)
        {
            if (!unitViews.TryGetValue(unit, out CombatUnitView view)) yield break;
            Vector3 destination = UnitWorldPosition(unit.Cell);
            while ((view.transform.position - destination).sqrMagnitude > 0.0004f)
            {
                view.transform.position = Vector3.MoveTowards(
                    view.transform.position,
                    destination,
                    Time.deltaTime * 3.2f);
                yield return null;
            }
        }

        private static string BuildAbilitySummary(AbilityResolution resolution, bool preview)
        {
            var descriptions = resolution.Results
                .Where(result => result.HealthDelta != 0 || result.ShieldDelta != 0 ||
                                 result.Kind == CombatEffectKind.Push ||
                                 result.Kind == CombatEffectKind.Burning || result.Kind == CombatEffectKind.Status)
                .Select(result => DescribeResult(result, preview))
                .Concat(resolution.TerrainResults.Select(result =>
                    $"cell {result.Coordinate}: {(preview ? "expected " : string.Empty)}height {result.PreviousHeight}→{result.NewHeight}"))
                .Concat(resolution.Clouds.Select(c => $"{c.Rules.Id}: {c.OccupiedCells.Count} cells, +{c.StacksPerTick} stacks/{c.Rules.PulseInterval}t"))
                .ToArray();
            return descriptions.Length == 0
                ? "No effect."
                : string.Join(", ", descriptions);
        }

        private static string DescribeResult(ICombatEffectResult result, bool preview)
        {
            string prefix = preview ? "expected " : string.Empty;
            switch (result.Kind)
            {
                case CombatEffectKind.Status:
                    var statusResult = (StatusAppliedResult)result;
                    return $"{result.Target.DisplayName}: {statusResult.State.Rules.Id} ×{statusResult.State.Stacks}, until tick {statusResult.State.ExpireTick}";
                case CombatEffectKind.Damage:
                    return $"{result.Target.DisplayName}: {prefix}{-result.ShieldDelta} shield damage, {-result.HealthDelta} health damage";
                case CombatEffectKind.Heal:
                    return $"{result.Target.DisplayName}: {prefix}+{result.HealthDelta} health";
                case CombatEffectKind.Shield:
                    return $"{result.Target.DisplayName}: {prefix}+{result.ShieldDelta} shield";
                case CombatEffectKind.Push:
                    var push = (PushResult)result;
                    string collision = push.Collided ? $", collision at {push.CollisionCell.Value}" : string.Empty;
                    return push.StepsMoved > 0
                        ? $"{result.Target.DisplayName}: {prefix}push {push.Origin}→{push.Destination}{collision}"
                        : $"{result.Target.DisplayName}: {prefix}push blocked{collision}";
                case CombatEffectKind.Burning:
                    if (result is BurnAppliedResult burn)
                    {
                        string oil = burn.ConsumedOilStacks > 0 ? $", burned {burn.ConsumedOilStacks} oil" : string.Empty;
                        return $"{result.Target.DisplayName}: {prefix}burn {burn.AddedDamagePerPulse}/100 ticks{oil}, " +
                               $"strongest {burn.NewDamagePerPulse}/100 ticks for {GetBurnPulseCount(burn)} pulses";
                    }

                    var tick = (BurnTickResult)result;
                    int remainingPulses = tick.NextPulseTick <= tick.ExpireTickAfterTick
                        ? (int)((tick.ExpireTickAfterTick - tick.NextPulseTick) /
                                BurningCalculator.DefaultPulseInterval) + 1
                        : 0;
                    return $"{result.Target.DisplayName}: burn pulse {tick.ShieldDamage} shield, " +
                           $"{tick.HealthDamage} health; {remainingPulses} pulses remain";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private IEnumerator EndTurnAfterDelay()
        {
            isBusy = true;
            yield return new WaitForSeconds(0.65f);
            isBusy = false;
            EndPlayerTurn();
        }

        private void EndPlayerTurn()
        {
            if (battleEnded || isBusy || turns?.CurrentUnit?.ControlType != ControlType.Player)
            {
                return;
            }

            turns.CurrentUnit.CurrentAction = "Activation ended";
            feedback.AddLog($"{turns.CurrentUnit.DisplayName} ends the activation");
            AdvanceTurn();
        }

        private void AdvanceTurn()
        {
            ClearInteractionState();
            selectedUnit = null;
            RefreshHighlights();
            BeginTurn(turns.Advance());
        }

        private void BeginTurn(CombatUnit unit)
        {
            PresentTimelineResults(turns.LastTimelineResults);
            RefreshCloudViews();
            if (EvaluateBattleEnd()) return;
            if (unit == null) return;

            Color teamColor = unit.Team == Team.Player
                ? new Color(0.35f, 0.75f, 1f)
                : new Color(1f, 0.4f, 0.28f);
            feedback.ShowTurnBanner($"TICK {turns.CurrentTick} · {unit.DisplayName}", teamColor);
            feedback.AddLog(
                $"Tick {turns.CurrentTick}: {unit.DisplayName} activation #{unit.ActivationCount} ({unit.CurrentActionPoints} AP)");
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition))
            {
                string abilityState = string.Join(", ", definition.Abilities.Select(ability =>
                    unit.GetCooldown(ability.StableId) == 0
                        ? $"{ability.DisplayName}: ready"
                        : $"{ability.DisplayName}: CD {unit.GetCooldown(ability.StableId)}"));
                feedback.AddLog(abilityState);
            }

            RefreshNextEnemyIntent();

            if (unit.ControlType == ControlType.Player)
            {
                SelectUnit(unit);
            }
            else
            {
                selectedUnit = unit;
                StartCoroutine(RunBasicAI(unit));
            }
        }

        private void PresentTimelineResults(IReadOnlyList<ICombatEffectResult> results)
        {
            foreach (ICombatEffectResult tick in results)
            {
                if (unitViews.TryGetValue(tick.Target, out CombatUnitView view))
                {
                    Vector3 position = view.transform.position + Vector3.up * 0.85f;
                    feedback.AddFloatingText(position, tick is StatusAppliedResult s ? $"{s.State.Rules.Id} ×{s.State.Stacks}" :
                        $"{tick.Kind} HP {tick.HealthDelta:+0;-0;0} SH {tick.ShieldDelta:+0;-0;0}", new Color(1f, 0.65f, 0.1f));
                    if (tick.Target.IsDefeated) view.gameObject.SetActive(false);
                }

                feedback.AddLog(DescribeResult(tick, false));
            }
        }

        private IEnumerator RunBasicAI(CombatUnit unit)
        {
            isBusy = true;
            ClearInteractionState();
            status = $"{unit.DisplayName} is evaluating its turn...";
            unit.CurrentAction = "Thinking";
            RefreshHighlights();
            yield return new WaitForSeconds(0.5f);

            CombatUnit nearestTarget = units
                .Where(candidate => candidate.Team != unit.Team && !candidate.IsDefeated)
                .OrderBy(candidate => candidate.Cell.ManhattanDistance(unit.Cell))
                .ThenBy(candidate => candidate.CurrentHealth + candidate.CurrentShield)
                .FirstOrDefault();
            if (nearestTarget == null)
            {
                aiDecisionRolls.Remove(unit);
                isBusy = false;
                if (!EvaluateBattleEnd()) AdvanceTurn();
                yield break;
            }

            bool canAttack = TryPlanBestAbility(unit, out AbilityDefinition ability, out GridCoordinate abilityTarget);
            bool seekOwnCloud = areaEffects.GetModifiers(unit).NativeMire && !areaEffects.IsInsideAnyCloud(unit.Cell) &&
                MovementRules.BuildMap(grid,unit,IsOccupied).ReachableCosts.Keys
                    .Any(c => areaEffects.IsInsideAnyCloud(c));
            if ((!canAttack || seekOwnCloud) && unit.CurrentActionPoints >= unit.MovementActionCost)
            {
                MovementMap aiMovementMap = MovementRules.BuildMap(grid, unit, IsOccupied);

                IReadOnlyDictionary<GridCoordinate, int> aiReachable = aiMovementMap.ReachableCosts;
                if (aiReachable.Count > 0)
                {
                    GridCoordinate destination = aiReachable.Keys
                        .OrderByDescending(coordinate => areaEffects.GetModifiers(unit).NativeMire && areaEffects.IsInsideAnyCloud(coordinate))
                        .ThenBy(coordinate => coordinate.ManhattanDistance(nearestTarget.Cell))
                        .ThenBy(coordinate => aiReachable[coordinate])
                        .ThenBy(coordinate => coordinate.X)
                        .ThenBy(coordinate => coordinate.Y)
                        .First();
                    IReadOnlyList<GridCoordinate> path = aiMovementMap.GetPath(destination);
                    if (path.Count > 1 && unit.SpendActionPoints(unit.MovementActionCost))
                    {
                        int movementCost = MovementRules.PathCost(grid, selectedUnit, path);
                        unit.CurrentAction = $"Moving to {destination}";
                        selectedPath = path;
                        status = $"{unit.DisplayName} moves closer.";
                        feedback.AddLog(
                            $"{unit.DisplayName} moves {unit.Cell} → {destination} " +
                            $"({movementCost}/{MovementBudget(unit)} movement, -{unit.MovementActionCost} AP)");
                        RefreshHighlights();
                        yield return AnimateMovement(unit, path);
                        resolvedActions++;
                        feedback.AddLog($"{unit.DisplayName} reached {unit.Cell}");
                        selectedPath = Array.Empty<GridCoordinate>();
                        canAttack = TryPlanBestAbility(unit, out ability, out abilityTarget);
                    }
                }
            }

            if (canAttack && unit.GetCooldown(ability.StableId) == 0 &&
                unit.CurrentActionPoints >= ability.ActionCost && unit.SpendActionPoints(ability.ActionCost))
            {
                mode = InteractionMode.AbilityTargeting;
                selectedAbilityTarget = abilityTarget;
                targetable = AbilityRuntime.GetTargetableCells(ability, grid, unit, units);
                targetingRange = AbilityRuntime.GetTargetingHighlightCells(ability, grid, unit, units);
                abilityPreview = AbilityRuntime.Preview(
                    ability, grid, unit, abilityTarget, units, turns.CurrentTick, areaEffects, definitionsByUnit[unit].PassiveModifiers);
                previewImpact = abilityPreview.ImpactCells;
                unit.CurrentAction = $"Targeting {ability.DisplayName}";
                status = $"{unit.DisplayName} previews {ability.DisplayName}.";
                feedback.AddLog($"{unit.DisplayName} casts {ability.DisplayName} at {abilityTarget} (-{ability.ActionCost} AP)");
                RefreshHighlights();
                yield return new WaitForSeconds(0.35f);
                yield return feedback.PlayAbility(
                    unitViews[unit].transform.position + Vector3.up * 0.4f,
                    EffectWorldPosition(abilityTarget) + Vector3.up * 0.45f,
                    previewImpact.Select(EffectWorldPosition).ToArray(),
                    GetAbilityColor(ability),
                    GetAbilityVisualKind(ability));

                AbilityResolution resolution = ResolveAbility(unit, ability, abilityTarget);
                EnsurePreviewMatchesExecution(abilityPreview, resolution);
                yield return PresentAbilityResults(unit, resolution);
                resolvedActions++;
                status = BuildAbilitySummary(resolution, false);
                ClearInteractionState();
                RefreshHighlights();
                yield return new WaitForSeconds(0.45f);
            }

            if (EvaluateBattleEnd()) yield break;

            unit.CurrentAction = "Turn complete";
            feedback.AddLog($"{unit.DisplayName} ends the turn");
            aiDecisionRolls.Remove(unit);
            isBusy = false;
            AdvanceTurn();
        }

        private bool TryPlanBestAbility(
            CombatUnit source,
            out AbilityDefinition selectedAbility,
            out GridCoordinate selectedTarget,
            bool logDecision = true,
            int? actionPointsOverride = null)
        {
            selectedAbility = null;
            selectedTarget = default;
            if (!definitionsByUnit.TryGetValue(source, out UnitDefinition definition)) return false;
            int availableActionPoints = actionPointsOverride ?? source.CurrentActionPoints;

            var plans = new List<AiAbilityPlan>();
            foreach (AbilityDefinition ability in definition.Abilities)
            {
                if (source.GetCooldown(ability.StableId) > 0 || availableActionPoints < ability.ActionCost)
                {
                    continue;
                }

                foreach (GridCoordinate candidate in AbilityRuntime.GetTargetableCells(ability, grid, source, units))
                {
                    AbilityResolution preview = AbilityRuntime.Preview(
                        ability, grid, source, candidate, units, turns.CurrentTick, areaEffects, definition.PassiveModifiers);
                    int damageValue = preview.Results
                        .Where(result => result.Kind == CombatEffectKind.Damage)
                        .Sum(result => (result.Target.Team == source.Team ? -1 : 1) *
                                       (Math.Max(0, -result.HealthDelta) + Math.Max(0, -result.ShieldDelta)));
                    damageValue += preview.Results.OfType<BurnAppliedResult>()
                        .Sum(result => (result.Target.Team == source.Team ? -1 : 1) *
                                       result.AddedDamagePerPulse * GetBurnPulseCount(result));
                    int healingValue = preview.Results
                        .Where(result => result.Kind == CombatEffectKind.Heal)
                        .Sum(result => (result.Target.Team == source.Team ? 1 : -1) *
                                       Math.Max(0, result.HealthDelta));
                    int shieldValue = preview.Results
                        .Where(result => result.Kind == CombatEffectKind.Shield)
                        .Sum(result => (result.Target.Team == source.Team ? 1 : -1) *
                                       Math.Max(0, result.ShieldDelta));
                    int pushValue = preview.Results.OfType<PushResult>()
                        .Sum(result => (result.Target.Team == source.Team ? -1 : 1) * result.StepsMoved * 2);
                    int terrainValue = preview.TerrainResults.Count(result => result.Changed);
                    int utility = shieldValue + pushValue + terrainValue - source.Cell.ManhattanDistance(candidate) + ScoreAreaControl(source, preview);
                    if (damageValue <= 0 && healingValue <= 0 && utility <= 0) continue;
                    plans.Add(new AiAbilityPlan
                    {
                        Ability = ability,
                        Target = candidate,
                        DamageValue = Math.Max(0, damageValue),
                        HealingValue = Math.Max(0, healingValue),
                        UtilityValue = utility
                    });
                }
            }

            AiAbilityPlan bestDamage = plans
                .Where(plan => plan.DamageValue > 0)
                .OrderByDescending(plan => plan.DamageValue)
                .ThenByDescending(plan => plan.UtilityValue)
                .FirstOrDefault();
            AiAbilityPlan bestHealing = plans
                .Where(plan => plan.HealingValue > 0)
                .OrderByDescending(plan => plan.HealingValue)
                .ThenByDescending(plan => plan.UtilityValue)
                .FirstOrDefault();

            AiAbilityPlan chosen;
            if (bestDamage != null && bestHealing != null)
            {
                double attackProbability = AiDecisionMath.CalculateFirstOptionProbability(
                    bestDamage.DamageValue,
                    bestHealing.HealingValue);
                if (!aiDecisionRolls.TryGetValue(source, out float roll))
                {
                    roll = UnityEngine.Random.value;
                    aiDecisionRolls[source] = roll;
                }

                chosen = roll < attackProbability ? bestDamage : bestHealing;
                if (logDecision)
                {
                    feedback.AddLog(
                        $"{source.DisplayName} weighs damage {bestDamage.DamageValue} vs heal {bestHealing.HealingValue}: " +
                        $"attack {attackProbability:P0}, roll {roll:P0} → {chosen.Ability.DisplayName}");
                }
            }
            else
            {
                chosen = bestDamage ?? bestHealing ?? plans
                    .OrderByDescending(plan => plan.UtilityValue)
                    .FirstOrDefault();
            }

            if (definition.Abilities.Any(a => a.Effects.OfType<CreateCloudEffectDefinition>().Any()))
            {
                var controlPlan = plans.OrderByDescending(p => p.DamageValue + p.HealingValue + p.UtilityValue).FirstOrDefault();
                if (controlPlan != null) chosen = controlPlan;
            }
            if (chosen == null) return false;
            selectedAbility = chosen.Ability;
            selectedTarget = chosen.Target;
            return true;
        }

        private void RefreshNextEnemyIntent()
        {
            if (turns == null || battleEnded)
            {
                nextEnemyIntent = null;
                return;
            }

            CombatUnit enemy = turns.GetUpcomingEvents(30, false, false)
                .Where(entry => entry.Kind == TimelineEventKind.Activation)
                .Select(entry => entry.Unit)
                .FirstOrDefault(unit => unit.Team == Team.Opponent && !unit.IsDefeated);
            if (enemy == null)
            {
                nextEnemyIntent = null;
                return;
            }

            BattleIntentKind first;
            BattleIntentKind second = BattleIntentKind.None;
            if (TryPlanBestAbility(enemy, out AbilityDefinition ability, out _, false, ActionsPerTurn))
            {
                first = GetIntentKind(ability);
            }
            else if (enemy.CurrentActionPoints >= enemy.MovementActionCost || turns.CurrentUnit != enemy)
            {
                first = BattleIntentKind.Movement;
                second = PredictFollowUpIntent(enemy);
            }
            else
            {
                first = BattleIntentKind.None;
            }

            var updated = new EnemyIntentPreview { Unit = enemy, First = first, Second = second };
            bool changed = nextEnemyIntent == null || nextEnemyIntent.Unit != updated.Unit ||
                           nextEnemyIntent.First != updated.First || nextEnemyIntent.Second != updated.Second;
            nextEnemyIntent = updated;
            if (changed && feedback != null)
            {
                feedback.AddLog($"Next enemy intent: {enemy.DisplayName} {updated.Codes}");
            }
        }

        private BattleIntentKind PredictFollowUpIntent(CombatUnit unit)
        {
            if (!definitionsByUnit.TryGetValue(unit, out UnitDefinition definition)) return BattleIntentKind.None;
            IReadOnlyList<AbilityDefinition> ready = definition.Abilities
                .Where(ability => unit.GetCooldown(ability.StableId) == 0 &&
                                  ability.ActionCost <= Math.Max(0, ActionsPerTurn - unit.MovementActionCost))
                .ToArray();
            bool woundedAlly = units.Any(candidate => candidate.Team == unit.Team && !candidate.IsDefeated &&
                                                        (candidate.CurrentHealth < candidate.MaximumHealth ||
                                                         candidate.CurrentShield < candidate.MaximumShield));
            if (woundedAlly && ready.Any(ability => GetIntentKind(ability) == BattleIntentKind.Help))
            {
                return BattleIntentKind.Help;
            }

            AbilityDefinition offensive = ready.FirstOrDefault(ability =>
                GetIntentKind(ability) == BattleIntentKind.Attack);
            if (offensive != null) return BattleIntentKind.Attack;
            return ready.Any(ability => GetIntentKind(ability) == BattleIntentKind.Special)
                ? BattleIntentKind.Special
                : BattleIntentKind.None;
        }

        private static BattleIntentKind GetIntentKind(AbilityDefinition ability)
        {
            if (ability.Effects.Any(effect => effect is PushEffectDefinition ||
                                              effect is ModifyTerrainEffectDefinition))
            {
                return BattleIntentKind.Special;
            }

            if (ability.Effects.Any(effect => effect is HealEffectDefinition ||
                                              effect is GrantShieldEffectDefinition))
            {
                return BattleIntentKind.Help;
            }

            return ability.Effects.Any(effect => effect is DamageEffectDefinition)
                ? BattleIntentKind.Attack
                : BattleIntentKind.Special;
        }

        private static void EnsurePreviewMatchesExecution(
            AbilityResolution preview,
            AbilityResolution execution)
        {
            if (preview == null)
            {
                throw new InvalidOperationException("Ability was executed without a preview.");
            }

            if (!new HashSet<GridCoordinate>(preview.ImpactCells).SetEquals(execution.ImpactCells) ||
                preview.Results.Count != execution.Results.Count ||
                preview.Clouds.Count != execution.Clouds.Count ||
                preview.TerrainResults.Count != execution.TerrainResults.Count)
            {
                throw new InvalidOperationException("Ability preview diverged from execution.");
            }

            for (int index = 0; index < preview.Results.Count; index++)
            {
                ICombatEffectResult expected = preview.Results[index];
                ICombatEffectResult actual = execution.Results[index];
                if (expected.Target != actual.Target || expected.Kind != actual.Kind ||
                    expected.HealthDelta != actual.HealthDelta || expected.ShieldDelta != actual.ShieldDelta)
                {
                    throw new InvalidOperationException("Ability preview resource changes diverged from execution.");
                }

                if (expected is PushResult expectedPush && actual is PushResult actualPush &&
                    !expectedPush.Path.SequenceEqual(actualPush.Path))
                {
                    throw new InvalidOperationException("Ability push preview diverged from execution.");
                }

                if (expected is StatusAppliedResult expectedStatus && actual is StatusAppliedResult actualStatus &&
                    (expectedStatus.State.Rules.Id != actualStatus.State.Rules.Id ||
                     expectedStatus.State.Stacks != actualStatus.State.Stacks ||
                     expectedStatus.State.ExpireTick != actualStatus.State.ExpireTick))
                    throw new InvalidOperationException("Ability status preview diverged from execution.");

                if (expected is BurnAppliedResult expectedBurn && actual is BurnAppliedResult actualBurn &&
                    (expectedBurn.AddedDamagePerPulse != actualBurn.AddedDamagePerPulse ||
                     expectedBurn.NewDamagePerPulse != actualBurn.NewDamagePerPulse ||
                     expectedBurn.NewNextPulseTick != actualBurn.NewNextPulseTick ||
                     expectedBurn.NewExpireTick != actualBurn.NewExpireTick))
                {
                    throw new InvalidOperationException("Ability burn preview diverged from execution.");
                }
            }

            for (int index = 0; index < preview.Clouds.Count; index++)
            {
                var expected = preview.Clouds[index]; var actual = execution.Clouds[index];
                if (!new HashSet<GridCoordinate>(expected.OccupiedCells).SetEquals(actual.OccupiedCells) ||
                    expected.OwnerId != actual.OwnerId || expected.MovementDirection != actual.MovementDirection ||
                    expected.LowerHeight != actual.LowerHeight || expected.StacksPerTick != actual.StacksPerTick)
                    throw new InvalidOperationException("Cloud preview diverged from execution.");
            }
            for (int index = 0; index < preview.TerrainResults.Count; index++)
            {
                TerrainChangeResult expected = preview.TerrainResults[index];
                TerrainChangeResult actual = execution.TerrainResults[index];
                if (expected.Coordinate != actual.Coordinate ||
                    expected.PreviousHeight != actual.PreviousHeight ||
                    expected.NewHeight != actual.NewHeight)
                {
                    throw new InvalidOperationException("Ability terrain preview diverged from execution.");
                }
            }
        }

        private static Color GetAbilityColor(AbilityDefinition ability)
        {
            if (ability.Effects.Any(effect => effect is HealEffectDefinition))
            {
                return new Color(0.2f, 1f, 0.42f);
            }

            if (ability.Effects.Any(effect => effect is GrantShieldEffectDefinition))
            {
                return new Color(0.18f, 0.82f, 1f);
            }

            if (ability.Effects.Any(effect => effect is PushEffectDefinition))
            {
                return new Color(0.78f, 0.3f, 1f);
            }

            if (ability.Effects.Any(effect => effect is ModifyTerrainEffectDefinition))
            {
                return new Color(0.68f, 0.42f, 1f);
            }

            return new Color(1f, 0.32f, 0.06f);
        }

        private static AbilityVisualKind GetAbilityVisualKind(AbilityDefinition ability)
        {
            if (ability.Effects.Any(effect => effect is ModifyTerrainEffectDefinition ||
                                              effect is PushEffectDefinition))
            {
                return AbilityVisualKind.Special;
            }

            if (ability.Effects.Any(effect => effect is HealEffectDefinition))
            {
                return AbilityVisualKind.Healing;
            }

            return ability.Effects.Any(effect => effect is GrantShieldEffectDefinition)
                ? AbilityVisualKind.Shield
                : AbilityVisualKind.Attack;
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

            if (mode == InteractionMode.Movement)
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
                    view.SetColor(color);
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

        private Vector3 UnitWorldPosition(GridCoordinate coordinate)
        {
            return new Vector3(
                WorldX(coordinate.X),
                grid.GetCell(coordinate).Height * HeightStep + 0.62f,
                WorldZ(coordinate.Y));
        }

        private Vector3 EffectWorldPosition(GridCoordinate coordinate)
        {
            return new Vector3(
                WorldX(coordinate.X),
                grid.GetCell(coordinate).Height * HeightStep + 0.12f,
                WorldZ(coordinate.Y));
        }

        private static float WorldX(int x) => (x - (GridWidth - 1) * 0.5f) * CellSpacing;
        private static float WorldZ(int y) => (y - (GridHeight - 1) * 0.5f) * CellSpacing;

        private void RefreshCellVisual(GridCoordinate coordinate)
        {
            if (!cellViews.TryGetValue(coordinate, out CellView view)) return;
            GridCell cell = grid.GetCell(coordinate);
            GetCellColumnGeometry(cell.Height, out float columnCenterY, out float columnHeight);
            view.transform.position = new Vector3(
                WorldX(coordinate.X),
                columnCenterY,
                WorldZ(coordinate.Y));
            view.transform.localScale = new Vector3(0.98f, columnHeight, 0.98f);
            view.SetBaseColor(GetCellBaseColor(cell));
        }

        private static Color GetCellBaseColor(GridCell cell)
        {
            float normalizedHeight = (cell.Height - MinimumTerrainHeight) /
                                     (float)(MaximumTerrainHeight - MinimumTerrainHeight);
            return cell.IsWalkable
                ? Color.Lerp(
                    new Color(0.12f, 0.25f, 0.42f),
                    new Color(0.58f, 0.42f, 0.25f),
                    normalizedHeight)
                : new Color(0.12f, 0.12f, 0.13f);
        }

        private static void GetCellColumnGeometry(int height, out float centerY, out float columnHeight)
        {
            float topY = height * HeightStep;
            float bottomY = MinimumTerrainHeight * HeightStep - 0.22f;
            columnHeight = Mathf.Max(0.22f, topY - bottomY);
            centerY = (topY + bottomY) * 0.5f;
        }

        private static int GetFixedHeight(GridCoordinate coordinate)
        {
            if (coordinate == new GridCoordinate(1, 4)) return 3;
            if (coordinate == new GridCoordinate(2, 2)) return 2;
            if (coordinate == new GridCoordinate(3, 2) || coordinate == new GridCoordinate(3, 3)) return 1;
            if (coordinate == new GridCoordinate(4, 4) || coordinate == new GridCoordinate(5, 4)) return 2;
            if (coordinate == new GridCoordinate(7, 7) || coordinate == new GridCoordinate(8, 7)) return 1;
            if (coordinate == new GridCoordinate(7, 5) || coordinate == new GridCoordinate(8, 5)) return -1;
            return 0;
        }

        private void Restart()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(string.IsNullOrEmpty(activeScene.path) ? activeScene.name : activeScene.path);
        }

        private void OnGUI()
        {
            EnsureInterfaceStyles();
            DrawMovementCostLabels();
            DrawAbilityPreviewLabels();
            DrawStatusEffectLabels();
            DrawCloudInformation();
            float hudWidth = Mathf.Min(760f, Screen.width * 0.6f);
            GUI.Box(new Rect(12f, 12f, hudWidth, 250f), "");
            GUI.Label(new Rect(24f, 18f, hudWidth - 180f, 24f), "SHARDS OF THE LIMIT — BLAZE WORM CONTENT SLICE");
            float cameraButtonX = hudWidth - 130f;
            if (GUI.Button(new Rect(cameraButtonX, 16f, 62f, 24f), "Cam [Q]")) RequestCameraRotation(-90f);
            if (GUI.Button(new Rect(cameraButtonX + 66f, 16f, 62f, 24f), "Cam [E]")) RequestCameraRotation(90f);

            CombatUnit active = turns?.CurrentUnit;
            if (active != null)
            {
                GUI.Label(new Rect(24f, 40f, hudWidth - 24f, 22f),
                    $"Tick {turns.CurrentTick}  Active: {active.DisplayName}  Actions: {active.CurrentActionPoints}/{ActionsPerTurn}  State: {active.CurrentAction}");
            }

            float columnWidth = (hudWidth - 42f) * 0.5f;
            DrawRoster(Team.Player, "ALLIED ECHOES", 24f, 66f, columnWidth, active);
            DrawRoster(Team.Opponent, "OPPONENTS", 30f + columnWidth, 66f, columnWidth, active);

            GUI.Label(new Rect(24f, 172f, hudWidth - 24f, 20f),
                "OBJECTIVE: Defeat every opponent before all allied Echoes fall.");
            DrawInitiativeTimeline(24f, 194f, hudWidth - 24f, active);

            GUI.Box(new Rect(12f, Screen.height - 194f, Screen.width - 24f, 182f), "");
            GUI.Label(new Rect(24f, Screen.height - 186f, Screen.width - 48f, 22f), status);
            GUI.Label(new Rect(24f, Screen.height - 164f, Screen.width - 48f, 22f), GetContextHint());
            GUI.Label(new Rect(24f, Screen.height - 142f, Screen.width - 48f, 20f),
                "[M] melee: height difference reduces range · [R] ranged: high ground adds range · AOE damage/healing affects both teams");
            bool canPlayerAct = !showOnboarding && !battleEnded && !isBusy && active?.ControlType == ControlType.Player;
            IReadOnlyList<AbilityDefinition> abilities = active != null && definitionsByUnit.TryGetValue(active, out UnitDefinition definition)
                ? definition.Abilities
                : Array.Empty<AbilityDefinition>();
            float buttonX = 24f;
            float abilityButtonWidth = Mathf.Min(190f, (Screen.width - 48f - 8f * Mathf.Max(0, abilities.Count - 1)) / Mathf.Max(1, abilities.Count));
            for (int index = 0; index < abilities.Count; index++)
            {
                AbilityDefinition ability = abilities[index];
                int remainingCooldown = active.GetCooldown(ability.StableId);
                string marker = mode == InteractionMode.AbilityTargeting && selectedAbilityIndex == index ? "▶ " : string.Empty;
                string rangeMarker = ability.RangeType == AbilityRangeType.Melee ? "M" : "R";
                string iconPadding = ability.Icon != null ? "      " : string.Empty;
                string label = $"{iconPadding}{marker}[{index + 1}][{rangeMarker}] {ability.DisplayName}\n{iconPadding}AP {ability.ActionCost}  CD {remainingCooldown}";
                GUI.enabled = canPlayerAct && active.CurrentActionPoints >= ability.ActionCost && remainingCooldown == 0;
                var abilityButton = new Rect(buttonX, Screen.height - 116f, abilityButtonWidth, 34f);
                if (GUI.Button(abilityButton, label)) BeginAbilityTargeting(index);
                if (ability.Icon != null)
                {
                    GUI.DrawTexture(
                        new Rect(abilityButton.x + 3f, abilityButton.y + 3f, 28f, 28f),
                        ability.Icon.texture,
                        ScaleMode.ScaleAndCrop,
                        true);
                }
                buttonX += abilityButtonWidth + 8f;
            }

            GUI.enabled = canPlayerAct;
            buttonX = 24f;
            if (GUI.Button(new Rect(buttonX, Screen.height - 74f, 122f, 34f), "Confirm [Enter]")) ConfirmCurrentChoice();
            buttonX += 130f;
            if (GUI.Button(new Rect(buttonX, Screen.height - 74f, 104f, 34f), "Cancel [Esc]")) CancelAbility();
            buttonX += 112f;
            if (GUI.Button(new Rect(buttonX, Screen.height - 74f, 126f, 34f), "End Turn [Space]")) EndPlayerTurn();
            GUI.enabled = true;
            if (GUI.Button(new Rect(Screen.width - 142f, Screen.height - 38f, 116f, 24f), "Restart [R]")) Restart();

            if (showOnboarding) DrawOnboarding();
            if (battleEnded) DrawBattleResult();
        }

        private void EnsureInterfaceStyles()
        {
            if (timelineStyle != null) return;
            timelineStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            timelineStyle.normal.textColor = Color.white;
            timelineActiveStyle = new GUIStyle(timelineStyle) { fontStyle = FontStyle.Bold };
            timelineActiveStyle.normal.textColor = new Color(1f, 0.94f, 0.35f);
            onboardingTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 25,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            onboardingBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            onboardingBodyStyle.normal.textColor = new Color(0.9f, 0.94f, 1f);
            resultTitleStyle = new GUIStyle(onboardingTitleStyle) { fontSize = 30 };
        }

        private void DrawInitiativeTimeline(float x, float y, float width, CombatUnit active)
        {
            if (turns == null) return;
            IReadOnlyList<TimelineEvent> upcoming = turns.GetUpcomingEvents(10, true, showStatusTimelineEvents);
            if (upcoming.Count == 0) return;
            float gap = 4f;
            float tileWidth = Mathf.Min(70f, (width - gap * (upcoming.Count - 1)) / upcoming.Count);
            float currentX = x;
            Color previousBackground = GUI.backgroundColor;
            showStatusTimelineEvents = GUI.Toggle(
                new Rect(x + width - 188f, y - 21f, 188f, 20f),
                showStatusTimelineEvents,
                "Show status events");
            foreach (TimelineEvent timelineEvent in upcoming)
            {
                CombatUnit unit = timelineEvent.Unit;
                GUI.backgroundColor = unit.Team == Team.Player
                    ? new Color(0.22f, 0.5f, 0.82f)
                    : new Color(0.82f, 0.3f, 0.2f);
                string intent = nextEnemyIntent != null && nextEnemyIntent.Unit == unit
                    ? $" {nextEnemyIntent.Codes}"
                    : string.Empty;
                bool isCurrent = timelineEvent.Kind == TimelineEventKind.Activation && unit == active &&
                                 timelineEvent.Tick == turns.CurrentTick;
                string prefix = isCurrent ? "▶" : timelineEvent.Kind == TimelineEventKind.StatusPulse ? "🔥" : string.Empty;
                var tile = new Rect(currentX, y, tileWidth, 40f);
                GUI.Box(tile, $"{prefix} {GetTimelineLabel(unit)}{intent}",
                    isCurrent ? timelineActiveStyle : timelineStyle);
                if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                {
                    GUI.DrawTexture(
                        new Rect(tile.x + 3f, tile.y + 3f, 34f, 34f),
                        definition.Portrait.texture,
                        ScaleMode.ScaleAndCrop,
                        true);
                }
                if (timelineEvent.Kind == TimelineEventKind.StatusPulse)
                {
                    string caption = timelineEvent.StatusId ?? "STATUS";
                    GUI.Box(new Rect(tile.x, tile.y + 24f, tile.width, 16f),
                        new GUIContent(caption.Length > 7 ? caption.Substring(0, 7) : caption, caption));
                }
                currentX += tileWidth + gap;
            }

            GUI.backgroundColor = previousBackground;
            GUI.Label(new Rect(x, y + 41f, width, 20f),
                "Next enemy only: A attack · M movement · H help · S special · – unused action");
        }

        private string GetTimelineLabel(CombatUnit unit)
        {
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                return string.Empty;
            string compact = new string(unit.DisplayName.Where(char.IsLetterOrDigit).ToArray());
            return compact.Length <= 4 ? compact.ToUpperInvariant() : compact.Substring(0, 4).ToUpperInvariant();
        }

        private string GetContextHint()
        {
            if (battleEnded) return "Battle complete. Retry the same scenario or exit Play Mode.";
            if (isBusy) return "Action is resolving. Watch the field markers, floating values and action log.";
            if (turns?.CurrentUnit?.ControlType != ControlType.Player)
                return "Enemy turn. Its resolved movement, ability and resource changes appear in the action log.";
            if (mode == InteractionMode.AbilityTargeting && selectedAbilityTarget.HasValue)
                return "Orange is the exact impact area. Review HP/SH, push destination or height, then press Enter.";
            if (mode == InteractionMode.AbilityTargeting)
                return "Dark green is total range; bright green is a legal target. Click a bright cell.";
            if (selectedDestination.HasValue)
                return "Cyan is the validated path. Click the destination again or press Enter to move.";
            return "Choose a blue reachable cell, or select a numbered ability. Space ends the active Echo's turn.";
        }

        private void DrawOnboarding()
        {
            float width = Mathf.Min(650f, Screen.width - 80f);
            float height = 330f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, string.Empty);
            string title;
            string body;
            switch (onboardingPage)
            {
                case 0:
                    title = "1/3 · MOVE AND HEIGHT";
                    body = "Blue cells are truly reachable, not just geometrically close. Numbers are movement cost. " +
                           "Climbing +1/+2 costs extra; a +3 step is blocked. Orange cones are obstacles.\n\n" +
                           "Click a destination once to preview the cyan path, then click again or press Enter. " +
                           "Right-drag rotates and tilts the camera; middle-drag or WASD/arrows moves it. " +
                           "Q/E rotates by 90 degrees, the wheel zooms, and Home resets the view.";
                    break;
                case 1:
                    title = "2/3 · ABILITIES";
                    body = "[M] is melee: every two height levels reduce range by one, rounded up. " +
                           "[R] is ranged: high ground adds range and a higher target reduces it.\n\n" +
                           "Dark green is total range, bright green is a legal target, orange is impact. " +
                           "Area damage and area healing affect allies and enemies alike. Burn stacks its damage, " +
                           "lasts three target turns and is shown above the affected unit.";
                    break;
                default:
                    title = "3/3 · TURNS AND INTENT";
                    body = "Each unit receives 2 actions. The timeline starts with the active unit. " +
                           "Only the next enemy reveals two intent slots: A attack, M movement, H help, S special.\n\n" +
                           "Floating values and the action log show what actually happened. Defeat every opponent. " +
                           "Press Space to end a turn and R to restart the scenario.";
                    break;
            }

            GUI.Label(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, 42f), title, onboardingTitleStyle);
            GUI.Label(new Rect(panel.x + 34f, panel.y + 78f, panel.width - 68f, 176f), body, onboardingBodyStyle);
            if (GUI.Button(new Rect(panel.x + 34f, panel.yMax - 58f, 126f, 34f), "Skip [Esc]")) CloseOnboarding();
            string next = onboardingPage < 2 ? "Next [Enter]" : "Start Battle";
            if (GUI.Button(new Rect(panel.xMax - 174f, panel.yMax - 58f, 140f, 34f), next)) AdvanceOnboarding();
        }

        private void AdvanceOnboarding()
        {
            if (onboardingPage < 2)
            {
                onboardingPage++;
                return;
            }

            CloseOnboarding();
        }

        private void CloseOnboarding()
        {
            if (!showOnboarding) return;
            showOnboarding = false;
            feedback?.AddLog("Onboarding complete. Objective: defeat every opponent.");
        }

        private void DrawBattleResult()
        {
            float width = 440f;
            float height = 250f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, string.Empty);
            bool victory = outcome == BattleOutcome.PlayerVictory;
            Color previous = GUI.color;
            GUI.color = victory ? new Color(0.35f, 1f, 0.48f) : new Color(1f, 0.32f, 0.25f);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 24f, panel.width - 40f, 48f),
                victory ? "VICTORY" : "DEFEAT", resultTitleStyle);
            GUI.color = previous;
            GUI.Label(new Rect(panel.x + 36f, panel.y + 84f, panel.width - 72f, 66f),
                $"Timeline tick {turns.CurrentTick} · Actions resolved: {resolvedActions}\n" +
                $"Battle time: {Time.unscaledTime - battleStartedAt:0.0}s",
                onboardingBodyStyle);
            if (GUI.Button(new Rect(panel.x + 52f, panel.yMax - 64f, 144f, 38f), "Retry")) Restart();
            if (GUI.Button(new Rect(panel.xMax - 196f, panel.yMax - 64f, 144f, 38f), "Exit")) ExitPrototype();
        }

        private static void ExitPrototype()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawMovementCostLabels()
        {
            if (mode != InteractionMode.Movement || selectedUnit == null ||
                selectedUnit.ControlType != ControlType.Player || battleCamera == null)
            {
                return;
            }

            if (movementCostStyle == null)
            {
                movementCostStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                movementCostStyle.normal.textColor = Color.white;
            }

            foreach (KeyValuePair<GridCoordinate, int> entry in reachable)
            {
                Vector3 screen = battleCamera.WorldToScreenPoint(
                    EffectWorldPosition(entry.Key) + Vector3.up * 0.42f);
                if (screen.z <= 0f) continue;
                GUI.Box(
                    new Rect(screen.x - 16f, Screen.height - screen.y - 12f, 32f, 24f),
                    entry.Value.ToString(),
                    movementCostStyle);
            }
        }

        private void DrawAbilityPreviewLabels()
        {
            if (mode != InteractionMode.AbilityTargeting || abilityPreview == null || battleCamera == null)
            {
                return;
            }

            if (abilityPreviewStyle == null)
            {
                abilityPreviewStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                abilityPreviewStyle.normal.textColor = Color.white;
            }

            foreach (IGrouping<CombatUnit, ICombatEffectResult> group in abilityPreview.Results.GroupBy(result => result.Target))
            {
                if (!unitViews.TryGetValue(group.Key, out CombatUnitView view)) continue;
                Vector3 screen = battleCamera.WorldToScreenPoint(view.transform.position + Vector3.up * 1.18f);
                if (screen.z <= 0f) continue;
                string label = string.Join("  ", group.Select(BuildPreviewLabel).Where(text => text.Length > 0));
                if (label.Length == 0) label = "NO CHANGE";
                GUI.Box(
                    new Rect(screen.x - 70f, Screen.height - screen.y - 14f, 140f, 28f),
                    label,
                    abilityPreviewStyle);
            }

            foreach (TerrainChangeResult terrainResult in abilityPreview.TerrainResults)
            {
                Vector3 screen = battleCamera.WorldToScreenPoint(
                    EffectWorldPosition(terrainResult.Coordinate) + Vector3.up * 0.42f);
                if (screen.z <= 0f) continue;
                GUI.Box(
                    new Rect(screen.x - 54f, Screen.height - screen.y - 14f, 108f, 28f),
                    $"H {terrainResult.PreviousHeight}→{terrainResult.NewHeight}",
                    abilityPreviewStyle);
            }
        }

        private static string BuildPreviewLabel(ICombatEffectResult result)
        {
            if (result is StatusAppliedResult status) return $"{status.State.Rules.Id} ×{status.State.Stacks}";
            switch (result.Kind)
            {
                case CombatEffectKind.Damage:
                    string shieldDamage = result.ShieldDelta < 0 ? $"{result.ShieldDelta} SH" : string.Empty;
                    string healthDamage = result.HealthDelta < 0 ? $"{result.HealthDelta} HP" : string.Empty;
                    return $"{shieldDamage} {healthDamage}".Trim();
                case CombatEffectKind.Heal:
                    return result.HealthDelta > 0 ? $"+{result.HealthDelta} HP" : string.Empty;
                case CombatEffectKind.Shield:
                    return result.ShieldDelta > 0 ? $"+{result.ShieldDelta} SH" : string.Empty;
                case CombatEffectKind.Push:
                    var push = (PushResult)result;
                    return push.StepsMoved > 0 ? $"PUSH {push.StepsMoved}" : "BLOCKED";
                case CombatEffectKind.Burning:
                    var burn = (BurnAppliedResult)result;
                    return $"BURN +{burn.AddedDamagePerPulse}/100t → {burn.NewDamagePerPulse}/100t ×{GetBurnPulseCount(burn)}";
                default:
                    return string.Empty;
            }
        }

        private static int GetBurnPulseCount(BurnAppliedResult burn)
        {
            return burn.NewNextPulseTick <= burn.NewExpireTick
                ? (int)((burn.NewExpireTick - burn.NewNextPulseTick) /
                        BurningCalculator.DefaultPulseInterval) + 1
                : 0;
        }

        private void DrawRoster(Team team, string heading, float x, float y, float width, CombatUnit active)
        {
            GUI.Label(new Rect(x, y, width, 20f), heading);
            int row = 0;
            foreach (CombatUnit unit in units.Where(candidate => candidate.Team == team))
            {
                string prefix = unit.IsDefeated ? "✕" : unit == active ? "▶" : "•";
                string burning = unit.IsBurning
                    ? $"  BURN {unit.BurningState.DamagePerPulse}/100t ({unit.BurningState.RemainingPulses()})"
                    : string.Empty;
                float rowY = y + 22f + row * 22f;
                float textX = x;
                if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                {
                    GUI.DrawTexture(
                        new Rect(x, rowY + 1f, 20f, 20f),
                        definition.Portrait.texture,
                        ScaleMode.ScaleAndCrop,
                        true);
                    textX += 24f;
                }

                string text = $"{prefix} {unit.DisplayName}  HP {unit.CurrentHealth}/{unit.MaximumHealth}  SH {unit.CurrentShield}/{unit.MaximumShield}  ATK {unit.Attack} DEF {unit.EffectiveDefense:0.#} SPD {unit.Speed}{burning}";
                GUI.Label(new Rect(textX, rowY, width - (textX - x), 22f), text);
                row++;
            }
        }

        private void DrawStatusEffectLabels()
        {
            if (battleCamera == null) return;
            if (statusEffectStyle == null)
            {
                statusEffectStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                statusEffectStyle.normal.textColor = new Color(1f, 0.72f, 0.18f);
            }

            foreach (CombatUnit unit in units.Where(candidate => candidate.IsBurning && !candidate.IsDefeated))
            {
                if (!unitViews.TryGetValue(unit, out CombatUnitView view) || !view.gameObject.activeInHierarchy) continue;
                Vector3 screen = battleCamera.WorldToScreenPoint(view.transform.position + Vector3.up * 1.38f);
                if (screen.z <= 0f) continue;
                var statusRect = new Rect(screen.x - 62f, Screen.height - screen.y - 13f, 124f, 26f);
                string padding = burningIcon != null ? "     " : string.Empty;
                GUI.Box(
                    statusRect,
                    $"{padding}BURN {unit.BurningState.DamagePerPulse}/100t · {unit.BurningState.RemainingPulses()}x",
                    statusEffectStyle);
                if (burningIcon != null)
                {
                    GUI.DrawTexture(
                        new Rect(statusRect.x + 3f, statusRect.y + 3f, 20f, 20f),
                        burningIcon.texture,
                        ScaleMode.ScaleAndCrop,
                        true);
                }
            }
        }
    }
}
