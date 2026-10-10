using System.IO;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 레이드 맵 · 마을에 쓰는 환경 스프라이트를 코드로 굽는다 (메뉴 Capstone/스프라이트 굽기/환경).
    /// 전부 흰색/회색조로 구워 SpriteRenderer.color 로 물들여 쓴다 - 한 장으로 길 · 덤불 · 건물 바닥을 다 칠한다.
    /// 등불만 색이 있다 (빛을 내는 물건이라 색을 바꿔 쓸 일이 없다).
    /// </summary>
    public static class EnvSpriteGenerator
    {
        public const string Folder = "Assets/_Game/Art/Env";
        public const string Square = Folder + "/env_square.png";
        public const string Circle = Folder + "/env_circle.png";
        public const string SoftCircle = Folder + "/env_soft_circle.png";
        public const string Tree = Folder + "/env_tree.png";
        public const string Bramble = Folder + "/env_bramble.png";
        public const string Ground = Folder + "/env_ground.png";
        public const string Lamp = Folder + "/env_lamp.png";

        [MenuItem("Capstone/스프라이트 굽기/환경")]
        public static void BakeAll()
        {
            Directory.CreateDirectory(Folder);
            var rng = new System.Random(4242);

            Save(Square, MakeSquare(8), 8, new Vector2(0.5f, 0.5f), false);
            Save(Circle, MakeCircle(128, 1f), 128, new Vector2(0.5f, 0.5f), false);
            Save(SoftCircle, MakeCircle(128, 0f), 128, new Vector2(0.5f, 0.5f), false);
            Save(Tree, MakeTree(128, rng), 64, new Vector2(0.5f, 0.3f), false);
            Save(Bramble, MakeBramble(128, rng), 64, new Vector2(0.5f, 0.5f), false);
            Save(Ground, MakeGround(128, rng), 32, new Vector2(0.5f, 0.5f), true);
            Save(Lamp, MakeLamp(), 24, new Vector2(0.5f, 0f), false);

            AssetDatabase.Refresh();
            Debug.Log("[EnvSpriteGenerator] 환경 스프라이트를 구웠다 - " + Folder);
        }

        // ---------- 그리기 ----------
        private static Texture2D New(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var clear = new Color32[w * h];
            t.SetPixels32(clear);
            return t;
        }

        private static Texture2D MakeSquare(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            return t;
        }

        /// <summary>hard = 1 이면 테두리만 1px 부드러운 원판, 0 이면 가운데서 바깥으로 옅어지는 빛 웅덩이.</summary>
        private static Texture2D MakeCircle(int s, float hard)
        {
            var t = New(s, s);
            float r = s * 0.5f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = hard > 0.5f ? Mathf.Clamp01((1f - d) * r) : Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            return t;
        }

        /// <summary>나무 - 뭉게뭉게한 수관을 위에서 본 모양. 아래 줄기 그림자 없이 둥근 덩어리 여럿을 겹친다.</summary>
        private static Texture2D MakeTree(int s, System.Random rng)
        {
            var t = New(s, s);
            var blobs = new Vector3[9];
            for (int i = 0; i < blobs.Length; i++)
            {
                float a = i / (float)blobs.Length * Mathf.PI * 2f;
                float d = i == 0 ? 0f : s * 0.2f;
                blobs[i] = new Vector3(s * 0.5f + Mathf.Cos(a) * d, s * 0.5f + Mathf.Sin(a) * d,
                                       s * (i == 0 ? 0.3f : 0.2f + (float)rng.NextDouble() * 0.06f));
            }
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float best = 0f; float shade = 0f;
                foreach (var b in blobs)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(b.x, b.y)) / b.z;
                    if (d < 1f && 1f - d > best)
                    {
                        best = 1f - d;
                        // 왼쪽 위에서 빛이 든다 - 덩어리마다 밝은 쪽이 생겨야 뭉게뭉게해 보인다
                        shade = Mathf.Clamp01(0.55f + ((x - b.x) * -0.5f + (y - b.y) * 0.5f) / b.z * 0.45f);
                    }
                }
                if (best <= 0f) continue;
                float v = Mathf.Lerp(0.55f, 1f, shade) * (0.92f + (float)rng.NextDouble() * 0.08f);
                t.SetPixel(x, y, new Color(v, v, v, Mathf.Clamp01(best * 10f)));
            }
            return t;
        }

        /// <summary>가시덤불 - 엉킨 줄기를 무작위 선으로 긋고 가시를 튀어나오게 한다.</summary>
        private static Texture2D MakeBramble(int s, System.Random rng)
        {
            var t = New(s, s);
            for (int k = 0; k < 26; k++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                var p = new Vector2((float)rng.NextDouble() * s, (float)rng.NextDouble() * s);
                for (int step = 0; step < 70; step++)
                {
                    ang += ((float)rng.NextDouble() - 0.5f) * 0.6f;
                    p += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 1.2f;
                    float edge = Mathf.Min(Mathf.Min(p.x, s - p.x), Mathf.Min(p.y, s - p.y));
                    if (edge < 4f) break;
                    float v = 0.6f + (float)rng.NextDouble() * 0.4f;
                    Dot(t, p, 1.6f, v);
                    if (step % 7 == 0)  // 가시
                    {
                        var thorn = p + new Vector2(Mathf.Cos(ang + 1.6f), Mathf.Sin(ang + 1.6f)) * 3.5f;
                        Dot(t, Vector2.Lerp(p, thorn, 0.5f), 0.9f, v);
                        Dot(t, thorn, 0.6f, v);
                    }
                }
            }
            return t;
        }

        private static void Dot(Texture2D t, Vector2 c, float r, float v)
        {
            for (int y = (int)(c.y - r - 1); y <= c.y + r + 1; y++)
            for (int x = (int)(c.x - r - 1); x <= c.x + r + 1; x++)
            {
                if (x < 0 || y < 0 || x >= t.width || y >= t.height) continue;
                float a = Mathf.Clamp01(r + 0.5f - Vector2.Distance(new Vector2(x, y), c));
                if (a <= 0f) continue;
                var old = t.GetPixel(x, y);
                t.SetPixel(x, y, new Color(v, v, v, Mathf.Max(old.a, a)));
            }
        }

        /// <summary>바닥 - 이어 붙여도 이음매가 안 보이도록 주기적인 잡음으로 만든다.</summary>
        private static Texture2D MakeGround(int s, System.Random rng)
        {
            var t = New(s, s);
            float ox = (float)rng.NextDouble() * 50f, oy = (float)rng.NextDouble() * 50f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // 원기둥 위에서 펄린을 읽으면 가로세로 모두 주기가 맞는다
                float u = x / (float)s * Mathf.PI * 2f, w = y / (float)s * Mathf.PI * 2f;
                float n = 0f, amp = 1f, norm = 0f;
                for (int o = 0; o < 3; o++)
                {
                    float f = 1.2f * (1 << o);
                    n += amp * Mathf.PerlinNoise(ox + Mathf.Cos(u) * f + Mathf.Cos(w) * f * 0.7f,
                                                  oy + Mathf.Sin(u) * f + Mathf.Sin(w) * f * 0.7f);
                    norm += amp; amp *= 0.5f;
                }
                n /= norm;
                float grain = (float)rng.NextDouble() * 0.06f;
                float v = Mathf.Clamp01(0.78f + (n - 0.5f) * 0.35f + grain);
                t.SetPixel(x, y, new Color(v, v, v, 1f));
            }
            return t;
        }

        /// <summary>등불 기둥 - 쇠 기둥 위에 등롱. 1m x 2m.</summary>
        private static Texture2D MakeLamp()
        {
            int w = 24, h = 48;
            var t = New(w, h);
            var iron = new Color(0.16f, 0.15f, 0.15f, 1f);
            var ironHi = new Color(0.28f, 0.26f, 0.25f, 1f);
            var glass = new Color(1f, 0.78f, 0.42f, 1f);
            var glassHot = new Color(1f, 0.93f, 0.7f, 1f);

            for (int y = 0; y < 3; y++) for (int x = 7; x < 17; x++) t.SetPixel(x, y, iron);        // 받침
            for (int y = 3; y < 34; y++) { t.SetPixel(11, y, iron); t.SetPixel(12, y, ironHi); }    // 기둥
            for (int x = 7; x < 17; x++) { t.SetPixel(x, 34, iron); t.SetPixel(x, 46, iron); }       // 등롱 테
            for (int y = 35; y < 46; y++)
            {
                t.SetPixel(7, y, iron); t.SetPixel(16, y, iron);
                for (int x = 8; x < 16; x++)
                    t.SetPixel(x, y, (x > 9 && x < 14 && y > 37 && y < 43) ? glassHot : glass);
            }
            for (int x = 9; x < 15; x++) t.SetPixel(x, 47, iron);                                      // 지붕
            return t;
        }

        // ---------- 저장 ----------
        private static void Save(string path, Texture2D tex, int ppu, Vector2 pivot, bool repeat)
        {
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            // PPU · 타입 · 필터는 importer 에 직접 넣어야 먹는다 (TextureImporterSettings 경유로는 조용히 무시됨)
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = ppu;
            imp.filterMode = path == Lamp ? FilterMode.Point : FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;     // 타일 그리기 · 9칸에 필요
            imp.SetTextureSettings(settings);
            imp.spritePixelsPerUnit = ppu;                         // SetTextureSettings 가 되돌릴 수 있어 한 번 더
            imp.SaveAndReimport();
        }
    }
}
