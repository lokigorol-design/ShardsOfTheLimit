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
    public sealed class BlightgulletSceneTests
    {
        private static void Call(object obj, string method, params object[] args) => obj.GetType()
            .GetMethod(method,BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj,args);
        [UnityTest]
        public IEnumerator SceneExecutesAllFiveAbilitiesAndDisplaysNonBlockingVolumes()
        {
            SceneManager.LoadScene("Assets/_Project/Scenes/BlightgulletBattle.unity");
            yield return null; yield return null;
            var controller = Object.FindAnyObjectByType<BattlePrototypeController>();
            Assert.That(controller.ActiveUnit.DefinitionId,Is.EqualTo("blightgullet"));
            Call(controller,"CloseOnboarding");
            var actor = controller.ActiveUnit;
            var target = controller.Units.Single(u => u.Cell == new GridCoordinate(4,0));
            var view = Object.FindObjectsByType<CombatUnitView>().Single(v => v.Unit == actor);
            var renderer = view.GetComponentInChildren<Renderer>();
            Assert.That(renderer.bounds.size.x,Is.InRange(.1f,1.5f));
            Assert.That(renderer.bounds.size.y,Is.InRange(.1f,1.5f));
            Assert.That(renderer.bounds.min.y,Is.EqualTo(0).Within(.04));
            Capture(Camera.main,"Blightgullet-board.png");
            Vector3 cameraPosition = Camera.main.transform.position; Quaternion cameraRotation = Camera.main.transform.rotation;
            float zoom = Camera.main.orthographicSize;
            Camera.main.transform.position = view.transform.position + new Vector3(2,1.7f,-2);
            Camera.main.transform.LookAt(view.transform.position);
            Camera.main.orthographicSize = 1.25f; Capture(Camera.main,"Blightgullet-model.png");
            Camera.main.transform.SetPositionAndRotation(cameraPosition,cameraRotation); Camera.main.orthographicSize = zoom;
            foreach (int index in new[] {1,2,4,0,3})
            {
                actor.BeginActivation(2);
                Call(controller,"BeginAbilityTargeting",index);
                Call(controller,"HandleCellChoice",index == 0 || index == 3 ? target.Cell : new GridCoordinate(1,0));
                Call(controller,"ConfirmCurrentChoice");
                yield return new WaitForSeconds(2f);
                Assert.That(actor.CurrentActionPoints,Is.EqualTo(1),"Ability must spend exactly one AP.");
            }
            Assert.That(controller.AreaEffects.Clouds.Count,Is.EqualTo(3));
            Assert.That(target.IsImmobilized,Is.True);
            Assert.That(target.Cell,Is.EqualTo(new GridCoordinate(1,0)));
            Assert.That(controller.AreaEffects.Clouds.All(c => c.OwnerId == actor.Id),Is.True);
            var cloudRoot = GameObject.Find("Area Effects (non-blocking)");
            Assert.That(cloudRoot.GetComponentsInChildren<Collider>(),Is.Empty);
            Assert.That(cloudRoot.GetComponentsInChildren<Renderer>().Length,Is.GreaterThan(0));
            Capture(Camera.main,"Blightgullet-clouds.png");
            controller.AreaEffects.ProcessTick(controller.CurrentTimelineTick + 100);
            Assert.That(target.GetStacks("POISON"),Is.EqualTo(1));
            Assert.That(target.GetStacks("CORROSION"),Is.EqualTo(1));
            Assert.That(target.GetStacks("OIL"),Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
        private static void Capture(Camera camera, string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var texture = new RenderTexture(1200,900,24);
            var previous = RenderTexture.active;
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            var output = new Texture2D(1200,900,TextureFormat.RGB24,false);
            output.ReadPixels(new Rect(0,0,1200,900),0,0); output.Apply();
            Directory.CreateDirectory("Logs/Blightgullet");
            // Encoding is editor-only; do not add an image-conversion runtime package for test captures.
            var encoder = System.Type.GetType("BlightgulletContentSetup, Assembly-CSharp-Editor");
            encoder.GetMethod("SavePreview").Invoke(null,new object[] { output,"Logs/Blightgullet/" + name });
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.Destroy(output); Object.Destroy(texture);
        }
    }
}
