using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 마을을 도트로 다시 입힌다 (2026-10-29). TownSceneBuilder 가 구운 뒤 부른다.
    /// 판정(가게 문 앞 상호작용 · 레이드 지역 · 둘레 벽)은 그대로 두고 보이는 것만 바꾼다.
    ///   바닥 - 숲 바닥 + 광장은 자갈, 길은 흙 (가로 길은 바퀴 자국이 길을 따라가게 돌린 그림)
    ///   가게 - GPT 도트 건물 (문이 가운데). 건물 그림 크기에 맞춰 막는 콜라이더를 다시 잡고 문 앞에 상호작용을 옮긴다
    ///   둘레 - 도트 가문비 · 덤불, 광장 소품 (장터 · 우물 · 게시판 · 화로), 레이드 쪽 출구 문
    /// 카메라는 레이드와 같은 정수 배율 (1080p 에서 38px/m × 2).
    /// </summary>
    public static class PixelTownDresser
    {
        private static System.Random _rng;
        private static Sprite P(string n) => PixelArtSetup.S(n);
        private static float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        [MenuItem("Capstone/씬 굽기/마을 도트 입히기 (현재 씬)")]
        public static void Dress()
        {
            var world = GameObject.Find("World");
            if (world == null || P("town_smithy") == null) { Debug.LogWarning("[PixelTownDresser] 마을 World 또는 도트 건물 그림이 없다"); return; }
            _rng = new System.Random(1029);

            // --- 예전 그림 걷기 (콜라이더 · 트리거는 남긴다)
            var remove = new List<GameObject>();
            foreach (Transform t in world.transform)
                if (t.name == "Ground" || t.name == "Paths" || t.name == "Border" || t.name == "Clearing" || t.name.StartsWith("SpawnLabel") || t.name.StartsWith("RaidLabel")
                    || t.GetComponent<SpriteMask>() != null) remove.Add(t.gameObject);
            foreach (var g in remove) Object.DestroyImmediate(g);
            var spawn = world.transform.Find("SpawnPoint"); var raid = world.transform.Find("RaidZone");
            if (spawn) spawn.GetComponent<SpriteRenderer>().enabled = false;
            if (raid) raid.GetComponent<SpriteRenderer>().enabled = false;

            // 광장 크기 - 둘레 벽에서 읽는다
            Rect plaza = Bounds(world.transform, "Fence_");

            var root = new GameObject("PixelTown").transform;
            root.SetParent(world.transform, false);

            // --- 바닥
            Tile("Ground", P("tile_forest"), plaza.center, -60, plaza.size + Vector2.one * 40f, root);
            Rect square = Rect.MinMaxRect(plaza.xMin + 4f, plaza.center.y - 5f, plaza.xMax - 4f, plaza.center.y + 1.6f);
            // 자갈 · 흙길은 사각 타일로 깔면 칼로 자른 듯 보인다 - 가장자리를 도트로 깎은 한 장으로 굽는다 (아래 GroundBake)
            var paths = new List<Rect>();

            // --- 가게
            foreach (var (name, sprite) in new[] { ("대장간", "town_smithy"), ("잡화점", "town_general"), ("의료소", "town_infirmary") })
            {
                var shop = world.transform.Find(name);
                if (shop == null || P(sprite) == null) continue;
                var body = shop.Find("Body");
                var entrance = shop.Find("Entrance");
                Rect old = body ? new Rect((Vector2)body.position - (Vector2)body.localScale * 0.5f, body.localScale) : new Rect();
                for (int i = shop.childCount - 1; i >= 0; i--)
                {
                    var c = shop.GetChild(i);
                    if (c.name != "Body" && c.name != "Entrance") Object.DestroyImmediate(c.gameObject);
                }
                Vector2 doorBase = new(old.center.x, old.yMin);
                var go = Spr(name + "_Art", P(sprite), doorBase, 0, Color.white, shop);
                Pivot(go);
                var size = go.GetComponent<SpriteRenderer>().sprite.bounds.size;
                // 막는 건 건물 그림의 아래 절반 (지붕 뒤로는 못 지나간다 - 광장 북쪽 끝이다)
                if (body)
                {
                    body.position = doorBase + new Vector2(0f, size.y * 0.3f);
                    body.localScale = new Vector3(size.x * 0.92f, size.y * 0.6f, 1f);
                }
                if (entrance) entrance.position = doorBase + Vector2.down * 1.0f;
                // 문 앞 흙 - 문턱 아래부터 광장까지, 문 앞은 밟혀서 조금 넓다
                paths.Add(Rect.MinMaxRect(doorBase.x - 1.4f, square.yMax - 0.6f, doorBase.x + 1.4f, doorBase.y + 0.3f));
            }

            // --- 스폰 → 광장, 광장 → 레이드 출구 흙길
            if (spawn) paths.Add(Rect.MinMaxRect(spawn.position.x - 1.5f, spawn.position.y - 2f, spawn.position.x + 1.5f, square.yMin + 0.6f));
            if (raid)
            {
                Vector2 rp = raid.position;
                paths.Add(Rect.MinMaxRect(rp.x - 1.6f, rp.y - 1.5f, rp.x + 1.6f, square.yMin + 0.6f));
                var gate = Spr("ExitGate", P("town_gate"), rp + Vector2.up * 1.6f, 0, Color.white, root);
                if (gate.GetComponent<SpriteRenderer>().sprite != null) { Pivot(gate); AddLight(gate, new Vector2(-1.4f, 2.2f)); AddLight(gate, new Vector2(1.4f, 2.2f)); }
            }

            BakeGround(square, paths, root);

            // --- 광장 소품
            Prop("town_prop_0", new Vector2(square.xMin + 3f, square.yMin + 1.2f), root, true);   // 장터
            Prop("town_prop_1", new Vector2(square.center.x - 7f, square.center.y - 0.6f), root, true); // 우물
            Prop("town_prop_2", new Vector2(square.center.x + 6f, square.yMin + 0.8f), root, true);  // 게시판
            Prop("town_prop_3", new Vector2(square.xMax - 0.6f, square.yMin + 1f), root, true);  // 건초 (출구 문 옆으로 비킨다)

            // 가게 앞 살림 - 가게가 무엇을 파는지 문 옆에서 먼저 보이게 (문 앞 흙길 ±1.4m 는 비운다)
            float front = square.yMax + 1.4f;
            foreach (var (sprite, dx) in new[] { ("prop_barrel", -2.6f), ("prop_wheel", 2.4f), ("prop_rack", 3.6f) })        // 대장간
                Prop(sprite, new Vector2(ShopX(world, "대장간") + dx, front), root, true);
            foreach (var (sprite, dx) in new[] { ("prop_crates", -2.5f), ("prop_sack", -3.5f), ("prop_barrel", 2.6f) })       // 잡화점
                Prop(sprite, new Vector2(ShopX(world, "잡화점") + dx, front), root, true);
            foreach (var (sprite, dx) in new[] { ("prop_apothecary", -2.5f), ("prop_chest", 2.5f) })                         // 의료소
                Prop(sprite, new Vector2(ShopX(world, "의료소") + dx, front), root, true);

            // 광장 가로등 - 가게 사이와 남쪽 모서리. 마을은 레이드보다 밝고 따뜻해야 쉬는 곳으로 읽힌다
            foreach (var p in new[] { new Vector2(-8.4f, square.yMax + 0.6f), new Vector2(8.8f, square.yMax + 0.6f),
                                      new Vector2(square.xMin - 0.8f, square.yMin - 0.4f), new Vector2(-9.5f, square.yMin - 0.4f) })
            {
                var lamp = Prop("lamppost_lit", p, root, true);
                if (lamp) { lamp.GetComponent<BoxCollider2D>().size = new Vector2(0.5f, 0.4f); lamp.GetComponent<BoxCollider2D>().offset = new Vector2(0f, 0.2f); AddLight(lamp, new Vector2(0f, 3.7f)); }
            }
            Prop("prop_cart", new Vector2(10.6f, square.center.y - 0.4f), root, true);
            Prop("prop_crates", new Vector2(11.9f, square.center.y + 0.3f), root, true);
            Prop("prop_tent", new Vector2(square.xMin + 0.4f, square.yMax - 2.4f), root, true);   // 장터 옆 천막
            Prop("town_prop_4", spawn ? (Vector2)spawn.position + new Vector2(2.6f, 1.6f) : square.center, root, false); // 이정표
            var brazier = Prop("town_prop_5", new Vector2(square.center.x, square.center.y - 1.2f), root, true);        // 화로
            if (brazier) AddLight(brazier, new Vector2(0f, 1.1f));

            // --- 둘레 나무 · 덤불
            var trees = new[] { P("tree_spruce_big"), P("tree_spruce_small"), P("tree_dead") };
            var bushes = new[] { P("bush_fern_a"), P("bush_leaf_a"), P("bush_leaf_b"), P("bush_fern_b"), P("bush_grass") };
            var border = new GameObject("Border").transform; border.SetParent(root, false);
            for (float x = plaza.xMin - 10f; x <= plaza.xMax + 10f; x += 2.8f)
            for (float y = plaza.yMin - 10f; y <= plaza.yMax + 12f; y += 2.6f)
            {
                var p = new Vector2(x + R(-1f, 1f), y + R(-1f, 1f));
                float inset = Mathf.Min(p.x - plaza.xMin, plaza.xMax - p.x, p.y - plaza.yMin, plaza.yMax - p.y);
                if (inset > 1.2f) continue;
                if (raid && Vector2.Distance(p, raid.position) < 5f) continue;
                var sp = inset > -2f && _rng.NextDouble() < 0.5 ? bushes[_rng.Next(bushes.Length)] : trees[_rng.NextDouble() < 0.5 ? 1 : (_rng.NextDouble() < 0.8 ? 0 : 2)];
                if (sp == null) continue;
                var g = Spr("Plant", sp, p, 0, Shade(), border); Pivot(g); g.GetComponent<SpriteRenderer>().flipX = _rng.NextDouble() < 0.5;
            }
            // 광장 안 풀
            for (int i = 0; i < 60; i++)
            {
                var p = new Vector2(R(plaza.xMin + 1f, plaza.xMax - 1f), R(plaza.yMin + 1f, plaza.yMax - 1f));
                if (square.Contains(p) || (spawn && Vector2.Distance(p, spawn.position) < 3.5f)) continue;
                var sp = bushes[_rng.Next(bushes.Length)]; if (sp == null) continue;
                var g = Spr("Grass", sp, p, 0, Shade(), border); Pivot(g); g.GetComponent<SpriteRenderer>().flipX = _rng.NextDouble() < 0.5;
            }

            // --- 카메라 - 정수 배율
            var cam = Camera.main;
            if (cam) cam.orthographicSize = 1080f / 2f / (38f * 2f);

            // 글꼴 - 레이드 HUD 와 같은 Galmuri11 Bold, 12 의 정수배
            PixelFontSetup.ApplyToOpenScene();
            SkinTownUI();
            ApplyDusk();
            Debug.Log("[PixelTownDresser] 마을을 도트로 입혔다");
        }

        /// <summary>
        /// 마을 저녁 조명 (2026-10-31, 교수님 피드백 - 공포 톤). 대낮처럼 평평하면 레이드에서 돌아왔을 때 분위기가 끊긴다.
        /// 전역광은 차갑고 어둡게, 가로등 · 화로 · 출구 문 불빛만 따뜻하게 - 불빛 둘레만 색이 산다.
        /// </summary>
        [MenuItem("Capstone/씬 굽기/마을 저녁 조명 (현재 씬)")]
        public static void ApplyDusk()
        {
            foreach (var l in Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.GetComponentInParent<Player.VisionController>(true) != null) continue;   // 플레이어 등불은 건드리지 않는다
                if (l.lightType == Light2D.LightType.Global) { l.intensity = 0.58f; l.color = new Color(0.62f, 0.68f, 0.86f); }
                else if (l.lightType == Light2D.LightType.Point) LightLook(l);
                EditorUtility.SetDirty(l);
            }

            var volGo = GameObject.Find("PostFX") ?? new GameObject("PostFX");
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            const string profPath = "Assets/_Game/Settings/TownDusk.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (prof == null)
            {
                System.IO.Directory.CreateDirectory("Assets/_Game/Settings");
                prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, profPath);
            }
            prof.components.RemoveAll(c => c == null);
            var bloom = prof.TryGet<Bloom>(out var b) ? b : prof.Add<Bloom>(true);
            bloom.threshold.Override(0.75f); bloom.intensity.Override(1.1f); bloom.scatter.Override(0.7f);
            var vig = prof.TryGet<Vignette>(out var v) ? v : prof.Add<Vignette>(true);
            vig.intensity.Override(0.32f); vig.smoothness.Override(0.5f); vig.color.Override(new Color(0.02f, 0.02f, 0.05f));
            var ca = prof.TryGet<ColorAdjustments>(out var c2) ? c2 : prof.Add<ColorAdjustments>(true);
            ca.contrast.Override(16f); ca.saturation.Override(-12f); ca.postExposure.Override(Feedback1110Setup.TownExposure);   // 2026-11-10 밝기 +50% ca.colorFilter.Override(Color.white);
            // 레이드(PlagueNight)와 같은 결 - 그늘은 차갑게, 불빛은 따뜻하게
            var smh = prof.TryGet<ShadowsMidtonesHighlights>(out var sm) ? sm : prof.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.85f, 0.92f, 1.15f, -0.05f)); smh.highlights.Override(new Vector4(1.1f, 1.02f, 0.9f, 0.05f));
            foreach (var comp in prof.components)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(comp)))
                {
                    comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(comp, prof);
                }
            }
            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            vol.sharedProfile = prof;

            var cam = Camera.main;
            if (cam)
            {
                var camData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
                EditorUtility.SetDirty(camData);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(volGo.scene);
        }

        private static void LightLook(Light2D l)
        {
            l.color = new Color(1f, 0.6f, 0.3f); l.intensity = 1.3f;
            l.pointLightInnerRadius = 0.6f; l.pointLightOuterRadius = 5.5f; l.falloffIntensity = 0.6f;
        }

        private static Rect Bounds(Transform world, string prefix)
        {
            bool any = false; Rect r = default;
            foreach (Transform t in world)
            {
                if (!t.name.StartsWith(prefix)) continue;
                // 새 씬에서 막 만든 콜라이더는 물리가 아직 안 돌아 bounds 가 0 이다 - 위치 · 크기에서 바로 잰다
                Vector2 c = t.position, h = (Vector2)t.lossyScale * 0.5f;
                var rr = Rect.MinMaxRect(c.x - h.x, c.y - h.y, c.x + h.x, c.y + h.y);
                r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, rr.xMin), Mathf.Min(r.yMin, rr.yMin), Mathf.Max(r.xMax, rr.xMax), Mathf.Max(r.yMax, rr.yMax)) : rr;
                any = true;
            }
            if (!any) return new Rect(-26, -13, 52, 26);
            return Rect.MinMaxRect(r.xMin + 0.6f, r.yMin + 0.6f, r.xMax - 0.6f, r.yMax - 0.6f);
        }

        /// <summary>
        /// 가게 · 레이드 확인 창 · 툴팁을 레이드 가방 화면과 같은 도트 틀로 (리벳 모서리만, 가시 장식 없음).
        /// 창은 ui_window, 버튼 · 판매대는 ui_slot. 색은 원래 색을 살짝 남겨 '예' 같은 강조 버튼이 구분되게 한다.
        /// </summary>
        private static void SkinTownUI()
        {
            var window = Resources.Load<Sprite>("PixelUI/ui_window");
            var slot = Resources.Load<Sprite>("PixelUI/ui_slot");
            if (window == null || slot == null) return;
            foreach (var img in Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (img.sprite != null) continue;    // 이미 그림이 있는 건 (격자 칸 · 아이콘) 건드리지 않는다
                string n = img.name;
                bool isWindow = n == "TradeWindow" || n == "RaidPopup" || n == "ItemTooltip";
                bool isButton = img.GetComponent<UnityEngine.UI.Button>() != null || n == "SellZone";
                if (!isWindow && !isButton) continue;
                var old = img.color;
                // 단색 바탕을 그대로 곱하면 틀이 까맣게 묻힌다 - 원래 색 쪽으로 조금만 물들인다
                var tint = Color.Lerp(Color.white, new Color(old.r * 4f, old.g * 4f, old.b * 4f), 0.35f);
                tint.a = 1f;
                UI.PixelSkin.Apply(img, isWindow ? window : slot, tint, 2f);
                // 큰 창은 가운데 나무결을 늘리면 도트가 뭉개진다 - 테두리는 그대로, 가운데는 반복해서 깐다
                if (isWindow) img.type = UnityEngine.UI.Image.Type.Tiled;
                EditorUtility.SetDirty(img);
            }
            GoldPlate(window);
        }

        /// <summary>소지금 뒤에 판자를 깐다 - 맨 글씨는 오른쪽 위 나무상자 그림과 겹쳐 묻혔다.</summary>
        private static void GoldPlate(Sprite window)
        {
            var hud = Object.FindFirstObjectByType<Town.TownHUD>(FindObjectsInactive.Include);
            if (hud == null) return;
            var so = new SerializedObject(hud);
            var gold = so.FindProperty("goldText").objectReferenceValue as TMPro.TMP_Text;
            if (gold == null) return;
            var parent = gold.rectTransform.parent;
            var plateT = parent.Find("GoldPlate");
            var plate = plateT ? (RectTransform)plateT : new GameObject("GoldPlate", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
            plate.SetParent(parent, false);
            plate.SetSiblingIndex(gold.rectTransform.GetSiblingIndex());   // 글자 바로 뒤
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(1f, 1f);
            // 글자 상자(오른쪽 -40, 위 -30, 높이 60)를 가운데에 두고 둘레 16
            plate.anchoredPosition = new Vector2(-16f, -26f);
            plate.sizeDelta = new Vector2(160f, 68f);
            var img = plate.GetComponent<UnityEngine.UI.Image>();
            UI.PixelSkin.Apply(img, window, Color.white, 2f);
            img.raycastTarget = false;
            so.FindProperty("goldPlate").objectReferenceValue = plate;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(img);
        }

        private static float ShopX(GameObject world, string name)
        {
            var body = world.transform.Find(name + "/Body");
            return body ? body.position.x : 0f;
        }

        // ---------- 바닥 굽기 ----------
        private const float Ppu = 38f;
        private const string GroundPath = "Assets/_Game/Art/Pixel/Generated/town_ground.png";

        /// <summary>
        /// 자갈 광장과 흙길을 한 장의 도트 그림으로 굽는다.
        /// 경계는 두 겹 잡음으로 흔들고 2px 단위로 끊어 도트답게 들쭉날쭉하게, 자갈 둘레엔 닳은 흙 띠를 두르고
        /// 바깥 한 줄은 어둡게 눌러 풀숲과 갈린다. 풀(숲 바닥)은 투명으로 남겨 아래 타일이 비친다.
        /// </summary>
        private static void BakeGround(Rect square, List<Rect> paths, Transform root)
        {
            var cobble = Readable(P("tile_cobble") ?? P("tile_stone"));
            var dirt = Readable(P("tile_dirt"));
            if (cobble == null || dirt == null) return;

            Rect area = square;
            foreach (var r in paths) area = Rect.MinMaxRect(Mathf.Min(area.xMin, r.xMin), Mathf.Min(area.yMin, r.yMin), Mathf.Max(area.xMax, r.xMax), Mathf.Max(area.yMax, r.yMax));
            int ox = Mathf.FloorToInt((area.xMin - 1.5f) * Ppu), oy = Mathf.FloorToInt((area.yMin - 1.5f) * Ppu);
            int w = Mathf.CeilToInt((area.xMax + 1.5f) * Ppu) - ox, h = Mathf.CeilToInt((area.yMax + 1.5f) * Ppu) - oy;

            // 0 풀 · 1 흙 · 2 자갈
            var kind = new byte[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 2px 덩어리로 잡음을 읽어야 경계가 한 픽셀씩 지글거리지 않는다
                float wx = (ox + (x & ~1) + 1f) / Ppu, wy = (oy + (y & ~1) + 1f) / Ppu;
                float jitter = Noise(wx * 1.1f, wy * 1.1f) * 0.38f + Noise(wx * 3.7f + 17f, wy * 3.7f) * 0.14f;
                float dSquare = RoundBox(square, 1.4f, wx, wy) + jitter;
                byte k = 0;
                if (dSquare < 0f) k = 2;   // 닳은 자리는 흙 덩어리로 파내지 않고 아래에서 색만 섞는다 (덩어리가 얼룩처럼 떠 보였다)
                else if (dSquare < 0.3f + 0.25f * Noise(wx * 2f + 9f, wy * 2f)) k = 1;                // 자갈 둘레 흙 띠
                else
                {
                    // 길은 옆선이 더 구불어야 밟아 낸 길로 보인다 - 길 방향으로 늘린 잡음을 하나 더 얹는다
                    float wobble = Noise(wx * 0.9f + 70f, wy * 0.35f) * 0.45f;
                    foreach (var r in paths) if (RoundBox(r, 0.9f, wx, wy) + jitter * 0.8f + wobble < 0f) { k = 1; break; }
                }
                kind[y * w + x] = k;
            }

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte k = kind[y * w + x];
                if (k == 0) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                int gx = ox + x, gy = oy + y;
                var src = k == 2 ? cobble : dirt;
                Color c = src.GetPixel(Mod(gx, src.width), Mod(gy, src.height));
                // 바깥 테두리 - 자기보다 '낮은' 땅과 닿은 한 줄은 어둡게
                bool edge = (x > 0 && kind[y * w + x - 1] < k) || (x < w - 1 && kind[y * w + x + 1] < k)
                         || (y > 0 && kind[(y - 1) * w + x] < k) || (y < h - 1 && kind[(y + 1) * w + x] < k);
                if (edge) c *= k == 2 ? 0.7f : 0.8f;
                else if (k == 2)
                {
                    // 광장 안 닳은 자리 - 테두리 없이 흙빛을 옅게 스며들게 해서 바닥 결만 바꾼다
                    float wx = (gx & ~3) / Ppu, wy = (gy & ~3) / Ppu;
                    float wear = Mathf.Clamp01((Noise(wx * 0.6f + 40f, wy * 0.6f) - 0.35f) * 1.6f);
                    float t = wear > 0.6f ? 0.4f : wear > 0.2f ? 0.2f : 0f;   // 두 단계로만 - 매끈한 그라데이션은 도트에서 뿌옇게 보인다
                    if (t > 0f) c = Color.Lerp(c, dirt.GetPixel(Mod(gx, dirt.width), Mod(gy, dirt.height)), t);
                }
                c.a = 1f;
                px[y * w + x] = c;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GroundPath));
            System.IO.File.WriteAllBytes(GroundPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(cobble); Object.DestroyImmediate(dirt);
            AssetDatabase.ImportAsset(GroundPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(GroundPath);
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Ppu; imp.filterMode = FilterMode.Point; imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 4096;
            var st = new TextureImporterSettings(); imp.ReadTextureSettings(st);
            st.spriteAlignment = (int)SpriteAlignment.BottomLeft; imp.SetTextureSettings(st);
            imp.SaveAndReimport();

            Spr("GroundPaint", AssetDatabase.LoadAssetAtPath<Sprite>(GroundPath), new Vector2(ox / Ppu, oy / Ppu), -58, Color.white, root);
        }

        /// <summary>바닥 한 장만 다시 굽는다 - Dress 를 다시 돌리면 가게 몸통이 또 밀리고 소품이 겹친다</summary>
        [MenuItem("Capstone/씬 굽기/마을 바닥만 다시 굽기 (현재 씬)")]
        public static void RebakeGroundInOpenScene()
        {
            var world = GameObject.Find("World");
            var root = world ? world.transform.Find("PixelTown") : null;
            if (root == null) { Debug.LogWarning("[PixelTownDresser] 마을 PixelTown 이 없다 - Town 을 열고 실행"); return; }
            Rect plaza = Bounds(world.transform, "Fence_");
            Rect square = Rect.MinMaxRect(plaza.xMin + 4f, plaza.center.y - 5f, plaza.xMax - 4f, plaza.center.y + 1.6f);
            var paths = new List<Rect>();
            foreach (var name in new[] { "대장간", "잡화점", "의료소" })
            {
                var art = world.transform.Find(name + "/" + name + "_Art");
                if (art == null) continue;
                Vector2 doorBase = art.position;
                paths.Add(Rect.MinMaxRect(doorBase.x - 1.4f, square.yMax - 0.6f, doorBase.x + 1.4f, doorBase.y + 0.3f));
            }
            var spawn = world.transform.Find("SpawnPoint"); var raid = world.transform.Find("RaidZone");
            if (spawn) paths.Add(Rect.MinMaxRect(spawn.position.x - 1.5f, spawn.position.y - 2f, spawn.position.x + 1.5f, square.yMin + 0.6f));
            if (raid) { Vector2 rp = raid.position; paths.Add(Rect.MinMaxRect(rp.x - 1.6f, rp.y - 1.5f, rp.x + 1.6f, square.yMin + 0.6f)); }
            var old = root.Find("GroundPaint");
            int index = old ? old.GetSiblingIndex() : 0;
            if (old) Object.DestroyImmediate(old.gameObject);
            BakeGround(square, paths, root);
            var fresh = root.Find("GroundPaint");
            if (fresh) fresh.SetSiblingIndex(index);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.scene);
        }

        private static Texture2D Readable(Sprite s)
        {
            if (s == null) return null;
            var t = new Texture2D(2, 2);
            t.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(s)));
            return t;
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>모서리를 반지름 rad 로 둥글린 상자까지의 부호 거리</summary>
        private static float RoundBox(Rect r, float rad, float x, float y)
        {
            rad = Mathf.Min(rad, r.width * 0.5f, r.height * 0.5f);
            return BoxDist(Rect.MinMaxRect(r.xMin + rad, r.yMin + rad, r.xMax - rad, r.yMax - rad), x, y) - rad;
        }

        /// <summary>상자까지의 부호 거리 (안쪽이 음수)</summary>
        private static float BoxDist(Rect r, float x, float y)
        {
            float dx = Mathf.Max(r.xMin - x, x - r.xMax), dy = Mathf.Max(r.yMin - y, y - r.yMax);
            return dx > 0f || dy > 0f ? new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude : Mathf.Max(dx, dy);
        }

        /// <summary>값 잡음, -1 ~ 1. 씬을 다시 구워도 같은 모양이 나오게 해시로만 만든다.</summary>
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

        private static GameObject Prop(string sprite, Vector2 p, Transform parent, bool solid)
        {
            var s = P(sprite); if (s == null) return null;
            var go = Spr(sprite, s, p, 0, Color.white, parent); Pivot(go);
            if (solid)
            {
                go.layer = LayerMask.NameToLayer(Core.GameLayers.Wall);
                var bc = go.AddComponent<BoxCollider2D>();
                bc.size = new Vector2(s.bounds.size.x * 0.8f, Mathf.Min(1f, s.bounds.size.y * 0.35f));
                bc.offset = new Vector2(0f, bc.size.y * 0.5f);
            }
            return go;
        }

        private static void AddLight(GameObject go, Vector2 local)
        {
            var lg = new GameObject("Light"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = local;
            var l = lg.AddComponent<Light2D>(); l.lightType = Light2D.LightType.Point;
            LightLook(l);
        }

        private static Color Shade() { float k = 1f + R(-0.1f, 0.1f); return new Color(k, k, k, 1f); }
        private static void Pivot(GameObject go) => go.GetComponent<SpriteRenderer>().spriteSortPoint = SpriteSortPoint.Pivot;

        private static void Tile(string name, Sprite s, Vector2 pos, int order, Vector2 size, Transform parent)
        {
            if (s == null) return;
            var go = Spr(name, s, pos, order, Color.white, parent);
            var sr = go.GetComponent<SpriteRenderer>(); sr.drawMode = SpriteDrawMode.Tiled; sr.tileMode = SpriteTileMode.Continuous; sr.size = size;
        }

        private static GameObject Spr(string name, Sprite s, Vector2 pos, int order, Color c, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = s; sr.color = c; sr.sortingOrder = order;
            return go;
        }
    }
}
