using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Presentation;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Main battle: the toad swaps itself for the Mist Panther, which arrives in Mist and finishes the turn.</summary>
    public sealed class MistPantherSceneTests
    {
        private static object Call(object obj, string method, params object[] args) => obj.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(obj, args);

        [UnityTest]
        public IEnumerator SwapBringsInThePantherWhichArrivesInMistAndFights()
        {
            BattlePrototypeController.SetBranchVariant("mist-panther", 0);
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            Call(controller, "CloseOnboarding");
            Assert.That(controller.Reserve[0].Unit.DefinitionId, Is.EqualTo("mist-panther"));

            // On the toad's turn the toad swaps itself for the panther, which continues the turn with 1 AP.
            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "blightgullet");
            CombatUnit toad = controller.ActiveUnit;
            CombatUnit panther = controller.Reserve[0].Unit;
            Assert.That(controller.BeginSummon(0), Is.True);
            Assert.That(controller.RequestReserveAction(toad.Cell), Is.True);
            yield return new WaitForSeconds(1.6f);
            Assert.That(controller.Units, Has.Member(panther));
            Assert.That(controller.ActiveUnit, Is.SameAs(panther));
            Assert.That(panther.IsInMist, Is.True, "The panther arrives in Mist.");
            yield return Capture("30-panther-arrives-in-mist.png");
            Assert.That(controller.BranchVariantName(panther), Is.EqualTo("Mist Panther · Deep Wounds"));
            var definitions = (System.Collections.Generic.Dictionary<CombatUnit, UnitDefinition>)controller.GetType()
                .GetField("definitionsByUnit", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            AbilityDefinition dash = definitions[panther].Abilities.Single(a => a.StableId == "BloodDash");
            var dashTargets = AbilityRuntime.GetTargetableCells(dash, controller.Grid, panther, controller.Units);
            if (dashTargets.Count > 0)
            {
                Call(controller, "BeginAbilityTargeting", 3);
                Call(controller, "HandleCellChoice", dashTargets.First());
                yield return Capture("31-panther-blood-dash-preview.png");
                Call(controller, "ConfirmCurrentChoice");
                yield return new WaitForSeconds(1.4f);
                yield return Capture("32-panther-blood-dash.png");
                Assert.That(panther.IsInMist, Is.False, "The first attack ends Mist.");
            }
            else
            {
                Call(controller, "BeginAbilityTargeting", 1);
                Call(controller, "HandleCellChoice", panther.Cell);
                Call(controller, "ConfirmCurrentChoice");
                yield return new WaitForSeconds(1f);
                yield return Capture("31-panther-vanishing-step.png");
                Assert.That(panther.IsInMist, Is.True, "Mist Veil: Vanishing Step gives Mist.");
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
