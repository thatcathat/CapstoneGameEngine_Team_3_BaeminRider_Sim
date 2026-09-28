using UnityEngine;
using UnityEngine.UI;

namespace DeliveryRider
{
    public class TitleUI : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button quitButton;

        private void Awake()
        {
            // 초기 설정 (타이틀 화면용)
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 버튼을 누르면 살아있는 매니저(SceneController.Instance)의 함수를 호출!
            if (startButton != null)
            {
                startButton.onClick.AddListener(() =>
                {
                    startButton.interactable = false; // 연타 방지
                    SceneController.Instance.NextScene(ESceneType.GameScene);
                });
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(() => SceneController.Instance.Quit());
            }
        }
    }
}