using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Capstone.Map;
using Capstone.Enemy;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 첫 번째 맵 - 역병의 시대 개선사항(2026-10-29) 시연용 (메뉴 Capstone/씬 굽기/첫 번째 맵).
    /// 기존 레이드 씬(Raid)을 복사해 Raid_Map1 을 만들고 그 안의 Map · Enemies · Loot 만 새로 굽는다.
    ///
    ///   서쪽 30% - 이교도 구역: 버려진 예배당 + 숙소. 2인 1조 세 조가 돌고, 숙소의 넷이 교대 · 증원을 맡는다
    ///   동쪽 50% - 도적 구역: 마을 집 넷 + 도적 야영지. 도적 열 명이 저마다의 구역을 돈다
    ///   가운데   - 교전 지역: 네거리 장터. 이교도 순찰로와 도적 구역이 여기서만 겹친다
    ///   북쪽 20% - 감염자 구역: 역병 묘지. 그 너머 최종 지역 길은 가시덤불로 막혀 있다
    ///   길가 등불 - 60% 확률로 빛이 담겨 있다
    /// 모든 그림은 도트(Art/Pixel). 도트는 돌리지 않는다 - 길도 벽도 가로 · 세로로만 놓는다.
    /// </summary>
    public static class FirstMapBuilder
    {
        private const string SrcScene = "Assets/_Game/Scenes/Raid.unity";
        public const string ScenePath = "Assets/_Game/Scenes/Raid_Map1.unity";
        private const string Prefabs = "Assets/_Game/Prefabs/";
        private const float PPU = 38f;
        private const float WallT = 0.8f;
        private const float DoorW = 3.6f;

        private static readonly Color Moon = new(0.38f, 0.48f, 0.86f);
        private static readonly Color LampColor = new(1f, 0.74f, 0.38f);
        private static readonly Color TorchColor = new(1f, 0.34f, 0.18f);

        private static Transform _map, _enemies, _loot;
        private static System.Random _rng;
        private static int _wall, _interact;
        private static readonly List<Rect> _blocked = new();     // 장식이 들어가면 안 되는 곳 (길 · 건물 · 묘지 길)
        private static readonly List<Rect> _roads = new();

        private static Sprite P(string n) => PixelArtSetup.S(n);
        private static float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private static bool Chance(float p) => _rng.NextDouble() < p;

        [MenuItem("Capstone/씬 굽기/첫 번째 맵 (도트)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[FirstMapBuilder] 플레이 중에는 구울 수 없다"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) AssetDatabase.CopyAsset(SrcScene, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            _rng = new System.Random(20261029);
            _wall = LayerMask.NameToLayer(Core.GameLayers.Wall);
            _interact = LayerMask.NameToLayer(Core.GameLayers.Interact);
            _blocked.Clear(); _roads.Clear();

            _map = Root("Map"); _enemies = Root("Enemies"); _loot = Root("Loot");
            Clear(_map); Clear(_enemies); Clear(_loot);
            var old = GameObject.Find("MapData"); if (old) { /* 미니맵 데이터는 아래에서 다시 굽는다 */ }

            Ground();
            Roads();
            Boundary();
            var church = Chapel();
            Village();
            Furnish();
            Camp();
            Graveyard();
            Crossroads();
            Physics2D.SyncTransforms();
            Belts();
            Groves();
            Lamps();
            HideBushes();
            Scatter();
            Physics2D.SyncTransforms();

            Extraction();
            PlaceLoot();
            Cultists(church);
            Rogues();
            Infected();
            PlacePlayer();
            BakeMapData();
            SceneSetup();
            HudSetup();

            AddToBuild();
            PointTownHere();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[FirstMapBuilder] 첫 번째 맵을 구웠다");
        }

        // ================= 바닥 · 길 =================
        private static readonly Rect MapRect = Rect.MinMaxRect(-62f, -52f, 62f, 50f);
        private static Vector2 Start => new(0f, -38f);
        private static Vector2 Exit => new(57f, -6f);

        private static void Ground()
        {
            var g = Spr("Ground", P("tile_forest"), Vector2.zero, -40, Color.white, _map);
            Tile(g, new Vector2(MapRect.width + 40f, MapRect.height + 40f));
            // 넓고 옅은 얼룩 - 한 장 반복이 덜 보이게
            var soft = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.SoftCircle);
            var bl = Group("Blotches", _map);
            for (int i = 0; i < 90; i++)
            {
                var b = Spr("Blotch", soft, new Vector2(R(-70, 70), R(-60, 58)), -39,
                    Chance(0.7f) ? new Color(0.02f, 0.03f, 0.02f, R(0.25f, 0.45f)) : new Color(0.35f, 0.33f, 0.22f, R(0.08f, 0.16f)), bl);
                b.transform.localScale = new Vector3(R(6, 16), R(5, 12), 1);
            }
        }

        private static void Road(string name, float x0, float y0, float x1, float y1, float w = 4f)
        {
            Rect r = x0 == x1 ? Rect.MinMaxRect(x0 - w / 2, Mathf.Min(y0, y1) - w / 2, x0 + w / 2, Mathf.Max(y0, y1) + w / 2)
                              : Rect.MinMaxRect(Mathf.Min(x0, x1) - w / 2, y0 - w / 2, Mathf.Max(x0, x1) + w / 2, y0 + w / 2);
            _roads.Add(r); _blocked.Add(r);
            // 곧은 타일 상자 대신 Roads() 끝에서 한꺼번에 구불구불한 도트로 굽는다 (GroundPainter)
            _roadPaths.Add(new GroundPainter.Path(name, r, P(x0 == x1 || P("tile_dirt_h") == null ? "tile_dirt" : "tile_dirt_h")));
        }

        private static readonly List<GroundPainter.Path> _roadPaths = new();

        /// <summary>열린 맵 씬의 흙길만 다시 굽는다 (맵 전체를 다시 굽지 않고).</summary>
        [MenuItem("Capstone/씬 굽기/첫 번째 맵 - 흙길만 다시 굽기")]
        public static void RepaintRoadsInOpenScene()
        {
            var map = GameObject.Find("Map");
            if (map == null) { Debug.LogWarning("Map 이 없다 - Raid_Map1 을 열고 실행"); return; }
            _map = map.transform;
            var old = _map.Find("Roads");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            _roads.Clear(); _roadPaths.Clear(); _blocked.Clear();
            RoadList();
            GroundPainter.PaintRoads(_roadPaths, Group("Roads", _map, true), -38);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(map.scene);
        }

        private static void RoadList()
        {
            Road("Main", 0, -50, 0, 44.5f);         // 시작 → 네거리 → 묘지
            Road("West", -34, 2, 0, 2);          // 네거리 → 예배당
            Road("East", 0, -6, 60, -6);         // 네거리 아래 → 마을 → 탈출
            Road("Camp", 36, -6, 36, 22);        // 마을 → 야영지
            Road("VillageN", 16, -6, 16, 8, 3f); // 마을 북쪽 집
        }

        private static void Roads()
        {
            _roadPaths.Clear();
            RoadList();
            GroundPainter.PaintRoads(_roadPaths, Group("Roads", _map, true), -38);
            // 길 가장자리 - 곧은 흙 경계를 풀 · 고사리로 덮어 끊는다
            var edge = Group("RoadEdge", _map);
            var tufts = new[] { P("bush_grass"), P("bush_fern_a"), P("bush_fern_b") };
            foreach (var r in _roads)
            {
                bool vertical = r.height > r.width;
                float len = vertical ? r.height : r.width;
                foreach (float side in new[] { -1f, 1f })
                for (float t = R(0f, 2f); t < len; t += R(1.4f, 4.2f))
                    {
                        if (!Chance(0.6f)) continue;
                        float off = R(-0.5f, 1.4f);
                        Vector2 p = vertical ? new Vector2(r.center.x + side * (r.width / 2 + off), r.yMin + t)
                                             : new Vector2(r.xMin + t, r.center.y + side * (r.height / 2 + off) - 0.3f);
                        if (InsideAnyRoad(p, 0.4f)) continue;
                        var s = tufts[_rng.Next(tufts.Length)];
                        if (s == null) continue;
                        var go = Spr("Tuft", s, p, 0, Shade(0.12f), edge);
                        go.GetComponent<SpriteRenderer>().flipX = Chance(0.5f);
                        Pivot(go);
                    }
            }
        }

        private static bool InsideAnyRoad(Vector2 p, float m)
        {
            foreach (var r in _roads) if (Rect.MinMaxRect(r.xMin + m, r.yMin + m, r.xMax - m, r.yMax - m).Contains(p)) return true;
            return false;
        }

        // ================= 경계 숲 =================
        private static void Boundary()
        {
            var b = Group("Boundary", _map);
            float t = 2f;
            Invisible("N", Rect.MinMaxRect(MapRect.xMin - t, MapRect.yMax, MapRect.xMax + t, MapRect.yMax + t), b);
            Invisible("S", Rect.MinMaxRect(MapRect.xMin - t, MapRect.yMin - t, MapRect.xMax + t, MapRect.yMin), b);
            Invisible("W", Rect.MinMaxRect(MapRect.xMin - t, MapRect.yMin, MapRect.xMin, MapRect.yMax), b);
            Invisible("E", Rect.MinMaxRect(MapRect.xMax, MapRect.yMin, MapRect.xMax + t, MapRect.yMax), b);
            // 둘레를 빽빽한 가문비로 - 바깥 12m 까지 채워 맵 끝이 허공으로 안 보이게
            var trees = Group("EdgeForest", _map);
            for (float x = MapRect.xMin - 12f; x <= MapRect.xMax + 12f; x += 3.2f)
            for (float y = MapRect.yMin - 12f; y <= MapRect.yMax + 14f; y += 3.0f)
            {
                var p = new Vector2(x + R(-1.2f, 1.2f), y + R(-1.2f, 1.2f));
                float inset = Mathf.Min(p.x - MapRect.xMin, MapRect.xMax - p.x, p.y - MapRect.yMin, MapRect.yMax - p.y);
                if (inset > 3.5f) continue;
                if (Vector2.Distance(p, Exit) < 6f) continue;
                if (Mathf.Abs(p.x) < 3.5f && p.y < MapRect.yMin + 4f) continue;   // 큰길 남쪽 끝은 트여 있게
                if (Mathf.Abs(p.x) < 8.5f && p.y > MapRect.yMax - 6f && p.y < MapRect.yMax + 1.5f) continue;   // 가시덤불 문 앞은 비운다 - 문이 보여야 한다
                Tree(p, trees, inset > -4f ? 0.9f : 0.6f, false);
            }
        }

        private static void Tree(Vector2 p, Transform parent, float bigChance, bool collide)
        {
            bool big = Chance(bigChance * 0.6f);
            bool dead = !big && Chance(0.12f);
            var s = big ? P("tree_spruce_big") : dead ? P("tree_dead") : P("tree_spruce_small");
            if (s == null) return;
            var go = Spr(big ? "Spruce" : dead ? "DeadTree" : "SpruceSmall", s, p, 0, Shade(0.1f), parent);
            Pivot(go);
            go.GetComponent<SpriteRenderer>().flipX = Chance(0.5f);
            if (collide)
            {
                var c = new GameObject("Trunk");
                c.transform.SetParent(go.transform, false);
                c.layer = _wall;
                var cc = c.AddComponent<CircleCollider2D>();
                cc.radius = big ? 0.75f : 0.5f;
                cc.offset = new Vector2(0f, 0.35f);
                TrunkShadow(go, cc.radius, cc.offset.y);
            }
        }

        /// <summary>
        /// 나무 그림자는 밑동 둘레만 (2026-10-31). 그림 전체 크기 상자로 두면 위에서 내려다보는 시점에서
        /// 나무 꼭대기까지 벽처럼 빛을 막아, 등불 둘레가 곧은 직사각형 띠로 잘려 보였다.
        /// 밑동만 막으면 등불을 들고 지나갈 때 기둥 그림자가 발치에서 뻗으며 돈다.
        /// </summary>
        public static void TrunkShadow(GameObject tree, float radius, float offsetY)
        {
            var sc = tree.GetComponent<ShadowCaster2D>();
            if (sc == null) sc = tree.AddComponent<ShadowCaster2D>();
            sc.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            sc.useRendererSilhouette = false;
            var so = new SerializedObject(sc);
            var src = so.FindProperty("m_ShadowCastingSource");
            if (src != null) src.intValue = 1;                                  // ShapeEditor - 아래 모양을 그대로 쓴다
            var path = so.FindProperty("m_ShapePath");
            const int n = 8;
            path.arraySize = n;
            float rx = radius * 0.6f, ry = radius * 0.4f;                       // 위에서 본 밑동은 납작한 타원
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                path.GetArrayElementAtIndex(i).vector3Value = new Vector3(Mathf.Cos(a) * rx, offsetY + Mathf.Sin(a) * ry, 0f);
            }
            so.FindProperty("m_ShapePathHash").intValue = Random.Range(int.MinValue, int.MaxValue);   // 그림자 메시를 다시 만들게
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================= 건물 =================
        private struct Door { public char side; public float at; public Door(char s, float a) { side = s; at = a; } }

        /// <summary>네 벽 + 바닥. 문 자리는 비운다. 벽의 남쪽 면(높이)을 어둡게 내려 그려 3/4 시점처럼 보이게.</summary>
        private static Rect Building(string name, Rect r, string floor, params Door[] doors)
        {
            var root = Group(name, Group("Buildings", _map, true));
            _blocked.Add(Expand(r, 1.5f));
            var f = Spr("Floor", P(floor), r.center, -36, Color.white, root);
            Tile(f, r.size);
            Side(root, r, 'N', doors); Side(root, r, 'S', doors); Side(root, r, 'W', doors); Side(root, r, 'E', doors);
            return r;
        }

        private static void Side(Transform parent, Rect r, char side, Door[] doors)
        {
            bool h = side == 'N' || side == 'S';
            float from = h ? r.xMin - WallT / 2 : r.yMin - WallT / 2;
            float to = h ? r.xMax + WallT / 2 : r.yMax + WallT / 2;
            float fixedC = side == 'N' ? r.yMax : side == 'S' ? r.yMin : side == 'W' ? r.xMin : r.xMax;
            var gaps = new List<Vector2>();
            foreach (var d in doors) if (d.side == side) gaps.Add(new Vector2(d.at - DoorW / 2, d.at + DoorW / 2));
            gaps.Sort((a, b) => a.x.CompareTo(b.x));
            float cur = from;
            foreach (var g in gaps) { if (g.x > cur) WallSeg(parent, h, fixedC, cur, g.x); cur = Mathf.Max(cur, g.y); }
            if (to > cur) WallSeg(parent, h, fixedC, cur, to);
        }

        public static void WallSeg(Transform parent, bool horizontal, float fixedC, float a, float b)
        {
            float len = b - a;
            if (len < 0.1f) return;
            Rect rect = horizontal ? new Rect(a, fixedC - WallT / 2, len, WallT) : new Rect(fixedC - WallT / 2, a, WallT, len);
            // 충돌 · 그림자 - 기존 Wall 프리팹을 늘려 쓴다 (그림은 투명하게, 그림자 모양만 빌린다)
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Wall.prefab");
            var col = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            col.name = "WallCollider";
            col.transform.position = rect.center;
            col.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            var csr = col.GetComponent<SpriteRenderer>();
            csr.color = new Color(1, 1, 1, 0);
            if (col.GetComponent<ShadowCaster2D>() == null)
                col.AddComponent<ShadowCaster2D>().castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            // 윗면 (벽 머리)
            var top = Spr("WallTop", P("tile_wall"), rect.center + Vector2.up * 0.9f, 0, Color.white, parent);
            Tile(top, rect.size);
            var tsr = top.GetComponent<SpriteRenderer>();
            tsr.spriteSortPoint = SpriteSortPoint.Center;
            // 앞면 (남쪽으로 내려온 벽 높이 0.9m) - 위와 같은 돌을 어둡게
            var face = Spr("WallFace", P("tile_wall"), new Vector2(rect.center.x, rect.yMin + 0.45f), 0, new Color(0.8f, 0.76f, 0.72f), parent);   // 너무 어두우면 불빛이 닿아도 벽이 안 살아난다 (11-01, 0.55 → 0.8)
            Tile(face, new Vector2(rect.width, 0.9f));
            face.GetComponent<SpriteRenderer>().sortingOrder = -1;
            tsr.sortingOrder = 0;
        }

        private static Rect _chapel, _barracks;

        private static Rect Chapel()
        {
            _chapel = Rect.MinMaxRect(-54f, -8f, -34f, 12f);
            Building("Chapel", _chapel, "tile_stone", new Door('E', 2f), new Door('S', -44f), new Door('N', -40f));
            // 숙소 - 서쪽 칸막이 너머. 쉬는 이교도가 여기서 교대 · 증원을 나간다
            var root = GameObject.Find("Chapel").transform;
            WallSeg(root, false, -46f, -8f, 4f);              // 칸막이 (위쪽 4m 는 통로)
            WallSeg(root, false, -46f, 7.6f, 12f);
            _barracks = Rect.MinMaxRect(-54f, -8f, -46f, 12f);
            return _chapel;
        }

        private static readonly List<Rect> _houses = new();
        private static void Village()
        {
            _houses.Clear();
            _houses.Add(Building("House_A", Rect.MinMaxRect(9f, -22f, 21f, -12f), "tile_wood", new Door('N', 15f)));
            _houses.Add(Building("House_B", Rect.MinMaxRect(25f, -24f, 35f, -14f), "tile_wood", new Door('N', 30f)));
            _houses.Add(Building("House_C", Rect.MinMaxRect(41f, -20f, 51f, -11f), "tile_wood", new Door('N', 46f)));
            _houses.Add(Building("House_D", Rect.MinMaxRect(10f, 9f, 22f, 18f), "tile_wood", new Door('S', 16f)));
        }

        // ================= 집 안 가구 (2026-11-01) =================
        // 버려진 집 - 식탁 · 의자 · 침대 · 선반 · 꺼진 벽난로 · 항아리. 등불을 들고 들어가야 하나씩 드러나고,
        // 큰 가구는 등불 그림자를 드리워 방 안에 숨을 그늘을 만든다 (요마와리식 - 빛이 닿아야 보인다).
        // 상자(PlaceLoot) 자리는 피했다. 북쪽 벽에 붙는 것은 바닥 = 벽 안쪽 - 0.55m.

        [MenuItem("Capstone/씬 굽기/첫 번째 맵 - 집 안 가구만 다시")]
        public static void FurnishInOpenScene()
        {
            var map = GameObject.Find("Map");
            if (map == null) { Debug.LogWarning("Map 이 없다 - Raid_Map1 을 열고 실행"); return; }
            _map = map.transform;
            _wall = LayerMask.NameToLayer(Core.GameLayers.Wall);
            Furnish();
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log("[FirstMapBuilder] 집 안 가구를 다시 놓았다");
        }

        private static void Furnish()
        {
            var old = _map.Find("Furniture");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = Group("Furniture", _map);
            // 집 A (9..21, -22..-12) 문 북쪽 15
            Furn("furn_fireplace", 11.3f, -12.95f, root, true);
            Furn("furn_shelf", 19.2f, -12.95f, root, true);
            Furn("furn_table", 13.4f, -17.2f, root, true);
            Furn("furn_chair", 11.9f, -17.3f, root, false);
            Furn("furn_chair_fallen", 15.4f, -18.4f, root, false);
            Furn("furn_bed", 17.0f, -21.55f, root, true);
            Furn("furn_jars", 10.2f, -15.6f, root, false);
            // 집 B (25..35, -24..-14) 문 북쪽 30
            Furn("furn_shelf", 26.6f, -14.95f, root, true);
            Furn("furn_fireplace", 33.3f, -14.95f, root, true);
            Furn("furn_table", 29.4f, -19.6f, root, true);
            Furn("furn_chair", 31.0f, -19.7f, root, false, true);
            Furn("furn_bed", 26.6f, -23.55f, root, true);
            Furn("furn_jars", 34.2f, -18.6f, root, false);
            // 집 C (41..51, -20..-11) 문 북쪽 46
            Furn("furn_fireplace", 43.2f, -11.95f, root, true);
            Furn("furn_shelf", 49.6f, -11.95f, root, true);
            Furn("furn_table", 46.2f, -15.6f, root, true);
            Furn("furn_chair_fallen", 48.0f, -16.6f, root, false, true);
            Furn("furn_jars", 42.0f, -15.4f, root, false);
            Furn("furn_bed", 46.0f, -19.55f, root, true);
            // 집 D (10..22, 9..18) 문 남쪽 16
            Furn("furn_fireplace", 16.0f, 17.05f, root, true);
            Furn("furn_shelf", 13.6f, 17.05f, root, true);
            Furn("furn_table", 13.2f, 12.4f, root, true);
            Furn("furn_chair", 11.6f, 12.3f, root, false);
            Furn("furn_chair", 14.8f, 12.3f, root, false, true);
            Furn("furn_bed", 20.6f, 9.45f, root, true);
            Furn("furn_jars", 21.0f, 13.6f, root, false);
        }

        /// <summary>가구 하나. 발밑 피벗 · Y 정렬. 큰 것(shadow)은 발치 상자만큼 막고 등불 그림자를 드리운다.</summary>
        private static void Furn(string sprite, float x, float y, Transform parent, bool shadow, bool flip = false)
        {
            var s = P(sprite);
            if (s == null) { Debug.LogWarning("[FirstMapBuilder] 그림 없음 " + sprite); return; }
            var go = Spr(sprite, s, new Vector2(x, y), 0, Color.white, parent);
            Pivot(go);
            go.GetComponent<SpriteRenderer>().flipX = flip;
            go.layer = _wall;
            var bc = go.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(s.bounds.size.x * 0.85f, Mathf.Min(0.9f, s.bounds.size.y * 0.45f));
            bc.offset = new Vector2(0f, bc.size.y * 0.5f);
            if (!shadow) return;
            // 그림자 모양은 발치 상자 - 그림 실루엣 전체로 드리우면 가구 윗면까지 그림자에 먹힌다
            var sc = new GameObject("Shadow");
            sc.transform.SetParent(go.transform, false);
            sc.transform.localPosition = new Vector3(0f, bc.size.y * 0.5f, 0f);
            sc.transform.localScale = new Vector3(bc.size.x, bc.size.y, 1f);
            var caster = sc.AddComponent<ShadowCaster2D>();
            caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
        }

        private static Vector2 CampCenter => new(38f, 28f);
        private static void Camp()
        {
            var root = Group("BanditCamp", _map);
            _blocked.Add(Rect.MinMaxRect(30f, 21f, 47f, 36f));
            Prop("prop_campfire", CampCenter, root, true);
            // 야영지 모닥불 - 도적의 불. 붉은빛, 거둘 수 없다
            var fire = new GameObject("CampfireLight");
            fire.transform.SetParent(root, false);
            fire.transform.position = CampCenter + Vector2.up * 0.5f;
            var l = AddLight(fire, TorchColor, 1.9f, 8.5f);
            l.volumetricEnabled = true; l.volumeIntensity = 0.22f;
            var t = fire.AddComponent<EnemyTorch>();
            var tso = new SerializedObject(t);
            tso.FindProperty("embersPerSecond").floatValue = 14f;
            tso.FindProperty("drawsRogues").boolValue = false;
            tso.FindProperty("emberOffset").vector2Value = new Vector2(0f, -0.1f);
            tso.FindProperty("baseIntensity").floatValue = 1.5f;
            tso.ApplyModifiedPropertiesWithoutUndo();
            Prop("prop_tent", CampCenter + new Vector2(-5.5f, 3.5f), root, true);
            Prop("prop_tent", CampCenter + new Vector2(5f, 4f), root, true, true);
            Prop("prop_crates", CampCenter + new Vector2(6.5f, -2.5f), root, true);
            Prop("prop_barrel", CampCenter + new Vector2(-6.2f, -2f), root, true);
            Prop("prop_barrel", CampCenter + new Vector2(-5.2f, -3f), root, true);
        }

        private static Rect _graves;
        private static void Graveyard()
        {
            _graves = Rect.MinMaxRect(-15f, 28f, 15f, 45f);
            var root = Group("PlagueGraveyard", _map);
            // 낮은 돌담 (네 귀퉁이만 남은)
            WallSeg(root, true, _graves.yMin, _graves.xMin, -3f);
            WallSeg(root, true, _graves.yMin, 3f, _graves.xMax);
            WallSeg(root, false, _graves.xMin, _graves.yMin, _graves.yMin + 6f);
            WallSeg(root, false, _graves.xMax, _graves.yMin, _graves.yMin + 6f);
            // 묘비
            string[] g = { "prop_grave_a", "prop_grave_b", "prop_grave_c" };
            for (float x = _graves.xMin + 2.5f; x < _graves.xMax - 2f; x += 3.2f)
            for (float y = _graves.yMin + 3f; y < _graves.yMax - 2f; y += 3.4f)
            {
                if (Mathf.Abs(x) < 3.6f) continue;           // 가운데 길
                if (!Chance(0.85f)) continue;
                Prop(g[_rng.Next(3)], new Vector2(x + R(-0.6f, 0.6f), y + R(-0.5f, 0.5f)), root, true);
            }
            // 역병 시신 - 묻지 못하고 쌓아 둔 자리
            var dead = new[] { P("corpse_infected"), P("corpse_cultist"), P("corpse_rogue") };
            for (int i = 0; i < 6; i++)
            {
                var s = dead[i % 3];
                if (s == null) continue;
                var c = Spr("PlagueDead", s, new Vector2(-9f + R(-2.5f, 2.5f), 39.5f + R(-1.5f, 1.5f)), 0, new Color(0.7f, 0.7f, 0.72f), root);
                c.transform.localScale = Vector3.one * 2f;     // 시체 그림은 적(2배 루트)용 PPU 라 2배로 되돌린다
                Pivot(c);
                c.GetComponent<SpriteRenderer>().flipX = Chance(0.5f);
            }
            for (int i = 0; i < 4; i++) { var tp = new Vector2(R(-14, 14), R(40, 44)); if (Mathf.Abs(tp.x) > 8f) Tree(tp, root, 0f, true); }

            // 최종 지역으로 가는 길 - 가시덤불로 막혀 있다
            var gate = new GameObject("ThornGate");
            gate.transform.SetParent(root, false);
            gate.transform.position = new Vector2(0f, 46.5f);
            var wall = P("thorn_wall");
            if (wall != null)
            {
                for (int i = -2; i <= 2; i++)
                {
                    var w = Spr("ThornWall", wall, new Vector2(i * 2.9f, 45.4f + (i % 2 == 0 ? 0f : 0.35f)), 0, Color.white, gate.transform);
                    Pivot(w);
                    w.GetComponent<SpriteRenderer>().flipX = i == 0;
                }
            }
            var block = new GameObject("Block");
            block.transform.SetParent(gate.transform, false);
            block.layer = _wall;
            var bc = block.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(15f, 2f);
            bc.offset = new Vector2(0f, -0.2f);
            var hint = new GameObject("Hint");
            hint.transform.SetParent(gate.transform, false);
            hint.layer = _interact;
            var hc = hint.AddComponent<BoxCollider2D>();
            hc.isTrigger = true;
            hc.size = new Vector2(15f, 4f);
            hc.offset = new Vector2(0f, -1.5f);
            hint.AddComponent<ThornGate>();
            // 덤불 사이로 보이는 건너편 - 희미한 푸른빛 (최종 지역의 기척)
            var far = new GameObject("BeyondGlow");
            far.transform.SetParent(gate.transform, false);
            far.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            AddLight(far, new Color(0.45f, 0.6f, 1f), 0.6f, 6f, false);
        }

        private static void Crossroads()
        {
            var root = Group("Crossroads", _map);
            Prop("prop_well", new Vector2(-4.2f, -1.8f), root, true);
            Prop("prop_cart", new Vector2(4.5f, 5.5f), root, true);
            Prop("prop_wheel", new Vector2(-3.6f, 6.2f), root, false);
            Prop("prop_barrel", new Vector2(5.2f, -1.5f), root, true);
        }

        /// <summary>
        /// 구역 사이 숲 띠 (피드백 - 교전 지역을 빼면 이교도와 도적의 순찰 지역은 겹치지 않는다).
        /// 예배당 쪽과 마을 쪽 사이를 나무로 가르고 네거리만 터 둔다. 묘지 남쪽도 숲으로 둘러 감염자 구역이 따로 읽히게.
        /// </summary>
        private static void Belts()
        {
            var root = Group("ZoneBelts", _map);
            void Belt(Vector2 a, Vector2 b, float width, float step)
            {
                float len = Vector2.Distance(a, b);
                Vector2 dir = (b - a) / len, n = new(-dir.y, dir.x);
                for (float t = 0f; t <= len; t += step)
                    for (float w = -width / 2; w <= width / 2; w += 2.6f)
                    {
                        var p = a + dir * t + n * w + new Vector2(R(-1f, 1f), R(-1f, 1f));
                        if (Blocked(p, 1.2f)) continue;
                        Tree(p, root, 0.55f, true);
                    }
            }
            Belt(new Vector2(-12f, -50f), new Vector2(-12f, -9f), 7f, 2.4f);      // 서쪽(이교도) ↔ 시작 길
            Belt(new Vector2(-12f, 12f), new Vector2(-12f, 26f), 6f, 2.4f);       // 예배당 북쪽 ↔ 네거리
            Belt(new Vector2(5f, 26f), new Vector2(28f, 26f), 5f, 2.4f);          // 묘지 ↔ 마을
            Belt(new Vector2(-30f, 27f), new Vector2(-16f, 27f), 4f, 2.6f);
            Belt(new Vector2(6f, -50f), new Vector2(6f, -28f), 6f, 2.4f);         // 시작 길 ↔ 남쪽 도적 숲
        }

        private static void Groves()
        {
            // 숨을 곳 - 길과 건물 사이의 작은 숲들 (지나갈 수 있는 사이 길을 남긴다)
            var root = Group("Groves", _map);
            Vector2[] centers = { new(-24f, -24f), new(-30f, 22f), new(-22f, -38f), new(26f, -36f), new(52f, 14f), new(22f, 30f), new(-48f, 30f), new(48f, -34f), new(10f, -30f),
                                  new(-40f, -20f), new(-52f, -38f), new(-26f, 40f), new(-48f, 44f), new(28f, 40f), new(52f, 40f), new(54f, 28f), new(24f, 0f), new(50f, -46f), new(-36f, -46f) };
            foreach (var c in centers)
                for (int i = 0; i < 9; i++)
                {
                    var p = c + new Vector2(R(-5f, 5f), R(-4f, 4f));
                    if (Blocked(p, 1.5f)) continue;
                    Tree(p, root, 0.5f, true);
                }
            // 가시덤불 무더기 - 못 지나간다
            string[] th = { "thorn_a", "thorn_b", "thorn_c", "thorn_d" };
            Vector2[] thorn = { new(-20f, -12f), new(-38f, -26f), new(30f, 10f), new(-6f, 22f), new(8f, 24f), new(54f, -26f) };
            foreach (var c in thorn)
            {
                for (int i = 0; i < 3; i++)
                {
                    var p = c + new Vector2(R(-2.5f, 2.5f), R(-1.5f, 1.5f));
                    if (Blocked(p, 1f)) continue;
                    var s = P(th[_rng.Next(th.Length)]);
                    if (s == null) continue;
                    var go = Spr("Thorns", s, p, 0, Shade(0.08f), root);
                    Pivot(go);
                    go.GetComponent<SpriteRenderer>().flipX = Chance(0.5f);
                    go.layer = _wall;
                    var bc = go.AddComponent<BoxCollider2D>();
                    bc.size = new Vector2(s.bounds.size.x * 0.75f, 0.9f);
                    bc.offset = new Vector2(0f, 0.45f);
                }
            }
        }

        private static bool Blocked(Vector2 p, float m)
        {
            foreach (var r in _blocked) if (Expand(r, m).Contains(p)) return true;
            if (Vector2.Distance(p, Start) < 6f || Vector2.Distance(p, Exit) < 6f) return true;
            return !MapRect.Contains(p);
        }

        // ================= 등불 =================
        private static readonly List<LampPost> _lamps = new();
        private static void Lamps()
        {
            _lamps.Clear();
            var root = Group("LampPosts", _map);
            var spots = new List<Vector2>();
            for (float y = -40f; y <= 36f; y += 11f) spots.Add(new Vector2((((int)(y / 11f)) % 2 == 0 ? 3.4f : -3.4f), y));
            for (float x = -30f; x <= -8f; x += 11f) spots.Add(new Vector2(x, 5.2f));
            for (float x = 8f; x <= 52f; x += 11f) spots.Add(new Vector2(x, -9.2f));
            for (float y = 2f; y <= 18f; y += 11f) spots.Add(new Vector2(39.2f, y));
            int n = 0;
            foreach (var p in spots)
            {
                var go = Spr($"LampPost_{++n}", P("lamppost_unlit"), p, 0, Color.white, root);
                Pivot(go);
                var lamp = go.AddComponent<LampPost>();
                var sr = go.GetComponent<SpriteRenderer>();
                // 등불 불꽃 자리 - 그림 위에서 랜턴이 걸린 곳 (오른쪽 위)
                Vector2 flame = new(sr.sprite.bounds.size.x * 0.30f, sr.sprite.bounds.size.y * 0.58f);
                bool flip = p.x < 0f && Mathf.Abs(p.x) < 5f;       // 큰길 서쪽 등불은 랜턴이 길 쪽(동쪽)으로
                sr.flipX = false;
                var lg = new GameObject("Light");
                lg.transform.SetParent(go.transform, false);
                lg.transform.localPosition = flame;
                var l = AddLight(lg, LampColor, 1.9f, 7.5f);
                l.volumetricEnabled = true; l.volumeIntensity = 0.18f;   // 빛무리 - 어둠 속에서 등불 자리가 멀리서도 읽힌다
                lg.AddComponent<FixedLightZone>();
                var so = new SerializedObject(lamp);
                so.FindProperty("body").objectReferenceValue = sr;
                so.FindProperty("litSprite").objectReferenceValue = P("lamppost_lit");
                so.FindProperty("unlitSprite").objectReferenceValue = P("lamppost_unlit");
                so.FindProperty("lampLight").objectReferenceValue = l;
                so.FindProperty("flameOffset").vector2Value = flame;
                so.ApplyModifiedPropertiesWithoutUndo();
                go.layer = _interact;
                var trig = go.AddComponent<CircleCollider2D>();
                trig.isTrigger = true;
                trig.radius = 1.2f;
                trig.offset = new Vector2(0f, 0.6f);
                var bse = new GameObject("Base");
                bse.transform.SetParent(go.transform, false);
                bse.layer = _wall;
                var bc = bse.AddComponent<BoxCollider2D>();
                bc.size = new Vector2(0.9f, 0.5f);
                bc.offset = new Vector2(0f, 0.25f);
                _lamps.Add(lamp);
                if (Vector2.Distance(p, Start) < 6f) { var aso = new SerializedObject(lamp); aso.FindProperty("alwaysLitAtStart").boolValue = true; aso.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        // ================= 숨는 덤불 =================
        /// <summary>
        /// 구역마다 숨을 덤불 - 순찰로 옆, 교전 지역(네거리) 둘레, 묘지 입구.
        /// 어부지리를 노리려면 숨을 곳이 있어야 한다 (피드백 - 제대로 숨지 못하면 다굴).
        /// </summary>
        private static void HideBushes()
        {
            var a = P("hide_bush_a"); if (a == null) return;
            var kinds = new[] { a, P("hide_bush_b") ?? a, P("hide_bush_c") ?? a };
            var root = Group("HideBushes", _map);
            Vector2[] spots =
            {
                new(-8f, 6f), new(-8f, -3.5f), new(8.5f, 5.5f), new(8f, -12f),          // 네거리 둘레
                new(-26f, -3.5f), new(-20f, 7f), new(-31f, 15f), new(-57f, -12f),       // 이교도 구역
                new(23f, -9.2f + 3.6f), new(38f, -26f), new(26f, 12f), new(44f, 22f),  // 도적 구역
                new(-5.5f, 26f), new(5.5f, 26f),                                       // 묘지 입구
                new(-4f, -26f), new(4.2f, -18f),                                       // 시작 길
            };
            int n = 0;
            foreach (var p0 in spots)
            {
                var p = Free(p0);
                if (InsideAnyRoad(p, 0.5f)) continue;
                var go = Spr($"HideBush_{++n}", kinds[n % kinds.Length], p, 0, Shade(0.06f), root);
                Pivot(go);
                go.GetComponent<SpriteRenderer>().flipX = Chance(0.5f);
                var col = go.AddComponent<BoxCollider2D>();
                var sz = go.GetComponent<SpriteRenderer>().sprite.bounds.size;
                col.size = new Vector2(sz.x * 0.8f, 1.2f);
                col.offset = new Vector2(0f, 0.6f);
                var hb = go.AddComponent<HideBush>();
                var so = new SerializedObject(hb);
                so.FindProperty("body").objectReferenceValue = go.GetComponent<SpriteRenderer>();
                so.FindProperty("idleSprite").objectReferenceValue = go.GetComponent<SpriteRenderer>().sprite;
                so.FindProperty("rustleSprite").objectReferenceValue = P("hide_bush_rustle");
                SetLeanFrames(so, go.GetComponent<SpriteRenderer>().sprite.name);
                so.ApplyModifiedPropertiesWithoutUndo();
                _blocked.Add(new Rect(p.x - sz.x / 2, p.y, sz.x, 1.5f));
            }
        }

        /// <summary>덤불 그림 이름에 맞는 기운 그림 4장 (왼 2 · 왼 1 · 오른 1 · 오른 2).</summary>
        public static void SetLeanFrames(SerializedObject so, string baseName)
        {
            var tags = new[] { "L2", "L1", "R1", "R2" };
            var arr = so.FindProperty("leanFrames");
            arr.arraySize = 0;
            foreach (var t in tags) { var s = P(baseName + "_lean" + t); if (s == null) { arr.arraySize = 0; return; } }
            arr.arraySize = tags.Length;
            for (int i = 0; i < tags.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = P(baseName + "_lean" + tags[i]);
        }

        // ================= 장식 =================
        private static void Scatter()
        {
            var root = Group("Undergrowth", _map);
            string[] names = { "bush_fern_a", "bush_fern_b", "bush_leaf_a", "bush_leaf_b", "bush_grass", "bush_grass", "bush_stump" };
            int placed = 0;
            for (int tries = 0; tries < 4000 && placed < 520; tries++)
            {
                var p = new Vector2(R(MapRect.xMin + 2f, MapRect.xMax - 2f), R(MapRect.yMin + 2f, MapRect.yMax - 2f));
                if (Blocked(p, 0.3f) || _graves.Contains(p)) continue;
                var s = P(names[_rng.Next(names.Length)]);
                if (s == null) continue;
                var go = Spr("Plant", s, p, 0, Shade(0.14f), root);
                Pivot(go);
                go.GetComponent<SpriteRenderer>().flipX = Chance(0.5f);
                placed++;
            }
        }

        // ================= 탈출 · 시작 =================
        private static void Extraction()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "ExtractionPoint.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _map);
            go.name = "ExtractionPoint";
            go.transform.position = Exit;
            go.transform.localScale = Vector3.one * 2.6f;
            var point = go.GetComponent<ExtractionPoint>();
            var raid = Object.FindFirstObjectByType<Core.RaidManager>();
            if (raid != null) { var so = new SerializedObject(raid); so.FindProperty("extraction").objectReferenceValue = point; so.ApplyModifiedPropertiesWithoutUndo(); }
            var minimap = Object.FindFirstObjectByType<UI.MinimapUI>(FindObjectsInactive.Include);
            if (minimap != null) { var so = new SerializedObject(minimap); so.FindProperty("exit").objectReferenceValue = point; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        private static void PlacePlayer()
        {
            var player = GameObject.Find("Player");
            if (player != null) player.transform.position = Start;
            var spawn = GameObject.Find("SpawnPoint");
            if (spawn != null) spawn.transform.position = Start;
            var cam = Camera.main;
            if (cam != null) cam.transform.position = new Vector3(Start.x, Start.y + 1.25f, cam.transform.position.z);
        }

        // ================= 상자 =================
        private static void Box(string kind, Vector2 p, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "LootBox_" + kind + ".prefab");
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _loot);
            go.transform.position = p;
            go.name = name;
        }

        private static void PlaceLoot()
        {
            // 예배당 - 고가치
            Box("Reliquary", new Vector2(-38f, 9.5f), "Chapel_Reliquary_1");
            Box("Reliquary", new Vector2(-43f, 9.5f), "Chapel_Reliquary_2");
            Box("Armory", new Vector2(-52.5f, 9.6f), "Barracks_Armory");
            Box("Strongbox", new Vector2(-52.5f, -6.2f), "Barracks_Strongbox");
            // 마을
            Box("Chest", new Vector2(10.6f, -20.8f), "HouseA_Chest");
            Box("Sack", new Vector2(19.6f, -20.8f), "HouseA_Sack");
            Box("Apothecary", new Vector2(33.6f, -22.6f), "HouseB_Apothecary");
            Box("Chest", new Vector2(49.6f, -18.8f), "HouseC_Chest");
            Box("Strongbox", new Vector2(42.4f, -18.8f), "HouseC_Strongbox");
            Box("Sack", new Vector2(20.6f, 16.6f), "HouseD_Sack");
            Box("Chest", new Vector2(11.4f, 16.6f), "HouseD_Chest");
            // 야영지
            Box("Strongbox", CampCenter + new Vector2(-1.5f, 5.5f), "Camp_Strongbox");
            Box("Sack", CampCenter + new Vector2(2.5f, -4f), "Camp_Sack");
            // 묘지 - 감염자 사이
            Box("Reliquary", new Vector2(10.5f, 43f), "Graveyard_Reliquary");
            // 네거리 수레
            Box("Cart", new Vector2(-6.5f, -6.5f), "Crossroads_Cart");
        }

        // ================= 적 =================
        private static GameObject Spawn(string prefab, Vector2 p, string name)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + prefab + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, _enemies);
            go.transform.position = Free(p);
            go.name = name;
            return go;
        }

        private static void GiveTorch(GameObject cultist)
        {
            var s = P("item_torch");
            var t = new GameObject("Torch");
            t.transform.SetParent(cultist.transform, false);
            var sr = t.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = 1;
            var lg = new GameObject("Flame");
            lg.transform.SetParent(t.transform, false);
            lg.transform.localPosition = new Vector3(0f, s != null ? s.bounds.size.y * 0.85f : 0.4f, 0f);
            var tl = AddLight(lg, TorchColor, 1.6f, 6f);
            tl.volumetricEnabled = true; tl.volumeIntensity = 0.15f;
            var et = t.AddComponent<EnemyTorch>();
            var so = new SerializedObject(et);
            so.FindProperty("torchLight").objectReferenceValue = lg.GetComponent<Light2D>();
            so.FindProperty("holder").objectReferenceValue = cultist.GetComponent<SpriteRenderer>();
            so.FindProperty("handRight").vector2Value = new Vector2(0.34f, 0.42f);   // 부모 2배 기준 로컬
            so.FindProperty("emberOffset").vector2Value = new Vector2(0f, s != null ? s.bounds.size.y * 1.7f : 0.8f);
            so.ApplyModifiedPropertiesWithoutUndo();
            var ai = cultist.GetComponent<RangedEnemy>();
            var aso = new SerializedObject(ai);
            aso.FindProperty("throwsFireFlask").boolValue = true;
            aso.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector2[] Loop(Rect r) => new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) };

        private static void Cultists(Rect church)
        {
            var squadGo = new GameObject("CultistSquad");
            squadGo.transform.SetParent(_enemies, false);
            var squad = squadGo.AddComponent<CultistSquad>();
            var routes = new List<CultistSquad.Route>();

            CultistSquad.Route Pair(string name, Vector2[] pts)
            {
                for (int i = 0; i < pts.Length; i++) pts[i] = Free(pts[i]);
                var l = Spawn("Enemy_Ranged", pts[0], name + "_Leader");
                var f = Spawn("Enemy_Ranged", pts[0] + (pts[0] - pts[1]).normalized * 1.8f, name + "_Follower");
                var la = l.GetComponent<RangedEnemy>(); var fa = f.GetComponent<RangedEnemy>();
                la.SetPatrol(pts, null); fa.SetPatrol(pts, la, 1f);
                EditorUtility.SetDirty(la); EditorUtility.SetDirty(fa);
                GiveTorch(l);
                var r = new CultistSquad.Route { name = name, points = pts, leader = la, follower = fa };
                routes.Add(r);
                return r;
            }

            // 예배당 둘레
            Pair("Cultist_Ring", Loop(Rect.MinMaxRect(church.xMin - 3.5f, church.yMin - 4f, church.xMax + 3.5f, church.yMax + 3.5f)));
            // 서쪽 길 → 네거리 장터 (교전 지역)
            Pair("Cultist_Road", new[] { new Vector2(-30f, 2f), new Vector2(-16f, 2f), new Vector2(4f, 3f), new Vector2(4f, -7f), new Vector2(-8f, -3f) });
            // 북서쪽 숲길 → 묘지 서쪽 (감염자와 부딪친다)
            Pair("Cultist_North", new[] { new Vector2(-40f, 17f), new Vector2(-40f, 31f), new Vector2(-10f, 34.5f), new Vector2(-24f, 23f) });

            // 숙소 - 쉬는 넷
            var spots = new[] { new Vector2(-52f, 6f), new Vector2(-49f, 6f), new Vector2(-52f, 1f), new Vector2(-49f, 1f), new Vector2(-50.5f, -3.5f) };
            var reserves = new List<RangedEnemy>();
            for (int i = 0; i < 4; i++)
            {
                var r = Spawn("Enemy_Ranged", spots[i], $"Cultist_Reserve_{i + 1}");
                var ra = r.GetComponent<RangedEnemy>();
                ra.SetPatrol(null, null);
                EditorUtility.SetDirty(ra);
                reserves.Add(ra);
            }
            squad.Setup(routes, reserves, spots);
            EditorUtility.SetDirty(squad);
        }

        private static void Rogues()
        {
            (Vector2 c, float r)[] zones =
            {
                (new Vector2(6f, -1f), 5f),       // 네거리 - 교전 지역 (이교도 서쪽 길 순찰과 여기서만 겹친다)
                (new Vector2(15f, -17f), 6f), (new Vector2(30f, -19f), 6f), (new Vector2(46f, -15f), 6f), (new Vector2(16f, 13f), 6f),
                (CampCenter + new Vector2(-3f, 0f), 6f), (CampCenter + new Vector2(3f, 2f), 6f), (CampCenter + new Vector2(0f, -4f), 7f),
                (new Vector2(48f, 0f), 7f), (new Vector2(5f, 4f), 5f),
            };
            int n = 0;
            foreach (var z in zones)
            {
                var go = Spawn("Enemy_Rogue", z.c + new Vector2(R(-1.5f, 1.5f), R(-1.5f, 1.5f)), $"Rogue_{++n}");
                var ai = go.GetComponent<RogueEnemy>();
                ai.SetHome(go.transform.position, z.r);
                var so = new SerializedObject(ai);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ai);
            }
        }

        private static void Infected()
        {
            Vector2[] spots = { new(-7f, 38f), new(6f, 35f), new(-2f, 42f), new(9f, 40.5f) };
            int n = 0;
            foreach (var p in spots) Spawn("Enemy_Melee", p, $"Infected_{++n}");
        }

        // ================= 미니맵 · 씬 =================
        private static void BakeMapData()
        {
            var host = GameObject.Find("MapData") ?? new GameObject("MapData");
            var data = host.GetComponent<MazeData>() ?? host.AddComponent<MazeData>();
            const float tile = 2f;
            int w = Mathf.CeilToInt((MapRect.width + 4f) / tile) | 1, h = Mathf.CeilToInt((MapRect.height + 4f) / tile) | 1;
            var grid = new bool[w, h];
            int mask = 1 << _wall;
            Vector2 c0 = MapRect.center;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = c0 + new Vector2((x - (w - 1) * 0.5f) * tile, ((h - 1) * 0.5f - y) * tile);
                grid[x, y] = Physics2D.OverlapBox(c, Vector2.one * tile * 0.7f, 0f, mask) != null;
            }
            data.Store(grid, w, h, tile, Start, Exit);
            EditorUtility.SetDirty(data);
        }

        private static void SceneSetup()
        {
            // 효과음 - 전투 · 빛 (CombatSfxBaker)
            if (AssetDatabase.LoadAssetAtPath<AudioClip>(CombatSfxBaker.Folder + "/slam.wav") == null) CombatSfxBaker.BakeAll();
            var bankGo = GameObject.Find("Sfx") ?? new GameObject("Sfx");
            var bank = bankGo.GetComponent<FX.SfxBank>() ?? bankGo.AddComponent<FX.SfxBank>();
            var clips = new List<AudioClip>();
            foreach (var n in CombatSfxBaker.Names) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{CombatSfxBaker.Folder}/{n}.wav"); if (c) clips.Add(c); }
            bank.SetClips(clips.ToArray());
            EditorUtility.SetDirty(bank);

            // 도트 이펙트 - vfx_<이름>_<번호> 프레임을 모은다
            var vb = bankGo.GetComponent<FX.VfxBank>() ?? bankGo.AddComponent<FX.VfxBank>();
            var vlist = new List<FX.VfxBank.Clip>();
            (string n, float fps, float rise)[] defs =
            {
                ("vfx_slash", 24f, 0f), ("vfx_impact", 22f, 0f), ("vfx_resist", 20f, 0f), ("vfx_light_burst", 12f, 0.3f),
                ("vfx_fire_burst", 12f, 0.2f), ("vfx_dust", 16f, 0.15f), ("vfx_blood", 18f, 0f), ("vfx_smoke", 9f, 0.6f),
                ("vfx_harvest", 14f, 0.2f), ("vfx_muzzle", 24f, 0f),
            };
            foreach (var d in defs)
            {
                var frames = new List<Sprite>();
                for (int i = 0; i < 12; i++) { var f = P($"{d.n}_{i}"); if (f == null) break; frames.Add(f); }
                if (frames.Count > 0) vlist.Add(new FX.VfxBank.Clip { name = d.n, frames = frames.ToArray(), fps = d.fps, rise = d.rise });
            }
            vb.SetClips(vlist);
            EditorUtility.SetDirty(vb);

            var cam = Camera.main;
            if (cam != null)
            {
                if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
                // 도트를 정수배로 - 1080p 에서 38px/m 가 정확히 2배 (세로 14.2m 가 보인다)
                var ppc = cam.GetComponent<PixelPerfectCamera>();
                if (ppc != null) Object.DestroyImmediate(ppc);
                cam.orthographicSize = 1080f / 2f / (38f * 2f);
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = true;
            }

            // 달빛 - 어둠이 너무 깊지 않게 (피드백 - 너무 어둡다, 빛이 있을 때와 없을 때의 대비)
            var vc = Object.FindFirstObjectByType<Player.VisionController>();
            if (vc != null)
            {
                var so = new SerializedObject(vc);
                so.FindProperty("ambientDarkness").floatValue = 0.12f;   // 대비 - 불빛 밖은 확실히 어둡다 (피드백: 대비가 없다)
                so.ApplyModifiedPropertiesWithoutUndo();
                var gl = so.FindProperty("globalLight").objectReferenceValue as Light2D;
                if (gl != null) { gl.color = Moon; EditorUtility.SetDirty(gl); }
            }

            // 후처리 - 빛이 번지게(블룸), 가장자리를 눌러(비네트) 등불 둘레가 살아나게
            var volGo = GameObject.Find("PostFX") ?? new GameObject("PostFX");
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            const string profPath = "Assets/_Game/Settings/PlagueNight.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (prof == null)
            {
                System.IO.Directory.CreateDirectory("Assets/_Game/Settings");
                prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, profPath);
            }
            // 하위 에셋으로 저장 안 된 효과는 다시 열면 빈 칸(null)으로 남는다 - 치우고 새로 붙인다
            prof.components.RemoveAll(c => c == null);
            var bloom = prof.TryGet<Bloom>(out var b) ? b : prof.Add<Bloom>(true);
            bloom.threshold.Override(0.7f); bloom.intensity.Override(1.4f); bloom.scatter.Override(0.7f);
            var vig = prof.TryGet<Vignette>(out var v) ? v : prof.Add<Vignette>(true);
            vig.intensity.Override(0.4f); vig.smoothness.Override(0.5f); vig.color.Override(new Color(0.02f, 0.02f, 0.05f));
            var ca = prof.TryGet<ColorAdjustments>(out var c2) ? c2 : prof.Add<ColorAdjustments>(true);
            ca.contrast.Override(24f); ca.saturation.Override(-6f); ca.postExposure.Override(0.15f); ca.colorFilter.Override(Color.white);
            // 그림자 - 어둠은 그늘에서 시작한다: 밝은 곳은 따뜻하게, 그늘은 차갑게
            var smh = prof.TryGet<ShadowsMidtonesHighlights>(out var sm) ? sm : prof.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.85f, 0.92f, 1.15f, -0.05f)); smh.highlights.Override(new Vector4(1.1f, 1.02f, 0.9f, 0.05f));
            // Add 는 메모리에만 붙인다 - 프로필 에셋 안에 같이 저장해야 씬을 다시 열어도 남는다
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

            // 카메라가 후처리를 안 켜면 볼륨은 아무 일도 안 한다 (URP 카메라 기본값이 꺼짐)
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                var camData = mainCam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
            }
        }

        /// <summary>오른쪽 위 랜턴 - 예전 불씨 막대 · 점등 표시를 대신한다. 타이머는 랜턴 왼쪽으로 비킨다.</summary>
        private static void HudSetup()
        {
            var hud = Object.FindFirstObjectByType<UI.HUDController>(FindObjectsInactive.Include);
            if (hud == null) return;
            var lh = hud.GetComponent<UI.LanternHUD>() ?? hud.gameObject.AddComponent<UI.LanternHUD>();
            var so = new SerializedObject(lh);
            so.FindProperty("frameSprite").objectReferenceValue = P("hud_lantern_frame");
            so.FindProperty("glassSprite").objectReferenceValue = P("hud_lantern_glass");
            so.FindProperty("flameSprite").objectReferenceValue = P("hud_flame");
            so.FindProperty("haloSprite").objectReferenceValue = P("hud_halo");
            so.FindProperty("pixelScale").floatValue = 2f;
            so.FindProperty("torch").objectReferenceValue = Object.FindFirstObjectByType<Player.TorchFuel>();
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var n in new[] { "FuelBG", "LitIndicator" })
            {
                var t = hud.transform.Find(n);
                if (t) t.gameObject.SetActive(false);
            }
            // 도트 고딕 HUD (피그마 시안) - 바 · 초상 · 상태 · 시간 · 무기 · 상호작용 · 조작키
            var ph = hud.GetComponent<UI.PixelHUD>() ?? hud.gameObject.AddComponent<UI.PixelHUD>();
            var pso = new SerializedObject(ph);
            string Pick(string a, string b) => P(a) != null ? a : b;
            void A(string prop, string sprite) => pso.FindProperty("art." + prop).objectReferenceValue = P(sprite);
            A("portraitFrame", Pick("ui_portrait2_small", "ui_portrait_small")); A("portrait", "hero_portrait");
            A("healthBar", Pick("ui_bar2_health", "ui_bar_health")); A("staminaBar", Pick("ui_bar2_stamina", "ui_bar_stamina"));
            A("chip", Pick("ui_bar2_status", "ui_bar_status")); A("panel", Pick("ui_panel2", "ui_panel")); A("keycap", Pick("ui_keycap2", "ui_keycap")); A("plaque", Pick("ui_plaque2", "ui_plaque"));
            A("iconBleed", "icon_bleed"); A("iconInfect", "icon_infect"); A("iconHidden", "icon_eye_closed"); A("iconSpotted", "icon_eye_open");
            A("iconFlask", "icon_flask"); A("iconCrossbow", "icon_crossbow");
            // 바 안쪽 칸 - 마스크 그림의 분홍 자리 (2배)
            Rect Inner(string mask, Rect fallback)
            {
                string path = PixelArtSetup.Dir + mask + ".png";
                if (!System.IO.File.Exists(path)) return fallback;
                var tex = new Texture2D(2, 2); tex.LoadImage(System.IO.File.ReadAllBytes(path));
                int x0 = tex.width, y0 = tex.height, x1 = -1, y1 = -1;
                var px = tex.GetPixels32();
                for (int y = 0; y < tex.height; y++) for (int x = 0; x < tex.width; x++)
                    if (px[y * tex.width + x].a > 0) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
                if (x1 < 0) return fallback;
                // 텍스처는 아래에서 위로 - 시안은 위에서 아래로
                return new Rect(x0 * 2, (tex.height - 1 - y1) * 2, (x1 - x0 + 1) * 2, (y1 - y0 + 1) * 2);
            }
            string hb = Pick("ui_bar2_health", "ui_bar_health"), sb = Pick("ui_bar2_stamina", "ui_bar_stamina");
            pso.FindProperty("healthFillRect").rectValue = Inner(hb + "_mask", new Rect(86, 48, 410, 20));
            pso.FindProperty("staminaFillRect").rectValue = Inner(sb + "_mask", new Rect(78, 34, 260, 16));
            pso.FindProperty("portraitInnerRect").rectValue = Inner(Pick("ui_portrait2_small", "ui_portrait_small") + "_mask", new Rect(14, 14, 60, 58));
            // 도트 글꼴 - HUD 의 모든 글자 (루팅 화면 · 사망 · 탈출 문구 포함)
            PixelFontSetup.Build();
            var mainFont = PixelFontSetup.MainFont;
            if (mainFont != null)
            {
                pso.FindProperty("font").objectReferenceValue = mainFont;
                pso.FindProperty("smallFont").objectReferenceValue = null;   // 글꼴 하나로 통일 (Galmuri11 Bold)
                foreach (var t in hud.GetComponentsInChildren<TMPro.TMP_Text>(true)) { t.font = mainFont; EditorUtility.SetDirty(t); }
            }
            pso.ApplyModifiedPropertiesWithoutUndo();
            // 화면 효과 - 번쩍임 · 발각 맥동 · 빈사 박동 · 화면 멈춤
            var sfx = hud.GetComponent<FX.ScreenFX>() ?? hud.gameObject.AddComponent<FX.ScreenFX>();
            var dv = hud.transform.Find("DamageVignette");
            var sfso = new SerializedObject(sfx);
            sfso.FindProperty("edgeSprite").objectReferenceValue = dv ? dv.GetComponent<UnityEngine.UI.Image>().sprite : null;
            sfso.ApplyModifiedPropertiesWithoutUndo();
            // 피격 흔들림은 킥이 맡는다 - 무작위 흔들림은 아주 약하게만 남긴다
            var df = hud.GetComponentInChildren<UI.DamageFeedback>(true) ?? Object.FindFirstObjectByType<UI.DamageFeedback>(FindObjectsInactive.Include);
            if (df) { var dso = new SerializedObject(df); dso.FindProperty("shakePerDamage").floatValue = 0.012f; dso.FindProperty("maxAlpha").floatValue = 0.42f; dso.ApplyModifiedPropertiesWithoutUndo(); }
            var hso = new SerializedObject(hud);
            hso.FindProperty("litIndicator").objectReferenceValue = null;
            hso.FindProperty("fuelFill").objectReferenceValue = null;
            hso.FindProperty("fuelText").objectReferenceValue = null;
            // 새 HUD 가 같은 정보를 보여 준다 - 예전 HUD 가 매 프레임 다시 켜지 않게 끊는다
            hso.FindProperty("stealthIndicator").objectReferenceValue = null;
            hso.FindProperty("promptText").objectReferenceValue = null;
            hso.FindProperty("healthFill").objectReferenceValue = null;
            hso.FindProperty("healthText").objectReferenceValue = null;
            hso.ApplyModifiedPropertiesWithoutUndo();
            var timer = hud.transform.Find("TimerText") as RectTransform;
            if (timer) timer.anchoredPosition = new Vector2(-230f, -52f);
        }

        private static void AddToBuild()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        /// <summary>마을의 레이드 지역이 이 맵으로 보내게.</summary>
        private static void PointTownHere()
        {
            const string town = "Assets/_Game/Scenes/Town.unity";
            var active = EditorSceneManager.GetActiveScene();
            var t = EditorSceneManager.OpenScene(town, OpenSceneMode.Additive);
            foreach (var root in t.GetRootGameObjects())
                foreach (var tm in root.GetComponentsInChildren<Core.TownManager>(true))
                {
                    var so = new SerializedObject(tm);
                    so.FindProperty("raidSceneName").stringValue = "Raid_Map1";
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            EditorSceneManager.SaveScene(t);
            EditorSceneManager.CloseScene(t, true);
            EditorSceneManager.SetActiveScene(active);
        }

        // ================= 도우미 =================
        private static Light2D AddLight(GameObject go, Color c, float intensity, float radius, bool shadows = true)
        {
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            l.pointLightInnerRadius = radius * 0.08f;
            l.falloffIntensity = 0.72f;
            l.shadowsEnabled = shadows;
            l.shadowIntensity = 0.85f;
            l.shadowSoftness = 0.3f;
            return l;
        }

        private static void Prop(string sprite, Vector2 p, Transform parent, bool solid, bool flip = false)
        {
            var s = P(sprite);
            if (s == null) return;
            var go = Spr(sprite, s, p, 0, Color.white, parent);
            Pivot(go);
            go.GetComponent<SpriteRenderer>().flipX = flip;
            if (!solid) return;
            go.layer = _wall;
            var bc = go.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(s.bounds.size.x * 0.85f, Mathf.Min(1f, s.bounds.size.y * 0.4f));
            bc.offset = new Vector2(0f, bc.size.y * 0.5f);
            _blocked.Add(new Rect(p.x - s.bounds.size.x / 2, p.y, s.bounds.size.x, 1f));
        }

        private static void Invisible(string name, Rect r, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = _wall;
            go.transform.position = r.center;
            var bc = go.AddComponent<BoxCollider2D>();
            bc.size = r.size;
        }

        private static Vector2 Free(Vector2 p)
        {
            int mask = 1 << _wall;
            Physics2D.SyncTransforms();
            if (Physics2D.OverlapCircle(p, 0.9f, mask) == null) return p;
            for (float d = 0.5f; d < 8f; d += 0.5f)
                for (int a = 0; a < 12; a++)
                {
                    var q = p + new Vector2(Mathf.Cos(a * 0.5236f), Mathf.Sin(a * 0.5236f)) * d;
                    if (Physics2D.OverlapCircle(q, 0.9f, mask) == null) return q;
                }
            return p;
        }

        private static Rect Expand(Rect r, float m) => new(r.xMin - m, r.yMin - m, r.width + m * 2f, r.height + m * 2f);
        private static Color Shade(float spread) { float k = 1f + R(-spread, spread); return new Color(k, k, k, 1f); }

        private static void Pivot(GameObject go)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
        }

        private static void Tile(GameObject go, Vector2 size)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr.sprite == null) return;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
        }

        private static Transform Root(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            return go.transform;
        }

        private static Transform Group(string name, Transform parent, bool reuse = false)
        {
            if (reuse) { var f = parent.Find(name); if (f != null) return f; }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        private static GameObject Spr(string name, Sprite sprite, Vector2 pos, int order, Color color, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }
    }
}
