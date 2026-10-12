using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider
{
    public enum ESceneType
    {
        NONE,
        Title,
        GameScene
    }

    [DefaultExecutionOrder(-200)]
    public sealed class SceneController : MonoBehaviour
    {
        public static SceneController Instance { get; private set; }

        private bool loading;
        public ESceneType currentScene { get; set; } = ESceneType.Title;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            UpdateCurrentScene(gameObject.scene.name);
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (Instance == this) SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            loading = false;
            UpdateCurrentScene(scene.name);
        }

        private void UpdateCurrentScene(string sceneName)
        {
            if (sceneName == "GameScene") currentScene = ESceneType.GameScene;
            else if (sceneName == "TitleScene") currentScene = ESceneType.Title;
        }

        public void NextScene(ESceneType nextScene)
        {
            if (loading) return;
            loading = true;

            string sceneName = nextScene switch
            {
                ESceneType.Title => "TitleScene",
                ESceneType.GameScene => "GameScene",
                _ => "TitleScene"
            };

            currentScene = nextScene;
            SceneManager.LoadSceneAsync(sceneName);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}