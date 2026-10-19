using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>HUD screenshots with the colored v3 icons for Primal Idol, Blaze Worm and Mist Panther.</summary>
    public sealed class AbilityIconSceneTests
    {
        [UnityTest]
        public IEnumerator HudShowsPaletteColoredIconsPerResonant()
        {
            BattlePrototypeController.SetBranchVariant("blaze-worm", 0);
            BattlePrototypeController.SetBranchVariant("mist-panther", 0);
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            yield return new WaitForSeconds(0.3f);
            yield return Capture("39-onboarding.png");
            controller.GetType().GetMethod("CloseOnboarding", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);

            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "primal-idol");
            yield return Capture("40-icons-primal-idol.png");
            controller.RequestEndTurn();
            controller.RequestEndTurn();

            // The toad calls the panther; the worm's turn comes meanwhile.
            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "blightgullet");
            Assert.That(controller.RequestCall(0), Is.True);
            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "blaze-worm");
            yield return Capture("41-icons-blaze-worm.png");
            controller.RequestEndTurn();
            controller.RequestEndTurn();

            yield return PassTurnsUntil(controller, unit => unit.DefinitionId == "mist-panther");
            yield return Capture("42-icons-mist-panther.png");

            // The same master (Wide Swipe) in three palettes, side by side.
            Sprite swipe = Resources.Load<AbilityDefinition>("Prototype/Abilities/WideSwipe").Icon;
            Texture[] variants = new[] { "mist", "fire", "earth", "neutral" }
                .Select(name => IconTintCache.Get(swipe, Resources.Load<ResonantPalette>("Prototype/Palettes/" + name))).ToArray();
            Assert.That(variants.All(texture => texture != null && texture.width == 256), Is.True);
            SaveRow(variants, "Logs/Screenshots/43-wide-swipe-mist-fire-earth-neutral.png");

            // Result screen.
            CombatUnit[] opponents = controller.Units.Where(unit => unit.Team == Team.Opponent).ToArray();
            DamageResolver.Apply(opponents, opponents.Select(unit => unit.Cell).ToArray(), int.MaxValue);
            controller.RequestEndTurn();
            controller.RequestEndTurn();
            yield return new WaitForSeconds(0.6f);
            yield return Capture("44-victory.png");
            LogAssert.NoUnexpectedReceived();
        }

        private static void SaveRow(Texture[] icons, string path)
        {
            var previous = RenderTexture.active;
            var row = new Texture2D(256 * icons.Length, 256, TextureFormat.RGBA32, false);
            for (int index = 0; index < icons.Length; index++)
            {
                RenderTexture.active = (RenderTexture)icons[index];
                row.ReadPixels(new Rect(0, 0, 256, 256), index * 256, 0);
            }

            row.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory("Logs/Screenshots");
            var encoder = System.Type.GetType("BlightgulletContentSetup, Assembly-CSharp-Editor");
            encoder?.GetMethod("SavePreview").Invoke(null, new object[] { row, path });
            Object.Destroy(row);
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
            yield return new WaitForSeconds(0.8f);
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
