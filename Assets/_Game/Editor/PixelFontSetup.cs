using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 도트 글꼴 (2026-10-29 - 글씨 · 숫자도 도트로 통일).
    /// 갈무리(Galmuri, SIL OFL 1.1 - 게임에 넣어 배포 가능, 라이선스 Art/Fonts/Galmuri-OFL-LICENSE.txt)
    ///   Galmuri11 Bold - 본문 · 숫자 (24 = 2배, 36 = 3배)
    ///   Galmuri9       - 작은 설명 (20 = 2배)
    /// 외곽선이 있어야 어두운 화면 위에서 읽혀서 SDF 로 굽는다. 도트 모서리가 무뎌지지 않게 4배 크기로 샘플링한다.
    /// </summary>
    public static class PixelFontSetup
    {
        public const string Main = "Assets/_Game/Art/Fonts/Galmuri11-Bold SDF.asset";
        public const string Small = "Assets/_Game/Art/Fonts/Galmuri9 SDF.asset";

        [MenuItem("Capstone/도트 반영/도트 글꼴 만들기")]
        public static void Build()
        {
            Make("Assets/_Game/Art/Fonts/Galmuri11-Bold.ttf", Main, 48);
            // Galmuri9 SDF 는 글자 칸이 어긋나 깨져 나와서 쓰지 않는다 - 본문 글꼴 하나로 통일
            AssetDatabase.SaveAssets();
        }

        private static void Make(string ttf, string path, int sampling)
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) return;
            AssetDatabase.ImportAsset(ttf);
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            var fa = TMP_FontAsset.CreateFontAsset(font, sampling, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            // 자주 쓰는 글자는 미리 굽는다 (동적 아틀라스라 없는 글자는 그때그때 추가된다)
            fa.TryAddCharacters("0123456789/:%×+-.,()[]!?<> 강약꺼짐남은시간빛플라스크석궁출혈감염은신등불켜기끄기밝기붕대가방거둔다건다불씨이정표");
            EditorUtility.SetDirty(fa);
        }

        /// <summary>
        /// 열린 씬의 모든 글자를 Galmuri11 Bold 로 바꾸고 크기를 12 의 정수배로 맞춘다.
        /// 도트 글꼴은 12px 이 원래 크기라 18 · 30 같은 크기에선 획 굵기가 들쭉날쭉해진다.
        /// </summary>
        [MenuItem("Capstone/도트 반영/열린 씬 글꼴 통일")]
        public static void ApplyToOpenScene()
        {
            Build();
            var font = MainFont;
            if (font == null) return;
            foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                t.font = font;
                t.fontSharedMaterial = font.material;
                t.fontStyle &= ~FontStyles.Bold;      // 이미 굵은 글꼴 - 가짜 굵기를 또 얹으면 획이 뭉개진다
                t.fontSize = SnapSize(t.fontSize);
                if (t.enableAutoSizing) { t.fontSizeMin = 12; t.fontSizeMax = SnapSize(t.fontSizeMax); }
                EditorUtility.SetDirty(t);
            }
        }

        public static float SnapSize(float s) => s < 18f ? 12f : s < 33f ? 24f : 36f;

        public static TMP_FontAsset MainFont => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Main);
        public static TMP_FontAsset SmallFont => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Small);
    }
}
