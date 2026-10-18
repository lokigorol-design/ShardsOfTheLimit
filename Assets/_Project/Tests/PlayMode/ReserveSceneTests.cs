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
    /// <summary>Main battle without an Archon: the idol calls a reserve resonant that arrives 200 ticks later; a fallen caller is not replaced.</summary>
    public sealed class ReserveSceneTests
    {
        [UnityTest]
        public IEnumerator CallingAReplacementTakesTwoHundredTicksAndFailsIfTheCallerFalls()
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
            Assert.That(controller.RequestCall(0), Is.True);
            Assert.That(idol.IsSummoning, Is.True, "The caller is vulnerable while it waits.");
            Assert.That(idol.CurrentActionPoints, Is.Zero, "The call takes the whole turn (2 AP).");
            Assert.That(controller.RequestCall(1), Is.False, "One call at a time.");
            yield return Capture("20-reserve-call.png");
            controller.RequestEndTurn();
            controller.RequestEndTurn();

            // 200 ticks later the panther appears where the idol stood.
            yield return PassTurnsUntil(controller, unit => controller.Units.Contains(incoming) && unit.ControlType == ControlType.Player);
            yield return Capture("21-reserve-arrival.png");
            Assert.That(controller.Units, Has.No.Member(idol));
            Assert.That(incoming.Cell, Is.EqualTo(idolCell));
            Assert.That(controller.AreaEffects.TrapAt(idolCell), Is.Not.Null, "The idol leaves a Lift Trap under the newcomer.");
            Assert.That(idol.IsSummoning, Is.False);
            Assert.That(controller.Reserve.Any(slot => slot.Unit == idol), Is.True, "The idol went to the reserve.");

            // A caller that falls before the arrival is not replaced.
            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "blightgullet");
            CombatUnit toad = controller.ActiveUnit;
            CombatUnit ranger = controller.Reserve.Single(slot => slot.Unit.DefinitionId != "primal-idol").Unit;
            Assert.That(controller.RequestCall(controller.Reserve.ToList().FindIndex(slot => slot.Unit == ranger)), Is.True);
            CombatEffectResolver.Apply(new[] { CombatEffectCalculator.Damage(toad, toad.ResourceState, 100000) });
            controller.RequestEndTurn();
            controller.RequestEndTurn();
            yield return PassTurnsUntil(controller, unit => unit.ControlType == ControlType.Player && controller.PendingSwaps.Count == 0);
            Assert.That(controller.Units, Has.No.Member(ranger), "Nobody comes in place of a fallen resonant.");
            Assert.That(controller.Reserve.Single(slot => slot.Unit == ranger).IsCalled, Is.False);            LogAssert.NoUnexpectedReceived();
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
