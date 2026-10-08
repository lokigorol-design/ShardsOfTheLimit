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
    /// <summary>Main battle with the Archon: swap the toad for a reserve resonant, then recall the worm.</summary>
    public sealed class ArchonSceneTests
    {
        [UnityTest]
        public IEnumerator ArchonSwapsAndRecallsWithEntryAndRecallEffects()
        {
            BattlePrototypeController.SetBranchVariant("blaze-worm", 0);
            BattlePrototypeController.SetBranchVariant("blightgullet", 0);
            BattlePrototypeController.SetBranchVariant("primal-idol", 0);
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            controller.GetType().GetMethod("CloseOnboarding", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);

            Assert.That(controller.Archon, Is.Not.Null);
            Assert.That(controller.Reserve.Count, Is.EqualTo(2));
            Assert.That(controller.Units.Count(unit => unit.Team == Team.Player && unit != controller.Archon), Is.EqualTo(3));

            yield return PassTurnsUntil(controller, unit => unit == controller.Archon);
            yield return Capture("20-archon-turn.png");
            CombatUnit toad = controller.Units.Single(unit => unit.DefinitionId == "blightgullet");
            Assert.That(ArchonRules.InLink(controller.Archon.Cell, toad.Cell), Is.True, "The toad starts within the link radius.");
            Assert.That(controller.BeginSummon(0), Is.True);
            yield return Capture("21-archon-summon-targets.png");
            GridCoordinate toadCell = toad.Cell;
            CombatUnit incoming = controller.Reserve[0].Unit;
            Assert.That(controller.RequestArchonAction(toadCell), Is.True, "Three on the field: summoning swaps.");
            yield return new WaitForSeconds(0.45f);
            yield return Capture("22-archon-swap.png");
            yield return new WaitForSeconds(1.2f);
            Assert.That(controller.Units, Has.No.Member(toad));
            Assert.That(controller.Units, Has.Member(incoming));
            Assert.That(incoming.Cell, Is.EqualTo(toadCell));
            Assert.That(controller.Reserve.Single(slot => slot.Unit == toad).AvailableTick,
                Is.EqualTo(controller.CurrentTimelineTick + ArchonRules.ReturnLockTicks));
            Assert.That(controller.AreaEffects.Clouds.Count, Is.GreaterThan(0), "The toad leaves an Oil cloud.");
            Assert.That(controller.BeginRecall(), Is.False, "Concentration is spent.");
            controller.RequestEndTurn();
            controller.RequestEndTurn();

            yield return PassTurnsUntil(controller, unit => unit == controller.Archon && controller.ConcentrationReady);
            CombatUnit worm = controller.Units.Single(unit => unit.DefinitionId == "blaze-worm");
            if (ArchonRules.InLink(controller.Archon.Cell, worm.Cell))
            {
                GridCoordinate wormCell = worm.Cell;
                Assert.That(controller.BeginRecall(), Is.True);
                Assert.That(controller.RequestArchonAction(wormCell), Is.True);
                yield return new WaitForSeconds(0.3f);
                yield return Capture("23-archon-recall.png");
                yield return new WaitForSeconds(1f);
                Assert.That(controller.Units, Has.No.Member(worm));
                Assert.That(controller.AreaEffects.MagmaAt(wormCell), Is.Not.Null, "The worm leaves Magma.");
            }

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
