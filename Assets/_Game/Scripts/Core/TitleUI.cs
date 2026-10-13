using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DeliveryRider
{
    public class TitleUI : MonoBehaviour
    {
        private const int MaxStartDay = 6;

        [SerializeField] private Button startButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button quitButton;
        [Tooltip("Shows the test start day; keys 1-6 change it (system plan v0.6: start day is selectable during testing).")]
        [SerializeField] private TMP_Text startDayLabel;
        [SerializeField] private DeliveryEconomySettings economy;
        private int startDay = 1;

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
                    SetButtonsInteractable(false); // 연타 방지
                    SceneController.Instance.StartNewGame(startDay, economy != null ? economy.StartingDebt : 0);
                });
            }

            if (continueButton != null)
            {
                continueButton.interactable = RunProgress.HasSave;
                continueButton.onClick.AddListener(() =>
                {
                    SetButtonsInteractable(false);
                    if (!SceneController.Instance.ContinueGame()) SetButtonsInteractable(true);
                });
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(() => SceneController.Instance.Quit());
            }
            RefreshStartDay();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            for (int day = 1; day <= MaxStartDay; day++)
            {
                if (!keyboard[Key.Digit1 + (day - 1)].wasPressedThisFrame) continue;
                startDay = day;
                RefreshStartDay();
            }
        }

        private void RefreshStartDay()
        {
            if (startDayLabel != null) startDayLabel.text = $"새 게임 시작 일차: {startDay}일차   (숫자 1~{MaxStartDay}로 변경)";
        }

        private void SetButtonsInteractable(bool value)
        {
            if (startButton != null) startButton.interactable = value;
            if (continueButton != null) continueButton.interactable = value && RunProgress.HasSave;
        }
    }
}
