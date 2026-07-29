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
    public sealed class BattlePrototypeController : MonoBehaviour
    {
        private enum InteractionMode
        {
            Movement,
            AbilityTargeting
        }

        private const int GridWidth = 6;
        private const int GridHeight = 6;
        private const float CellSpacing = 1.05f;
        private const float HeightStep = 0.55f;
        private const int ActionsPerTurn = 2;
        private const int MoveActionCost = 1;

        private static readonly Color ReachableColor = new Color(0.18f, 0.48f, 0.92f);
        private static readonly Color PathColor = new Color(0.15f, 0.9f, 1f);
        private static readonly Color TargetableColor = new Color(0.25f, 0.75f, 0.35f);
        private static readonly Color ImpactColor = new Color(1f, 0.38f, 0.12f);
        private static readonly Color SelectedCellColor = new Color(1f, 0.9f, 0.2f);

        private readonly Dictionary<GridCoordinate, CellView> cellViews = new Dictionary<GridCoordinate, CellView>();
        private readonly Dictionary<CombatUnit, CombatUnitView> unitViews = new Dictionary<CombatUnit, CombatUnitView>();
        private readonly Dictionary<CombatUnit, UnitDefinition> definitionsByUnit = new Dictionary<CombatUnit, UnitDefinition>();
        private readonly List<CombatUnit> units = new List<CombatUnit>();

        private TacticalGrid grid;
        private IReadOnlyList<UnitDefinition> playerDefinitions = Array.Empty<UnitDefinition>();
        private IReadOnlyList<UnitDefinition> opponentDefinitions = Array.Empty<UnitDefinition>();
        private CombatUnit selectedUnit;
        private int selectedAbilityIndex;
        private TurnCoordinator turns;
        private BattleFeedbackView feedback;
        private InteractionMode mode;
        private IReadOnlyDictionary<GridCoordinate, int> reachable = new Dictionary<GridCoordinate, int>();
        private IReadOnlyList<GridCoordinate> selectedPath = Array.Empty<GridCoordinate>();
        private IReadOnlyCollection<GridCoordinate> targetable = Array.Empty<GridCoordinate>();
        private IReadOnlyCollection<GridCoordinate> previewImpact = Array.Empty<GridCoordinate>();
        private GridCoordinate? selectedDestination;
        private GridCoordinate? selectedAbilityTarget;
        private bool isBusy;
        private bool battleEnded;
        private string status = "Select the blue Echo.";

        public CombatUnit ActiveUnit => turns?.CurrentUnit;
        public int Round => turns?.Round ?? 0;
        public IReadOnlyList<CombatUnit> Units => units;

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

        private void Start()
        {
            LoadDefinitions();
            BuildGrid();
            ConfigureCameraAndLight();
            feedback = gameObject.AddComponent<BattleFeedbackView>();
            SpawnUnits();
            turns = new TurnCoordinator(units, ActionsPerTurn);
            BeginTurn(turns.StartBattle());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                Restart();
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

            bool pointerOutsideHud = Input.mousePosition.y > 125f && Input.mousePosition.y < Screen.height - 270f;
            if (Input.GetMouseButtonDown(0) && pointerOutsideHud)
            {
                HandlePointer(Input.mousePosition);
            }
        }

        private void LoadDefinitions()
        {
            playerDefinitions = new[]
            {
                Resources.Load<UnitDefinition>("Prototype/Units/PlayerEcho"),
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

            if (abilities.Length < 4 || abilities.Length > 6)
            {
                throw new InvalidOperationException("Stage 2 content must expose four to six distinct abilities.");
            }

            AbilityDefinition sharedFireBurst = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst");
            if (sharedFireBurst == null || playerDefinitions.Count(definition => definition.Abilities.Contains(sharedFireBurst)) < 2)
            {
                throw new InvalidOperationException("At least two allied Echoes must reuse the shared Fire Burst definition.");
            }
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
                    bool walkable = coordinate != new GridCoordinate(2, 4) && coordinate != new GridCoordinate(3, 1);
                    logicalCells.Add(new GridCell(coordinate, height, walkable));
                }
            }

            grid = new TacticalGrid(logicalCells);
            foreach (GridCell cell in grid.Cells)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Cell {cell.Coordinate} H{cell.Height}{(cell.IsWalkable ? string.Empty : " Blocked")}";
                block.transform.SetParent(transform, false);
                float columnHeight = 0.22f + cell.Height * HeightStep;
                block.transform.position = new Vector3(
                    WorldX(cell.Coordinate.X),
                    columnHeight * 0.5f - 0.22f,
                    WorldZ(cell.Coordinate.Y));
                block.transform.localScale = new Vector3(0.98f, columnHeight, 0.98f);
                Color baseColor = cell.IsWalkable
                    ? Color.Lerp(new Color(0.22f, 0.25f, 0.29f), new Color(0.48f, 0.43f, 0.35f), cell.Height / 3f)
                    : new Color(0.12f, 0.12f, 0.13f);
                CellView view = block.AddComponent<CellView>();
                view.Initialize(cell.Coordinate, baseColor);
                cellViews.Add(cell.Coordinate, view);
            }
        }

        private void ConfigureCameraAndLight()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 5.3f;
            camera.transform.position = new Vector3(6.7f, 8.4f, -7.2f);
            camera.transform.LookAt(new Vector3(0f, 0.4f, 0f));
            camera.backgroundColor = new Color(0.055f, 0.07f, 0.1f);

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

        private void SpawnUnits()
        {
            SpawnUnit(playerDefinitions[0], Team.Player, ControlType.Player, new GridCoordinate(0, 0), new Color(0.15f, 0.55f, 1f));
            SpawnUnit(playerDefinitions[1], Team.Player, ControlType.Player, new GridCoordinate(0, 2), new Color(0.18f, 0.82f, 1f));
            SpawnUnit(playerDefinitions[2], Team.Player, ControlType.Player, new GridCoordinate(1, 1), new Color(0.1f, 0.86f, 0.68f));
            SpawnUnit(opponentDefinitions[0], Team.Opponent, ControlType.BasicAI, new GridCoordinate(5, 5), new Color(0.95f, 0.2f, 0.18f));
            SpawnUnit(opponentDefinitions[1], Team.Opponent, ControlType.BasicAI, new GridCoordinate(5, 2), new Color(1f, 0.52f, 0.12f));
            SpawnUnit(opponentDefinitions[1], Team.Opponent, ControlType.BasicAI, new GridCoordinate(4, 4), new Color(0.95f, 0.72f, 0.12f));
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
                definition.Initiative,
                definition.MovementRange,
                team,
                controlType,
                coordinate);
            units.Add(unit);
            definitionsByUnit.Add(unit, definition);

            GameObject body = definition.VisualPrefab != null
                ? Instantiate(definition.VisualPrefab)
                : GameObject.CreatePrimitive(team == Team.Player ? PrimitiveType.Capsule : PrimitiveType.Cube);
            body.name = $"{definition.DisplayName} [{team}]";
            body.transform.SetParent(transform, false);
            body.transform.position = UnitWorldPosition(coordinate);
            body.transform.localScale = team == Team.Player
                ? new Vector3(0.58f, 0.72f, 0.58f)
                : new Vector3(0.66f, 0.66f, 0.66f);
            CombatUnitView view = body.AddComponent<CombatUnitView>();
            view.Initialize(unit, fallbackColor);
            unitViews.Add(unit, view);
        }

        private void HandlePointer(Vector3 screenPosition)
        {
            Camera camera = Camera.main;
            if (camera == null || !Physics.Raycast(camera.ScreenPointToRay(screenPosition), out RaycastHit hit, 100f))
            {
                return;
            }

            CombatUnitView unitView = hit.collider.GetComponent<CombatUnitView>();
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
                previewImpact = AbilityRuntime.GetImpactCells(ability, grid, coordinate);
                selectedUnit.CurrentAction = $"Previewing {ability.DisplayName} at {coordinate}";
                status = "Orange cells are the impact preview. Confirm with Enter or the button.";
                feedback.AddLog($"Preview: {ability.DisplayName} at {coordinate}, {previewImpact.Count} affected cell(s)");
                RefreshHighlights();
                return;
            }

            if (!reachable.ContainsKey(coordinate))
            {
                status = "That cell is not reachable.";
                return;
            }

            IReadOnlyList<GridCoordinate> path = grid.FindPath(
                selectedUnit.Cell,
                coordinate,
                selectedUnit.MovementRange,
                IsOccupied);
            if (path.Count == 0)
            {
                status = "No legal path found.";
                return;
            }

            if (selectedDestination == coordinate)
            {
                StartCoroutine(MoveSelectedUnit(path));
                return;
            }

            selectedDestination = coordinate;
            selectedPath = path;
            selectedUnit.CurrentAction = $"Path to {coordinate}";
            status = "Cyan path selected. Click the cell again, press Enter, or confirm.";
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
            previewImpact = Array.Empty<GridCoordinate>();
            selectedAbilityTarget = null;
            selectedUnit.CurrentAction = "Ready for player input";
            reachable = unit.CurrentActionPoints >= MoveActionCost
                ? grid.GetReachable(unit.Cell, unit.MovementRange, IsOccupied)
                : new Dictionary<GridCoordinate, int>();
            status = unit.CurrentActionPoints > 0
                ? "Your turn. Blue cells are reachable; choose an ability with 1–6. Space ends the turn."
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
            targetable = AbilityRuntime.GetTargetableCells(ability, grid, selectedUnit);
            selectedUnit.CurrentAction = $"Choosing target for {ability.DisplayName}";
            status = "Green cells are valid targets. Choose one to reveal the orange impact area.";
            RefreshHighlights();
        }

        private void ConfirmCurrentChoice()
        {
            if (mode == InteractionMode.Movement && selectedDestination.HasValue && selectedPath.Count > 0)
            {
                StartCoroutine(MoveSelectedUnit(selectedPath));
            }
            else if (mode == InteractionMode.AbilityTargeting && selectedAbilityTarget.HasValue)
            {
                StartCoroutine(ConfirmAbility());
            }
        }

        private IEnumerator MoveSelectedUnit(IReadOnlyList<GridCoordinate> path)
        {
            if (!selectedUnit.SpendActionPoints(MoveActionCost))
            {
                status = "Not enough action points to move.";
                yield break;
            }

            isBusy = true;
            GridCoordinate start = selectedUnit.Cell;
            selectedUnit.CurrentAction = "Moving";
            status = "Moving along the validated path...";
            feedback.AddLog($"{selectedUnit.DisplayName} moves {start} → {path[path.Count - 1]} (-{MoveActionCost} AP)");
            yield return AnimateMovement(selectedUnit, path);

            selectedUnit.CurrentAction = "Move complete";
            feedback.AddLog($"{selectedUnit.DisplayName} reached {selectedUnit.Cell}");
            selectedDestination = null;
            selectedPath = Array.Empty<GridCoordinate>();
            reachable = selectedUnit.CurrentActionPoints >= MoveActionCost
                ? grid.GetReachable(selectedUnit.Cell, selectedUnit.MovementRange, IsOccupied)
                : new Dictionary<GridCoordinate, int>();
            status = selectedUnit.CurrentActionPoints > 0
                ? "Move complete. Choose an ability or spend the remaining action."
                : "Move complete. No actions remain; ending turn...";
            isBusy = false;
            RefreshHighlights();
            if (selectedUnit.CurrentActionPoints == 0)
            {
                yield return new WaitForSeconds(0.45f);
                EndPlayerTurn();
            }
        }

        private IEnumerator AnimateMovement(CombatUnit unit, IReadOnlyList<GridCoordinate> path)
        {
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
                actualImpact.Select(EffectWorldPosition).ToArray());

            IReadOnlyList<DamageResult> results = ResolveAbility(selectedUnit, ability, target);
            yield return PresentDamageResults(results);
            isBusy = false;
            status = BuildDamageSummary(results);
            ClearInteractionState();
            if (EvaluateBattleEnd()) yield break;

            reachable = selectedUnit.CurrentActionPoints >= MoveActionCost
                ? grid.GetReachable(selectedUnit.Cell, selectedUnit.MovementRange, IsOccupied)
                : new Dictionary<GridCoordinate, int>();
            RefreshHighlights();
            if (selectedUnit.CurrentActionPoints == 0)
            {
                status += " No actions remain; ending turn...";
                StartCoroutine(EndTurnAfterDelay());
            }
        }

        private IReadOnlyList<DamageResult> ResolveAbility(
            CombatUnit source,
            AbilityDefinition ability,
            GridCoordinate target)
        {
            IReadOnlyList<DamageResult> results = AbilityRuntime.Execute(ability, grid, target, units);
            source.SetCooldown(ability.StableId, ability.Cooldown);
            source.CurrentAction = $"Used {ability.DisplayName}";
            feedback.AddLog($"{ability.DisplayName} cooldown set to {ability.Cooldown}");
            return results;
        }

        private IEnumerator PresentDamageResults(IReadOnlyList<DamageResult> results)
        {
            var hitViews = new List<CombatUnitView>();
            foreach (DamageResult result in results)
            {
                if (!unitViews.TryGetValue(result.Target, out CombatUnitView view)) continue;
                if (!hitViews.Contains(view)) hitViews.Add(view);
                Vector3 labelPosition = view.transform.position + Vector3.up * 0.75f;
                if (result.ShieldDamage > 0)
                {
                    feedback.AddFloatingText(labelPosition, $"-{result.ShieldDamage} SHIELD", new Color(0.2f, 0.75f, 1f));
                }

                if (result.HealthDamage > 0)
                {
                    feedback.AddFloatingText(labelPosition + Vector3.up * 0.22f, $"-{result.HealthDamage} HP", new Color(1f, 0.28f, 0.2f));
                }

                feedback.AddLog(
                    $"{result.Target.DisplayName}: shield -{result.ShieldDamage}, health -{result.HealthDamage}");
            }

            yield return feedback.PlayHitReactions(hitViews);
            foreach (DamageResult result in results.Where(result => result.Defeated))
            {
                if (unitViews.TryGetValue(result.Target, out CombatUnitView defeatedView))
                {
                    feedback.AddLog($"{result.Target.DisplayName} is defeated");
                    defeatedView.gameObject.SetActive(false);
                }
            }
        }

        private static string BuildDamageSummary(IReadOnlyList<DamageResult> results)
        {
            return results.Count == 0
                ? "No unit was inside the impact area."
                : string.Join(", ", results.Select(result =>
                    $"{result.Target.DisplayName}: -{result.ShieldDamage} shield, -{result.HealthDamage} health"));
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

            turns.CurrentUnit.CurrentAction = "Turn ended";
            feedback.AddLog($"{turns.CurrentUnit.DisplayName} ends the turn");
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
            if (unit == null || EvaluateBattleEnd()) return;

            Color teamColor = unit.Team == Team.Player
                ? new Color(0.35f, 0.75f, 1f)
                : new Color(1f, 0.4f, 0.28f);
            feedback.ShowTurnBanner($"ROUND {turns.Round} · {unit.DisplayName}", teamColor);
            feedback.AddLog($"Round {turns.Round}: {unit.DisplayName} turn ({unit.CurrentActionPoints} AP)");
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition))
            {
                string abilityState = string.Join(", ", definition.Abilities.Select(ability =>
                    unit.GetCooldown(ability.StableId) == 0
                        ? $"{ability.DisplayName}: ready"
                        : $"{ability.DisplayName}: CD {unit.GetCooldown(ability.StableId)}"));
                feedback.AddLog(abilityState);
            }

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
                isBusy = false;
                if (!EvaluateBattleEnd()) AdvanceTurn();
                yield break;
            }

            bool canAttack = TryPlanBestAbility(unit, out AbilityDefinition ability, out GridCoordinate abilityTarget);
            if (!canAttack && unit.CurrentActionPoints >= MoveActionCost)
            {
                IReadOnlyDictionary<GridCoordinate, int> aiReachable = grid.GetReachable(
                    unit.Cell,
                    unit.MovementRange,
                    IsOccupied);
                if (aiReachable.Count > 0)
                {
                    GridCoordinate destination = aiReachable.Keys
                        .OrderBy(coordinate => coordinate.ManhattanDistance(nearestTarget.Cell))
                        .ThenBy(coordinate => aiReachable[coordinate])
                        .ThenBy(coordinate => coordinate.X)
                        .ThenBy(coordinate => coordinate.Y)
                        .First();
                    IReadOnlyList<GridCoordinate> path = grid.FindPath(
                        unit.Cell,
                        destination,
                        unit.MovementRange,
                        IsOccupied);
                    if (path.Count > 1 && unit.SpendActionPoints(MoveActionCost))
                    {
                        unit.CurrentAction = $"Moving to {destination}";
                        selectedPath = path;
                        status = $"{unit.DisplayName} moves closer.";
                        feedback.AddLog($"{unit.DisplayName} moves {unit.Cell} → {destination} (-{MoveActionCost} AP)");
                        RefreshHighlights();
                        yield return AnimateMovement(unit, path);
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
                targetable = AbilityRuntime.GetTargetableCells(ability, grid, unit);
                previewImpact = AbilityRuntime.GetImpactCells(ability, grid, abilityTarget);
                unit.CurrentAction = $"Targeting {ability.DisplayName}";
                status = $"{unit.DisplayName} previews {ability.DisplayName}.";
                feedback.AddLog($"{unit.DisplayName} casts {ability.DisplayName} at {abilityTarget} (-{ability.ActionCost} AP)");
                RefreshHighlights();
                yield return new WaitForSeconds(0.35f);
                yield return feedback.PlayAbility(
                    unitViews[unit].transform.position + Vector3.up * 0.4f,
                    EffectWorldPosition(abilityTarget) + Vector3.up * 0.45f,
                    previewImpact.Select(EffectWorldPosition).ToArray());

                IReadOnlyList<DamageResult> results = ResolveAbility(unit, ability, abilityTarget);
                yield return PresentDamageResults(results);
                status = BuildDamageSummary(results);
                ClearInteractionState();
                RefreshHighlights();
                yield return new WaitForSeconds(0.45f);
            }

            if (EvaluateBattleEnd()) yield break;

            unit.CurrentAction = "Turn complete";
            feedback.AddLog($"{unit.DisplayName} ends the turn");
            isBusy = false;
            AdvanceTurn();
        }

        private bool TryPlanBestAbility(
            CombatUnit source,
            out AbilityDefinition selectedAbility,
            out GridCoordinate selectedTarget)
        {
            selectedAbility = null;
            selectedTarget = default;
            if (!definitionsByUnit.TryGetValue(source, out UnitDefinition definition)) return false;

            int bestScore = int.MinValue;
            foreach (AbilityDefinition ability in definition.Abilities)
            {
                if (source.GetCooldown(ability.StableId) > 0 || source.CurrentActionPoints < ability.ActionCost)
                {
                    continue;
                }

                int damage = ability.Effects.OfType<DamageEffectDefinition>().Sum(effect => effect.Amount);
                foreach (GridCoordinate candidate in AbilityRuntime.GetTargetableCells(ability, grid, source))
                {
                    IReadOnlyCollection<GridCoordinate> impact = AbilityRuntime.GetImpactCells(ability, grid, candidate);
                    bool hitsFriendly = units.Any(target =>
                        !target.IsDefeated && target.Team == source.Team && impact.Contains(target.Cell));
                    if (hitsFriendly) continue;

                    int enemyHits = units.Count(target =>
                        !target.IsDefeated && target.Team != source.Team && impact.Contains(target.Cell));
                    if (enemyHits == 0) continue;

                    int score = enemyHits * 1000 + damage * 10 - source.Cell.ManhattanDistance(candidate);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    selectedAbility = ability;
                    selectedTarget = candidate;
                }
            }

            return selectedAbility != null;
        }

        private bool EvaluateBattleEnd()
        {
            bool playerAlive = units.Any(unit => unit.Team == Team.Player && !unit.IsDefeated);
            bool opponentAlive = units.Any(unit => unit.Team == Team.Opponent && !unit.IsDefeated);
            if (playerAlive && opponentAlive) return false;

            battleEnded = true;
            isBusy = false;
            ClearInteractionState();
            status = playerAlive
                ? "Victory! The opponent is defeated. Press R to restart."
                : "Defeat. Your Echo is defeated. Press R to restart.";
            feedback.AddLog(status);
            feedback.ShowTurnBanner(playerAlive ? "VICTORY" : "DEFEAT", playerAlive ? Color.green : Color.red, 2f);
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
            previewImpact = Array.Empty<GridCoordinate>();
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
                SetColor(targetable, TargetableColor);
                SetColor(previewImpact, ImpactColor);
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

        private static int GetFixedHeight(GridCoordinate coordinate)
        {
            if (coordinate == new GridCoordinate(2, 2)) return 2;
            if (coordinate == new GridCoordinate(3, 2) || coordinate == new GridCoordinate(3, 3)) return 1;
            if (coordinate == new GridCoordinate(4, 4) || coordinate == new GridCoordinate(5, 4)) return 2;
            return 0;
        }

        private void Restart()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(string.IsNullOrEmpty(activeScene.path) ? activeScene.name : activeScene.path);
        }

        private void OnGUI()
        {
            float hudWidth = Mathf.Min(760f, Screen.width * 0.6f);
            GUI.Box(new Rect(12f, 12f, hudWidth, 222f), "");
            GUI.Label(new Rect(24f, 18f, hudWidth - 24f, 24f), "SHARDS OF THE LIMIT — STAGE 2 BATTLE");

            CombatUnit active = turns?.CurrentUnit;
            if (active != null)
            {
                GUI.Label(new Rect(24f, 40f, hudWidth - 24f, 22f),
                    $"Round {turns.Round}  Active: {active.DisplayName}  Actions: {active.CurrentActionPoints}/{ActionsPerTurn}  State: {active.CurrentAction}");
            }

            float columnWidth = (hudWidth - 42f) * 0.5f;
            DrawRoster(Team.Player, "ALLIED ECHOES", 24f, 66f, columnWidth, active);
            DrawRoster(Team.Opponent, "OPPONENTS", 30f + columnWidth, 66f, columnWidth, active);

            string order = turns == null
                ? string.Empty
                : string.Join(" → ", turns.LivingTurnOrder.Select(unit => unit == active ? $"[{unit.DisplayName}]" : unit.DisplayName));
            GUI.Label(new Rect(24f, 190f, hudWidth - 24f, 34f), $"Initiative: {order}");

            GUI.Box(new Rect(12f, Screen.height - 114f, Screen.width - 24f, 102f), "");
            GUI.Label(new Rect(24f, Screen.height - 106f, Screen.width - 48f, 22f), status);
            bool canPlayerAct = !battleEnded && !isBusy && active?.ControlType == ControlType.Player;
            IReadOnlyList<AbilityDefinition> abilities = active != null && definitionsByUnit.TryGetValue(active, out UnitDefinition definition)
                ? definition.Abilities
                : Array.Empty<AbilityDefinition>();
            float buttonX = 24f;
            for (int index = 0; index < abilities.Count; index++)
            {
                AbilityDefinition ability = abilities[index];
                int remainingCooldown = active.GetCooldown(ability.StableId);
                string marker = mode == InteractionMode.AbilityTargeting && selectedAbilityIndex == index ? "▶ " : string.Empty;
                string label = $"{marker}[{index + 1}] {ability.DisplayName}  AP {ability.ActionCost}  CD {remainingCooldown}";
                GUI.enabled = canPlayerAct && active.CurrentActionPoints >= ability.ActionCost && remainingCooldown == 0;
                if (GUI.Button(new Rect(buttonX, Screen.height - 76f, 190f, 34f), label)) BeginAbilityTargeting(index);
                buttonX += 198f;
            }

            GUI.enabled = canPlayerAct;
            if (GUI.Button(new Rect(buttonX, Screen.height - 76f, 122f, 34f), "Confirm [Enter]")) ConfirmCurrentChoice();
            buttonX += 130f;
            if (GUI.Button(new Rect(buttonX, Screen.height - 76f, 104f, 34f), "Cancel [Esc]")) CancelAbility();
            buttonX += 112f;
            if (GUI.Button(new Rect(buttonX, Screen.height - 76f, 126f, 34f), "End Turn [Space]")) EndPlayerTurn();
            GUI.enabled = true;
            if (GUI.Button(new Rect(Screen.width - 142f, Screen.height - 38f, 116f, 24f), "Restart [R]")) Restart();
        }

        private void DrawRoster(Team team, string heading, float x, float y, float width, CombatUnit active)
        {
            GUI.Label(new Rect(x, y, width, 20f), heading);
            int row = 0;
            foreach (CombatUnit unit in units.Where(candidate => candidate.Team == team))
            {
                string prefix = unit.IsDefeated ? "✕" : unit == active ? "▶" : "•";
                string text = $"{prefix} {unit.DisplayName}  HP {unit.CurrentHealth}/{unit.MaximumHealth}  SH {unit.CurrentShield}/{unit.MaximumShield}";
                GUI.Label(new Rect(x, y + 22f + row * 22f, width, 22f), text);
                row++;
            }
        }
    }
}
