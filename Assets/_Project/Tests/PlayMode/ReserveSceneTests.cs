using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Main battle without an Archon: the idol swaps itself for a reserve resonant, later the toad is recalled.</summary>
    public sealed class ReserveSceneTests
    {
        [UnityTest]
        public IEnumerator ActiveResonantSwapsAndRecallsWithEntryAndRecallEffects()
        {
            BattlePrototypeController.SetBranchVariant("blaze-worm", 0);
            BattlePrototypeController.SetBranchVariant("blightgullet", 0);
            BattlePrototypeController.SetBranchVariant("primal-idol", 0);
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            controller.GetType().GetMethod("CloseOnboarding", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);

            Assert.That(controller.Units.Any(unit => unit.DefinitionId == "archon"), Is.False, "There is no Archon unit.");
            Assert.That(controller.Reserve.Count, Is.EqualTo(2));
            Assert.That(controller.Units.Count(unit => unit.Team == Team.Player), Is.EqualTo(3));

            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "primal-idol");
            CombatUnit idol = controller.ActiveUnit;
            GridCoordinate idolCell = idol.Cell;
            CombatUnit incoming = controller.Reserve[0].Unit;
            Assert.That(controller.BeginSummon(0), Is.True);
            yield return Capture("20-reserve-summon-targets.png");
            Assert.That(controller.RequestReserveAction(idolCell), Is.True, "The active resonant's own cell swaps it out.");
            yield return new WaitForSeconds(0.45f);
            yield return Capture("21-reserve-swap.png");
            yield return new WaitForSeconds(1.4f);
            Assert.That(controller.Units, Has.No.Member(idol));
            Assert.That(controller.ActiveUnit, Is.SameAs(incoming), "The newcomer continues the turn.");
            Assert.That(incoming.Cell, Is.EqualTo(idolCell));
            Assert.That(incoming.CurrentActionPoints, Is.EqualTo(1), "It keeps the AP that were left.");
            Assert.That(controller.AreaEffects.TrapAt(idolCell), Is.Not.Null, "The idol leaves a Lift Trap.");
            Assert.That(controller.Reserve.Single(slot => slot.Unit == idol).AvailableTick,
                Is.EqualTo(controller.CurrentTimelineTick + ReserveRules.ReturnLockTicks));
            Assert.That(controller.BeginRecall(), Is.False, "Concentration is spent.");
            controller.RequestEndTurn();
            controller.RequestEndTurn();

            yield return PassTurnsUntil(controller, unit => unit.Team == Team.Player && controller.ConcentrationReady &&
                                                            controller.Units.Any(other => other.DefinitionId == "blightgullet" && other != unit));
            CombatUnit toad = controller.Units.Single(unit => unit.DefinitionId == "blightgullet");
            GridCoordinate toadCell = toad.Cell;
            Assert.That(controller.BeginRecall(), Is.True);
            Assert.That(controller.RequestReserveAction(toadCell), Is.True);
            yield return new WaitForSeconds(0.3f);
            yield return Capture("22-reserve-recall.png");
            yield return new WaitForSeconds(1f);
            Assert.That(controller.Units, Has.No.Member(toad));
            Assert.That(controller.AreaEffects.Clouds.Count, Is.GreaterThan(0), "The toad leaves an Oil cloud.");
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator PassTurnsUntil(BattlePrototypeController controller, System.Func<CombatUnit, bool> predicate)
        {
            float timeout = Time.time + 40f;
            while (Time.time < timeout && !(predicate(controller.ActiveUnit) && !IsBusy(controller)))
            {
                if (controller.ActiveUnit.ControlType == ControlType.Player && !predicate(controller.ActiveUnit))
                    controller.RequestEndTurn();
                yield return null;
            }

            Assert.That(predicate(controller.ActiveUnit), Is.True);
            yield return new WaitForSeconds(0.6f);
        }

        private static bool IsBusy(BattlePrototypeController controller) => (bool)controller.GetType()
            .GetField("isBusy", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply();
            Directory.CreateDirectory("Logs/Screenshots");
            var encoder = System.Type.GetType("BlightgulletContentSetup, Assembly-CSharp-Editor");
            encoder?.GetMethod("SavePreview").Invoke(null, new object[] { shot, "Logs/Screenshots/" + name });
            Object.Destroy(shot);
        }
    }
}
