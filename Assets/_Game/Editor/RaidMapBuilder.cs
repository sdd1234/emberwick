using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Capstone.Map;
using Capstone.Enemy;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 레이드 맵을 피드백 문서(2026-10)의 "레이드 맵 레이아웃" 그대로 굽는다 (메뉴 Capstone/씬 굽기/레이드 맵).
    ///
    /// 미로(MazeBuilder)를 걷어내고 손으로 그린 지도를 옮긴다. 좌표는 문서 그림의 픽셀을 그대로 적고
    /// Px() 로 미터로 바꾼다 - 그림과 대조하며 고칠 수 있도록 숫자를 그림 기준으로 남겨 둔다.
    /// 그림 1px = 0.25m, 맵 400x565px = 100 x 141m.
    ///
    ///   - 왼쪽 가시덤불 2곳 · 오른쪽 나무 숲 2곳 = 지나갈 수 없는 경계
    ///   - 가운데 숲 = 지나갈 수 있다 (나무만 있고 막지 않는다)
    ///   - 버려진 교회(고가치) · 작은 집 · 집 · 오두막 3 · 마을(고가치 구역) 집 3
    ///   - 집 · 오두막 안은 상자나 파밍 주머니만 있는 단순한 사각형
    ///   - 고정 광원 9개 - 안에 서면 어둠 디버프가 풀린다
    ///   - 교회 안과 주변에 이교도 2인 1조 순찰, 나머지 들판에 감염체(소리로 찾는다)
    /// </summary>
    public static class RaidMapBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/Raid.unity";
        private const string Prefabs = "Assets/_Game/Prefabs";
        private const float S = 0.25f;                 // 그림 1px = 0.25m
        private const float DoorWidth = 3.6f;          // 2배 몸집의 적이 길찾기 격자로 지나갈 수 있는 폭
        private const float WallThick = 0.6f;

        // 색 - 밤의 폐촌. 전부 흰 스프라이트에 물들여 쓴다
        private static readonly Color GroundCol   = new(0.36f, 0.40f, 0.30f);
        private static readonly Color RoadCol     = new(0.47f, 0.43f, 0.37f);
        private static readonly Color GroveCol    = new(0.24f, 0.33f, 0.22f);
        private static readonly Color TreeCol     = new(0.20f, 0.32f, 0.19f);
        private static readonly Color DenseTree   = new(0.14f, 0.24f, 0.14f);
        private static readonly Color BrambleCol  = new(0.42f, 0.20f, 0.26f);
        private static readonly Color BrambleBed  = new(0.26f, 0.17f, 0.18f);
        private static readonly Color HouseWall   = new(0.55f, 0.49f, 0.42f);
        private static readonly Color HouseFloor  = new(0.40f, 0.31f, 0.22f);
        private static readonly Color ChurchWall  = new(0.60f, 0.58f, 0.62f);
        private static readonly Color ChurchFloor = new(0.33f, 0.31f, 0.36f);
        private static readonly Color LampLight   = new(1f, 0.72f, 0.40f);

        private static Transform _map, _enemies, _loot;
        private static Sprite _square, _circle, _tree, _bramble, _ground, _lamp;
        // 2026-10 원화 - 있으면 이걸 쓰고, 없으면 코드로 구운 도형으로 떨어진다
        private static Sprite _spruceBig, _spruceSmall, _branchDense, _branchLoose;
        private static Sprite[] _bushes;
        private static Sprite _groundFern, _groundDirt, _roadEdge, _dirtDisc, _softCircle;
        private static System.Random _rng;
        private static int _wallLayer;

        /// <summary>문 하나. side = N/S/E/W, at = 그 변 위의 그림 좌표(N/S 면 x, E/W 면 y).</summary>
        private struct Door { public char side; public float at; public Door(char s, float a) { side = s; at = a; } }

        [MenuItem("Capstone/씬 굽기/레이드 맵")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[RaidMapBuilder] 플레이 모드에서는 구울 수 없다."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Square) == null) EnvSpriteGenerator.BakeAll();
            _square  = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Square);
            _circle  = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Circle);
            _tree    = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Tree);
            _bramble = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Bramble);
            _ground  = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Ground);
            _lamp    = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Lamp);
            const string forest = "Assets/_Game/Art/Env/Forest/";
            _spruceBig   = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "spruce_big.png");
            _spruceSmall = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "spruce_small.png");
            _branchDense = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "branches_dense.png");
            _branchLoose = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "branches_loose.png");
            _groundFern = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "ground_fern.png");
            _groundDirt = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "ground_dirt.png");
            _roadEdge   = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "road_edge.png");
            _dirtDisc   = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "dirt_disc.png");
            _softCircle = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.SoftCircle);
            _bushes = new[]
            {
                AssetDatabase.LoadAssetAtPath<Sprite>(forest + "bush_fern.png"),
                AssetDatabase.LoadAssetAtPath<Sprite>(forest + "bush_leaf.png"),
                AssetDatabase.LoadAssetAtPath<Sprite>(forest + "bush_leaf_2.png"),
            };
            _rng = new System.Random(20261010);
            _wallLayer = LayerMask.NameToLayer(Core.GameLayers.Wall);

            _map = Root("Map");
            _enemies = Root("Enemies");
            _loot = Root("Loot");
            Clear(_map); Clear(_enemies); Clear(_loot);

            BuildGround();
            BuildRoads();
            BuildBoundary();
            BuildBrambles();
            BuildForests();
            BuildGrove();
            var doors = BuildBuildings();
            BuildLamps(doors);
            ScatterBushes();
            Physics2D.SyncTransforms();

            BuildExtraction();
            PlaceLoot();
            PlaceEnemies();
            PlacePlayer();
            BakeMapData();
            SetupScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[RaidMapBuilder] 레이드 맵을 구웠다 - 피드백 레이아웃");
        }

        // ================= 좌표 =================
        /// <summary>그림 픽셀 -> 월드 미터. 그림의 맵 중심(462, 429.5)이 원점.</summary>
        private static Vector2 Px(float x, float y) => new((x - 462f) * S, (429.5f - y) * S);

        private static Rect PxRect(float x0, float y0, float x1, float y1)
        {
            Vector2 a = Px(x0, y1), b = Px(x1, y0);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        private static Vector2[] PxPoly(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = Px(xy[i * 2], xy[i * 2 + 1]);
            return pts;
        }

        private static float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // ================= 바닥 · 길 =================
        private static void BuildGround()
        {
            // 원화 바닥 타일(고사리)이 있으면 그걸 깐다. 색은 원화 그대로 - 어둡기는 조명이 정한다
            Vector2 size = new(100f + 30f, 141.25f + 30f);   // 경계 너머까지 깔아 맵 끝이 허공으로 안 보이게
            if (_groundFern == null)
            {
                var g0 = Sprite("Ground", _ground, Vector2.zero, -30, GroundCol, _map);
                Tile(g0, size);
                return;
            }
            Tile(Sprite("Ground", _groundFern, Vector2.zero, -32, Color.white, _map), size);

            // 반복 무늬 가리기 - 원화 고사리 조각이 작아서 그대로 깔면 바둑판이 보인다.
            // (1) 바닥 텍스처 자체를 조각을 흩뿌려 짠 8m 짜리로 만들었다 (process_art.py scatter_tile)
            // (2) 넓고 옅은 얼룩 - 땅이 고르게 한 색이면 반복이 더 잘 보인다
            var soft = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.SoftCircle);
            var blotches = Group("GroundBlotches", _map).transform;
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector2(Rand(-55f, 55f), Rand(-75f, 75f));
                bool dark = Rand(0f, 1f) < 0.65f;
                var b = Sprite("Blotch", soft, p, -30, dark ? new Color(0.03f, 0.05f, 0.03f, Rand(0.25f, 0.45f)) : new Color(0.55f, 0.45f, 0.3f, Rand(0.10f, 0.2f)), blotches);
                b.transform.localScale = new Vector3(Rand(6f, 15f), Rand(5f, 12f), 1f);
                b.transform.rotation = Quaternion.Euler(0f, 0f, Rand(0f, 180f));
            }
        }

        /// <summary>
        /// 빈 들판에 덤불을 흩는다 (반복 무늬 가리기 (3)). 길 · 건물 · 막힌 지역 · 시작 지점은 비운다.
        /// 지나갈 수 있는 장식이라 콜라이더는 없다.
        /// </summary>
        private static void ScatterBushes()
        {
            if (_bushes == null || _bushes[0] == null) return;
            var parent = Group("FieldBushes", _map).transform;
            Rect map = PxRect(262, 147, 662, 712);
            int placed = 0;
            for (int tries = 0; tries < 900 && placed < 80; tries++)
            {
                var p = new Vector2(Rand(map.xMin + 2f, map.xMax - 2f), Rand(map.yMin + 2f, map.yMax - 2f));
                if (Vector2.Distance(p, Start) < 8f || Vector2.Distance(p, Exit) < 6f) continue;
                if (NearRoad(p, 4.5f)) continue;
                bool blocked = false;
                foreach (var b in _buildings) if (Expand(b.rect, 3f).Contains(p)) { blocked = true; break; }
                foreach (var poly in Brambles) if (InPoly(p, PxPoly(poly))) blocked = true;
                foreach (var poly in Forests) if (InPoly(p, PxPoly(poly))) blocked = true;
                if (blocked) continue;

                var t = Sprite("Bush", _bushes[(int)Rand(0f, 2.99f)], p, 0, Shade(Color.white, 0.12f), parent);
                t.transform.localScale = Vector3.one * Rand(0.45f, 0.8f);
                var sr = t.GetComponent<SpriteRenderer>();
                sr.spriteSortPoint = SpriteSortPoint.Pivot;
                sr.flipX = Rand(0f, 1f) > 0.5f;
                placed++;
            }
        }

        private static Rect Expand(Rect r, float m) => new(r.xMin - m, r.yMin - m, r.width + m * 2f, r.height + m * 2f);

        private static bool NearRoad(Vector2 p, float d)
        {
            foreach (var road in Roads)
                for (int i = 0; i + 3 < road.Length; i += 2)
                {
                    Vector2 a = Px(road[i], road[i + 1]), b = Px(road[i + 2], road[i + 3]), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                    if (Vector2.Distance(p, a + ab * t) < d) return true;
                }
            return false;
        }

        private static readonly float[][] Roads =
        {
            // 시작 위치 -> 서쪽으로 크게 돌아 교회 서쪽 문
            new float[] { 443,680, 441,600, 440,517, 405,497, 380,468, 367,420, 367,330, 378,282, 405,250, 430,233 },
            // 갈림길 -> 집 사이로 올라가 교회 남쪽 문
            new float[] { 440,517, 468,465, 498,425, 519,388, 516,345, 500,312, 478,288, 473,264 },
            // 교회 앞 -> 마을 위로 탈출 지점
            new float[] { 478,288, 505,282, 530,268, 545,240, 542,217 },
            // 교회 동쪽 문 -> 탈출 지점
            new float[] { 507,215, 540,216 },
        };

        private static void BuildRoads()
        {
            var parent = Group("Roads", _map).transform;
            const float width = 4f;
            bool art = _groundDirt != null && _roadEdge != null && _dirtDisc != null;
            foreach (var road in Roads)
            {
                for (int i = 0; i + 3 < road.Length; i += 2)
                {
                    Vector2 a = Px(road[i], road[i + 1]), b = Px(road[i + 2], road[i + 3]);
                    float len = Vector2.Distance(a, b);
                    float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                    if (!art)
                    {
                        var seg0 = Sprite("Road", _square, (a + b) * 0.5f, -29, RoadCol, parent);
                        seg0.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                        seg0.transform.localScale = new Vector3(len, width, 1f);
                        continue;
                    }

                    // 흙길 - 원화 흙을 길이만큼 이어 붙인다
                    var seg = Sprite("Road", _groundDirt, (a + b) * 0.5f, -29, Color.white, parent);
                    seg.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                    Tile(seg, new Vector2(len, width));
                    // 길 안쪽 3m 는 마스크 - 어느 길의 가장자리 띠든 여기엔 안 그려진다.
                    // 없으면 갈림길에서 한 길의 고사리 테두리가 다른 길 흙 위를 가로질러 "덜 칠한 땅"처럼 보였다
                    // 직사각형 마스크는 토막 끝에 못 미치게 - 끝은 둥근 마스크가 맡아 갈림길 입구가 둥글게 열린다
                    RoadMask(parent, (a + b) * 0.5f, ang, new Vector2(Mathf.Max(0.1f, len - width * 0.6f), width - 1f), false);

                    // 양옆 가장자리 - 원화의 "흙 → 고사리" 경계 띠. 흙 쪽이 길 안을 보게 돌려서
                    // 고사리가 길 쪽으로 늘어지게 한다 (띠 = 위 0.5m 흙 + 아래 1.5m 고사리)
                    Vector2 dir = (b - a) / Mathf.Max(0.001f, len);
                    Vector2 n = new(-dir.y, dir.x);
                    foreach (float side in new[] { 1f, -1f })
                    {
                        Vector2 c = (a + b) * 0.5f + n * side * (width * 0.5f + 0.5f);
                        var e = Sprite("RoadEdge", _roadEdge, c, -28, Color.white, parent);
                        // 띠의 위(흙)가 길 가운데(-n*side)를 보도록
                        Vector2 up = -n * side;
                        e.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg - 90f);
                        Tile(e, new Vector2(len + 0.6f, 2f));
                        e.GetComponent<SpriteRenderer>().maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
                    }
                }
                // 꺾이는 곳마다 둥근 흙을 덧대 이음매를 메운다
                for (int i = 0; i + 1 < road.Length; i += 2)
                {
                    var j = Sprite("RoadJoint", art ? _dirtDisc : _circle, Px(road[i], road[i + 1]), -29, art ? Color.white : RoadCol, parent);
                    j.transform.localScale = Vector3.one * (art ? width * 1.25f / 3f : width);
                    // 길의 첫 · 마지막 점은 다른 길과 만나는 갈림길이다 - 크게 둥글게 막아 남의 테두리가 입구를 가로지르지 않게.
                    // 중간 꺾임은 작게 (크게 막으면 길가 고사리가 꺾일 때마다 끊긴다)
                    bool end = i == 0 || i + 2 >= road.Length;
                    if (art) RoadMask(parent, Px(road[i], road[i + 1]), 0f, Vector2.one * (end ? width * 1.6f : width - 0.6f), true);
                }
            }
        }

        private static void RoadMask(Transform parent, Vector2 pos, float ang, Vector2 size, bool round)
        {
            var go = new GameObject("RoadMask");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var m = go.AddComponent<SpriteMask>();
            m.sprite = round ? _circle : _square;
            m.alphaCutoff = 0.5f;
        }

        /// <summary>스프라이트를 늘리지 않고 이어 붙여 그 크기를 채운다 (원화 해상도 유지).</summary>
        private static void Tile(GameObject go, Vector2 size)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
        }

        // ================= 경계 =================
        private static void BuildBoundary()
        {
            var parent = Group("Boundary", _map).transform;
            Rect r = PxRect(262, 147, 662, 712);
            const float t = 2f;
            WallBox("North", parent, new Rect(r.xMin - t, r.yMax, r.width + t * 2, t), false);
            WallBox("South", parent, new Rect(r.xMin - t, r.yMin - t, r.width + t * 2, t), false);
            WallBox("West",  parent, new Rect(r.xMin - t, r.yMin, t, r.height), false);
            WallBox("East",  parent, new Rect(r.xMax, r.yMin, t, r.height), false);
        }

        private static readonly float[][] Brambles =
        {
            new float[] { 262,147, 410,147, 398,205, 375,255, 335,293, 262,312 },
            new float[] { 262,418, 300,440, 330,490, 352,560, 368,640, 378,712, 262,712 },
        };

        private static readonly float[][] Forests =
        {
            new float[] { 518,147, 662,147, 662,255, 640,248, 622,228, 612,205, 590,192, 560,188, 535,176, 522,160 },
            new float[] { 662,472, 600,478, 560,492, 538,520, 528,580, 512,650, 495,712, 662,712 },
        };

        /// <summary>가시덤불 - 못 지나간다. 막는 건 다각형 콜라이더 하나, 보이는 건 흩뿌린 덤불.</summary>
        private static void BuildBrambles()
        {
            int n = 0;
            foreach (var poly in Brambles)
            {
                var pts = PxPoly(poly);
                var region = Region($"Brambles_{++n}", pts, _map);
                FillPolygon(region.transform, pts, 2.4f, (p, inside) =>
                {
                    if (_branchDense != null)
                    {
                        // 원화 가지 - 바닥은 둥근 판으로 어둡게 깔고(네모난 가지 판은 모서리가 각져 보였다)
                        // 그 위에 배경이 투명한 가지를 각도를 바꿔 두 겹 얹는다
                        var bed = Sprite("Bed", _softCircle, p, -28, new Color(0.17f, 0.12f, 0.10f, 0.95f), region.transform);
                        bed.transform.localScale = Vector3.one * Rand(5.5f, 6.5f);
                        for (int k = 0; k < 2; k++)
                        {
                            var b = Sprite("Bramble", _branchLoose, p + new Vector2(Rand(-0.8f, 0.8f), Rand(-0.8f, 0.8f)), -27, Shade(Color.white, 0.12f), region.transform);
                            b.transform.localScale = Vector3.one * Rand(0.75f, 1.05f);
                            b.transform.rotation = Quaternion.Euler(0, 0, Rand(-35f, 35f) + k * 90f);
                            b.GetComponent<SpriteRenderer>().flipX = Rand(0f, 1f) > 0.5f;
                        }
                        return;
                    }
                    var bed0 = Sprite("Bed", _circle, p, -28, BrambleBed, region.transform);
                    bed0.transform.localScale = Vector3.one * Rand(3.2f, 4.2f);
                    var b0 = Sprite("Bramble", _bramble, p + new Vector2(Rand(-0.6f, 0.6f), Rand(-0.6f, 0.6f)), 0, BrambleCol, region.transform);
                    b0.transform.localScale = Vector3.one * Rand(1.2f, 1.7f);
                    b0.transform.rotation = Quaternion.Euler(0, 0, Rand(0, 360));
                });
            }
        }

        /// <summary>빽빽한 숲 - 경계. 나무를 촘촘히 박고 다각형으로 막는다.</summary>
        private static void BuildForests()
        {
            int n = 0;
            foreach (var poly in Forests)
            {
                var pts = PxPoly(poly);
                var region = Region($"Forest_{++n}", pts, _map);
                FillPolygon(region.transform, pts, _spruceBig != null ? 2.1f : 2.6f, (p, inside) =>
                {
                    if (_spruceBig != null)
                    {
                        // 원화 가문비 - 큰 것과 작은 것을 섞는다. 발밑 피벗이라 Y 정렬로 앞뒤가 겹친다.
                        // 나무 사이로 흙이 비치지 않게 짙은 숲 바닥을 먼저 깐다
                        var floor = Sprite("ForestFloor", _softCircle, p, -28, new Color(0.11f, 0.16f, 0.12f, 0.95f), region.transform);
                        floor.transform.localScale = Vector3.one * Rand(6f, 7f);
                        bool big = Rand(0f, 1f) < 0.45f;
                        var sp = Sprite("Spruce", big ? _spruceBig : _spruceSmall, p, 0, Shade(Color.white, 0.12f), region.transform);
                        sp.transform.localScale = Vector3.one * (big ? Rand(0.5f, 0.75f) : Rand(1.1f, 1.6f));
                        var sr = sp.GetComponent<SpriteRenderer>();
                        sr.spriteSortPoint = SpriteSortPoint.Pivot;
                        sr.flipX = Rand(0f, 1f) > 0.5f;
                        return;
                    }
                    var t = Sprite("Tree", _tree, p, 0, Shade(DenseTree, 0.12f), region.transform);
                    t.transform.localScale = Vector3.one * Rand(1.5f, 2.2f);
                });
            }
        }

        /// <summary>가운데 숲 - 지나갈 수 있다. 나무가 그림자를 드리워 숨을 곳이 된다.</summary>
        private static void BuildGrove()
        {
            var parent = Group("Grove", _map);
            Rect r = PxRect(404, 318, 450, 383);
            var bed = Sprite("GroveBed", _softCircle, r.center, -28, GroveCol, parent.transform);
            bed.transform.localScale = new Vector3(r.width * 1.6f, r.height * 1.5f, 1f);
            for (float x = r.xMin + 1.5f; x < r.xMax - 1f; x += 3.4f)
            for (float y = r.yMin + 1.5f; y < r.yMax - 1f; y += 3.4f)
            {
                var p = new Vector2(x + Rand(-0.9f, 0.9f), y + Rand(-0.9f, 0.9f));
                GameObject t;
                if (_bushes != null && _bushes[0] != null)
                {
                    // 원화 덤불 · 작은 가문비 - 지나갈 수 있는 숲이라 콜라이더는 없고 그림자만 드리운다
                    bool spruce = _spruceSmall != null && Rand(0f, 1f) < 0.25f;
                    t = Sprite(spruce ? "Spruce" : "Bush", spruce ? _spruceSmall : _bushes[(int)Rand(0f, 2.99f)], p, 0, Shade(Color.white, 0.1f), parent.transform);
                    t.transform.localScale = Vector3.one * (spruce ? Rand(0.9f, 1.2f) : Rand(0.7f, 0.95f));
                    var sr = t.GetComponent<SpriteRenderer>();
                    sr.spriteSortPoint = SpriteSortPoint.Pivot;
                    sr.flipX = Rand(0f, 1f) > 0.5f;
                }
                else
                {
                    t = Sprite("Tree", _tree, p, 0, Shade(TreeCol, 0.12f), parent.transform);
                    t.transform.localScale = Vector3.one * Rand(1.2f, 1.6f);
                }
                Shadow(t);
            }
        }

        private static GameObject Region(string name, Vector2[] pts, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = _wallLayer;
            var col = go.AddComponent<PolygonCollider2D>();
            col.points = pts;
            return go;
        }

        private static void FillPolygon(Transform parent, Vector2[] pts, float step, System.Action<Vector2, bool> place)
        {
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            // 경계 바깥 4m 까지 채워 맵 끝이 비어 보이지 않게 한다
            Rect map = PxRect(262, 147, 662, 712);
            for (float x = min.x - 4f; x <= max.x + 4f; x += step)
            for (float y = min.y - 4f; y <= max.y + 4f; y += step)
            {
                var p = new Vector2(x + Rand(-step * 0.35f, step * 0.35f), y + Rand(-step * 0.35f, step * 0.35f));
                bool inside = InPoly(p, pts);
                bool outsideMap = !map.Contains(p);
                if (inside || (outsideMap && NearPoly(p, pts, 4f))) place(p, inside);
            }
        }

        private static bool InPoly(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }

        private static bool NearPoly(Vector2 p, Vector2[] poly, float d)
        {
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                if (Vector2.Distance(p, a + ab * t) <= d) return true;
            }
            return false;
        }

        // ================= 건물 =================
        private struct Building
        {
            public string name; public Rect rect; public Door[] doors; public bool church; public bool highValue;
        }

        private static readonly List<Building> _buildings = new();

        /// <summary>문마다 (문 앞 바깥 지점, 바깥 방향)을 돌려준다 - 등불을 문 앞에 세우는 데 쓴다.</summary>
        private static Dictionary<string, (Vector2 pos, Vector2 outward)> BuildBuildings()
        {
            _buildings.Clear();
            void Add(string name, float x0, float y0, float x1, float y1, bool church, bool high, params Door[] d)
                => _buildings.Add(new Building { name = name, rect = PxRect(x0, y0, x1, y1), doors = d, church = church, highValue = high });

            Add("Church",     435, 180, 507, 262, true,  true,  new Door('W', 233), new Door('E', 215), new Door('S', 473));
            Add("SmallHouse", 410, 282, 455, 308, false, false, new Door('W', 295));
            Add("CabinWest",  295, 344, 336, 382, false, false, new Door('E', 366));
            Add("House",      458, 335, 488, 378, false, false, new Door('E', 366));
            Add("CabinMid",   403, 395, 441, 432, false, false, new Door('W', 413));
            Add("VillageHouse1", 543, 289, 577, 326, false, true, new Door('W', 299));
            Add("VillageHouse2", 565, 356, 606, 393, false, true, new Door('W', 367));
            Add("VillageHouse3", 530, 428, 568, 467, false, true, new Door('W', 438));
            Add("CabinSouth", 462, 556, 500, 588, false, false, new Door('W', 566));

            var doorSpots = new Dictionary<string, (Vector2, Vector2)>();
            var parent = Group("Buildings", _map);
            foreach (var b in _buildings)
            {
                var root = Group(b.name, parent.transform);
                var floor = Sprite("Floor", _square, b.rect.center, -27, b.church ? ChurchFloor : HouseFloor, root.transform);
                floor.transform.localScale = new Vector3(b.rect.width, b.rect.height, 1f);

                Color wall = b.church ? ChurchWall : HouseWall;
                BuildSide(root.transform, b, 'N', wall);
                BuildSide(root.transform, b, 'S', wall);
                BuildSide(root.transform, b, 'W', wall);
                BuildSide(root.transform, b, 'E', wall);

                foreach (var d in b.doors)
                {
                    Vector2 c = DoorCenter(b.rect, d, out Vector2 outward);
                    doorSpots[$"{b.name}_{d.side}"] = (c, outward);
                }
            }
            return doorSpots;
        }

        private static Vector2 DoorCenter(Rect r, Door d, out Vector2 outward)
        {
            switch (d.side)
            {
                case 'N': outward = Vector2.up;    return new Vector2(Px(d.at, 0).x, r.yMax);
                case 'S': outward = Vector2.down;  return new Vector2(Px(d.at, 0).x, r.yMin);
                case 'W': outward = Vector2.left;  return new Vector2(r.xMin, Px(0, d.at).y);
                default:  outward = Vector2.right; return new Vector2(r.xMax, Px(0, d.at).y);
            }
        }

        /// <summary>한 변을 벽으로 세운다. 그 변에 문이 있으면 그만큼 비운다.</summary>
        private static void BuildSide(Transform parent, Building b, char side, Color color)
        {
            Rect r = b.rect;
            bool horizontal = side == 'N' || side == 'S';
            float from = horizontal ? r.xMin - WallThick * 0.5f : r.yMin;
            float to   = horizontal ? r.xMax + WallThick * 0.5f : r.yMax;
            float fixedCoord = side == 'N' ? r.yMax : side == 'S' ? r.yMin : side == 'W' ? r.xMin : r.xMax;

            var gaps = new List<Vector2>();
            foreach (var d in b.doors)
            {
                if (d.side != side) continue;
                float c = horizontal ? Px(d.at, 0).x : Px(0, d.at).y;
                gaps.Add(new Vector2(c - DoorWidth * 0.5f, c + DoorWidth * 0.5f));
            }
            gaps.Sort((x, y) => x.x.CompareTo(y.x));

            float cursor = from;
            int i = 0;
            foreach (var g in gaps)
            {
                if (g.x > cursor) Segment(parent, $"Wall_{side}{i++}", horizontal, fixedCoord, cursor, g.x, color);
                cursor = Mathf.Max(cursor, g.y);
            }
            if (to > cursor) Segment(parent, $"Wall_{side}{i}", horizontal, fixedCoord, cursor, to, color);
        }

        private static void Segment(Transform parent, string name, bool horizontal, float fixedCoord, float a, float b, Color color)
        {
            float len = b - a;
            if (len < 0.05f) return;
            Rect rect = horizontal
                ? new Rect(a, fixedCoord - WallThick * 0.5f, len, WallThick)
                : new Rect(fixedCoord - WallThick * 0.5f, a, WallThick, len);
            var go = WallBox(name, parent, rect, true);
            go.GetComponent<SpriteRenderer>().color = color;
        }

        /// <summary>
        /// 벽 한 토막 - 기존 Wall 프리팹(스프라이트 · 콜라이더 · Wall 레이어)을 늘려 쓰고 그림자 캐스터를 단다.
        /// visible=false 면 맵 테두리처럼 보이지 않는 벽.
        /// </summary>
        private static GameObject WallBox(string name, Transform parent, Rect rect, bool visible)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Wall.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = rect.center;
            go.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            var sr = go.GetComponent<SpriteRenderer>();
            if (!visible) sr.enabled = false;
            else Shadow(go);
            return go;
        }

        private static void Shadow(GameObject go)
        {
            if (go.GetComponent<ShadowCaster2D>() != null) return;
            var sc = go.AddComponent<ShadowCaster2D>();
            sc.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
        }

        // ================= 고정 광원 =================
        private static void BuildLamps(Dictionary<string, (Vector2 pos, Vector2 outward)> doors)
        {
            var parent = Group("FixedLights", _map);
            var spots = new List<Vector2>();
            // 문 앞에 서는 등불 - 문에서 2.4m 바깥, 문에서 살짝 비켜서 (길을 막지 않게)
            foreach (var key in new[] { "Church_S", "VillageHouse1_W", "CabinWest_E", "House_E", "VillageHouse2_W", "VillageHouse3_W", "CabinSouth_W" })
            {
                var d = doors[key];
                Vector2 side = Vector2.Perpendicular(d.outward);
                spots.Add(d.pos + d.outward * 2.4f + side * (DoorWidth * 0.5f + 0.6f));
            }
            // 길가 등불 2개 - 갈림길과 남쪽 길
            spots.Add(Px(430, 517));
            spots.Add(Px(424, 616));

            int n = 0;
            foreach (var p in spots)
            {
                var lamp = Sprite($"Lamp_{++n}", _lamp, p, 0, Color.white, parent.transform);
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(lamp.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                var light = lightGo.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                light.color = LampLight;
                light.intensity = 1.5f;
                light.pointLightOuterRadius = 6f;
                light.pointLightInnerRadius = 0.6f;
                light.falloffIntensity = 0.7f;
                light.shadowsEnabled = true;
                light.shadowIntensity = 0.9f;
                light.shadowSoftness = 0.35f;
                lightGo.AddComponent<FixedLightZone>();
            }
        }

        // ================= 탈출 · 시작 =================
        private static Vector2 Start => Px(443, 673);
        private static Vector2 Exit => Px(542, 216);

        private static void BuildExtraction()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/ExtractionPoint.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _map);
            go.name = "ExtractionPoint";
            go.transform.position = Exit;
            go.transform.localScale = Vector3.one * 2.6f;

            // 탈출 지점을 새로 만들었으니 물고 있던 쪽들도 다시 잇는다 (비어 있어도 Awake 에서 찾긴 하지만 명시해 둔다)
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
            if (cam != null) cam.transform.position = new Vector3(Start.x, Start.y + 1f, cam.transform.position.z);
        }

        // ================= 상자 =================
        private static void PlaceLoot()
        {
            int n = 0;
            foreach (var b in _buildings)
            {
                string[] kinds = b.name switch
                {
                    "Church"        => new[] { "Reliquary", "Reliquary", "Armory", "Strongbox" },
                    "VillageHouse1" => new[] { "Strongbox", "Sack" },
                    "VillageHouse2" => new[] { "Reliquary", "Chest" },
                    "VillageHouse3" => new[] { "Apothecary", "Strongbox" },
                    "House"         => new[] { "Chest", "Apothecary" },
                    "SmallHouse"    => new[] { "Sack" },
                    _               => new[] { "Chest", "Sack" },         // 오두막
                };
                // 문에서 먼 안쪽 벽을 따라 놓는다 - 들어와서 한 바퀴 둘러보게
                var spots = InteriorSpots(b, kinds.Length);
                for (int i = 0; i < kinds.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/LootBox_{kinds[i]}.prefab");
                    if (prefab == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _loot);
                    go.transform.position = spots[i];
                    go.name = $"{b.name}_{kinds[i]}_{++n}";
                    Shadow(go);
                }
            }
            // 길가 수레 하나 - 집 바깥에도 털 것이 있다는 신호
            var cart = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/LootBox_Cart.prefab");
            if (cart != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(cart, _loot);
                go.transform.position = Px(392, 470);
                go.name = "Roadside_Cart";
                Shadow(go);
            }
        }

        private static List<Vector2> InteriorSpots(Building b, int count)
        {
            Rect r = b.rect;
            const float inset = 1.6f;
            var corners = new List<Vector2>
            {
                new(r.xMin + inset, r.yMax - inset), new(r.xMax - inset, r.yMax - inset),
                new(r.xMax - inset, r.yMin + inset), new(r.xMin + inset, r.yMin + inset),
                new(r.center.x, r.yMax - inset), new(r.center.x, r.yMin + inset),
            };
            // 문에서 먼 순서
            var doorPts = new List<Vector2>();
            foreach (var d in b.doors) doorPts.Add(DoorCenter(r, d, out _));
            corners.Sort((a, c) => MinDist(c, doorPts).CompareTo(MinDist(a, doorPts)));
            return corners.GetRange(0, Mathf.Min(count, corners.Count));
        }

        private static float MinDist(Vector2 p, List<Vector2> pts)
        {
            float m = float.MaxValue;
            foreach (var q in pts) m = Mathf.Min(m, Vector2.Distance(p, q));
            return m;
        }

        // ================= 적 =================
        private static void PlaceEnemies()
        {
            var cultist = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Enemy_Ranged.prefab");
            var infected = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Enemy_Melee.prefab");
            var church = _buildings.Find(b => b.church).rect;

            // 이교도 - 2인 1조. 교회 안 한 조, 교회 바깥을 도는 두 조
            Rect inner = new Rect(church.xMin + 3f, church.yMin + 3f, church.width - 6f, church.height - 6f);
            Pair(cultist, "Cultist_Inside", Loop(inner));

            Rect outer = new Rect(church.xMin - 4.5f, church.yMin - 4.5f, church.width + 9f, church.height + 9f);
            var outerLoop = Loop(outer);
            Pair(cultist, "Cultist_Ring", outerLoop);
            // 두 번째 바깥 조는 반대편에서 반대로 돈다 - 한쪽을 피하면 다른 쪽과 마주친다
            var reversed = new List<Vector2>(outerLoop);
            reversed.Reverse();
            var shifted = new Vector2[reversed.Count];
            for (int i = 0; i < reversed.Count; i++) shifted[i] = reversed[(i + 2) % reversed.Count];
            Pair(cultist, "Cultist_RingB", shifted);

            // 감염체 - 들판 · 마을 사이 · 길가에 흩어 둔다. 집 안에는 두지 않는다
            float[] spots =
            {
                585,345, 552,410, 600,440, 520,300,     // 마을 (고가치 구역)
                385,445, 330,410, 480,400,              // 가운데
                480,520, 468,612,                       // 남쪽 길
                392,300,                                // 북서쪽
            };
            for (int i = 0; i + 1 < spots.Length; i += 2)
            {
                var p = FreeSpot(Px(spots[i], spots[i + 1]), 0.9f);
                if (Vector2.Distance(p, Start) < 15f) continue;              // 시작하자마자 붙지 않게
                var go = (GameObject)PrefabUtility.InstantiatePrefab(infected, _enemies);
                go.transform.position = p;
                go.name = $"Infected_{i / 2 + 1}";
            }
        }

        private static Vector2[] Loop(Rect r)
            => new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) };

        private static void Pair(GameObject prefab, string name, IList<Vector2> route)
        {
            var points = new Vector2[route.Count];
            for (int i = 0; i < route.Count; i++) points[i] = FreeSpot(route[i], 0.9f);

            var leader = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _enemies);
            leader.name = name + "_Leader";
            leader.transform.position = points[0];
            var follower = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _enemies);
            follower.name = name + "_Follower";
            Vector2 back = (points[0] - points[1]).normalized;
            follower.transform.position = FreeSpot(points[0] + back * 1.8f, 0.9f);

            var la = leader.GetComponent<RangedEnemy>();
            var fa = follower.GetComponent<RangedEnemy>();
            la.SetPatrol(points, null);
            fa.SetPatrol(points, la);
            EditorUtility.SetDirty(la);
            EditorUtility.SetDirty(fa);
        }

        /// <summary>벽이나 덤불 속이면 가까운 빈 자리로 민다.</summary>
        private static Vector2 FreeSpot(Vector2 p, float radius)
        {
            int mask = 1 << _wallLayer;
            if (Physics2D.OverlapCircle(p, radius, mask) == null) return p;
            for (float d = 0.5f; d < 8f; d += 0.5f)
                for (int a = 0; a < 12; a++)
                {
                    var q = p + new Vector2(Mathf.Cos(a * 0.5236f), Mathf.Sin(a * 0.5236f)) * d;
                    if (Physics2D.OverlapCircle(q, radius, mask) == null) return q;
                }
            return p;
        }

        // ================= 미니맵 데이터 =================
        /// <summary>
        /// 미니맵은 MazeData 의 벽 격자를 읽는다. 미로가 아니어도 같은 형식으로 굽기만 하면 그대로 돈다.
        /// 2m 칸마다 벽 콜라이더가 걸치는지 재서 채운다.
        /// </summary>
        private static void BakeMapData()
        {
            // 미니맵이 이 오브젝트의 MazeData 를 물고 있으므로 새로 만들지 않고 이름만 바꿔 쓴다
            var host = GameObject.Find("MazeBuilder");
            if (host == null) host = GameObject.Find("MapData");
            if (host == null) host = new GameObject("MapData");
            host.name = "MapData";
            var builder = host.GetComponent<MazeBuilder>();
            if (builder != null) Object.DestroyImmediate(builder);
            var data = host.GetComponent<MazeData>();
            if (data == null) data = host.AddComponent<MazeData>();

            const float tile = 2f;
            int w = Mathf.CeilToInt(104f / tile) | 1, h = Mathf.CeilToInt(145f / tile) | 1;   // 홀수로 맞춰 중심 칸이 원점에 온다
            var grid = new bool[w, h];
            int mask = 1 << _wallLayer;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new Vector2((x - (w - 1) * 0.5f) * tile, ((h - 1) * 0.5f - y) * tile);
                grid[x, y] = Physics2D.OverlapBox(c, Vector2.one * tile * 0.7f, 0f, mask) != null;
            }
            data.Store(grid, w, h, tile, Start, Exit);
            EditorUtility.SetDirty(data);
        }

        /// <summary>씬 공용 설정 - 소리를 들을 귀, 전역 어둠.</summary>
        private static void SetupScene()
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();

            // 캐릭터가 2배(2.5m)가 됐으니 화면 중심도 발밑에서 몸 가운데로 올린다
            var follow = cam != null ? cam.GetComponent<Player.CameraFollow>() : null;
            if (follow != null)
            {
                var so = new SerializedObject(follow);
                so.FindProperty("verticalOffset").floatValue = 1.25f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ================= 도우미 =================
        private static Transform Root(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            return go.transform;
        }

        private static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        private static GameObject Sprite(string name, Sprite sprite, Vector2 pos, int order, Color color, Transform parent)
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

        private static Color Shade(Color c, float spread)
        {
            float k = 1f + Rand(-spread, spread);
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }
}
