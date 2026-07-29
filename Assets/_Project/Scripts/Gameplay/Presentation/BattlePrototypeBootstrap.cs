using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShardsOfTheLimit.Presentation
{
    public static class BattlePrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneBootstrap()
        {
            SceneManager.sceneLoaded -= CreatePrototype;
            SceneManager.sceneLoaded += CreatePrototype;
        }

        private static void CreatePrototype(Scene scene, LoadSceneMode loadMode)
        {
            if (Object.FindAnyObjectByType<BattlePrototypeController>() != null)
            {
                return;
            }

            var root = new GameObject("Battle Prototype");
            root.AddComponent<BattlePrototypeController>();
        }
    }
}
