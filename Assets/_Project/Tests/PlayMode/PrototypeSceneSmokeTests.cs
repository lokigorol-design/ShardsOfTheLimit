using System.Collections;
using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShardsOfTheLimit.Tests
{
    public sealed class PrototypeSceneSmokeTests
    {
        [UnityTest]
        public IEnumerator PrototypeSceneLoadsStageThreeRosterAndCompletesAiSequence()
        {
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;

            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo("Assets/_Project/Scenes/PrototypeBattle.unity"));
            UnitDefinition blazeDefinition = Resources.Load<UnitDefinition>("Prototype/Units/BlazeWorm");
            Assert.That(blazeDefinition, Is.Not.Null);
            UnitDefinition rangerDefinition = Resources.Load<UnitDefinition>("Prototype/Units/RangerEcho");
            UnitDefinition skirmisherDefinition = Resources.Load<UnitDefinition>("Prototype/Units/SkirmisherEcho");
            UnitDefinition raiderDefinition = Resources.Load<UnitDefinition>("Prototype/Units/RaiderEnemy");
            Assert.That(rangerDefinition, Is.Not.Null);
            Assert.That(skirmisherDefinition, Is.Not.Null);
            Assert.That(raiderDefinition, Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst"), Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/RepairPulse"), Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/AegisField"), Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/RepulseStrike"), Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/RaiseGround"), Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/LowerGround"), Is.Not.Null);
            Assert.That(Resources.LoadAll<AbilityDefinition>("Prototype/Abilities").Length, Is.EqualTo(18));
            Assert.That(blazeDefinition.Abilities.Count, Is.EqualTo(3));
            Assert.That(blazeDefinition.Abilities.All(ability => ability.Icon != null), Is.True);
            BattlePrototypeController controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            BattleFeedbackView feedback = Object.FindAnyObjectByType<BattleFeedbackView>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(feedback, Is.Not.Null);
            Assert.That(controller.CurrentTimelineTick, Is.GreaterThan(0));
            Assert.That(controller.ActiveUnit.ControlType, Is.EqualTo(ControlType.Player));
            Assert.That(controller.ActiveUnit.DisplayName, Is.EqualTo("Teal Skirmisher"),
                "Speed 12 acts first (83 ticks); the Speed 8 worm acts at 125.");
            Assert.That(controller.ActiveUnit.CurrentActionPoints, Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<CellView>().Length, Is.EqualTo(100));
            Assert.That(Object.FindObjectsByType<CombatUnitView>().Length, Is.EqualTo(6));
            CombatUnitView[] unitViews = Object.FindObjectsByType<CombatUnitView>();
            CombatUnitView blazeView = unitViews.Single(view => view.Unit.DefinitionId == "blaze-worm");
            Assert.That(blazeView.GetComponentsInChildren<Renderer>().Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(blazeView.transform.eulerAngles.x, 0f)), Is.LessThan(0.1f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(blazeView.transform.eulerAngles.y, 45f)), Is.LessThan(0.1f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(blazeView.transform.eulerAngles.z, 0f)), Is.LessThan(0.1f),
                "The authored model must use only a deliberate horizontal facing rotation.");
            Transform blazeVisual = blazeView.transform.GetChild(0);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(blazeVisual.localEulerAngles.x, 90f)), Is.LessThan(0.1f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(blazeVisual.localEulerAngles.y, -135f)), Is.LessThan(0.1f),
                "The authored visual correction must preserve the user-approved facing.");
            Assert.That(blazeView.GetComponent<BoxCollider>(), Is.Not.Null,
                "The authored model root must remain selectable.");
            Renderer blazeRenderer = blazeView.GetComponentInChildren<Renderer>();
            Assert.That(blazeRenderer.bounds.size.x, Is.InRange(0.8f, 1.7f));
            Assert.That(blazeRenderer.bounds.size.y, Is.InRange(0.8f, 1.7f));
            Assert.That(blazeRenderer.bounds.size.z, Is.InRange(0.8f, 1.7f),
                "The user-authored facing may swap model bounds, but the visual must remain cell-sized.");
            Assert.That(blazeRenderer.bounds.min.y, Is.EqualTo(0f).Within(0.03f),
                "The authored model must stand on the height-zero cell surface.");
            Assert.That(blazeRenderer.material.GetTexture("_MainTex"), Is.Not.Null);
            Assert.That(blazeRenderer.material.GetTexture("_BumpMap"), Is.Not.Null);
            Assert.That(blazeRenderer.material.GetTexture("_MetallicGlossMap"), Is.Not.Null);
            Assert.That(blazeRenderer.material.GetTexture("_EmissionMap"), Is.Not.Null);
            Assert.That(unitViews.Where(view => view.Unit.DefinitionId != "blaze-worm")
                .All(view => view.GetComponentsInChildren<Renderer>().Length >= 5), Is.True,
                "Remaining temporary models must keep readable composite silhouettes.");
            ObstacleView[] obstacles = Object.FindObjectsByType<ObstacleView>();
            Assert.That(obstacles.Length, Is.EqualTo(5));
            Assert.That(obstacles.All(obstacle =>
                obstacle.GetComponent<MeshFilter>().sharedMesh.vertexCount == 20), Is.True);
            Assert.That(controller.Units.Count(unit => unit.Team == Team.Player), Is.EqualTo(3));
            Assert.That(controller.Units.Count(unit => unit.Team == Team.Opponent), Is.EqualTo(3));
            Assert.That(controller.Units.Count(unit => unit.DefinitionId == "amber-raider"), Is.EqualTo(2));
            Assert.That(controller.NextIntentUnit, Is.Not.Null);
            Assert.That(controller.NextIntentUnit.Team, Is.EqualTo(Team.Opponent));
            Assert.That(controller.NextEnemyIntentCodes, Does.Match("^\\[[AMHS–]\\]\\[[AMHS–]\\]$"));

            AbilityDefinition sharedFireBurst = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst");
            Assert.That(rangerDefinition.Abilities, Does.Contain(sharedFireBurst));
            Assert.That(skirmisherDefinition.Abilities, Does.Contain(sharedFireBurst));

            Vector3 initialCameraPosition = Camera.main.transform.position;
            controller.RequestCameraRotation(90f);
            yield return new WaitForSeconds(0.5f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(controller.CameraRotation, 90f)), Is.LessThan(1f));
            Assert.That(Vector3.Distance(Camera.main.transform.position, initialCameraPosition), Is.GreaterThan(1f));

            float initialCameraPitch = controller.CameraPitch;
            controller.RequestCameraTilt(12f);
            yield return new WaitForSeconds(0.25f);
            Assert.That(controller.CameraPitch, Is.GreaterThan(initialCameraPitch + 10f));

            Vector3 initialCameraPivot = controller.CameraPivotPosition;
            controller.RequestCameraPan(new Vector3(2f, 0f, 1f));
            Assert.That(controller.CameraPivotPosition.x, Is.EqualTo(initialCameraPivot.x + 2f).Within(0.01f));
            Assert.That(controller.CameraPivotPosition.z, Is.EqualTo(initialCameraPivot.z + 1f).Within(0.01f));
            controller.RequestCameraPan(new Vector3(100f, 0f, 100f));
            Assert.That(controller.CameraPivotPosition.x, Is.EqualTo(4.725f).Within(0.01f));
            Assert.That(controller.CameraPivotPosition.z, Is.EqualTo(4.725f).Within(0.01f));

            float initialCameraZoom = controller.CameraZoom;
            controller.RequestCameraZoom(2f);
            yield return new WaitForSeconds(0.5f);
            Assert.That(controller.CameraZoom, Is.LessThan(initialCameraZoom));
            Assert.That(Camera.main.orthographicSize, Is.EqualTo(controller.CameraZoom).Within(0.01f));
            controller.RequestCameraZoom(100f);
            yield return new WaitForSeconds(0.5f);
            Assert.That(controller.CameraZoom, Is.EqualTo(4.25f).Within(0.01f));
            controller.RequestCameraZoom(-100f);
            yield return new WaitForSeconds(1f);
            Assert.That(controller.CameraZoom, Is.EqualTo(11.5f).Within(0.01f));

            int playerDurabilityBefore = controller.Units
                .Where(unit => unit.Team == Team.Player)
                .Sum(unit => unit.CurrentHealth + unit.CurrentShield);

            // The log keeps only recent lines, so remember everything shown while turns pass.
            var seenLog = new System.Collections.Generic.HashSet<string>();

            // Skirmisher → two AI raiders → Ranger → Blaze Worm (Speed 12, 11, 11, 10, 8).
            float timeout = Time.time + 20f;
            while (Time.time < timeout && controller.ActiveUnit.DisplayName != "Blaze Worm")
            {
                if (controller.ActiveUnit.ControlType == ControlType.Player) controller.RequestEndTurn();
                seenLog.UnionWith(feedback.RecentEntries);
                yield return null;
            }

            Assert.That(controller.ActiveUnit.DisplayName, Is.EqualTo("Blaze Worm"));
            Assert.That(controller.ActiveUnit.CurrentActionPoints, Is.EqualTo(2));
            Assert.That(controller.ActiveUnit.MaximumHealth, Is.EqualTo(150));
            Assert.That(controller.ActiveUnit.Attack, Is.EqualTo(18));
            Assert.That(controller.CurrentReachableCells[new GridCoordinate(0, 2)], Is.EqualTo(2));
            Assert.That(controller.CurrentReachableCells.ContainsKey(new GridCoordinate(0, 3)), Is.False,
                "Fire Worm I has 2 movement points per move.");
            Assert.That(controller.RequestMove(new GridCoordinate(0, 3)), Is.False);
            Assert.That(controller.RequestMove(new GridCoordinate(1, 4)), Is.False);
            Assert.That(controller.RequestMove(new GridCoordinate(0, 2)), Is.True);
            float movementTimeout = Time.time + 2f;
            while (Time.time < movementTimeout && controller.ActiveUnit.Cell != new GridCoordinate(0, 2))
            {
                yield return null;
            }
            Assert.That(controller.ActiveUnit.Cell, Is.EqualTo(new GridCoordinate(0, 2)));
            Assert.That(controller.ActiveUnit.CurrentActionPoints, Is.EqualTo(1));

            controller.RequestEndTurn();
            timeout = Time.time + 12f;
            while (Time.time < timeout && controller.ActiveUnit != null &&
                   controller.ActiveUnit.ControlType != ControlType.Player)
            {
                seenLog.UnionWith(feedback.RecentEntries);
                yield return null;
            }
            seenLog.UnionWith(feedback.RecentEntries);

            Assert.That(controller.ActiveUnit.ControlType, Is.EqualTo(ControlType.Player));
            Assert.That(controller.ActiveUnit.DisplayName, Is.EqualTo("Teal Skirmisher"));
            Assert.That(controller.ActiveUnit.CurrentActionPoints, Is.EqualTo(2));
            int playerDurabilityAfter = controller.Units
                .Where(unit => unit.Team == Team.Player)
                .Sum(unit => unit.CurrentHealth + unit.CurrentShield);
            Assert.That(playerDurabilityAfter, Is.LessThan(playerDurabilityBefore));
            Assert.That(seenLog.Any(entry => entry.Contains("casts")), Is.True);
            Assert.That(seenLog.Any(entry => entry.Contains("shield damage")), Is.True);
        }

        [UnityTest]
        public IEnumerator PrototypeBattleReachesVictoryAndExposesResultState()
        {
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;

            BattlePrototypeController controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            BattleFeedbackView feedback = Object.FindAnyObjectByType<BattleFeedbackView>();
            Assert.That(controller, Is.Not.Null);
            CombatUnit[] opponents = controller.Units
                .Where(unit => unit.Team == Team.Opponent)
                .ToArray();
            DamageResolver.Apply(opponents, opponents.Select(unit => unit.Cell).ToArray(), int.MaxValue);

            controller.RequestEndTurn();
            yield return null;

            Assert.That(controller.BattleEnded, Is.True);
            Assert.That(controller.Outcome, Is.EqualTo(BattleOutcome.PlayerVictory));
            Assert.That(controller.NextIntentUnit, Is.Null);
            Assert.That(feedback.RecentEntries.Any(entry => entry.Contains("Victory")), Is.True);
            Assert.That(feedback.RecentEntries.Any(entry => entry.Contains("Battle finished")), Is.True);
        }
    }
}
