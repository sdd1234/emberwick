using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Capstone.UI;
using Capstone.Core;
using Capstone.Town;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 마을 씬을 코드로 굽는다 (메뉴 Capstone/씬 굽기/마을).
    ///
    /// 피드백(2026-10) 이후 마을은 걸어다니는 광장이다. 문서의 "마을 레이아웃" 그림을 그대로 옮겼다.
    ///   위  - 대장간 · 잡화점 · 의료소. 문 앞에서 F 를 누르면 그 가게의 거래창이 열린다 (거래만 한다)
    ///   아래 - 스폰 위치 (생환하든 죽든 여기서 시작) · 레이드 지역 (들어서면 이동할지 묻는 창)
    /// 좌표는 그림 픽셀로 적고 Px() 로 미터로 바꾼다. 그림 1px = 0.12m, 광장 440x221px = 53 x 26.5m.
    ///
    /// 씬을 손으로 만들어 두면 왜 그렇게 생겼는지가 어디에도 안 남는다.
    /// 스프라이트 생성기와 같은 이유로 여기에 스크립트로 남긴다 - 다시 구우면 같은 것이 나온다.
    /// </summary>
    public static class TownSceneBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/Town.unity";
        private const string CellPrefab = "Assets/_Game/Prefabs/UI/GridCell.prefab";
        private const string ItemPrefab = "Assets/_Game/Prefabs/UI/GridItemBlock.prefab";
        private const string DragPrefab = "Assets/_Game/Prefabs/UI/DragVisual.prefab";
        private const string BuyRowPrefab = "Assets/_Game/Prefabs/UI/BuyRow.prefab";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";
        private const string WallPrefab = "Assets/_Game/Prefabs/Wall.prefab";
        private const float S = 0.12f;

        // 레이드 UI 와 같은 팔레트를 쓴다. 마을만 따로 놀면 다른 게임처럼 보인다
        private static readonly Color Ink      = new(0.055f, 0.052f, 0.058f, 1f);
        private static readonly Color Panel    = new(0.105f, 0.10f, 0.108f, 0.97f);
        private static readonly Color PanelLit = new(0.145f, 0.135f, 0.125f, 0.97f);
        private static readonly Color Parchment= new(0.86f, 0.84f, 0.78f, 1f);
        private static readonly Color Dim      = new(0.62f, 0.60f, 0.56f, 1f);
        private static readonly Color Gold     = new(0.91f, 0.77f, 0.41f, 1f);
        private static readonly Color Ember    = new(0.85f, 0.42f, 0.18f, 1f);

        // 광장
        private static readonly Color Plaza     = new(0.58f, 0.52f, 0.43f);
        private static readonly Color BldWall   = new(0.80f, 0.76f, 0.68f);
        private static readonly Color BldRoof   = new(0.42f, 0.27f, 0.20f);
        private static readonly Color DoorCol   = new(0.16f, 0.11f, 0.08f);
        private static readonly Color SpawnCol  = new(0.55f, 0.70f, 0.92f, 0.55f);
        private static readonly Color RaidCol   = new(0.45f, 0.85f, 0.62f, 0.5f);

        private static Sprite _square, _ground;

        [MenuItem("Capstone/씬 굽기/마을")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[TownSceneBuilder] 플레이 모드에서는 씬을 구울 수 없다. 먼저 정지할 것.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Square) == null) EnvSpriteGenerator.BakeAll();
            _square = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Square);
            _ground = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Ground);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            BuildEventSystem();
            BuildWorld(out var gateZone);
            var player = BuildPlayer();
            var canvas = BuildCanvas();
            BuildScreen(canvas, gateZone, player);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();

            Debug.Log($"[TownSceneBuilder] 마을 씬을 구웠다 - {ScenePath}");
        }

        /// <summary>테스트용. 창고와 소지금을 처음 상태로 되돌린다.</summary>
        [MenuItem("Capstone/세션 초기화 (창고 · 골드)")]
        public static void ResetSession()
        {
            GameSession.ResetAll();
            Debug.Log("[TownSceneBuilder] 세션을 초기화했다 - 창고 · 보관함 비움, 골드 0");
        }

        private static Vector2 Px(float x, float y) => new((x - 493f) * S, (707.5f - y) * S);

        private static Rect PxRect(float x0, float y0, float x1, float y1)
        {
            Vector2 a = Px(x0, y1), b = Px(x1, y0);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        private static Vector2 SpawnPos => PxRect(449, 748, 522, 800).center;

        // ---------- 뼈대 ----------
        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ink;
            go.AddComponent<UniversalAdditionalCameraData>();
            go.AddComponent<AudioListener>();
            go.AddComponent<Player.CameraFollow>();
            go.transform.position = new Vector3(SpawnPos.x, SpawnPos.y + 1f, -10f);

            // 마을은 낮이다 - 레이드와 달리 전역광을 그대로 켠다
            var light = new GameObject("Global Light 2D").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.color = new Color(1f, 0.97f, 0.92f);
        }

        private static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();   // 프로젝트가 새 Input System 을 쓴다
        }

        // ---------- 광장 ----------
        private static void BuildWorld(out Collider2D gateZone)
        {
            var world = new GameObject("World").transform;
            Rect plaza = PxRect(273, 597, 713, 818);

            const string forest = "Assets/_Game/Art/Env/Forest/";
            var fern = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "ground_fern.png");
            var dirt = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "ground_dirt.png");
            var edge = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "road_edge.png");
            var disc = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "dirt_disc.png");
            if (fern != null && dirt != null && edge != null && disc != null)
            {
                // 원화 바닥 - 광장 전체를 흙으로 깔았더니 갈색 판이 너무 넓어 보였다 (피드백).
                // 바닥은 고사리, 사람이 다니는 곳만 흙길: 스폰 → 큰길 → 각 가게 문 앞 / 레이드 지역
                TiledSprite("Ground", fern, plaza.center, -60, plaza.size + Vector2.one * 30f, world);
                var paths = new GameObject("Paths").transform;
                paths.SetParent(world, false);
                float[][] lines =
                {
                    new float[] { 320, 704, 666, 704 },                 // 가게 앞 큰길
                    new float[] { 485, 760, 485, 704 },                 // 스폰 → 큰길
                    new float[] { 357, 704, 357, 676 },                 // → 대장간 문
                    new float[] { 497, 704, 497, 676 },                 // → 잡화점 문
                    new float[] { 639, 704, 639, 676 },                 // → 의료소 문
                    new float[] { 600, 704, 625, 745 },                 // → 레이드 지역
                };
                int k = 0;
                foreach (var l in lines)
                    for (int i = 0; i + 3 < l.Length; i += 2)
                        Path(paths, dirt, edge, disc, Px(l[i], l[i + 1]), Px(l[i + 2], l[i + 3]), 3.2f, k++);
            }
            else
            {
                var ground = Sprite("Ground", _ground, plaza.center, -30, Plaza, world);
                var sr = ground.GetComponent<SpriteRenderer>();
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = plaza.size + Vector2.one * 24f;
            }

            // 광장 테두리 - 막는 건 보이지 않는 벽, 보이는 건 둘레에 심은 가문비 · 덤불.
            // 갈색 막대 담장은 덜 만든 것처럼 보였다
            const float t = 0.6f;
            foreach (var w in new[]
            {
                Wall("Fence_N", world, new Rect(plaza.xMin - t, plaza.yMax, plaza.width + t * 2, t)),
                Wall("Fence_S", world, new Rect(plaza.xMin - t, plaza.yMin - t, plaza.width + t * 2, t)),
                Wall("Fence_W", world, new Rect(plaza.xMin - t, plaza.yMin, t, plaza.height)),
                Wall("Fence_E", world, new Rect(plaza.xMax, plaza.yMin, t, plaza.height)),
            }) w.GetComponent<SpriteRenderer>().enabled = false;
            PlantBorder(world, plaza);

            var smith = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>("Assets/_Game/Items/Item_Bolt.asset");
            var powder = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>("Assets/_Game/Items/Item_Gunpowder.asset");
            var oil = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>("Assets/_Game/Items/Item_Oil.asset");
            var catalyst = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>("Assets/_Game/Items/Item_Catalyst.asset");
            var bandage = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>("Assets/_Game/Items/Item_Bandage.asset");

            Shop(world, "대장간", PxRect(302, 619, 412, 671), 357,
                 "모루 소리가 멎는다. \"쇠붙이든 화약이든, 쓸 만한 거면 쳐주지.\"", smith, powder);
            Shop(world, "잡화점", PxRect(438, 619, 556, 671), 497,
                 "기름 냄새가 밴 선반. \"등유 떨어지면 밤에 못 버틴다고.\"", oil, catalyst);
            Shop(world, "의료소", PxRect(580, 619, 698, 671), 639,
                 "약초 연기가 자욱하다. \"다친 데는? 없으면 붕대나 챙겨 가.\"", bandage);

            // 스폰 위치
            // 흙 공터 위에 옅은 원판으로 표시한다. 네모 판을 바닥에 깔았더니 흙길 테두리가 위를 덮어 지저분했다
            Rect spawn = PxRect(449, 748, 522, 800);
            var soft = AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.SoftCircle);
            Clearing(world, spawn.center, 7.5f);
            var sp = Sprite("SpawnPoint", soft, spawn.center, -27, new Color(0.55f, 0.72f, 1f, 0.45f), world);
            sp.transform.localScale = new Vector3(spawn.width * 1.1f, spawn.height * 1.1f, 1f);
            WorldLabel("SpawnLabel", world, spawn.center + Vector2.down * (spawn.height * 0.5f + 0.6f), "스폰 위치", 3.2f, new Color(0.75f, 0.85f, 1f));

            // 레이드 지역 - 들어서면 RaidGate 가 이동할지 묻는다
            Rect raid = PxRect(588, 726, 676, 792);
            Clearing(world, raid.center, 9f);
            var gate = Sprite("RaidZone", soft, raid.center, -27, new Color(0.45f, 0.95f, 0.6f, 0.5f), world);
            gate.transform.localScale = new Vector3(raid.width * 1.1f, raid.height * 1.1f, 1f);
            var col = gate.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = Vector2.one / 1.1f;          // 판정은 그림의 원래 네모 크기 그대로
            gateZone = col;
            WorldLabel("RaidLabel", world, raid.center + Vector2.down * (raid.height * 0.5f + 0.6f), "레이드 지역", 3.2f, new Color(0.65f, 0.95f, 0.75f));
        }

        /// <summary>
        /// 건물 한 채. 들어갈 수는 없고(거래만 한다) 벽 덩어리 + 지붕 + 문 + 간판.
        /// 문 앞에 상호작용 트리거를 둔다 - 플레이어의 Interactor 가 이걸 집는다.
        /// </summary>
        private static void Shop(Transform parent, string name, Rect rect, float doorPx, string greeting,
                                 params Items.ItemDefinition[] stock)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);

            // 막는 건 건물 전체 크기의 콜라이더 하나, 보이는 건 앞벽(아래 40%)과 지붕(위 60%) 두 장.
            // 둘을 겹치지 않게 나눠야 Y 정렬이 맞는다 - 겹치면 위쪽(지붕)이 뒤로 가서 앞벽에 가린다
            var body = Wall("Body", root, rect);
            body.GetComponent<SpriteRenderer>().enabled = false;

            float frontH = rect.height * 0.4f;
            float doorX = Px(doorPx, 0).x;
            const float doorW = 1.8f, doorH = 1.6f;

            // 앞벽은 문 양옆 두 장 + 문 위 한 장으로 나눈다. 문과 벽이 겹치면 Y 정렬에서 문이 벽 뒤로 숨는다
            float y0 = rect.yMin, y1 = rect.yMin + frontH;
            Block(root, "FrontWall_L", Rect.MinMaxRect(rect.xMin, y0, doorX - doorW * 0.5f, y1), BldWall);
            Block(root, "FrontWall_R", Rect.MinMaxRect(doorX + doorW * 0.5f, y0, rect.xMax, y1), BldWall);
            Block(root, "FrontWall_Top", Rect.MinMaxRect(doorX - doorW * 0.5f, y0 + doorH, doorX + doorW * 0.5f, y1), BldWall);
            Block(root, "Door", Rect.MinMaxRect(doorX - doorW * 0.5f, y0, doorX + doorW * 0.5f, y0 + doorH), DoorCol);

            Rect roofRect = new Rect(rect.xMin - 0.3f, y1, rect.width + 0.6f, rect.height - frontH + 0.3f);
            Block(root, "Roof", roofRect, BldRoof);

            WorldLabel("Sign", root, new Vector2(rect.center.x, roofRect.center.y), name, 5f, Parchment);

            // 문 앞 상호작용 지점
            var spot = new GameObject("Entrance");
            spot.transform.SetParent(root, false);
            spot.layer = LayerMask.NameToLayer("Interactable");     // Interactor 는 이 레이어만 훑는다
            spot.transform.position = new Vector2(doorX, rect.yMin - 1.0f);
            var trigger = spot.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 1.6f;
            var shop = spot.AddComponent<TownShop>();
            var so = new SerializedObject(shop);
            so.FindProperty("shopName").stringValue = name;
            so.FindProperty("greeting").stringValue = greeting;
            var arr = so.FindProperty("stock");
            arr.arraySize = stock.Length;
            for (int i = 0; i < stock.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = stock[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject BuildPlayer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "Player";
            go.transform.position = SpawnPos;

            // 마을은 밝고 안전하다. 시야 빛 · 횃불 · 석궁 · 어둠 패널티 · 소음 원은 끈다
            var vision = go.GetComponent<Player.VisionController>();
            if (vision) vision.enabled = false;
            foreach (var n in new[] { "ProximityLight", "ConeLight" })
            {
                var t = go.transform.Find(n);
                if (t) t.gameObject.SetActive(false);
            }
            var torch = go.GetComponent<Player.TorchFuel>();
            if (torch) torch.enabled = false;
            var xbow = go.GetComponent<Combat.Crossbow>();
            if (xbow) xbow.enabled = false;
            var dark = go.GetComponent<Player.DarknessPenalty>();
            if (dark) dark.enabled = false;
            var steps = go.GetComponent<Player.Footsteps>();
            if (steps)
            {
                var so = new SerializedObject(steps);
                so.FindProperty("showRings").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return go;
        }

        private static Canvas BuildCanvas()
        {
            var go = new GameObject("TownUI");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ---------- 화면 ----------
        private static void BuildScreen(Canvas canvas, Collider2D gateZone, GameObject player)
        {
            var root = canvas.transform;

            // 머리말 - 지난 판 결과
            var title = Label("Title", root, new Vector2(900f, 60f), new Vector2(-480f, 495f), "잿불 마을", 44f, TextAlignmentOptions.Left, Parchment);
            title.fontStyle = FontStyles.Bold;
            var headline = Label("Headline", root, new Vector2(1400f, 40f), new Vector2(-230f, 448f),
                                "", 26f, TextAlignmentOptions.Left, Parchment);
            var goldText = Label("Gold", root, new Vector2(500f, 60f), new Vector2(680f, 495f),
                                "0 G", 44f, TextAlignmentOptions.Right, Gold);
            goldText.fontStyle = FontStyles.Bold;

            Corner(title, new Vector2(0f, 1f), new Vector2(40f, -30f));
            Corner(headline, new Vector2(0f, 1f), new Vector2(40f, -92f));
            Corner(goldText, new Vector2(1f, 1f), new Vector2(-40f, -30f));

            var prompt = Label("Prompt", root, new Vector2(1200f, 50f), new Vector2(0f, -440f), "", 30f, TextAlignmentOptions.Center, Parchment);

            var hud = canvas.gameObject.AddComponent<TownHUD>();
            SetRef(hud, "promptText", prompt);
            SetRef(hud, "goldText", goldText);
            SetRef(hud, "interactor", player.GetComponent<Items.Interactor>());

            // ---------- 거래창 ----------
            var window = Node("TradeWindow", root, new Vector2(1780f, 900f), new Vector2(0f, -20f));
            Plate(window, Panel);
            var shopTitle = Label("ShopTitle", window, new Vector2(900f, 60f), new Vector2(-400f, 400f), "가게", 44f, TextAlignmentOptions.Left, Parchment);
            shopTitle.fontStyle = FontStyles.Bold;
            var greeting = Label("Greeting", window, new Vector2(1300f, 40f), new Vector2(-200f, 352f), "", 24f, TextAlignmentOptions.Left, Dim);
            var close = MakeButton("CloseButton", window, new Vector2(220f, 56f), new Vector2(750f, 400f), "닫기  [ESC]", PanelLit, Parchment);

            var stash = GridPanel("Stash", window, new Vector2(-470f, 40f), 52f, "창고");
            var stashValue = Label("StashValue", window, new Vector2(700f, 34f), new Vector2(-470f, -260f),
                                  "창고가 비었다", 22f, TextAlignmentOptions.Center, Dim);
            var secure = GridPanel("Secure", window, new Vector2(30f, 140f), 52f, "중요 보관함");
            Label("SecureNote", window, new Vector2(300f, 80f), new Vector2(30f, -10f),
                 "레이드에 들고 가고\n죽어도 남는다", 20f, TextAlignmentOptions.Center, Dim);

            // 판매
            var sell = Node("SellZone", window, new Vector2(560f, 200f), new Vector2(520f, 210f));
            var sellImg = Plate(sell, new Color(0.20f, 0.17f, 0.12f, 0.92f));
            var sellZone = sell.gameObject.AddComponent<SellDropZone>();
            SetRef(sellZone, "plate", sellImg);
            Label("SellLabel", sell, new Vector2(500f, 120f), Vector2.zero,
                 "판매대\n여기에 끌어다 놓으면 값을 쳐준다", 24f, TextAlignmentOptions.Center, Dim);
            var sellAll = MakeButton("SellAllButton", window, new Vector2(300f, 56f), new Vector2(520f, 75f), "전부 팔기", PanelLit, Parchment);

            // 구매
            Label("BuyTitle", window, new Vector2(560f, 40f), new Vector2(520f, 5f), "파는 물건  (산 물건은 창고로)", 24f, TextAlignmentOptions.Center, Parchment);
            var list = Node("BuyList", window, new Vector2(560f, 300f), new Vector2(520f, -170f));
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            var receipt = Label("Receipt", window, new Vector2(1400f, 40f), new Vector2(0f, -410f), "", 26f, TextAlignmentOptions.Center, Parchment);

            var shopUI = canvas.gameObject.AddComponent<ShopUI>();
            SetRef(shopUI, "window", window.gameObject);
            SetRef(shopUI, "titleText", shopTitle);
            SetRef(shopUI, "greetingText", greeting);
            SetRef(shopUI, "closeButton", close);
            SetRef(shopUI, "stashPanel", stash);
            SetRef(shopUI, "securePanel", secure);
            SetRef(shopUI, "sellZone", sellZone);
            SetRef(shopUI, "receiptText", receipt);
            SetRef(shopUI, "stashValueText", stashValue);
            SetRef(shopUI, "sellAllButton", sellAll);
            SetRef(shopUI, "buyList", list);
            SetRef(shopUI, "buyRowPrefab", MakeBuyRowPrefab().GetComponent<Button>());
            window.gameObject.SetActive(false);

            // ---------- 레이드 이동 창 ----------
            var popup = Node("RaidPopup", root, new Vector2(680f, 250f), new Vector2(0f, 120f));
            Plate(popup, new Color(0.09f, 0.085f, 0.09f, 0.97f));
            var q = Label("Question", popup, new Vector2(640f, 60f), new Vector2(0f, 50f), "레이드 지역으로 이동하겠습니까?", 32f, TextAlignmentOptions.Center, Parchment);
            var yes = MakeButton("Yes", popup, new Vector2(220f, 64f), new Vector2(-130f, -55f), "예", new Color(0.24f, 0.13f, 0.07f, 0.97f), Gold);
            var no = MakeButton("No", popup, new Vector2(220f, 64f), new Vector2(130f, -55f), "아니오", PanelLit, Parchment);
            popup.gameObject.SetActive(false);

            var gate = gateZone.gameObject.AddComponent<RaidGate>();
            SetRef(gate, "popup", popup.gameObject);
            SetRef(gate, "yesButton", yes);
            SetRef(gate, "noButton", no);
            SetRef(gate, "questionText", q);

            // ---------- 설명창 ----------
            // 팔기 전에 값어치를 봐야 한다. 레이드의 ItemTooltip 을 같은 구조로 다시 세운다
            // (컴포넌트가 "ItemTooltip" 이라는 이름의 자식을 스스로 찾아 잇는다)
            var tipRoot = Node("ItemTooltip", root, new Vector2(320f, 200f), Vector2.zero);
            Plate(tipRoot, new Color(0.08f, 0.078f, 0.085f, 0.98f));
            TopLeft(tipRoot);
            var bar = Node("GradeBar", tipRoot, new Vector2(320f, 5f), new Vector2(0f, 0f));
            Plate(bar, Ember);
            TopLeft(bar);
            TipLabel("Name",  tipRoot, new Vector2(300f, 34f), new Vector2(10f, -14f), 26f, Parchment);
            TipLabel("Grade", tipRoot, new Vector2(300f, 26f), new Vector2(10f, -48f), 20f, Dim);
            TipLabel("Stat",  tipRoot, new Vector2(300f, 26f), new Vector2(10f, -76f), 20f, Gold);
            TipLabel("Desc",  tipRoot, new Vector2(300f, 84f), new Vector2(10f, -104f), 19f, Dim);
            canvas.gameObject.AddComponent<ItemTooltip>();

            var fade = Node("SceneFade", root, Vector2.zero, Vector2.zero, stretch: true);
            Plate(fade, Color.black).raycastTarget = false;
            fade.gameObject.AddComponent<SceneFade>();
            fade.SetAsLastSibling();

            var town = canvas.gameObject.AddComponent<TownManager>();
            SetRef(town, "headlineText", headline);
            SetRef(gate, "town", town);
        }

        /// <summary>레이드 루팅 화면과 같은 구조의 격자 패널 (Title · Cells · Items/Ghost).</summary>
        private static GridPanelUI GridPanel(string name, Transform parent, Vector2 pos, float cell, string title)
        {
            var grid = Node(name, parent, new Vector2(100f, 100f), pos);
            var panelUI = grid.gameObject.AddComponent<GridPanelUI>();

            var gridTitle = Label("Title", grid, new Vector2(400f, 30f), new Vector2(0f, 6f), title, 26f, TextAlignmentOptions.Center, Parchment);
            var titleRt = (RectTransform)gridTitle.transform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 0f);
            titleRt.anchoredPosition = new Vector2(0f, 6f);

            var cells = Node("Cells", grid, Vector2.zero, Vector2.zero);
            var items = Node("Items", grid, Vector2.zero, Vector2.zero);
            var ghost = Node("Ghost", items, new Vector2(56f, 56f), Vector2.zero);
            var ghostImg = Plate(ghost, new Color(0.30f, 0.75f, 0.40f, 0.35f));
            ghost.gameObject.SetActive(false);
            // 격자 자식은 좌상단 기준으로 깔린다 (GridPanelUI.CellToLocal 이 그렇게 계산한다)
            TopLeft(cells); TopLeft(items); TopLeft(ghost);

            SetRef(panelUI, "titleText", gridTitle);
            SetRef(panelUI, "cellRoot", cells);
            SetRef(panelUI, "itemRoot", items);
            SetRef(panelUI, "ghost", ghostImg);
            SetRef(panelUI, "cellPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefab).GetComponent<Image>());
            SetRef(panelUI, "itemViewPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefab).GetComponent<GridItemView>());
            SetRef(panelUI, "dragVisualPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(DragPrefab).GetComponent<DragVisual>());
            var so = new SerializedObject(panelUI);
            so.FindProperty("cellSize").floatValue = cell;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panelUI;
        }

        /// <summary>파는 물건 한 줄 프리팹 - 개수가 가게마다 달라 런타임에 찍어낸다.</summary>
        private static GameObject MakeBuyRowPrefab()
        {
            var row = Node("BuyRow", null, new Vector2(540f, 64f), Vector2.zero);
            var img = Plate(row, PanelLit);
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.35f, 1.3f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;

            var icon = Node("Icon", row, new Vector2(48f, 48f), Vector2.zero);
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
            icon.anchoredPosition = new Vector2(36f, 0f);
            icon.gameObject.AddComponent<Image>().preserveAspect = true;

            var n = Label("Name", row, new Vector2(320f, 50f), Vector2.zero, "물건", 26f, TextAlignmentOptions.Left, Parchment);
            var nrt = (RectTransform)n.transform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 0.5f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.anchoredPosition = new Vector2(72f, 0f);

            var p = Label("Price", row, new Vector2(160f, 50f), Vector2.zero, "0 G", 26f, TextAlignmentOptions.Right, Gold);
            var prt = (RectTransform)p.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 0.5f);
            prt.pivot = new Vector2(1f, 0.5f);
            prt.anchoredPosition = new Vector2(-16f, 0f);

            var prefab = PrefabUtility.SaveAsPrefabAsset(row.gameObject, BuyRowPrefab);
            Object.DestroyImmediate(row.gameObject);
            return prefab;
        }

        // ---------- 월드 조각 ----------
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

        private static void Block(Transform parent, string name, Rect r, Color color)
        {
            var go = Sprite(name, _square, r.center, 0, color, parent);
            go.transform.localScale = new Vector3(r.width, r.height, 1f);
        }

        private static void TiledSprite(string name, Sprite sprite, Vector2 pos, int order, Vector2 size, Transform parent)
        {
            var go = Sprite(name, sprite, pos, order, Color.white, parent);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
        }

        /// <summary>
        /// 흙길 한 토막. 흙을 이어 붙이고 양옆에 "흙 → 고사리" 띠를 안쪽을 보게 두른다.
        ///
        /// 갈림길 처리 - 토막마다 띠의 정렬 순서를 따로 주고(-40 + index), 이 토막의 흙 영역을 마스크로 만들되
        /// "자기 띠는 빼고 다른 토막 띠만" 가리도록 범위를 둘로 나눈다. 그러면 큰길 띠는 갈림길 흙에서 끊기고,
        /// 갈림길 띠는 큰길 흙에서 끊겨서 안쪽 모서리가 L 자로 만난다.
        /// (전부 가리는 마스크를 쓰면 자기 띠까지 사라지고, 안 가리면 남의 띠가 흙 위를 가로지른다)
        /// </summary>
        private static void Path(Transform parent, Sprite dirt, Sprite edge, Sprite disc, Vector2 a, Vector2 b, float width, int index)
        {
            float len = Vector2.Distance(a, b);
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            Vector2 mid = (a + b) * 0.5f;
            int own = EdgeOrderBase + index;

            TiledSprite("Path", dirt, mid, DirtOrder, new Vector2(len, width), parent);
            parent.GetChild(parent.childCount - 1).rotation = Quaternion.Euler(0f, 0f, ang);
            MaskExcept(parent, mid, ang, new Vector2(len, width), false, own);

            Vector2 dir = (b - a) / Mathf.Max(0.001f, len), n = new(-dir.y, dir.x);
            foreach (float side in new[] { 1f, -1f })
            {
                var e = Sprite("PathEdge", edge, mid + n * side * (width * 0.5f + 0.5f), own, Color.white, parent);
                Vector2 up = -n * side;
                e.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg - 90f);
                var sr = e.GetComponent<SpriteRenderer>();
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
                sr.size = new Vector2(len, 2f);
                sr.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            }
            foreach (var p in new[] { a, b })
            {
                var j = Sprite("PathJoint", disc, p, DirtOrder, Color.white, parent);
                j.transform.localScale = Vector3.one * (width * 1.25f / 3f);
                MaskExcept(parent, p, 0f, Vector2.one * width, true, own);
            }
        }

        private const int DirtOrder = -45;          // 흙길 · 공터
        private const int EdgeOrderBase = -40;      // 길 테두리 띠 (토막마다 +1)

        /// <summary>own 순서의 띠만 빼고 나머지 띠를 가리는 마스크 (범위 두 개로 "제외"를 만든다).</summary>
        private static void MaskExcept(Transform parent, Vector2 pos, float ang, Vector2 size, bool round, int own)
        {
            var lo = Mask(parent, pos, ang, size, round);
            lo.isCustomRangeActive = true; lo.backSortingOrder = -1000; lo.frontSortingOrder = own - 1;
            var hi = Mask(parent, pos, ang, size, round);
            hi.isCustomRangeActive = true; hi.backSortingOrder = own; hi.frontSortingOrder = 1000;
        }

        /// <summary>광장 둘레에 가문비와 덤불을 심는다 (경계 안쪽은 비워 둔다).</summary>
        private static void PlantBorder(Transform parent, Rect plaza)
        {
            const string forest = "Assets/_Game/Art/Env/Forest/";
            var spruce = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "spruce_small.png");
            var big = AssetDatabase.LoadAssetAtPath<Sprite>(forest + "spruce_big.png");
            var bushes = new[] { "bush_fern", "bush_leaf", "bush_leaf_2" };
            if (spruce == null) return;
            var root = new GameObject("Border").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(77);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            void Plant(Vector2 p)
            {
                double roll = rng.NextDouble();
                Sprite sp = roll < 0.35 ? spruce : roll < 0.5 ? big
                          : AssetDatabase.LoadAssetAtPath<Sprite>(forest + bushes[rng.Next(3)] + ".png");
                var go = Sprite("Plant", sp, p, 0, Color.white * R(0.85f, 1f) + new Color(0, 0, 0, 1), root);
                float sc = sp == big ? R(0.4f, 0.55f) : sp == spruce ? R(0.9f, 1.25f) : R(0.5f, 0.75f);
                go.transform.localScale = Vector3.one * sc;
                var sr = go.GetComponent<SpriteRenderer>();
                sr.spriteSortPoint = SpriteSortPoint.Pivot;
                sr.flipX = rng.NextDouble() < 0.5;
            }
            // 위 · 아래 두 줄, 좌 · 우 두 줄 - 담장 선 바깥으로 살짝 들쭉날쭉하게
            for (float x = plaza.xMin - 1f; x <= plaza.xMax + 1f; x += 2.4f)
            {
                Plant(new Vector2(x + R(-0.6f, 0.6f), plaza.yMax + R(0.6f, 1.6f)));
                Plant(new Vector2(x + R(-0.6f, 0.6f), plaza.yMin - R(0.3f, 1.2f)));
            }
            for (float y = plaza.yMin; y <= plaza.yMax; y += 2.4f)
            {
                Plant(new Vector2(plaza.xMin - R(0.6f, 1.6f), y + R(-0.6f, 0.6f)));
                Plant(new Vector2(plaza.xMax + R(0.6f, 1.6f), y + R(-0.6f, 0.6f)));
            }
        }

        /// <summary>흙 공터 - 둥근 흙을 크게 깔고, 고사리 테두리가 그 안을 가로지르지 않게 막는다.</summary>
        private static void Clearing(Transform parent, Vector2 pos, float diameter)
        {
            var disc = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/Art/Env/Forest/dirt_disc.png");
            if (disc == null) return;
            var d = Sprite("Clearing", disc, pos, DirtOrder, Color.white, parent);
            d.transform.localScale = Vector3.one * (diameter / 3f);
            Mask(parent, pos, 0f, Vector2.one * (diameter * 0.75f), true);     // 공터 안에는 어떤 띠도 안 그린다
        }

        private static SpriteMask Mask(Transform parent, Vector2 pos, float ang, Vector2 size, bool round)
        {
            var go = new GameObject("PathMask");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var m = go.AddComponent<SpriteMask>();
            m.sprite = round ? AssetDatabase.LoadAssetAtPath<Sprite>(EnvSpriteGenerator.Circle) : _square;
            m.alphaCutoff = 0.5f;
            return m;
        }

        /// <summary>광장 가장자리 띠. 띠의 위쪽(흙)이 광장 안을 보도록 돌린다 (rot 0 = 위가 북쪽).</summary>
        private static void PlazaEdge(Transform parent, Sprite edge, Vector2 pos, float rot, float length)
        {
            var go = Sprite("PlazaEdge", edge, pos, -29, Color.white, parent);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rot);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(length, 2f);
        }

        private static GameObject Wall(string name, Transform parent, Rect rect)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefab), parent);
            go.name = name;
            go.transform.position = rect.center;
            go.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = _square;
            sr.color = new Color(0.36f, 0.30f, 0.24f);
            return go;
        }

        private static void WorldLabel(string name, Transform parent, Vector2 pos, string text, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.sizeDelta = new Vector2(14f, 2f);
            t.sortingOrder = 20;
        }

        // ---------- UI 조각 ----------
        private static RectTransform Node(string name, Transform parent, Vector2 size, Vector2 pos, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
            }
            return rt;
        }

        /// <summary>화면 구석에 붙인다. 화면비가 달라도 글자가 잘리지 않게.</summary>
        private static void Corner(TMP_Text t, Vector2 corner, Vector2 offset)
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.anchoredPosition = offset;
        }

        /// <summary>격자 자식처럼 좌상단을 원점으로 삼아야 하는 것들.</summary>
        private static void TopLeft(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        private static Image Plate(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TMP_Text Label(string name, Transform parent, Vector2 size, Vector2 pos,
                                     string content, float fontSize, TextAlignmentOptions align, Color color)
        {
            var rt = Node(name, parent, size, pos);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static void TipLabel(string name, Transform parent, Vector2 size, Vector2 pos, float fontSize, Color color)
        {
            var t = Label(name, parent, size, pos, "", fontSize, TextAlignmentOptions.TopLeft, color);
            TopLeft((RectTransform)t.transform);
            ((RectTransform)t.transform).anchoredPosition = pos;
        }

        private static Button MakeButton(string name, Transform parent, Vector2 size, Vector2 pos,
                                     string label, Color plate, Color ink)
        {
            var rt = Node(name, parent, size, pos);
            var img = Plate(rt, plate);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color(plate.r * 1.45f, plate.g * 1.45f, plate.b * 1.45f, plate.a);
            colors.pressedColor = new Color(plate.r * 0.8f, plate.g * 0.8f, plate.b * 0.8f, plate.a);
            colors.disabledColor = new Color(plate.r, plate.g, plate.b, plate.a * 0.45f);
            btn.colors = colors;

            var t = Label("Label", rt, size, Vector2.zero, label, 28f, TextAlignmentOptions.Center, ink);
            t.raycastTarget = false;
            return btn;
        }

        /// <summary>private [SerializeField] 에 값을 꽂는다.</summary>
        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[TownSceneBuilder] {target.GetType().Name} 에 '{field}' 필드가 없다.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes) if (s.path == ScenePath) return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[TownSceneBuilder] Build Settings 에 마을 씬을 등록했다.");
        }
    }
}
