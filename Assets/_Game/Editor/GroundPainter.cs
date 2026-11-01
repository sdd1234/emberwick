using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 흙길을 도트 한 장씩 굽는다 (2026-10-31, 레이드 맵). 마을 땅(PixelTownDresser.BakeGround)과 같은 방식.
    /// 곧은 상자 타일 대신 옆선이 구불구불하고 끝이 둥근 길 - 등불 빛 안에서 자로 그은 선이 보이지 않게.
    /// 길마다 텍스처를 따로 굽되, 겹치는 네거리는 '더 깊이 들어간 길'만 칠해서 두 장이 겹쳐 그려지지 않는다.
    /// </summary>
    public static class GroundPainter
    {
        private const float Ppu = 38f;
        private const float Margin = 1.6f;          // 옆선이 길 밖으로 삐져나갈 여유 (m)
        private const string Dir = "Assets/_Game/Art/Pixel/Generated/";

        public struct Path
        {
            public string name; public Rect rect; public Sprite tile;
            public Path(string n, Rect r, Sprite t) { name = n; rect = r; tile = t; }
        }

        public static void PaintRoads(List<Path> roads, Transform parent, int order)
        {
            var tiles = new Dictionary<Sprite, Texture2D>();
            foreach (var r in roads) if (r.tile != null && !tiles.ContainsKey(r.tile)) tiles[r.tile] = Readable(r.tile);

            for (int i = 0; i < roads.Count; i++)
            {
                var road = roads[i];
                if (road.tile == null) continue;
                Rect area = road.rect;
                int ox = Mathf.FloorToInt((area.xMin - Margin) * Ppu), oy = Mathf.FloorToInt((area.yMin - Margin) * Ppu);
                int w = Mathf.CeilToInt((area.xMax + Margin) * Ppu) - ox, h = Mathf.CeilToInt((area.yMax + Margin) * Ppu) - oy;

                // 흙(1)인지, 그중 이 길이 칠할 자리(2)인지
                var kind = new byte[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 2px 덩어리로 잡음을 읽어야 경계가 한 픽셀씩 지글거리지 않는다
                    float wx = (ox + (x & ~1) + 1f) / Ppu, wy = (oy + (y & ~1) + 1f) / Ppu;
                    float jitter = Noise(wx * 1.1f, wy * 1.1f) * 0.38f + Noise(wx * 3.7f + 17f, wy * 3.7f) * 0.14f;
                    int owner = -1; float best = 0f;
                    for (int j = 0; j < roads.Count; j++)
                    {
                        var r = roads[j].rect;
                        bool vertical = r.height > r.width;
                        // 길 방향으로 늘린 잡음 - 옆선이 길게 굽이친다
                        float wobble = vertical ? Noise(wx * 0.9f + 70f, wy * 0.3f) : Noise(wx * 0.3f + 70f, wy * 0.9f + 11f);
                        float d = RoundBox(r, 1.2f, wx, wy) + jitter + wobble * 0.55f;
                        if (d < best) { best = d; owner = j; }
                    }
                    kind[y * w + x] = owner < 0 ? (byte)0 : owner == i ? (byte)2 : (byte)1;
                }

                var src = tiles[road.tile];
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (kind[y * w + x] != 2) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    int gx = ox + x, gy = oy + y;
                    Color c = src.GetPixel(Mod(gx, src.width), Mod(gy, src.height));
                    // 풀과 닿은 한 줄은 어둡게 - 다른 길과 닿은 곳은 그대로 이어진다
                    bool edge = (x > 0 && kind[y * w + x - 1] == 0) || (x < w - 1 && kind[y * w + x + 1] == 0)
                             || (y > 0 && kind[(y - 1) * w + x] == 0) || (y < h - 1 && kind[(y + 1) * w + x] == 0);
                    if (edge) c *= 0.78f;
                    c.a = 1f;
                    px[y * w + x] = c;
                }

                string path = Dir + "road_" + road.name.ToLowerInvariant() + ".png";
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(px); tex.Apply();
                System.IO.Directory.CreateDirectory(Dir);
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = Ppu; imp.filterMode = FilterMode.Point; imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 4096;
                var st = new TextureImporterSettings(); imp.ReadTextureSettings(st);
                st.spriteAlignment = (int)SpriteAlignment.BottomLeft; st.spriteMeshType = SpriteMeshType.FullRect; imp.SetTextureSettings(st);
                imp.SaveAndReimport();

                var go = new GameObject(road.name);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector2(ox / Ppu, oy / Ppu);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                sr.sortingOrder = order;
            }
            foreach (var t in tiles.Values) Object.DestroyImmediate(t);
        }

        private static Texture2D Readable(Sprite s)
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(s)));
            return t;
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>모서리를 반지름 rad 로 둥글린 상자까지의 부호 거리</summary>
        private static float RoundBox(Rect r, float rad, float x, float y)
        {
            rad = Mathf.Min(rad, r.width * 0.5f, r.height * 0.5f);
            var inner = Rect.MinMaxRect(r.xMin + rad, r.yMin + rad, r.xMax - rad, r.yMax - rad);
            float dx = Mathf.Max(inner.xMin - x, x - inner.xMax), dy = Mathf.Max(inner.yMin - y, y - inner.yMax);
            float d = dx > 0f || dy > 0f ? new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude : Mathf.Max(dx, dy);
            return d - rad;
        }

        /// <summary>값 잡음, -1 ~ 1. 다시 구워도 같은 모양이 나오게 해시로만 만든다.</summary>
        private static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy) * 2f - 1f;
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + 1029);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
