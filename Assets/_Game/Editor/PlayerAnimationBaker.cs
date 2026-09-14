using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 정지 스프라이트 한 장에서 걷기 · 달리기 · 대기 · 사격 프레임을 굽는다.
    ///
    /// 플레이어 원화는 단색 도형을 쌓아 만든 그림이라, 색이 같고 서로 붙어 있는 덩어리를
    /// 골라내면 그대로 부위가 된다. 다시 그리지 않고 **원본 픽셀을 그대로 돌려서** 쓰는 이유는
    /// 나중에 진짜 원화로 갈아끼워도 같은 절차가 그대로 통하기 때문이다.
    ///
    /// 메뉴: Capstone/스프라이트 굽기/플레이어 애니메이션
    /// </summary>
    public static class PlayerAnimationBaker
    {
        private const string Source = "Assets/_Game/Art/Characters/player_placeholder.png";
        private const string OutFolder = "Assets/_Game/Art/Characters/Anim";

        // 원본 1000x2000 에서 절반으로 줄여 굽는다. 2x2 평균이라 돌린 자리가 부드럽게 남는다
        private const int Down = 2;

        // ---------- 부위 ----------
        private enum Limb { Crossbow, ArmL, ArmR, LegL, LegR, Torso, Head }

        /// <summary>원본에서 오려낸 조각 하나. 회전축은 원본 좌표계 그대로 들고 있는다.</summary>
        private class Piece
        {
            public int X0, Y0, W, H;        // 원본 안에서의 자리
            public Color32[] Px;            // W*H
            public Limb Part;
        }

        private class Xf
        {
            public float Deg;
            public Vector2 Pivot;
            public Vector2 Move;
            public float Scale;
            public Xf(float deg, Vector2 pivot, Vector2 move, float scale = 1f)
            { Deg = deg; Pivot = pivot; Move = move; Scale = scale; }
        }

        // 관절 위치 (원본 픽셀, y 는 아래가 0)
        private static readonly Vector2 ShoulderL = new Vector2(214f, 1155f);
        private static readonly Vector2 ShoulderR = new Vector2(785f, 1155f);
        private static readonly Vector2 HipL      = new Vector2(385f, 800f);
        private static readonly Vector2 HipR      = new Vector2(615f, 800f);
        private static readonly Vector2 Waist     = new Vector2(500f, 800f);
        private static readonly Vector2 Neck      = new Vector2(500f, 1300f);

        private static int _w, _h;

        [MenuItem("Capstone/스프라이트 굽기/플레이어 애니메이션")]
        public static void Bake()
        {
            var pieces = Cut();
            if (pieces == null) return;

            Directory.CreateDirectory(OutFolder);

            BakeClip(pieces, "idle",  4);
            BakeClip(pieces, "walk",  8);
            BakeClip(pieces, "run",   8);
            BakeClip(pieces, "shoot", 5);

            AssetDatabase.Refresh();
            ConfigureAll();
            Debug.Log($"[PlayerAnimationBaker] {OutFolder} 에 프레임을 구웠다");
        }

        // ---------- 1) 부위로 오리기 ----------
        private static List<Piece> Cut()
        {
            if (!File.Exists(Source)) { Debug.LogError($"원본 없음: {Source}"); return null; }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(Source));
            _w = tex.width; _h = tex.height;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);

            var label = new int[_w * _h];
            for (int i = 0; i < label.Length; i++) label[i] = -1;

            var pieces = new List<Piece>();
            var stack = new List<int>();
            var members = new List<int>();

            for (int start = 0; start < label.Length; start++)
            {
                if (label[start] != -1) continue;
                var c0 = px[start];
                if (c0.a < 128) { label[start] = -2; continue; }

                members.Clear(); stack.Clear();
                stack.Add(start); label[start] = 0;
                int mnx = _w, mxx = 0, mny = _h, mxy = 0;

                while (stack.Count > 0)
                {
                    int p = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                    members.Add(p);
                    int x = p % _w, y = p / _w;
                    if (x < mnx) mnx = x; if (x > mxx) mxx = x;
                    if (y < mny) mny = y; if (y > mxy) mxy = y;

                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                        int q = ny * _w + nx;
                        if (label[q] != -1) continue;
                        var c = px[q];
                        if (c.a < 128) { label[q] = -2; continue; }
                        if (c.r != c0.r || c.g != c0.g || c.b != c0.b) continue;
                        label[q] = 0; stack.Add(q);
                    }
                }

                if (members.Count < 40) continue;                 // 먼지는 버린다

                var piece = new Piece
                {
                    X0 = mnx, Y0 = mny,
                    W = mxx - mnx + 1, H = mxy - mny + 1,
                    Part = Classify(c0, (mnx + mxx) / 2, (mny + mxy) / 2),
                };
                piece.Px = new Color32[piece.W * piece.H];
                foreach (int p in members)
                {
                    int x = p % _w - piece.X0, y = p / _w - piece.Y0;
                    piece.Px[y * piece.W + x] = px[p];
                }
                pieces.Add(piece);
            }

            return pieces;
        }

        /// <summary>
        /// 덩어리를 부위로 나눈다. 색과 자리를 같이 본다.
        /// 다리 · 소매 · 석궁은 쓰는 색이 겹치지 않아서 색만으로도 거의 갈린다.
        /// </summary>
        private static Limb Classify(Color32 c, int cx, int cy)
        {
            int rgb = (c.r << 16) | (c.g << 8) | c.b;

            // 다리 · 부츠 커프 · 부츠
            if (rgb == 0x46423E || rgb == 0x584C40 || rgb == 0x2E2620)
                return cx < 500 ? Limb.LegL : Limb.LegR;

            // 등에 멘 석궁 (회색 #84868E 는 허리 버클에도 쓰여서 자리로 가른다)
            if (rgb == 0x785C3A || rgb == 0xB0A896) return Limb.Crossbow;
            if (rgb == 0x84868E && cx < 330) return Limb.Crossbow;

            // 소매
            if (rgb == 0x2E2C32) return cx < 500 ? Limb.ArmL : Limb.ArmR;
            // 손 (같은 살색이 목과 얼굴에도 쓰여서 높이로 가른다)
            if (rgb == 0xA0785C && cy < 800) return cx < 500 ? Limb.ArmL : Limb.ArmR;

            if (cy >= 1270) return Limb.Head;
            return Limb.Torso;
        }

        // ---------- 2) 포즈 ----------
        /// <summary>부위마다 이번 프레임에 어떻게 돌고 얼마나 움직이는지.</summary>
        private static Dictionary<Limb, Xf> PoseAt(string clip, int i, int count)
        {
            float t = (float)i / count;
            float tau = Mathf.PI * 2f * t;
            float s = Mathf.Sin(tau);

            // 정면에서 보는 캐릭터라 팔다리를 크게 돌리면 걷는 게 아니라 옆으로 휘청이는 걸로 보인다.
            // 걸음은 (1) 발을 들어올리고 (2) 앞으로 나온 다리를 살짝 크게 그리고 (3) 몸을 튕겨서 읽힌다.
            float legRot = 0f, armRot = 0f, armLift = 0f, bob = 0f, roll = 0f, head = 0f, rise = 0f;
            float liftL = 0f, liftR = 0f, scaleL = 1f, scaleR = 1f;
            float armLFixed = float.NaN, armRFixed = float.NaN, armPush = 0f;

            if (clip == "idle")
            {
                bob  = s * 5f;
                head = Mathf.Sin(tau + 0.7f) * 3f;
                armLift = s * 3f;
                armRot = s * 1.2f;
            }
            else if (clip == "walk")
            {
                legRot = s * 4.5f;
                liftL = Mathf.Max(0f, s) * 26f;          // 뒤로 빠진 다리를 들어 올린다
                liftR = Mathf.Max(0f, -s) * 26f;
                scaleL = 1f + s * 0.022f;                // 앞으로 나온 다리가 조금 커 보인다
                scaleR = 1f - s * 0.022f;
                bob  = -Mathf.Abs(s) * 9f + 4.5f;        // 한 걸음마다 한 번 튄다
                roll = s * 1.2f;
                armRot = -s * 5f;
                armLift = -s * 7f;
                head = bob * 0.45f;
            }
            else if (clip == "run")
            {
                legRot = s * 7f;
                liftL = Mathf.Max(0f, s) * 48f;
                liftR = Mathf.Max(0f, -s) * 48f;
                scaleL = 1f + s * 0.035f;
                scaleR = 1f - s * 0.035f;
                bob  = -Mathf.Abs(s) * 16f + 8f;
                roll = s * 2.2f;
                armRot = -s * 8f;
                armLift = -s * 11f;
                head = bob * 0.5f - 7f;                  // 고개를 조금 숙이고 달린다
                rise = 9f;
            }
            else // shoot - 양팔을 가슴 앞으로 들어 석궁을 겨눈다
            {
                float[] aim  = { 45f, 34f, 39f, 43f, 45f };
                float[] push = { 140f, 120f, 130f, 137f, 140f };
                float[] kick = { 0f, 13f, 7f, 2f, 0f };
                armLFixed =  aim[i];
                armRFixed = -aim[i];
                armPush = push[i];
                bob  = kick[i] * 0.5f;
                head = -kick[i] * 0.4f;
            }

            var body = new Vector2(0f, bob + rise);
            var map = new Dictionary<Limb, Xf>();

            map[Limb.Torso]    = new Xf(roll, Waist, body);
            map[Limb.Crossbow] = new Xf(roll, Waist, body);              // 등에 메고 있으니 몸통을 따른다
            map[Limb.Head]     = new Xf(roll * 0.5f, Neck, new Vector2(0f, bob + rise + head));

            float aL = float.IsNaN(armLFixed) ? armRot : armLFixed;
            float aR = float.IsNaN(armRFixed) ? -armRot : armRFixed;
            var armMove = new Vector2(0f, bob + rise + armPush);
            map[Limb.ArmL] = new Xf(aL, ShoulderL, armMove + new Vector2(0f, armLift));
            map[Limb.ArmR] = new Xf(aR, ShoulderR, armMove - new Vector2(0f, armLift));

            // 디딘 발은 바닥에 붙어 있어야 한다. 다리는 몸통 바운스를 따라가지 않는다
            map[Limb.LegL] = new Xf(legRot,  HipL, new Vector2(0f, rise + liftL), scaleL);
            map[Limb.LegR] = new Xf(-legRot, HipR, new Vector2(0f, rise + liftR), scaleR);
            return map;
        }

        // ---------- 3) 굽기 ----------
        // 평소엔 흉갑이 소매 윗부분을 덮는다
        private static readonly Limb[] DrawOrder =
        {
            Limb.Crossbow, Limb.ArmL, Limb.ArmR, Limb.LegL, Limb.LegR, Limb.Torso, Limb.Head,
        };

        // 겨눌 땐 팔을 가슴 앞으로 가져오므로 몸통보다 뒤에 그리면 손이 통째로 가려진다
        private static readonly Limb[] DrawOrderArmsFront =
        {
            Limb.Crossbow, Limb.LegL, Limb.LegR, Limb.Torso, Limb.Head, Limb.ArmL, Limb.ArmR,
        };

        private static void BakeClip(List<Piece> pieces, string clip, int frames)
        {
            for (int f = 0; f < frames; f++)
            {
                var pose = PoseAt(clip, f, frames);
                var canvas = new Color[_w * _h];
                var order = clip == "shoot" ? DrawOrderArmsFront : DrawOrder;

                foreach (var limb in order)
                foreach (var piece in pieces)
                {
                    if (piece.Part != limb) continue;
                    Stamp(canvas, piece, pose[limb]);
                }

                WriteDownscaled($"{OutFolder}/{clip}_{f}.png", canvas);
            }
        }

        /// <summary>조각 하나를 회전·이동시켜 캔버스에 찍는다. 목적지에서 원본을 역으로 훑는다.</summary>
        private static void Stamp(Color[] canvas, Piece p, Xf xf)
        {
            float rad = xf.Deg * Mathf.Deg2Rad;
            float sc = Mathf.Max(0.01f, xf.Scale);
            float cos = Mathf.Cos(rad) * sc, sin = Mathf.Sin(rad) * sc;
            // 역변환에 쓸 값 (스케일을 되돌려야 한다)
            float icos = Mathf.Cos(rad) / sc, isin = Mathf.Sin(rad) / sc;

            // 네 모서리를 보내보고 훑을 범위를 잡는다
            float mnx = float.MaxValue, mxx = float.MinValue, mny = float.MaxValue, mxy = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float sx = p.X0 + ((k & 1) == 0 ? 0 : p.W);
                float sy = p.Y0 + ((k & 2) == 0 ? 0 : p.H);
                float dx = sx - xf.Pivot.x, dy = sy - xf.Pivot.y;
                float tx = xf.Pivot.x + dx * cos - dy * sin + xf.Move.x;
                float ty = xf.Pivot.y + dx * sin + dy * cos + xf.Move.y;
                if (tx < mnx) mnx = tx; if (tx > mxx) mxx = tx;
                if (ty < mny) mny = ty; if (ty > mxy) mxy = ty;
            }

            int x0 = Mathf.Max(0, Mathf.FloorToInt(mnx) - 1);
            int x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(mxx) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(mny) - 1);
            int y1 = Mathf.Min(_h - 1, Mathf.CeilToInt(mxy) + 1);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                // 목적지 → 원본
                float dx = x + 0.5f - xf.Pivot.x - xf.Move.x;
                float dy = y + 0.5f - xf.Pivot.y - xf.Move.y;
                float sx = xf.Pivot.x + dx * icos + dy * isin - p.X0 - 0.5f;
                float sy = xf.Pivot.y - dx * isin + dy * icos - p.Y0 - 0.5f;

                var c = Sample(p, sx, sy);
                if (c.a <= 0.003f) continue;

                var under = canvas[y * _w + x];
                float a = c.a;
                canvas[y * _w + x] = new Color(
                    c.r * a + under.r * (1f - a),
                    c.g * a + under.g * (1f - a),
                    c.b * a + under.b * (1f - a),
                    a + under.a * (1f - a));
            }
        }

        /// <summary>조각에서 겹선형 보간으로 한 점을 읽는다. 돌린 가장자리가 톱니로 남지 않게.</summary>
        private static Color Sample(Piece p, float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;

            float r = 0f, g = 0f, b = 0f, a = 0f;
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                int xx = xi + i, yy = yi + j;
                if (xx < 0 || yy < 0 || xx >= p.W || yy >= p.H) continue;
                var c = p.Px[yy * p.W + xx];
                if (c.a == 0) continue;
                float wgt = (i == 0 ? 1f - fx : fx) * (j == 0 ? 1f - fy : fy);
                float ca = c.a / 255f;
                r += c.r / 255f * ca * wgt; g += c.g / 255f * ca * wgt; b += c.b / 255f * ca * wgt;
                a += ca * wgt;
            }
            if (a <= 0.0001f) return new Color(0, 0, 0, 0);
            return new Color(r / a, g / a, b / a, a);   // 알파를 곱해 뒀다가 되돌린다 (가장자리 검은 테 방지)
        }

        private static void WriteDownscaled(string path, Color[] canvas)
        {
            int ow = _w / Down, oh = _h / Down;
            var outPx = new Color[ow * oh];
            float inv = 1f / (Down * Down);

            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int j = 0; j < Down; j++)
                for (int i = 0; i < Down; i++)
                {
                    var c = canvas[(y * Down + j) * _w + (x * Down + i)];
                    r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                }
                if (a <= 0.0001f) { outPx[y * ow + x] = new Color(0, 0, 0, 0); continue; }
                outPx[y * ow + x] = new Color(r / a, g / a, b / a, a * inv);
            }

            var tex = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
            tex.SetPixels(outPx); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ---------- 4) 임포터 ----------
        private static void ConfigureAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { OutFolder }))
                Configure(AssetDatabase.GUIDToAssetPath(guid));
            AssetDatabase.Refresh();
        }

        private static void Configure(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            // 원본이 PPU 1600 이었고 절반으로 줄였으니 800 이어야 월드 크기가 그대로다
            imp.spritePixelsPerUnit = 1600f / Down;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.maxTextureSize = 2048;

            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Center;    // 원본과 같은 정렬이라야 자리가 안 밀린다
            s.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(s);

            imp.SaveAndReimport();
        }
    }
}
