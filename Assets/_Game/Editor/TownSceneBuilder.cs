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

namespace Capstone.EditorTools
{
    /// <summary>
    /// 마을 씬을 코드로 굽는다 (메뉴 Capstone/씬 굽기/마을).
    ///
    /// 씬을 손으로 만들어 두면 왜 그렇게 생겼는지가 어디에도 안 남는다.
    /// 스프라이트 생성기와 같은 이유로 여기에 스크립트로 남긴다 - 다시 구우면 같은 것이 나온다.
    /// 격자 · 아이템 블록 · 드래그 블록은 레이드에서 쓰던 프리팹을 그대로 꽂는다.
    /// </summary>
    public static class TownSceneBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/Town.unity";
        private const string CellPrefab = "Assets/_Game/Prefabs/UI/GridCell.prefab";
        private const string ItemPrefab = "Assets/_Game/Prefabs/UI/GridItemBlock.prefab";
        private const string DragPrefab = "Assets/_Game/Prefabs/UI/DragVisual.prefab";

        // 레이드 UI 와 같은 팔레트를 쓴다. 마을만 따로 놀면 다른 게임처럼 보인다
        private static readonly Color Ink      = new(0.055f, 0.052f, 0.058f, 1f);
        private static readonly Color Panel    = new(0.105f, 0.10f, 0.108f, 0.96f);
        private static readonly Color PanelLit = new(0.145f, 0.135f, 0.125f, 0.96f);
        private static readonly Color Parchment= new(0.86f, 0.84f, 0.78f, 1f);
        private static readonly Color Dim      = new(0.62f, 0.60f, 0.56f, 1f);
        private static readonly Color Gold     = new(0.91f, 0.77f, 0.41f, 1f);
        private static readonly Color Ember    = new(0.85f, 0.42f, 0.18f, 1f);

        [MenuItem("Capstone/씬 굽기/마을")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[TownSceneBuilder] 플레이 모드에서는 씬을 구울 수 없다. 먼저 정지할 것.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            BuildEventSystem();
            var canvas = BuildCanvas();
            BuildScreen(canvas);

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
            Debug.Log("[TownSceneBuilder] 세션을 초기화했다 - 창고 비움, 골드 0");
        }

        // ---------- 뼈대 ----------
        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ink;
            go.AddComponent<UniversalAdditionalCameraData>();
            go.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();   // 프로젝트가 새 Input System 을 쓴다
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
        private static void BuildScreen(Canvas canvas)
        {
            var root = canvas.transform;

            // 배경 - 위는 그을린 하늘, 아래는 흙바닥. 판 두 장으로 지평선을 만든다
            Plate(Node("Sky", root, new Vector2(0f, 0f), new Vector2(0f, 0f), stretch: true), new Color(0.075f, 0.072f, 0.082f, 1f));
            var ground = Node("Ground", root, new Vector2(1920f, 420f), new Vector2(0f, -330f));
            Plate(ground, new Color(0.095f, 0.082f, 0.072f, 1f));
            var horizon = Node("Horizon", root, new Vector2(1920f, 3f), new Vector2(0f, -120f));
            Plate(horizon, new Color(0.24f, 0.17f, 0.11f, 0.9f));

            // 머리말
            var title = Label("Title", root, new Vector2(900f, 70f), new Vector2(-460f, 430f), "잿불 상회", 52f, TextAlignmentOptions.Left, Parchment);
            title.fontStyle = FontStyles.Bold;
            Label("Subtitle", root, new Vector2(900f, 40f), new Vector2(-460f, 382f),
                 "들고 나온 것만이 내 것이 된다", 24f, TextAlignmentOptions.Left, Dim);

            // 폭 1400 · x -210 이면 왼쪽 끝이 제목과 같은 -910 에 선다.
            // 더 넓히면 화면 밖으로 나가 글자가 잘린다 (1920 기준 왼쪽 끝은 -960)
            var headline = Label("Headline", root, new Vector2(1400f, 40f), new Vector2(-210f, 338f),
                                "", 26f, TextAlignmentOptions.Left, Parchment);

            var goldText = Label("Gold", root, new Vector2(500f, 70f), new Vector2(660f, 430f),
                                "0 G", 46f, TextAlignmentOptions.Right, Gold);
            goldText.fontStyle = FontStyles.Bold;

            // ---------- 창고 ----------
            var stashPlate = Node("StashPanel", root, new Vector2(780f, 620f), new Vector2(-470f, -20f));
            Plate(stashPlate, Panel);

            var grid = Node("Grid", stashPlate, new Vector2(705f, 469f), new Vector2(0f, -20f));
            var panelUI = grid.gameObject.AddComponent<GridPanelUI>();

            // 제목은 격자 위로 얹는다 (레이드 쪽 격자와 같은 배치)
            var gridTitle = Label("Title", grid, new Vector2(400f, 30f), new Vector2(0f, 6f), "창고", 28f, TextAlignmentOptions.Center, Parchment);
            var titleRt = (RectTransform)gridTitle.transform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 0f);
            titleRt.anchoredPosition = new Vector2(0f, 6f);

            var cells = Node("Cells", grid, new Vector2(0f, 0f), Vector2.zero);
            var items = Node("Items", grid, new Vector2(0f, 0f), Vector2.zero);
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

            var stashValue = Label("StashValue", stashPlate, new Vector2(740f, 34f), new Vector2(0f, -272f),
                                  "창고가 비었다", 24f, TextAlignmentOptions.Center, Dim);

            // ---------- 판매대 ----------
            var shopPlate = Node("ShopPanel", root, new Vector2(600f, 620f), new Vector2(480f, -20f));
            Plate(shopPlate, Panel);

            Label("ShopTitle", shopPlate, new Vector2(560f, 44f), new Vector2(0f, 258f),
                 "판매대", 30f, TextAlignmentOptions.Center, Parchment);

            var sell = Node("SellZone", shopPlate, new Vector2(520f, 300f), new Vector2(0f, 60f));
            var sellImg = Plate(sell, new Color(0.20f, 0.17f, 0.12f, 0.92f));
            var sellZone = sell.gameObject.AddComponent<SellDropZone>();
            SetRef(sellZone, "plate", sellImg);
            Label("SellLabel", sell, new Vector2(460f, 120f), new Vector2(0f, 0f),
                 "여기에 끌어다 놓으면\n그 자리에서 값을 쳐준다", 26f, TextAlignmentOptions.Center, Dim);

            var receipt = Label("Receipt", shopPlate, new Vector2(560f, 40f), new Vector2(0f, -120f),
                               "", 24f, TextAlignmentOptions.Center, Parchment);

            var sellAll = MakeButton("SellAllButton", shopPlate, new Vector2(320f, 66f), new Vector2(0f, -200f),
                                 "전부 팔기", PanelLit, Parchment);

            // ---------- 출격 ----------
            var depart = MakeButton("DepartButton", root, new Vector2(420f, 92f), new Vector2(0f, -410f),
                                "출격  [Space]", new Color(0.24f, 0.13f, 0.07f, 0.97f), Gold);
            var departLabel = depart.transform.Find("Label").GetComponent<TMP_Text>();
            departLabel.fontSize = 34f;
            departLabel.fontStyle = FontStyles.Bold;

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

            // ---------- 배선 ----------
            var town = canvas.gameObject.AddComponent<TownManager>();
            SetRef(town, "headlineText", headline);
            SetRef(town, "raidButton", depart);

            var shop = canvas.gameObject.AddComponent<ShopUI>();
            SetRef(shop, "stashPanel", panelUI);
            SetRef(shop, "sellZone", sellZone);
            SetRef(shop, "goldText", goldText);
            SetRef(shop, "receiptText", receipt);
            SetRef(shop, "stashValueText", stashValue);
            SetRef(shop, "sellAllButton", sellAll);
        }

        // ---------- 조각 만들기 ----------
        private static RectTransform Node(string name, Transform parent, Vector2 size, Vector2 pos, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
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
