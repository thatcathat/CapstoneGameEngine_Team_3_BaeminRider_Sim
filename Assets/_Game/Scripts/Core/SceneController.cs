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
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            loading = false; // 씬 로드가 끝나면 로딩 상태 해제
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