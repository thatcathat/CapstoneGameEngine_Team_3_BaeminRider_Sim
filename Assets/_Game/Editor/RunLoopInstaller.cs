using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeliveryRider.Editor
{
    // M4 run loop: settlement ledger/purchase/repay controls in the game scene and new game/continue on the title.
    // Safe to run repeatedly. Opens both scenes in turn and finishes with the title scene open.
    public static class RunLoopInstaller
    {
        private const string GameScenePath = "Assets/_Game/Scenes/FourDistrictPrototype.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
        private const string EconomyAssetPath = "Assets/_Game/Settings/EconomySettings.asset";

        [MenuItem("Delivery Rider/Apply Run Loop (M4)")]
        public static void Apply()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard open scene changes first.");
            var economy = AssetDatabase.LoadAssetAtPath<DeliveryEconomySettings>(EconomyAssetPath);
            if (economy == null) throw new InvalidOperationException("Run Apply Zone Calls (M3) first to create the economy settings.");

            var game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            InstallSettlement();
            EditorSceneManager.MarkSceneDirty(game);
            EditorSceneManager.SaveScene(game);

            var title = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            InstallTitle(economy);
            EditorSceneManager.MarkSceneDirty(title);
            EditorSceneManager.SaveScene(title);
            Debug.Log("Run loop applied to the settlement panel and title. Play Mode not started.");
        }

        private static void InstallSettlement()
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<DeliveryDayHud>(FindObjectsInactive.Include);
            var data = new SerializedObject(hud);
            var replay = (Button)data.FindProperty("replay").objectReferenceValue;
            var summary = (TMP_Text)data.FindProperty("summary").objectReferenceValue;
            var deliveries = (TMP_Text)data.FindProperty("deliveries").objectReferenceValue;
            var card = replay.transform.parent;

            // Results list on the left, money on the right.
            SetAnchors(deliveries.rectTransform, 0.06f, 0.19f, 0.60f, 0.70f);
            var ledger = card.Find("Ledger")?.GetComponent<TMP_Text>();
            if (ledger == null)
            {
                ledger = UnityEngine.Object.Instantiate(summary.gameObject, card).GetComponent<TMP_Text>();
                ledger.name = "Ledger";
            }
            SetAnchors(ledger.rectTransform, 0.63f, 0.42f, 0.95f, 0.70f);
            ledger.fontSize = 18;
            ledger.text = "";

            var buyInline = CloneButton(replay, "BuyInline", card);
            SetAnchors((RectTransform)buyInline.transform, 0.63f, 0.31f, 0.95f, 0.40f);
            var repay = CloneButton(replay, "Repay", card);
            SetAnchors((RectTransform)repay.transform, 0.63f, 0.20f, 0.95f, 0.29f);

            var note = card.parent.Find("ReplayNote")?.GetComponent<TMP_Text>();
            if (note != null) note.text = "다음 날 또는 타이틀로 넘어갈 때, 다음 날 아침부터 이어지도록 저장됩니다.";

            data.FindProperty("heading").objectReferenceValue = card.Find("Heading").GetComponent<TMP_Text>();
            data.FindProperty("ledger").objectReferenceValue = ledger;
            data.FindProperty("buyInline").objectReferenceValue = buyInline;
            data.FindProperty("repay").objectReferenceValue = repay;
            data.ApplyModifiedProperties();
        }

        private static void InstallTitle(DeliveryEconomySettings economy)
        {
            var titleUi = UnityEngine.Object.FindFirstObjectByType<TitleUI>(FindObjectsInactive.Include);
            if (titleUi == null) throw new InvalidOperationException("TitleUI is missing in the title scene.");
            var data = new SerializedObject(titleUi);
            var start = (Button)data.FindProperty("startButton").objectReferenceValue;
            var quit = (Button)data.FindProperty("quitButton").objectReferenceValue;
            var content = (RectTransform)start.transform.parent;

            var continueButton = CloneButton(start, "ContinueButton", content);
            continueButton.transform.SetSiblingIndex(start.transform.GetSiblingIndex() + 1);
            SetLabel(start, "새 게임");
            SetLabel(continueButton, "이어하기");

            // The old one-line hint ("bump to deliver") is outdated; it becomes the start-day selector text.
            var hint = content.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(t => t.transform.parent == content && (t.name == "StartDay" || t.text.Contains("부딪혀")));
            if (hint != null)
            {
                hint.name = "StartDay";
                hint.text = "새 게임 시작 일차: 1일차   (숫자 1~6으로 변경)";
            }

            // Make room for the extra button; positions are set as well in case no layout group drives them.
            content.anchorMin = new Vector2(content.anchorMin.x, 0.06f);
            content.anchorMax = new Vector2(content.anchorMax.x, 0.92f);
            var startRect = (RectTransform)start.transform;
            var step = new Vector2(0f, -74f);
            ((RectTransform)continueButton.transform).anchoredPosition = startRect.anchoredPosition + step;
            ((RectTransform)quit.transform).anchoredPosition = startRect.anchoredPosition + step * 2f;

            data.FindProperty("continueButton").objectReferenceValue = continueButton;
            data.FindProperty("startDayLabel").objectReferenceValue = hint;
            data.FindProperty("economy").objectReferenceValue = economy;
            data.ApplyModifiedProperties();
        }

        private static Button CloneButton(Button template, string name, Transform parent)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<Button>();
            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent).GetComponent<Button>();
            clone.name = name;
            clone.onClick = new Button.ButtonClickedEvent();
            return clone;
        }

        private static void SetLabel(Button button, string text)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
        }

        private static void SetAnchors(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
