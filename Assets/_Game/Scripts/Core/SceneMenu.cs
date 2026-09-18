using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider
{
    public sealed class SceneMenu : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button startButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private string gameScene = "GameScene";
        private bool loading;

        private void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            startButton.onClick.AddListener(StartGame);
            quitButton.onClick.AddListener(Quit);
        }

        public void StartGame()
        {
            if (loading) return;
            loading = true;
            startButton.interactable = false;
            SceneManager.LoadSceneAsync(gameScene);
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
