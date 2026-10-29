using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeliveryRider.Editor
{
    // M1 core loop: sundial day clock, two call slots and the 600 s test day. Safe to run repeatedly.
    public static class CoreLoopInstaller
    {
        private const string SettingsPath = "Assets/_Game/Settings/DaySettings.asset";
        private const float DayLengthSeconds = 600f;
        private const int CallSlots = 2;

        [MenuItem("Delivery Rider/Apply Core Loop (M1)")]
        public static void Apply()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>(FindObjectsInactive.Include);
            var dayHud = UnityEngine.Object.FindFirstObjectByType<DeliveryDayHud>(FindObjectsInactive.Include);
            if (session == null || dayHud == null) throw new InvalidOperationException("Open a scene with DeliverySession and DeliveryDayHud.");

            var sessionData = new SerializedObject(session);
            sessionData.FindProperty("candidateCount").intValue = CallSlots;
            sessionData.ApplyModifiedProperties();

            var settings = AssetDatabase.LoadAssetAtPath<DeliveryDaySettings>(SettingsPath);
            if (settings != null)
            {
                var settingsData = new SerializedObject(settings);
                settingsData.FindProperty("durationSeconds").floatValue = DayLengthSeconds;
                settingsData.ApplyModifiedProperties();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            SundialInstaller.Build(dayHud);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);
        }
    }
}
