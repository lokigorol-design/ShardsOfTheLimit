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
    /// <summary>
    /// Main battle: Primal Idol casts from a wall (Ritual Surfaces), the worm uses Purifying Flame and holds Flame Burst
    /// over two turns. Screenshots go to Logs/Screenshots when a graphics device exists.
    /// </summary>
    public sealed class ResonantAbilitiesSceneTests
    {
        private static object Call(object obj, string method, params object[] args) => obj.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(obj, args);

        private static UnitDefinition Definition(BattlePrototypeController controller, CombatUnit unit) =>
            ((System.Collections.Generic.Dictionary<CombatUnit, UnitDefinition>)controller.GetType()
                .GetField("definitionsByUnit", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller))[unit];

        private static int AbilityIndex(BattlePrototypeController controller, CombatUnit unit, string id) =>
            Definition(controller, unit).Abilities.Select(a => a.StableId).ToList().IndexOf(id);

        [UnityTest]
        public IEnumerator IdolCastsFromWallAndWormHoldsFlameBurst()
        {
            BattlePrototypeController.SetBranchVariant("primal-idol", 0);
            BattlePrototypeController.SetBranchVariant("blaze-worm", 1); // Fire Worm · Roaring Furnace
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            Call(controller, "CloseOnboarding");

            yield return PassTurnsUntil(controller, "primal-idol");
            CombatUnit idol = controller.ActiveUnit;
            Assert.That(controller.BranchVariantName(idol), Is.EqualTo("Primal Idol · Twin Eruption"));
            AbilityDefinition geyser = Definition(controller, idol).Abilities.Single(a => a.StableId == "FireGeyser");
            var walls = AbilityRuntime.GetTargetableCells(geyser, controller.Grid, idol, controller.Units, true);
            if (walls.Count > 0)
            {
                Call(controller, "ToggleWallMode");
                Assert.That(controller.WallMode, Is.True);
                Call(controller, "BeginAbilityTargeting", AbilityIndex(controller, idol, "FireGeyser"));
                yield return Capture("10-idol-wall-targeting.png");
                Call(controller, "HandleCellChoice", walls.First());
                Call(controller, "ConfirmCurrentChoice");
                yield return new WaitForSeconds(1.2f);
                yield return Capture("11-idol-wall-geyser.png");
                Assert.That(controller.AreaEffects.PendingStrikes.Count, Is.EqualTo(2));
                Call(controller, "ToggleWallMode");
            }

            controller.RequestEndTurn();
            controller.RequestEndTurn();

            yield return PassTurnsUntil(controller, "blaze-worm");
            CombatUnit worm = controller.ActiveUnit;
            Assert.That(controller.BranchVariantName(worm), Is.EqualTo("Fire Worm · Roaring Furnace"));
            Assert.That(worm.MaximumHealth, Is.EqualTo(165), "Roaring Furnace: HP 150 × 1,1.");
            Call(controller, "BeginAbilityTargeting", AbilityIndex(controller, worm, "PurifyingFlame"));
            Call(controller, "HandleCellChoice", worm.Cell);
            Call(controller, "ConfirmCurrentChoice");
            yield return new WaitForSeconds(0.3f);
            yield return Capture("12-worm-purifying-flame.png");
            yield return new WaitForSeconds(0.8f);
            Assert.That(worm.GetMechanicStacks(StatusMechanic.PurifyingFlame), Is.EqualTo(1));

            AbilityDefinition burst = Definition(controller, worm).Abilities.Single(a => a.StableId == "FlameBurst");
            GridCoordinate direction = AbilityRuntime.GetTargetableCells(burst, controller.Grid, worm, controller.Units)
                .OrderByDescending(cell => cell.X).First();
            Call(controller, "BeginAbilityTargeting", AbilityIndex(controller, worm, "FlameBurst"));
            Call(controller, "HandleCellChoice", direction);
            Call(controller, "ConfirmCurrentChoice");
            yield return new WaitForSeconds(0.35f);
            yield return Capture("13-worm-flame-burst-1.png");
            yield return new WaitForSeconds(1f);
            Assert.That(worm.Channel, Is.Not.Null);
            Assert.That(worm.Channel.Stage, Is.EqualTo(1));
            Assert.That(controller.CurrentReachableCells, Is.Empty, "A channelling worm cannot move.");
            controller.RequestEndTurn();
            controller.RequestEndTurn();

            yield return PassTurnsUntil(controller, "blaze-worm");
            Assert.That(worm.Channel, Is.Not.Null, "The cone survives until the worm's next turn.");
            Assert.That(BattlePrototypeController.AbilityCost(worm, burst), Is.EqualTo(2));
            Assert.That(BattlePrototypeController.CanUseAbility(worm,
                Definition(controller, worm).Abilities.Single(a => a.StableId == "blaze-long-bite")), Is.False,
                "No other abilities while channelling.");
            yield return Capture("14-worm-cone-held.png");
            Call(controller, "BeginAbilityTargeting", AbilityIndex(controller, worm, "FlameBurst"));
            Call(controller, "HandleCellChoice", direction);
            Call(controller, "ConfirmCurrentChoice");
            yield return new WaitForSeconds(0.35f);
            yield return Capture("15-worm-flame-burst-full.png");
            yield return new WaitForSeconds(1f);
            Assert.That(worm.Channel, Is.Null, "Roaring Furnace: full power on the second turn, then the channel ends.");
            Assert.That(worm.GetCooldown("FlameBurst"), Is.EqualTo(3));
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator PassTurnsUntil(BattlePrototypeController controller, string definitionId)
        {
            float timeout = Time.time + 30f;
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
            var encoder = System.Type.GetType("BlightgulletContentSetup, Assembly-CSharp-Editor");
            encoder?.GetMethod("SavePreview").Invoke(null, new object[] { shot, "Logs/Screenshots/" + name });
            Object.Destroy(shot);
        }
    }
}
