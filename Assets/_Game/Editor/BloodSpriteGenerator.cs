using System.IO;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 사망 연출에 쓰는 핏방울 · 핏자국 스프라이트를 굽는다.
    /// 예전엔 DeathEffect 가 실행 중에 텍스처를 만들어 썼는데, 그러면 인스펙터에서 볼 수가 없어서
    /// 애셋으로 뽑아 프리팹에 물린다.
    ///
    /// 메뉴: Capstone/스프라이트 굽기/피
    /// </summary>
    public static class BloodSpriteGenerator
    {
        private const int N = 64;
        private const string Folder = "Assets/_Game/Art/Props";
        public const int SplatVariants = 4;

        [MenuItem("Capstone/스프라이트 굽기/피")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);

            Write($"{Folder}/blood_disc.png", Disc());
            for (int i = 0; i < SplatVariants; i++)
                Write($"{Folder}/blood_splat_{i}.png", Splat(i));

            AssetDatabase.Refresh();

            Configure($"{Folder}/blood_disc.png");
            for (int i = 0; i < SplatVariants; i++) Configure($"{Folder}/blood_splat_{i}.png");

            Debug.Log($"[BloodSpriteGenerator] 핏방울 1 + 핏자국 {SplatVariants} 종 구움");
        }

        /// <summary>가장자리가 부드럽게 빠지는 원. 핏방울과 바닥에 남는 점에 쓴다.</summary>
        private static Color[] Disc()
        {
            var px = new Color[N * N];
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.SmoothStep(0.72f, 1f, d)));
            }
            return px;
        }

        /// <summary>사방으로 삐죽삐죽한 얼룩. 씨앗마다 달라서 같은 자국이 반복되지 않는다.</summary>
        private static Color[] Splat(int seed)
        {
            var rnd = new System.Random(seed * 7919 + 13);
            var amp = new float[4];
            var phase = new float[4];
            for (int i = 0; i < 4; i++)
            {
                amp[i] = (float)rnd.NextDouble() * 0.14f;
                phase[i] = (float)rnd.NextDouble() * Mathf.PI * 2f;
            }

            var px = new Color[N * N];
            float c = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float ang = Mathf.Atan2(dy, dx);

                // 낮은 주파수 몇 개만 겹쳐서 둥글되 울퉁불퉁한 윤곽을 만든다
                float edge = 0.78f;
                for (int i = 0; i < 4; i++) edge += amp[i] * Mathf.Sin(ang * (i + 2) + phase[i]);

                px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.SmoothStep(edge * 0.72f, edge, dist)));
            }
            return px;
        }

        private static void Write(string path, Color[] px)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>PPU 를 크기와 같게 줘서 스프라이트가 정확히 1x1 월드 유닛이 되게 한다.</summary>
        private static void Configure(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = N;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;

            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Center;
            s.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(s);

            imp.SaveAndReimport();
        }
    }
}
