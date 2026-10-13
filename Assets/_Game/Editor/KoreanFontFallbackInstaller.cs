using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DeliveryRider.Editor
{
    // The UI font asset (PrototypeKoreanRegular) is a static atlas of about 300 pre-baked characters, so any other
    // Hangul syllable (e.g. '자') rendered as a box. This adds a dynamic font asset built from the source OTF as its
    // fallback: missing glyphs are rasterised on demand, including generated customer and food names.
    // Safe to run repeatedly.
    public static class KoreanFontFallbackInstaller
    {
        private const string PrimaryPath = "Assets/_Game/Art/Fonts/PrototypeKoreanRegular.asset";
        private const string SourcePath = "Assets/_Game/Art/Fonts/NotoSansKR-Regular.otf";
        private const string FallbackPath = "Assets/_Game/Art/Fonts/PrototypeKoreanDynamic.asset";

        [MenuItem("Delivery Rider/Apply Korean Font Fallback")]
        public static void Apply()
        {
            var primary = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PrimaryPath);
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (primary == null || source == null) throw new InvalidOperationException("Primary font asset and source OTF are required.");

            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
            if (fallback == null)
            {
                // Match the primary asset's sampling so both atlases render at the same visual size.
                int pointSize = Mathf.Max(32, (int)primary.faceInfo.pointSize);
                fallback = TMP_FontAsset.CreateFontAsset(source, pointSize, primary.atlasPadding, GlyphRenderMode.SDFAA,
                    1024, 1024, AtlasPopulationMode.Dynamic, true);
                fallback.name = "PrototypeKoreanDynamic";
                AssetDatabase.CreateAsset(fallback, FallbackPath);
                // The atlas texture and material must live inside the font asset file to be saved.
                foreach (var texture in fallback.atlasTextures)
                {
                    texture.name = fallback.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(texture, fallback);
                }
                fallback.material.name = fallback.name + " Material";
                AssetDatabase.AddObjectToAsset(fallback.material, fallback);
            }
            // Glyphs added while testing in the editor are dropped from builds; they are regenerated at runtime.
            var data = new SerializedObject(fallback);
            data.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fallback);

            if (primary.fallbackFontAssetTable == null) primary.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (!primary.fallbackFontAssetTable.Contains(fallback)) primary.fallbackFontAssetTable.Add(fallback);
            EditorUtility.SetDirty(primary);
            AssetDatabase.SaveAssets();
            Debug.Log($"Korean font fallback: {primary.name} -> {string.Join(", ", primary.fallbackFontAssetTable.Select(f => f.name))}.");
        }
    }
}
