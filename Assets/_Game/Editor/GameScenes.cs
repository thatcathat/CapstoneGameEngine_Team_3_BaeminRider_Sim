using System;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // The game scenes the map-independent installers (curved world, developer tools, day sky) may run on. Installers
    // that place things on a particular map stay tied to that map's scene.
    public static class GameScenes
    {
        public static readonly string[] Paths =
        {
            "Assets/_Game/Scenes/FourDistrictPrototype.unity",
            "Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity",
        };

        public static Scene RequireActive()
        {
            var scene = SceneManager.GetActiveScene();
            if (Array.IndexOf(Paths, scene.path) < 0) throw new InvalidOperationException("Open a game scene: " + string.Join(", ", Paths));
            return scene;
        }
    }
}
