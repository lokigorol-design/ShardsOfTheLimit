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
    /// <summary>
    /// Visual smoke pass: plays the toad and the worm in the main battle and saves screenshots with the HUD
    /// to Logs/Screenshots when a graphics device exists. Without one it still checks that effects spawn.
    /// </summary>
    public sealed class BattleScreenshotTests
    {
        private static void Call(object obj, string method, params object[] args) => obj.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, args);

        [UnityTest]
        public IEnumerator MainBattlePlaysToadAndWormEffects()
        {
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            Call(controller, "CloseOnboarding");
            yield return new WaitForSeconds(0.3f);
            yield return Capture("01-start.png");

            yield return PassTurnsUntil(controller, "blightgullet");
            CombatUnit toad = controller.ActiveUnit;
            int poisonIndex = AbilityIndex(controller, toad, "PoisonCloud");
            Call(controller, "BeginAbilityTargeting", poisonIndex);
            var cloudCell = new GridCoordinate(toad.Cell.X - 1, toad.Cell.Y);
            Call(controller, "HandleCellChoice", cloudCell);
            yield return Capture("02-toad-targeting.png");
            Call(controller, "ConfirmCurrentChoice");
            yield return new WaitForSeconds(0.45f);
            Assert.That(VfxSystem.Instance.LiveParticleCount, Is.GreaterThan(0), "The cloud exhale spawns particles.");
            yield return Capture("03-toad-exhale.png");
            yield return new WaitForSeconds(1.5f);
            yield return Capture("04-poison-cloud.png");
            controller.RequestEndTurn();

            yield return PassTurnsUntil(controller, "blaze-worm");
            CombatUnit worm = controller.ActiveUnit;
            Call(controller, "BeginAbilityTargeting", AbilityIndex(controller, worm, "blaze-fiery-spit"));
            Call(controller, "HandleCellChoice", new GridCoordinate(0, 3));
            Call(controller, "ConfirmCurrentChoice");
            yield return new WaitForSeconds(0.25f);
            yield return Capture("05a-fire-spit-flight.png");
            yield return new WaitForSeconds(0.25f);
            yield return Capture("05b-fire-spit-impact.png");
            yield return new WaitForSeconds(1.5f);
            // Visual test only: give the worm its AP back to also film Deep Burrow this turn.
            worm.BeginActivation(2);
            Call(controller, "RefreshMovementOptions", worm);
            Assert.That(controller.RequestMove(new GridCoordinate(2, 1)), Is.True);
            yield return new WaitForSeconds(0.35f);
            yield return Capture("05-worm-burrow.png");
            float timeout = Time.time + 4f;
            while (Time.time < timeout && worm.Cell != new GridCoordinate(2, 1)) yield return null;
            yield return Capture("06-worm-emerged.png");
            LogAssert.NoUnexpectedReceived();
        }

        private static int AbilityIndex(BattlePrototypeController controller, CombatUnit unit, string id)
        {
            var definitions = (System.Collections.Generic.Dictionary<CombatUnit, ShardsOfTheLimit.Definitions.UnitDefinition>)
                controller.GetType().GetField("definitionsByUnit", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            return definitions[unit].Abilities.Select(a => a.StableId).ToList().IndexOf(id);
        }

        private static IEnumerator PassTurnsUntil(BattlePrototypeController controller, string definitionId)
        {
            float timeout = Time.time + 25f;
            while (Time.time < timeout && controller.ActiveUnit.DefinitionId != definitionId)
            {
                if (controller.ActiveUnit.ControlType == ControlType.Player) controller.RequestEndTurn();
                yield return null;
            }

            Assert.That(controller.ActiveUnit.DefinitionId, Is.EqualTo(definitionId));
            yield return new WaitForSeconds(0.8f);
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply();
            Directory.CreateDirectory("Logs/Screenshots");
            // Encoding is editor-only; reuse the existing editor helper instead of adding a runtime package.
            var encoder = System.Type.GetType("BlightgulletContentSetup, Assembly-CSharp-Editor");
            encoder?.GetMethod("SavePreview").Invoke(null, new object[] { shot, "Logs/Screenshots/" + name });
            Object.Destroy(shot);
        }
    }
}
