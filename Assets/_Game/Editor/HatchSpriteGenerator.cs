using System.IO;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 탈출구 스프라이트를 코드로 굽는다 (기획서 5.2).
    /// 인게임 아트가 전부 플레이스홀더라 이것도 같은 방식으로 그린다.
    /// 나중에 원화로 갈아끼울 때는 같은 경로에 같은 크기로 덮어쓰면 된다.
    ///
    /// 탑다운이라 위에서 내려다본 지하 통로 뚜껑이다.
    /// 돌 테두리에 박힌 참나무 판자 + 쇠 밴드 + 가운데 쇠고리.
    /// </summary>
    public static class HatchSpriteGenerator
    {
        private const int N = 256;                 // PPU 를 256 으로 주면 정확히 1x1 월드 유닛이 된다
        private const string Folder = "Assets/_Game/Art/Props";
        private const string HatchPath = Folder + "/exit_hatch.png";
        private const string GlowPath  = Folder + "/exit_hatch_glow.png";

        // 돌 테두리 · 그림자 틈 · 문짝의 경계 (픽셀)
        private const float FrameOuter = 6f;
        private const float FrameInner = 32f;
        private const float DoorInset  = 37f;

        private const int Planks = 5;

        [MenuItem("Capstone/스프라이트 굽기/탈출 해치")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);

            WritePng(HatchPath, BuildHatch());
            WritePng(GlowPath,  BuildGlow());

            AssetDatabase.Refresh();
            Configure(HatchPath);
            Configure(GlowPath);

            Debug.Log($"[HatchSpriteGenerator] 구움: {HatchPath}, {GlowPath}");
        }

        // ---------- 문짝 ----------
        private static Color[] BuildHatch()
        {
            var px = new Color[N * N];

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 텍스처는 아래가 0 이지만 그림은 위에서부터 생각하는 게 편하다
                float fx = x + 0.5f;
                float fy = N - (y + 0.5f);

                px[y * N + x] = Pixel(fx, fy);
            }

            Shade(px);
            return px;
        }

        private static Color Pixel(float x, float y)
        {
            // 중심에서 가장 먼 축까지의 거리. 사각 테두리를 만드는 데 쓴다
            float cx = Mathf.Abs(x - N * 0.5f);
            float cy = Mathf.Abs(y - N * 0.5f);
            float box = Mathf.Max(cx, cy);                       // 0 = 한가운데, 128 = 모서리

            // 테두리는 반듯하면 새 물건처럼 보인다. 가장자리를 조금 갉아낸다
            float wobble = (Fbm(x * 0.035f, y * 0.035f, 11, 3) - 0.5f) * 5f;
            float outer = N * 0.5f - FrameOuter + wobble;
            float inner = N * 0.5f - FrameInner + wobble * 0.6f;
            float door  = N * 0.5f - DoorInset  + wobble * 0.4f;

            if (box > outer) return Halo(box, outer);            // 바깥은 바닥에 지는 그림자만
            if (box > inner) return Stone(x, y, box, outer, inner);
            if (box > door)  return Gap(box, inner, door);       // 문과 틀 사이의 검은 틈

            return Door(x, y, door);
        }

        /// <summary>바닥에 깔린 것처럼 보이도록 테두리 바깥으로 옅은 그림자를 흘린다.</summary>
        private static Color Halo(float box, float outer)
        {
            float t = Mathf.Clamp01((box - outer) / 7f);
            return new Color(0.03f, 0.028f, 0.026f, (1f - t) * 0.5f);
        }

        /// <summary>문을 물고 있는 돌 테두리.</summary>
        private static Color Stone(float x, float y, float box, float outer, float inner)
        {
            float cx = Mathf.Abs(x - N * 0.5f);
            float cy = Mathf.Abs(y - N * 0.5f);
            float grain = Fbm(x * 0.09f, y * 0.09f, 23, 4);
            float v = 0.245f + grain * 0.13f;

            // 돌을 쪼갠 자국. 낮은 주파수 노이즈가 어떤 값을 가로지르는 선을 긁는다
            float crack = Mathf.Abs(Fbm(x * 0.045f, y * 0.045f, 71, 3) - 0.5f);
            if (crack < 0.022f) v *= 0.55f;

            // 블록 이음매. 통짜 돌보다 쌓아 만든 것처럼 보여야 한다
            float joint = 999f;
            bool horizontal = cy > cx;                        // 위아래 변이면 세로 이음매를 넣는다
            float along = horizontal ? x : y;
            float wob = (Fbm(along * 0.06f, 7f, 41, 2) - 0.5f) * 6f;
            float period = N / 4f;
            joint = Mathf.Abs(Mathf.Repeat(along + wob + period * 0.5f, period) - period * 0.5f);
            if (joint < 2.2f) v *= Mathf.Lerp(0.42f, 1f, joint / 2.2f);

            // 안쪽 모서리는 그늘지고 바깥 모서리는 빛을 받는다
            float edgeIn  = Mathf.Clamp01((box - inner) / 6f);
            float edgeOut = Mathf.Clamp01((outer - box) / 5f);
            v *= Mathf.Lerp(0.62f, 1f, edgeIn);
            v *= Mathf.Lerp(0.72f, 1f, edgeOut);

            v *= TopLeft(x, y);
            return new Color(v * 1.02f, v * 0.99f, v * 0.93f, 1f);
        }

        private static Color Gap(float box, float inner, float door)
        {
            // 틈 안쪽으로 갈수록 깊어 보이게
            float t = Mathf.InverseLerp(door, inner, box);
            float v = Mathf.Lerp(0.035f, 0.075f, t);
            return new Color(v, v * 0.95f, v * 0.9f, 1f);
        }

        // ---------- 판자 · 쇠붙이 ----------
        private static Color Door(float x, float y, float door)
        {
            float span = door * 2f;
            float left = N * 0.5f - door;
            float u = (x - left) / span;                          // 문짝 안에서의 가로 위치 0~1
            float vpos = (y - (N * 0.5f - door)) / span;          // 세로 위치 0~1

            // --- 참나무 판자
            float pf = u * Planks;
            int plank = Mathf.Clamp(Mathf.FloorToInt(pf), 0, Planks - 1);
            float inPlank = pf - plank;                           // 판자 하나 안에서의 위치

            float tone = 0.86f + Hash01(plank * 17 + 3) * 0.28f;  // 판자마다 나뭇결 색이 다르다
            Color wood = new Color(0.255f, 0.168f, 0.098f) * tone;

            // 결은 세로로 길게 늘어난다 → 가로 주파수만 높게
            float grain = Fbm(x * 0.5f + plank * 40f, y * 0.045f, 31 + plank, 4);
            float knot  = Fbm(x * 0.11f, y * 0.11f, 51 + plank, 3);
            float w = 0.78f + grain * 0.34f;
            if (knot > 0.80f) w *= 0.66f;                          // 옹이
            wood *= w;

            // 판자 사이 틈
            float seam = Mathf.Min(inPlank, 1f - inPlank) * (span / Planks);
            if (seam < 2.2f) wood *= Mathf.Lerp(0.20f, 1f, seam / 2.2f);

            Color c = wood * TopLeft(x, y);

            // --- 쇠 밴드 두 줄
            float b1 = Mathf.Abs(vpos - 0.19f) * span;
            float b2 = Mathf.Abs(vpos - 0.81f) * span;
            float band = Mathf.Min(b1, b2);
            if (band < 11f)
            {
                float edge = Mathf.Clamp01((11f - band) / 3.5f);
                Color iron = Iron(x, y, band, 11f);
                c = Color.Lerp(c, iron, edge);

                // 못. 밴드를 따라 일정 간격으로 박혀 있다
                float rivetSpacing = span / 6f;
                float nearest = Mathf.Round((x - left) / rivetSpacing) * rivetSpacing + left;
                float rd = Mathf.Sqrt((x - nearest) * (x - nearest) + band * band);
                if (rd < 4.2f) c = Color.Lerp(c, Rivet(rd), Mathf.Clamp01((4.2f - rd) / 1.4f));
            }

            // --- 가운데 쇠고리
            const float RingR = 30f;      // 고리 중심선 반지름
            const float RingHalf = 6f;    // 쇠 단면의 반지름

            float rx = x - N * 0.5f;
            float ry = y - N * 0.5f;
            float rr = Mathf.Sqrt(rx * rx + ry * ry);

            // 판자에 지는 그림자를 먼저 깐다. 빛이 좌상단에서 오니 그림자는 우하단으로 밀린다
            float sx = rx - 4f, sy = ry + 4f;
            float sTube = Mathf.Abs(Mathf.Sqrt(sx * sx + sy * sy) - RingR);
            if (sTube < RingHalf + 3f)
                c *= Mathf.Lerp(1f, 0.46f, Mathf.Clamp01((RingHalf + 3f - sTube) / (RingHalf + 3f)));

            float tube = Mathf.Abs(rr - RingR);
            if (tube < RingHalf)
            {
                // 단면이 원이라 가운데가 부풀어 오른다 → 화면 바깥쪽을 향하는 법선 성분
                float t = tube / RingHalf;
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                float side = rr < RingR ? -1f : 1f;
                float inv = rr < 0.001f ? 0f : 1f / rr;
                float nx = rx * inv * side * t;
                float ny = ry * inv * side * t;

                // 좌상단 위쪽에서 들어오는 빛
                const float lx = -0.50f, ly = 0.50f, lz = 0.71f;
                float ndl = Mathf.Clamp01(nx * lx + ny * ly + nz * lz);

                float v = 0.055f + 0.40f * Mathf.Pow(ndl, 1.4f) + 0.55f * Mathf.Pow(ndl, 26f);
                // 두드려 만든 쇠라 표면이 고르지 않다
                v *= 0.88f + Fbm(x * 0.35f, y * 0.35f, 137, 2) * 0.26f;

                float edge = Mathf.Clamp01((RingHalf - tube) / 1.6f);
                c = Color.Lerp(c, new Color(v, v * 0.975f, v * 0.93f, 1f), edge);
            }

            // --- 세월
            float stain = Fbm(x * 0.05f, y * 0.05f, 97, 3);
            if (stain > 0.68f) c *= Mathf.Lerp(1f, 0.74f, (stain - 0.68f) / 0.32f);

            return new Color(c.r, c.g, c.b, 1f);
        }

        private static Color Iron(float x, float y, float band, float half)
        {
            float n = Fbm(x * 0.16f, y * 0.16f, 61, 3);
            float v = 0.135f + n * 0.10f;

            // 밴드 윗면이 빛을 받는다
            v *= Mathf.Lerp(1.35f, 0.7f, Mathf.Clamp01(band / half));

            // 녹
            float rust = Fbm(x * 0.07f, y * 0.07f, 83, 3);
            if (rust > 0.72f)
            {
                float k = (rust - 0.72f) / 0.28f;
                return Color.Lerp(new Color(v, v * 0.98f, v * 0.95f), new Color(0.30f, 0.145f, 0.075f), k * 0.8f);
            }
            return new Color(v, v * 0.98f, v * 0.95f, 1f);
        }

        private static Color Rivet(float d)
        {
            float lit = Mathf.Clamp01(1f - d / 4.2f);
            float v = Mathf.Lerp(0.13f, 0.52f, lit * lit);
            return new Color(v, v * 0.98f, v * 0.94f, 1f);
        }

        /// <summary>왼쪽 위에서 빛이 든다고 가정한 전체 명암.</summary>
        private static float TopLeft(float x, float y)
        {
            float k = ((N - x) + y) / (N * 2f);       // 좌상단 1, 우하단 0
            return Mathf.Lerp(0.82f, 1.10f, k);
        }

        /// <summary>가장자리를 한 번 더 눌러 전체가 바닥에 눌러앉아 보이게 한다.</summary>
        private static void Shade(Color[] px)
        {
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float cx = Mathf.Abs(x + 0.5f - N * 0.5f) / (N * 0.5f);
                float cy = Mathf.Abs(y + 0.5f - N * 0.5f) / (N * 0.5f);
                float r = Mathf.Max(cx, cy);
                float v = Mathf.Lerp(1f, 0.86f, Mathf.SmoothStep(0.55f, 1f, r));
                var c = px[y * N + x];
                px[y * N + x] = new Color(c.r * v, c.g * v, c.b * v, c.a);
            }
        }

        // ---------- 새어 나오는 빛 ----------
        /// <summary>
        /// 판자 틈과 문 둘레에서 빛이 새어 나오는 모양.
        /// 초록 원 대신 이걸 문짝 위에 겹치고 알파만 흔들면 "아래에서 빛이 올라온다"로 읽힌다.
        /// </summary>
        private static Color[] BuildGlow()
        {
            var mask = new float[N * N];

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x + 0.5f;
                float fy = N - (y + 0.5f);

                float cx = Mathf.Abs(fx - N * 0.5f);
                float cy = Mathf.Abs(fy - N * 0.5f);
                float box = Mathf.Max(cx, cy);

                float door = N * 0.5f - DoorInset;
                float inner = N * 0.5f - FrameInner;

                float m = 0f;

                // 문과 틀 사이의 틈
                if (box > door && box < inner) m = 1f;

                // 판자 사이 틈
                if (box <= door)
                {
                    float span = door * 2f;
                    float left = N * 0.5f - door;
                    float pf = (fx - left) / span * Planks;
                    float inPlank = pf - Mathf.Floor(pf);
                    float seam = Mathf.Min(inPlank, 1f - inPlank) * (span / Planks);
                    if (seam < 2.4f) m = Mathf.Max(m, 0.85f * (1f - seam / 2.4f));
                }

                // 가운데로 갈수록 세게 (아래 통로에서 올라오는 빛)
                float radial = 1f - Mathf.Clamp01(box / (N * 0.5f));
                mask[y * N + x] = Mathf.Max(m * Mathf.Lerp(0.45f, 1f, radial), radial * radial * 0.30f);
            }

            Blur(mask, 2);
            Blur(mask, 5);

            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(mask[i] * 2.4f));
            return px;
        }

        private static void Blur(float[] buf, int r)
        {
            var tmp = new float[buf.Length];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float s = 0f; int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int xx = Mathf.Clamp(x + k, 0, N - 1);
                    s += buf[y * N + xx]; n++;
                }
                tmp[y * N + x] = s / n;
            }
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float s = 0f; int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int yy = Mathf.Clamp(y + k, 0, N - 1);
                    s += tmp[yy * N + x]; n++;
                }
                buf[y * N + x] = s / n;
            }
        }

        // ---------- 잡동사니 ----------
        private static float Hash01(int i)
        {
            float v = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static float Hash(int x, int y, int seed)
        {
            float v = Mathf.Sin(x * 127.1f + y * 311.7f + seed * 74.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static float Value(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);
            float a = Hash(xi, yi, seed),     b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * freq, y * freq, seed + i * 13) * amp;
                norm += amp;
                amp *= 0.5f; freq *= 2f;
            }
            return sum / norm;
        }

        private static void WritePng(string path, Color[] px)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// PPU 를 256 으로 줘서 스프라이트가 정확히 1x1 월드 유닛이 되게 한다.
        /// (MazeBuilder 가 localScale 로 타일 크기를 곱하므로 1 유닛 기준이어야 계산이 맞는다)
        /// PPU · textureType · spriteImportMode 는 importer 에 직접 넣어야 먹는다.
        /// TextureImporterSettings 를 경유하면 조용히 무시된다.
        /// </summary>
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
            s.spriteMeshType = SpriteMeshType.FullRect;    // 안 주면 투명 여백이 잘려 bounds 가 작아진다
            imp.SetTextureSettings(s);

            imp.SaveAndReimport();
        }
    }
}
