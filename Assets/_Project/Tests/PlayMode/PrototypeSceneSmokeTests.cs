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
        public IEnumerator PrototypeSceneLoadsStageTwoRosterAndCompletesAiSequence()
        {
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;

            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo("Assets/_Project/Scenes/PrototypeBattle.unity"));
            Assert.That(Resources.Load<UnitDefinition>("Prototype/Units/PlayerEcho"), Is.Not.Null);
            UnitDefinition rangerDefinition = Resources.Load<UnitDefinition>("Prototype/Units/RangerEcho");
            UnitDefinition skirmisherDefinition = Resources.Load<UnitDefinition>("Prototype/Units/SkirmisherEcho");
            UnitDefinition raiderDefinition = Resources.Load<UnitDefinition>("Prototype/Units/RaiderEnemy");
            Assert.That(rangerDefinition, Is.Not.Null);
            Assert.That(skirmisherDefinition, Is.Not.Null);
            Assert.That(raiderDefinition, Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst"), Is.Not.Null);
            Assert.That(Resources.LoadAll<AbilityDefinition>("Prototype/Abilities").Length, Is.EqualTo(5));
            BattlePrototypeController controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            BattleFeedbackView feedback = Object.FindAnyObjectByType<BattleFeedbackView>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(feedback, Is.Not.Null);
            Assert.That(controller.Round, Is.EqualTo(1));
            Assert.That(controller.ActiveUnit.ControlType, Is.EqualTo(ControlType.Player));
            Assert.That(controller.ActiveUnit.DisplayName, Is.EqualTo("Teal Skirmisher"));
            Assert.That(controller.ActiveUnit.CurrentActionPoints, Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<CellView>().Length, Is.EqualTo(36));
            Assert.That(Object.FindObjectsByType<CombatUnitView>().Length, Is.EqualTo(6));
            Assert.That(controller.Units.Count(unit => unit.Team == Team.Player), Is.EqualTo(3));
            Assert.That(controller.Units.Count(unit => unit.Team == Team.Opponent), Is.EqualTo(3));
            Assert.That(controller.Units.Count(unit => unit.DefinitionId == "amber-raider"), Is.EqualTo(2));

            AbilityDefinition sharedFireBurst = Resources.Load<AbilityDefinition>("Prototype/Abilities/FireBurst");
            Assert.That(rangerDefinition.Abilities, Does.Contain(sharedFireBurst));
            Assert.That(skirmisherDefinition.Abilities, Does.Contain(sharedFireBurst));

            int playerDurabilityBefore = controller.Units
                .Where(unit => unit.Team == Team.Player)
                .Sum(unit => unit.CurrentHealth + unit.CurrentShield);

            controller.RequestEndTurn();
            Assert.That(controller.ActiveUnit.DisplayName, Is.EqualTo("Azure Vanguard"));
            controller.RequestEndTurn();

            float timeout = Time.time + 12f;
            while (Time.time < timeout && controller.ActiveUnit != null &&
                   controller.ActiveUnit.ControlType != ControlType.Player)
            {
                yield return null;
            }

            Assert.That(controller.ActiveUnit.ControlType, Is.EqualTo(ControlType.Player));
            Assert.That(controller.ActiveUnit.DisplayName, Is.EqualTo("Cobalt Ranger"));
            Assert.That(controller.ActiveUnit.CurrentActionPoints, Is.EqualTo(2));
            int playerDurabilityAfter = controller.Units
                .Where(unit => unit.Team == Team.Player)
                .Sum(unit => unit.CurrentHealth + unit.CurrentShield);
            Assert.That(playerDurabilityAfter, Is.LessThan(playerDurabilityBefore));
            Assert.That(feedback.RecentEntries.Any(entry => entry.Contains("casts")), Is.True);
            Assert.That(feedback.RecentEntries.Any(entry => entry.Contains("shield -")), Is.True);
        }
    }
}
