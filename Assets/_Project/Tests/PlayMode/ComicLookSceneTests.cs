using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using ShardsOfTheLimit.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>The comic full-screen pass: before/after screenshots of the same frame and the F2 switch.</summary>
    public sealed class ComicLookSceneTests
    {
        [UnityTest]
        public IEnumerator ComicLookCanBeToggled()
        {
            SceneManager.LoadScene("Assets/_Project/Scenes/PrototypeBattle.unity");
            yield return null;
            yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            controller.GetType().GetMethod("CloseOnboarding", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
            yield return new WaitForSeconds(1.2f);

            Assert.That(BattlePrototypeController.ComicLook, Is.True);
            Assert.That(Shader.GetGlobalFloat("_ComicStrength"), Is.EqualTo(1f));
            Assert.That(Shader.Find("Shards/ComicPost"), Is.Not.Null);
            yield return Capture("45-comic-on.png");

            BattlePrototypeController.SetComicLook(false);
            Assert.That(Shader.GetGlobalFloat("_ComicStrength"), Is.EqualTo(0f));
            yield return null;
            yield return Capture("46-comic-off.png");

            BattlePrototypeController.SetComicLook(true);

            // The tabletop around the board: low angle towards the window, then close up on the miniatures.
            Assert.That(GameObject.Find("Tabletop"), Is.Not.Null);
            controller.RequestCameraTilt(-22f);
            yield return new WaitForSeconds(0.6f);
            yield return Capture("47-tabletop-low.png");
            controller.RequestCameraRotation(180f);
            yield return new WaitForSeconds(1.2f);
            yield return Capture("48-tabletop-window.png");
            controller.ResetCameraView();
            controller.RequestCameraZoom(5f);
            yield return new WaitForSeconds(0.8f);
            yield return Capture("49-tabletop-close.png");
            LogAssert.NoUnexpectedReceived();
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
