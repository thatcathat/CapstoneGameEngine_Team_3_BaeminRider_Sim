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
        // Scene copies can keep their own next-day destination. Existing scenes retain the original default.
        [SerializeField] private string gameSceneName = "FourDistrictPrototype";
        public static SceneController Instance { get; private set; }

        private bool loading;
        public ESceneType currentScene { get; set; } = ESceneType.Title;
        // Kept across scene loads; the game scene creates a fresh run when it is played directly.
        public RunProgress Progress { get; set; }

        public void StartNewGame(int startDay, long startingDebt)
        {
            RunProgress.DeleteSave();
            Progress = new RunProgress(startDay, startingDebt);
            NextScene(ESceneType.GameScene);
        }

        public bool ContinueGame()
        {
            var saved = RunProgress.Load();
            if (saved == null) return false;
            Progress = saved;
            NextScene(ESceneType.GameScene);
            return true;
        }

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
            if (sceneName == gameSceneName) currentScene = ESceneType.GameScene;
            else if (sceneName == "TitleScene") currentScene = ESceneType.Title;
        }

        public void NextScene(ESceneType nextScene)
        {
            if (loading) return;
            loading = true;

            string sceneName = nextScene switch
            {
                ESceneType.Title => "TitleScene",
                ESceneType.GameScene => gameSceneName,
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
