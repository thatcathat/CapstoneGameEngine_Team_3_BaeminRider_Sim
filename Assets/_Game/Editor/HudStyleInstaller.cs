using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DeliveryRider.Editor
{
    // Delivery-app look for the playing HUD of a game scene (GameScenes): light rounded cards with soft shadows instead
    // of the dark bars. The objective sits top left, the day dial hangs from the middle of the top edge with the order
    // timer in a pill under it, the wallet is top right and the key hints run in a slim pill at the bottom. The call and
    // map card lists the call candidates as order cards beside the map; pause is a small menu card with the controls,
    // and the settlement is a receipt card with only the next-day button in the accent colour. Existing objects are
    // restyled and moved, so the HUD scripts keep their references. Safe to run repeatedly; run it after Apply Sundial,
    // which places the dial in the old clock panel.
    public static class HudStyleInstaller
    {
        private const string Folder = "Assets/_Game/Art/UI";
        private const string CardPath = Folder + "/RoundedCard.png";
        private const string ShadowPath = Folder + "/SoftShadow.png";
        private const string SundialMaterialPath = "Assets/_Game/Art/Materials/UISundial.mat";
        private const string HangingSundialPath = "Assets/_Game/Art/Materials/UISundialHanging.mat";
        // Corner radius baked into the card sprite, in pixels; Image.pixelsPerUnitMultiplier scales it per element.
        private const float CardRadius = 48f, ShadowInset = 24f, ShadowRadius = 32f, ShadowBlur = 14f;
        private static readonly Color Card = new Color(1f, 1f, 1f, 0.96f);
        private static readonly Color Ink = new Color32(29, 43, 51, 255);
        private static readonly Color MintInk = new Color32(15, 143, 128, 255);
        private static readonly Color LateInk = new Color32(229, 72, 77, 255);
        private static readonly Color Night = new Color32(19, 39, 48, 255);
        private static readonly Color Paper = new Color32(246, 244, 232, 255);
        private static readonly Color Muted = new Color32(91, 107, 112, 255);
        private static readonly Color IdleCall = new Color32(243, 245, 244, 255);
        private static readonly Color Mint = new Color32(40, 197, 177, 255);
        private static readonly Color DeepTeal = new Color32(11, 59, 54, 255);
        private static readonly Color SoftMint = new Color32(214, 243, 238, 255);

        [MenuItem("Delivery Rider/Apply HUD Style")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Use edit mode after compilation.");
            var scene = GameScenes.RequireActive();
            var hud = UnityEngine.Object.FindFirstObjectByType<GameHud>(FindObjectsInactive.Include);
            var dayHud = UnityEngine.Object.FindFirstObjectByType<DeliveryDayHud>(FindObjectsInactive.Include);
            if (hud == null || dayHud == null) throw new InvalidOperationException("The scene needs GameHud and DeliveryDayHud.");
            var card = LoadOrCreateSprite(CardPath, 128, 0f, CardRadius, 0f, (int)CardRadius);
            var shadow = LoadOrCreateSprite(ShadowPath, 128, ShadowInset, ShadowRadius, ShadowBlur, (int)(ShadowInset + ShadowRadius));
            var root = (RectTransform)hud.transform;
            var hudData = new SerializedObject(hud);
            var dayData = new SerializedObject(dayHud);
            Text Field(SerializedObject data, string name) => new Text((TMP_Text)data.FindProperty(name).objectReferenceValue);

            // Objective card, top left.
            var objective = Field(hudData, "objective");
            var bar = (RectTransform)objective.Rect.parent;
            Place(bar, new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(430f, 74f));
            Style(bar, card, 18f, Card);
            ShadowUnder(bar, shadow, 18f);
            Stretch(objective.Rect, new Vector2(0f, 0.42f), Vector2.one, new Vector2(20f, 0f), new Vector2(-16f, -8f));
            objective.Set(21f, Ink, TextAlignmentOptions.Left, true, 14f);
            var distance = Field(hudData, "distance");
            Stretch(distance.Rect, Vector2.zero, new Vector2(1f, 0.42f), new Vector2(20f, 8f), new Vector2(-16f, 0f));
            distance.Set(14f, MintInk, TextAlignmentOptions.Left, false);

            // Key hints, a slim dark pill at the bottom.
            var prompt = Field(hudData, "prompt");
            var controls = (RectTransform)prompt.Rect.parent;
            Place(controls, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(780f, 40f));
            Style(controls, card, 20f, new Color(Night.r, Night.g, Night.b, 0.72f));
            Stretch(prompt.Rect, Vector2.zero, Vector2.one, new Vector2(16f, 0f), new Vector2(-16f, 0f));
            prompt.Set(15f, Paper, TextAlignmentOptions.Center, false, 11f);

            // Day dial hanging from the top edge with the order timer in a pill below. The dial alone tells the time of
            // day (the user wants no words in it), so the phase name stays hidden; DeliveryDayHud still fills it.
            var dayClock = Field(dayData, "dayClock");
            var top = (RectTransform)dayClock.Rect.parent;
            Place(top, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(260f, 160f));
            var topImage = top.GetComponent<Image>();
            if (topImage != null) { Undo.RecordObject(topImage, "HUD style"); topImage.enabled = false; }
            var sundial = (SundialView)dayData.FindProperty("sundial").objectReferenceValue;
            if (sundial != null)
            {
                var dial = (RectTransform)sundial.transform;
                Undo.SetTransformParent(dial, top, "HUD style");
                Place(dial, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(260f, 112f));
                var dialImage = dial.GetComponent<Image>();
                Undo.RecordObject(dialImage, "HUD style");
                dialImage.material = LoadOrCreateHangingSundial();
            }
            Undo.RecordObject(dayClock.Component.gameObject, "HUD style");
            dayClock.Component.gameObject.SetActive(false);
            var orderClock = Field(dayData, "orderClock");
            var pill = Child(top, "OrderPill");
            Place(pill, new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(176f, 32f));
            Style(pill, card, 16f, Card);
            ShadowUnder(pill, shadow, 16f);
            Undo.SetTransformParent(orderClock.Rect, pill, "HUD style");
            Stretch(orderClock.Rect, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            orderClock.Set(15f, MintInk, TextAlignmentOptions.Center, true, 11f);

            // Wallet card, top right.
            var cash = Field(dayData, "cashLabel");
            var wallet = Child(root, "Wallet");
            // Among the HUD cards, under the pause and settlement overlays.
            wallet.SetSiblingIndex(top.GetSiblingIndex() + 1);
            Place(wallet, new Vector2(1f, 1f), new Vector2(-24f, -20f), new Vector2(210f, 64f));
            Style(wallet, card, 18f, Card);
            ShadowUnder(wallet, shadow, 18f);
            Undo.SetTransformParent(cash.Rect, wallet, "HUD style");
            Stretch(cash.Rect, Vector2.zero, Vector2.one, new Vector2(16f, 6f), new Vector2(-16f, -6f));
            cash.Set(20f, Ink, TextAlignmentOptions.Right, true, 12f);
            cash.Component.richText = true;

            // Destination marker: a dark pill drawn behind every card.
            var guide = UnityEngine.Object.FindFirstObjectByType<DestinationGuide>(FindObjectsInactive.Include);
            if (guide != null)
            {
                var guideData = new SerializedObject(guide);
                var indicator = (RectTransform)guideData.FindProperty("indicator").objectReferenceValue;
                if (indicator != null)
                {
                    Undo.RecordObject(indicator, "HUD style");
                    indicator.sizeDelta = new Vector2(86f, 56f);
                    indicator.SetAsFirstSibling();
                    Style(indicator, card, 14f, new Color(Night.r, Night.g, Night.b, 0.82f));
                }
                var label = (TMP_Text)guideData.FindProperty("distanceLabel").objectReferenceValue;
                if (label != null) new Text(label).Set(14f, Color.white, TextAlignmentOptions.Center, true);
            }

            StyleCallCard(hudData, card, shadow);
            StylePause(hudData, card, shadow);
            StyleSettlement(dayData, card, shadow);

            Undo.RecordObject(dayHud, "HUD style");
            dayData.Update();
            dayData.FindProperty("orderColor").colorValue = MintInk;
            dayData.FindProperty("lateColor").colorValue = LateInk;
            dayData.FindProperty("twoLineCash").boolValue = true;
            dayData.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene.");
            Debug.Log($"HUD style applied to {scene.name}: objective, hanging day dial with order pill, wallet, key hints and the call and map card. Play Mode not started.");
        }

        // Call and map card below the hanging dial: the map in a rounded frame on the left; on the right one order card
        // per call candidate (GameHud fills them; clicking selects), the order detail card and the accept button.
        private static void StyleCallCard(SerializedObject hudData, Sprite card, Sprite shadow)
        {
            var orderCard = (RectTransform)((GameObject)hudData.FindProperty("orderCard").objectReferenceValue).transform;
            Stretch(orderCard, new Vector2(0.03f, 0.1f), new Vector2(0.97f, 0.78f), Vector2.zero, Vector2.zero);
            Backdrop(orderCard, card, shadow, 24f);

            var title = new Text((TMP_Text)hudData.FindProperty("cardTitle").objectReferenceValue);
            Stretch(title.Rect, new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(28f, -62f), new Vector2(0f, -16f));
            title.Set(22f, Ink, TextAlignmentOptions.Left, true, 14f);

            var mapView = UnityEngine.Object.FindFirstObjectByType<DeliveryMapView>(FindObjectsInactive.Include);
            var mapData = new SerializedObject(mapView);
            var close = (Button)mapData.FindProperty("closeButton").objectReferenceValue;
            Place((RectTransform)close.transform, Vector2.one, new Vector2(-20f, -16f), new Vector2(116f, 40f));
            Style((RectTransform)close.transform, card, 20f, Night, true);
            var closeLabel = close.GetComponentInChildren<TMP_Text>(true);
            if (closeLabel != null) new Text(closeLabel).Set(15f, Color.white, TextAlignmentOptions.Center, true);

            // Map on the left, in a rounded frame that clips its markers; the markers get rounded corners too.
            var mapArea = (RectTransform)mapData.FindProperty("mapArea").objectReferenceValue;
            Stretch((RectTransform)mapArea.parent, Vector2.zero, new Vector2(0.56f, 1f), new Vector2(24f, 50f), new Vector2(0f, -72f));
            Style(mapArea, card, 16f, mapArea.GetComponent<Image>().color);
            var mask = mapArea.GetComponent<Mask>();
            if (mask == null) mask = Undo.AddComponent<Mask>(mapArea.gameObject);
            mask.showMaskGraphic = true;
            foreach (string field in new[] { "candidateMarkers", "pickupMarkers" })
            {
                var array = mapData.FindProperty(field);
                for (int i = 0; i < array.arraySize; i++)
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue is Component marker) RoundMarker(marker, card);
            }
            foreach (string field in new[] { "activeCustomerMarker", "thiefMarker" })
                if (mapData.FindProperty(field).objectReferenceValue is Component marker) RoundMarker(marker, card);
            var legend = orderCard.Find("Legend") as RectTransform;
            if (legend != null)
            {
                Stretch(legend, Vector2.zero, new Vector2(0.56f, 0f), new Vector2(24f, 16f), new Vector2(0f, 46f));
                new Text(legend.GetComponent<TMP_Text>()).Set(13f, Muted, TextAlignmentOptions.Center, false);
            }

            // Right column, stacked: the call cards, the detail card, the accept button and the note.
            var list = Child(orderCard, "OrderList");
            Stretch(list, new Vector2(0.58f, 0f), Vector2.one, new Vector2(0f, 20f), new Vector2(-24f, -72f));
            var layout = list.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = Undo.AddComponent<VerticalLayoutGroup>(list.gameObject);
            Undo.RecordObject(layout, "HUD style");
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var font = title.Component.font;
            var calls = new Button[3];
            for (int i = 0; i < calls.Length; i++)
            {
                var item = Child(list, "CallCard_" + i);
                item.SetSiblingIndex(i);
                Style(item, card, 14f, IdleCall, true);
                Height(item, 96f);
                var button = item.GetComponent<Button>();
                if (button == null) button = Undo.AddComponent<Button>(item.gameObject);
                Undo.RecordObject(button, "HUD style");
                button.transition = Selectable.Transition.None;
                button.targetGraphic = item.GetComponent<Image>();
                var detail = Child(item, "Detail");
                var text = detail.GetComponent<TextMeshProUGUI>();
                if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(detail.gameObject);
                Undo.RecordObject(text, "HUD style");
                text.font = font;
                text.richText = true;
                text.raycastTarget = false;
                Stretch(detail, Vector2.zero, Vector2.one, new Vector2(16f, 8f), new Vector2(-14f, -8f));
                new Text(text).Set(16f, Ink, TextAlignmentOptions.MidlineLeft, false, 11f);
                calls[i] = button;
            }
            var detailCard = Child(list, "DetailCard");
            detailCard.SetSiblingIndex(calls.Length);
            Style(detailCard, card, 14f, IdleCall);
            Height(detailCard, 240f);
            var body = new Text((TMP_Text)hudData.FindProperty("cardBody").objectReferenceValue);
            Undo.SetTransformParent(body.Rect, detailCard, "HUD style");
            Stretch(body.Rect, Vector2.zero, Vector2.one, new Vector2(18f, 12f), new Vector2(-18f, -12f));
            body.Set(15f, Ink, TextAlignmentOptions.TopLeft, false, 11f);
            var accept = (RectTransform)((Button)hudData.FindProperty("accept").objectReferenceValue).transform;
            Undo.SetTransformParent(accept, list, "HUD style");
            accept.SetSiblingIndex(calls.Length + 1);
            Style(accept, card, 26f, Mint, true);
            Height(accept, 52f);
            new Text((TMP_Text)hudData.FindProperty("actionLabel").objectReferenceValue).Set(18f, DeepTeal, TextAlignmentOptions.Center, true, 12f);
            var note = (orderCard.Find("MapNote") ?? list.Find("MapNote")) as RectTransform;
            if (note != null)
            {
                Undo.SetTransformParent(note, list, "HUD style");
                note.SetSiblingIndex(calls.Length + 2);
                Height(note, 54f);
                new Text(note.GetComponent<TMP_Text>()).Set(12f, Muted, TextAlignmentOptions.Center, false, 10f);
            }

            hudData.Update();
            var callsProperty = hudData.FindProperty("callCards");
            callsProperty.arraySize = calls.Length;
            for (int i = 0; i < calls.Length; i++) callsProperty.GetArrayElementAtIndex(i).objectReferenceValue = calls[i];
            hudData.FindProperty("detailCard").objectReferenceValue = detailCard.gameObject;
            hudData.ApplyModifiedProperties();
        }

        // Pause: the dimmed screen with a small menu card holding the buttons and the controls.
        private static void StylePause(SerializedObject hudData, Sprite card, Sprite shadow)
        {
            var overlay = (RectTransform)((GameObject)hudData.FindProperty("pausePanel").objectReferenceValue).transform;
            var overlayImage = overlay.GetComponent<Image>();
            Undo.RecordObject(overlayImage, "HUD style");
            overlayImage.color = new Color(Night.r, Night.g, Night.b, 0.6f);
            var menu = Child(overlay, "PauseCard");
            Place(menu, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 420f));
            Style(menu, card, 24f, Card);
            ShadowUnder(menu, shadow, 24f);
            var heading = (overlay.Find("Heading") ?? menu.Find("Heading")) as RectTransform;
            if (heading != null)
            {
                Undo.SetTransformParent(heading, menu, "HUD style");
                Place(heading, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(380f, 44f));
                new Text(heading.GetComponent<TMP_Text>()).Set(26f, Ink, TextAlignmentOptions.Center, true);
            }
            var resume = (RectTransform)((Button)hudData.FindProperty("resume").objectReferenceValue).transform;
            Undo.SetTransformParent(resume, menu, "HUD style");
            Place(resume, new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(360f, 56f));
            Style(resume, card, 28f, Mint, true);
            Label(resume, 18f, DeepTeal);
            var leave = (RectTransform)((Button)hudData.FindProperty("title").objectReferenceValue).transform;
            Undo.SetTransformParent(leave, menu, "HUD style");
            Place(leave, new Vector2(0.5f, 1f), new Vector2(0f, -164f), new Vector2(360f, 52f));
            Style(leave, card, 26f, IdleCall, true);
            Label(leave, 16f, Ink);
            var controls = Child(menu, "ControlsList");
            Stretch(controls, Vector2.zero, new Vector2(1f, 0f), new Vector2(28f, 24f), new Vector2(-28f, 176f));
            var list = Text.Make(controls, heading != null ? heading.GetComponent<TMP_Text>().font : null);
            list.Component.text = "<size=85%><color=#5B6B70>조작</color></size>\n"
                + $"{DarkKey("WASD")} 이동   마우스 시점\n{DarkKey("F")} {DarkKey("클릭")} 주먹   {DarkKey("Space")} 점프\n"
                + $"{DarkKey("1")} 콜   {DarkKey("M")} 지도   {DarkKey("R")} 복귀   {DarkKey("Esc")} 일시정지";
            list.Set(15f, Ink, TextAlignmentOptions.Center, false, 11f);
            Undo.RecordObject(list.Component, "HUD style");
            list.Component.lineSpacing = 18f;
        }

        // Settlement: the dimmed screen with a receipt card. Left, today's deliveries with the page buttons; right, the
        // money (ledger) and the inline and repay buttons; at the bottom, leave to the title and the next day, the only
        // accent-coloured button.
        private static void StyleSettlement(SerializedObject dayData, Sprite card, Sprite shadow)
        {
            var overlay = (RectTransform)((GameObject)dayData.FindProperty("settlementPanel").objectReferenceValue).transform;
            var overlayImage = overlay.GetComponent<Image>();
            Undo.RecordObject(overlayImage, "HUD style");
            overlayImage.color = new Color(Night.r, Night.g, Night.b, 0.92f);
            var receipt = overlay.Find("Card") as RectTransform;
            if (receipt == null) throw new InvalidOperationException("The settlement panel needs its Card.");
            Stretch(receipt, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero);
            Backdrop(receipt, card, shadow, 24f);
            Text Field(string name) => new Text((TMP_Text)dayData.FindProperty(name).objectReferenceValue);
            RectTransform ButtonRect(string name) => (RectTransform)((Button)dayData.FindProperty(name).objectReferenceValue).transform;

            var heading = Field("heading");
            Stretch(heading.Rect, new Vector2(0f, 1f), Vector2.one, new Vector2(32f, -70f), new Vector2(-32f, -24f));
            heading.Set(26f, Ink, TextAlignmentOptions.Left, true, 16f);
            var summary = Field("summary");
            Stretch(summary.Rect, new Vector2(0f, 1f), Vector2.one, new Vector2(32f, -128f), new Vector2(-32f, -72f));
            summary.Set(16f, Muted, TextAlignmentOptions.TopLeft, false, 11f);

            var list = Child(receipt, "DeliveriesCard");
            Stretch(list, Vector2.zero, new Vector2(0.56f, 1f), new Vector2(32f, 96f), new Vector2(0f, -140f));
            Style(list, card, 16f, IdleCall);
            var deliveries = Field("deliveries");
            Undo.SetTransformParent(deliveries.Rect, list, "HUD style");
            Stretch(deliveries.Rect, Vector2.zero, Vector2.one, new Vector2(20f, 54f), new Vector2(-20f, -16f));
            deliveries.Set(15f, Ink, TextAlignmentOptions.TopLeft, false, 11f);
            var previous = ButtonRect("previousPage");
            Undo.SetTransformParent(previous, list, "HUD style");
            Place(previous, Vector2.zero, new Vector2(16f, 12f), new Vector2(76f, 34f));
            Style(previous, card, 17f, Card, true);
            Label(previous, 14f, Ink);
            var next = ButtonRect("nextPage");
            Undo.SetTransformParent(next, list, "HUD style");
            Place(next, new Vector2(1f, 0f), new Vector2(-16f, 12f), new Vector2(76f, 34f));
            Style(next, card, 17f, Card, true);
            Label(next, 14f, Ink);
            var page = Field("pageLabel");
            Undo.SetTransformParent(page.Rect, list, "HUD style");
            Place(page.Rect, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(120f, 34f));
            page.Set(14f, Muted, TextAlignmentOptions.Center, false);

            var money = Child(receipt, "ReceiptCard");
            Stretch(money, new Vector2(0.59f, 0f), Vector2.one, new Vector2(0f, 216f), new Vector2(-32f, -140f));
            Style(money, card, 16f, IdleCall);
            var ledger = Field("ledger");
            Undo.SetTransformParent(ledger.Rect, money, "HUD style");
            Stretch(ledger.Rect, Vector2.zero, Vector2.one, new Vector2(20f, 14f), new Vector2(-20f, -14f));
            ledger.Set(16f, Ink, TextAlignmentOptions.TopLeft, false, 11f);
            var buy = ButtonRect("buyInline");
            Stretch(buy, new Vector2(0.59f, 0f), new Vector2(1f, 0f), new Vector2(0f, 160f), new Vector2(-32f, 204f));
            Style(buy, card, 22f, SoftMint, true);
            Label(buy, 15f, DeepTeal);
            var repay = ButtonRect("repay");
            Stretch(repay, new Vector2(0.59f, 0f), new Vector2(1f, 0f), new Vector2(0f, 106f), new Vector2(-32f, 150f));
            Style(repay, card, 22f, SoftMint, true);
            Label(repay, 15f, DeepTeal);

            var leave = ButtonRect("backToTitle");
            Place(leave, Vector2.zero, new Vector2(32f, 28f), new Vector2(240f, 52f));
            Style(leave, card, 26f, IdleCall, true);
            Label(leave, 16f, Ink);
            var nextDay = ButtonRect("replay");
            Place(nextDay, new Vector2(1f, 0f), new Vector2(-32f, 28f), new Vector2(300f, 56f));
            Style(nextDay, card, 28f, Mint, true);
            Label(nextDay, 18f, DeepTeal);

            // The save note sits between the two bottom buttons, clear of the key hints under the dimmed screen.
            var note = (overlay.Find("ReplayNote") ?? receipt.Find("ReplayNote")) as RectTransform;
            if (note != null)
            {
                Undo.SetTransformParent(note, receipt, "HUD style");
                Place(note, new Vector2(0.5f, 0f), new Vector2(-10f, 34f), new Vector2(460f, 44f));
                new Text(note.GetComponent<TMP_Text>()).Set(13f, Muted, TextAlignmentOptions.Center, false, 10f);
            }
        }

        // A card whose own image gives way to a shadow and a panel inside it, so the shadow hides with the card.
        private static void Backdrop(RectTransform rect, Sprite card, Sprite shadow, float radius)
        {
            var own = rect.GetComponent<Image>();
            if (own != null) { Undo.RecordObject(own, "HUD style"); own.enabled = false; }
            float spread = ShadowInset * radius / ShadowRadius;
            var back = Child(rect, "Shadow");
            Stretch(back, Vector2.zero, Vector2.one, new Vector2(-spread, -spread - 4f), new Vector2(spread, spread - 4f));
            ShadowImage(back, shadow, radius);
            back.SetAsFirstSibling();
            var panel = Child(rect, "Panel");
            Stretch(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Style(panel, card, radius, Card);
            panel.SetSiblingIndex(1);
        }

        private static void Label(RectTransform button, float size, Color color)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            new Text(label).Set(size, color, TextAlignmentOptions.Center, true, 11f);
        }

        private static string DarkKey(string key) => $"<mark=#1D2B3322 padding=\"8,8,3,3\"><b>{key}</b></mark>";

        private static void RoundMarker(Component marker, Sprite card)
        {
            var image = marker.GetComponent<Image>();
            if (image == null) return;
            Undo.RecordObject(image, "HUD style");
            image.sprite = card;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = CardRadius / 10f;
        }

        private static void Height(RectTransform rect, float height)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = Undo.AddComponent<LayoutElement>(rect.gameObject);
            Undo.RecordObject(element, "HUD style");
            element.preferredHeight = element.minHeight = height;
        }

        private readonly struct Text
        {
            public readonly TMP_Text Component;
            public RectTransform Rect => Component.rectTransform;
            public Text(TMP_Text component) => Component = component;

            public static Text Make(RectTransform rect, TMP_FontAsset font)
            {
                var text = rect.GetComponent<TextMeshProUGUI>();
                if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
                Undo.RecordObject(text, "HUD style");
                if (font != null) text.font = font;
                text.richText = true;
                text.raycastTarget = false;
                return new Text(text);
            }

            public void Set(float size, Color color, TextAlignmentOptions alignment, bool bold, float minimum = 0f)
            {
                Undo.RecordObject(Component, "HUD style");
                Component.fontSize = size;
                Component.color = color;
                Component.alignment = alignment;
                Component.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                Component.enableAutoSizing = minimum > 0f;
                if (minimum > 0f) { Component.fontSizeMin = minimum; Component.fontSizeMax = size; }
                Component.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            Undo.RecordObject(rect, "HUD style");
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            Undo.RecordObject(rect, "HUD style");
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        // Rounded card: the sliced sprite's corners scaled to the wanted radius in canvas units.
        private static void Style(RectTransform rect, Sprite sprite, float radius, Color color, bool clickable = false)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(rect.gameObject);
            Undo.RecordObject(image, "HUD style");
            image.enabled = true;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = CardRadius / radius;
            image.color = color;
            image.raycastTarget = clickable;
        }

        // A soft shadow is a sibling drawn just before the card, a little lower and larger.
        private static void ShadowUnder(RectTransform rect, Sprite sprite, float radius)
        {
            var shadow = Child((RectTransform)rect.parent, rect.name + "Shadow");
            Undo.RecordObject(shadow, "HUD style");
            float scale = radius / ShadowRadius;
            float spread = ShadowInset * scale;
            shadow.anchorMin = rect.anchorMin;
            shadow.anchorMax = rect.anchorMax;
            shadow.pivot = rect.pivot;
            shadow.sizeDelta = rect.sizeDelta + Vector2.one * spread * 2f;
            shadow.anchoredPosition = rect.anchoredPosition + new Vector2((rect.pivot.x * 2f - 1f) * spread, (rect.pivot.y * 2f - 1f) * spread - 3f);
            shadow.SetAsLastSibling();
            shadow.SetSiblingIndex(rect.GetSiblingIndex());
            ShadowImage(shadow, sprite, radius);
        }

        private static void ShadowImage(RectTransform rect, Sprite sprite, float radius)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(rect.gameObject);
            Undo.RecordObject(image, "HUD style");
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = ShadowRadius / radius;
            image.color = new Color(0f, 0f, 0f, 0.22f);
            image.raycastTarget = false;
        }

        private static RectTransform Child(RectTransform parent, string name)
        {
            var found = parent.Find(name) as RectTransform;
            if (found != null) return found;
            var created = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(created.gameObject, "HUD style");
            created.gameObject.layer = parent.gameObject.layer;
            created.SetParent(parent, false);
            return created;
        }

        private static Material LoadOrCreateHangingSundial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(HangingSundialPath);
            if (material != null) return material;
            var standing = AssetDatabase.LoadAssetAtPath<Material>(SundialMaterialPath);
            if (standing == null) throw new InvalidOperationException(SundialMaterialPath + " is missing; run Apply Sundial first.");
            material = new Material(standing) { name = "UISundialHanging" };
            material.SetFloat("_Hanging", 1f);
            AssetDatabase.CreateAsset(material, HangingSundialPath);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        // White rounded rectangle (or its blurred shadow) as a sliced sprite, written once.
        private static Sprite LoadOrCreateSprite(string path, int size, float inset, float radius, float blur, int border)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game/Art", "UI");
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f, core = half - inset - radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var q = new Vector2(Mathf.Abs(x + 0.5f - half) - core, Mathf.Abs(y + 0.5f - half) - core);
                    float distance = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                    float alpha = blur > 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-blur * 0.5f, blur, distance)) : Mathf.Clamp01(0.5f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
