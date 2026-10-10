using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Capstone.Player;
using Capstone.Enemy;
using Capstone.UI;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 피드백(2026-10) 반영분 중 프리팹에 들어가는 것을 한 번에 건다 (메뉴 Capstone/피드백 적용/프리팹).
    /// 여러 번 눌러도 결과가 같다 - 이미 걸린 것은 건너뛰고, 크기는 "기준값 x 배율"로 덮어쓴다.
    ///
    ///   - 캐릭터 · 적 · 상자 크기 2배 (프로토타입에서 에디터로 스케일 2를 줬을 때의 크기)
    ///   - 발소리 + 소음 원, 어둠 패널티(속삭임 · 가장자리 어둠)
    ///   - 감염체(근접) = 소리로만 찾는다, 이교도(원거리) = 기존 시야
    ///   - 루팅 화면: 가방 ↔ 털 대상 자리 교체, 칸 2배, 중요 보관함 3x3, 감정 소리
    /// </summary>
    public static class FeedbackPrefabSetup
    {
        private const string Prefabs = "Assets/_Game/Prefabs";
        private const float WorldScale = 2f;
        private const float CellSize = 112f;           // 56 x 2

        [MenuItem("Capstone/피드백 적용/프리팹")]
        public static void Apply()
        {
            var ring = MakeNoiseRing();
            FixEnemyPivots();
            SetupPlayer(ring);
            SetupEnemy($"{Prefabs}/Enemy_Melee.prefab", true);
            SetupEnemy($"{Prefabs}/Enemy_Ranged.prefab", false);
            foreach (var guid in AssetDatabase.FindAssets("LootBox", new[] { Prefabs }))
                ScaleRoot(AssetDatabase.GUIDToAssetPath(guid));
            SetupHud();
            ApplyCombatSystems();
            AssetDatabase.SaveAssets();
            Debug.Log("[FeedbackPrefabSetup] 프리팹에 피드백을 반영했다.");
        }

        // ---------- 소음 원 ----------
        private static NoiseRing MakeNoiseRing()
        {
            const string path = Prefabs + "/FX/NoiseRing.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<NoiseRing>(path);
            if (existing != null) return existing;

            var go = new GameObject("NoiseRing");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/UnlitSprite.mat");
            line.loop = true;
            line.useWorldSpace = false;
            line.numCornerVertices = 2;
            line.sortingOrder = 40;              // 바닥 · 캐릭터보다 위. 소리는 가려지지 않는다
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<NoiseRing>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NoiseRing>();
        }

        // ---------- 플레이어 ----------
        private static void SetupPlayer(NoiseRing ring)
        {
            const string path = Prefabs + "/Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // 크기 - 그림과 몸통(허트박스)과 발자국(이동 콜라이더)을 같이 키운다.
                // 루트를 키우면 자식 빛까지 커져 시야 반경이 두 배가 되므로 루트는 1 로 둔다
                Child(root, "Sprite").localScale = Vector3.one * WorldScale;
                // 애니메이션 스프라이트는 중앙 피벗으로 구워져 있다(PlayerAnimationBaker). 그대로 두면 그림의 발이
                // 루트(=이동 콜라이더 · 허트박스 · 빛의 기준인 발밑)보다 반 키만큼 아래로 내려간다.
                // 크기가 1배일 땐 0.6m 라 티가 덜 났지만 2배면 1.25m 라 벽 앞에서 바로 보인다 - 그림을 반 키만큼 올린다
                // (2026-10 원화부터는 발밑 피벗이라 다시 0 - NewArtImporter 참고)
                Child(root, "Sprite").localPosition = Vector3.zero;
                // 그림을 올리면 Y 정렬 기준(스프라이트 중심)도 같이 올라가서, 벽 바로 아래에 서면 머리가 벽에 가려진다.
                // SortingGroup 을 루트(발밑)에 달아 플레이어 전체를 발 위치로 정렬한다
                if (root.GetComponent<UnityEngine.Rendering.SortingGroup>() == null)
                    root.AddComponent<UnityEngine.Rendering.SortingGroup>();
                Child(root, "Hurtbox").localScale = Vector3.one * WorldScale;
                var cap = root.GetComponent<CapsuleCollider2D>();
                cap.size = new Vector2(0.44f, 0.20f) * WorldScale;
                cap.offset = new Vector2(0f, 0.10f) * WorldScale;
                Child(root, "StaminaDial").localPosition = new Vector3(-0.39f, 0.26f, 0f) * WorldScale;

                // 시야 크기 전용 컴포넌트 - 처음 붙일 때만 기존 값(근접 4m · 원뿔 4/9m · 90/70도)으로 채운다.
                // 이미 있으면 손대지 않는다 (에디터에서 고친 값을 덮어쓰지 않게)
                if (root.GetComponent<PlayerVisionSize>() == null)
                {
                    var vs = root.AddComponent<PlayerVisionSize>();
                    vs.proximityRadius = 4f;
                    vs.coneRangeUnlit = 4f;
                    vs.coneRangeLit = 9f;
                    vs.fovNormal = 90f;
                    vs.fovAiming = 70f;
                    vs.edgeBlur = 0.85f;
                }

                // 빛이 벽 · 물건을 뚫지 않고 그림자를 드리운다 (벽 쪽에는 ShadowCaster2D 를 맵 빌더가 단다)
                foreach (var light in root.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>(true))
                {
                    light.shadowsEnabled = true;
                    light.shadowIntensity = 0.95f;
                    light.shadowSoftness = 0.3f;
                }

                var interactor = root.GetComponent<Items.Interactor>();
                Set(interactor, "radius", 1.6f * 1.5f);
                Set(interactor, "breakOffDistance", 2.2f * 1.5f);

                // 발소리
                var src = root.GetComponent<AudioSource>();
                if (src == null) src = root.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                var steps = root.GetComponent<Footsteps>();
                if (steps == null) steps = root.AddComponent<Footsteps>();
                SetRef(steps, "source", src);
                SetRef(steps, "ringPrefab", ring);
                SetClips(steps, "walkClips", "step_walk_");
                SetClips(steps, "runClips", "step_run_");

                // 어둠 패널티 - 속삭임은 자식 오디오에서 따로 돈다 (발소리와 음량을 따로 만지기 위해)
                var whisperT = root.transform.Find("Whisper");
                if (whisperT == null)
                {
                    whisperT = new GameObject("Whisper").transform;
                    whisperT.SetParent(root.transform, false);
                }
                var ws = whisperT.GetComponent<AudioSource>();
                if (ws == null) ws = whisperT.gameObject.AddComponent<AudioSource>();
                ws.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundBaker.Folder + "/witch_whisper.wav");
                ws.loop = true;
                ws.playOnAwake = false;
                ws.spatialBlend = 0f;
                ws.volume = 0f;

                var dark = root.GetComponent<DarknessPenalty>();
                if (dark == null) dark = root.AddComponent<DarknessPenalty>();
                SetRef(dark, "whisperSource", ws);

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------- 적 ----------
        private static void SetupEnemy(string path, bool infected)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // 적은 SpriteRenderer · 콜라이더가 루트에 있어 루트째 키운다.
                // 공격 예고(Telegraph)는 부모 스케일을 스스로 상쇄하고, 머리 위 표시는 높이만 올려준다
                root.transform.localScale = Vector3.one * WorldScale;
                // 피벗을 발밑으로 내렸으니 정렬도 발 기준 (중심 기준이면 벽 바로 아래에서 머리가 벽에 가려진다)
                root.GetComponent<SpriteRenderer>().spriteSortPoint = SpriteSortPoint.Pivot;
                var ai = root.GetComponent<EnemyAI>();
                Set(ai, "agentRadius", 0.8f);
                Set(ai, "wallClearance", 0.95f);
                SetEnum(ai, "senseMode", infected ? (int)SenseMode.Hearing : (int)SenseMode.Sight);
                Set(ai, "noticeRadius", 3f);
                // 기본 시야 원뿔 - 소리만으로는 감지가 너무 적었다 (피드백 2차). 불을 꺼도 정면이면 보인다
                Set(ai, "sightConeRange", infected ? 7f : 10f);
                Set(ai, "sightConeAngle", infected ? 100f : 110f);
                // 감염체는 한 번 물면 안 놓는다 (피드백 3차)
                SetBool(ai, "keepChasingOnceSeen", infected);
                Set(ai, "giveUpDistance", 25f);

                var icon = root.GetComponentInChildren<EnemyAlertIcon>(true);
                if (icon != null) Set(icon, "height", 1.75f * WorldScale);

                var tele = root.GetComponentInChildren<EnemyTelegraph>(true);
                if (tele != null) Set(tele, "ringEndRadius", 0.75f * WorldScale);

                if (infected) Set(ai, "attackRange", 1.2f * 1.5f);

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// 적 스프라이트는 중앙 피벗이라 그림이 발 콜라이더 · 허트박스보다 반 키 아래에 그려졌다.
        /// 기획 규격(발밑 BottomCenter)대로 피벗을 내린다. 시트(Multiple) 스프라이트라 데이터 프로바이더로 고친다.
        /// </summary>
        private static void FixEnemyPivots()
        {
            foreach (var path in new[] { "Assets/_Game/Art/Enemies/enemy_melee.png", "Assets/_Game/Art/Enemies/enemy_ranged.png" })
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
                factory.Init();
                var provider = factory.GetSpriteEditorDataProviderFromObject(imp);
                provider.InitSpriteEditorDataProvider();
                var rects = provider.GetSpriteRects();
                foreach (var r in rects)
                {
                    r.alignment = SpriteAlignment.BottomCenter;
                    r.pivot = new Vector2(0.5f, 0f);
                }
                provider.SetSpriteRects(rects);
                provider.Apply();
                imp.SaveAndReimport();
            }
        }

        private static void ScaleRoot(string path)
        {
            if (!path.EndsWith(".prefab")) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.transform.localScale = Vector3.one * WorldScale;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------- HUD ----------
        private static void SetupHud()
        {
            const string path = Prefabs + "/UI/HUD.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var loot = root.transform.Find("LootScreen");
                var bagSide = (RectTransform)loot.Find("BagSide");
                var lootSide = (RectTransform)loot.Find("LootSide");

                // 중요 보관함 - 가방 패널을 그대로 떠서 만든다 (격자 · 제목 · 미리보기 구조가 같아야 드래그가 통한다)
                var secureSide = loot.Find("SecureSide") as RectTransform;
                if (secureSide == null)
                {
                    secureSide = (RectTransform)Object.Instantiate(bagSide.gameObject, loot).transform;
                    secureSide.name = "SecureSide";
                }

                // 가방은 왼쪽, 털 대상은 오른쪽 위, 중요 보관함은 오른쪽 아래 (1920x1080 기준)
                Place(bagSide,   new Vector2(-480f, 10f),   new Vector2(940f, 720f));
                Place(lootSide,  new Vector2(480f, 250f),   new Vector2(720f, 470f));
                Place(secureSide,new Vector2(480f, -300f),  new Vector2(360f, 360f));
                foreach (var side in new[] { bagSide, lootSide, secureSide })
                {
                    var grid = side.GetComponentInChildren<GridPanelUI>(true);
                    ((RectTransform)grid.transform).anchoredPosition = Vector2.zero;
                    Set(grid, "cellSize", CellSize);
                    var title = grid.transform.Find("Title") as RectTransform;
                    if (title != null)
                    {
                        title.sizeDelta = new Vector2(700f, 44f);
                        var t = title.GetComponent<TMP_Text>();
                        t.fontSize = 34f;
                    }
                }

                // 감정 진행 막대는 털 대상 격자 아래로
                var prog = (RectTransform)lootSide.Find("ProgressBG");
                prog.anchorMin = prog.anchorMax = new Vector2(0.5f, 0f);
                prog.pivot = new Vector2(0.5f, 1f);
                prog.anchoredPosition = new Vector2(0f, -6f);
                prog.sizeDelta = new Vector2(680f, 14f);

                var mag = lootSide.Find("Grid/Items/Magnifier") as RectTransform;
                if (mag != null) mag.sizeDelta = Vector2.one * 104f;
                var ui = root.GetComponent<LootScreenUI>();
                Set(ui, "orbitRadius", 32f);
                Set(ui, "magnifierSize", 104f);

                var weight = (RectTransform)loot.Find("Weight");
                weight.anchoredPosition = new Vector2(-480f, -385f);
                weight.sizeDelta = new Vector2(600f, 40f);
                weight.GetComponent<TMP_Text>().fontSize = 30f;
                var hint = (RectTransform)loot.Find("Hint");
                hint.anchoredPosition = new Vector2(0f, -505f);
                hint.sizeDelta = new Vector2(1600f, 40f);

                // 감정 소리
                var sfx = root.GetComponent<AudioSource>();
                if (sfx == null) sfx = root.AddComponent<AudioSource>();
                sfx.playOnAwake = false;
                sfx.spatialBlend = 0f;
                SetRef(ui, "sfxSource", sfx);
                var so = new SerializedObject(ui);
                var clips = so.FindProperty("revealClips");
                clips.arraySize = 4;
                string[] names = { "reveal_common", "reveal_rare", "reveal_unique", "reveal_legendary" };
                for (int i = 0; i < 4; i++)
                    clips.GetArrayElementAtIndex(i).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<AudioClip>($"{SoundBaker.Folder}/{names[i]}.wav");
                so.ApplyModifiedPropertiesWithoutUndo();

                // 어둠 패널티의 가장자리 어둠 - 피격 비네트를 떠서 검게 칠한다. 피격 붉은빛이 그 위에 와야 한다
                var dmg = root.transform.Find("DamageVignette");
                var dark = root.transform.Find("DarknessVignette");
                if (dark == null)
                {
                    dark = Object.Instantiate(dmg.gameObject, root.transform).transform;
                    dark.name = "DarknessVignette";
                }
                dark.SetSiblingIndex(dmg.GetSiblingIndex());
                var img = dark.GetComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
                img.raycastTarget = false;
                img.enabled = false;

                // 장면 전환 페이드 - 화면 전체를 덮는 검은 판. 레이드가 검정에서 밝아지고, 탈출하면 어두워지며 마을로
                var fadeT = root.transform.Find("SceneFade");
                if (fadeT == null)
                {
                    var f = new GameObject("SceneFade", typeof(RectTransform));
                    f.transform.SetParent(root.transform, false);
                    fadeT = f.transform;
                }
                var frt = (RectTransform)fadeT;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
                var fimg = fadeT.GetComponent<Image>();
                if (fimg == null) fimg = fadeT.gameObject.AddComponent<Image>();
                fimg.color = Color.black;
                fimg.raycastTarget = false;
                if (fadeT.GetComponent<SceneFade>() == null) fadeT.gameObject.AddComponent<SceneFade>();
                fadeT.SetAsLastSibling();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------- 탄약 · 상처 · 붕대 (피드백 4차) ----------
        private const string ItemsDir = "Assets/_Game/Items";

        [MenuItem("Capstone/피드백 적용/탄약 · 상처 · 붕대")]
        public static void ApplyCombatSystems()
        {
            var bolt = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>($"{ItemsDir}/Item_Bolt.asset");
            var bandage = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>($"{ItemsDir}/Item_Bandage.asset");
            if (bandage != null)
            {
                bandage.healAmount = 15f;
                bandage.stopsBleeding = true;
                bandage.description = "낡았지만 쓸 만하다. 피를 멎게 하고 체력을 조금 회복한다.  (H 또는 가방에서 우클릭)";
                EditorUtility.SetDirty(bandage);
            }

            var root = PrefabUtility.LoadPrefabContents($"{Prefabs}/Player.prefab");
            try
            {
                if (root.GetComponent<Combat.PlayerAilments>() == null) root.AddComponent<Combat.PlayerAilments>();
                var quick = root.GetComponent<QuickUse>();
                if (quick == null) quick = root.AddComponent<QuickUse>();
                SetRef(quick, "bandage", bandage);
                var xbow = root.GetComponent<Combat.Crossbow>();
                SetRef(xbow, "boltItem", bolt);
                SetRef(xbow, "inventory", root.GetComponent<Items.PlayerInventory>());
                PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            Wounds($"{Prefabs}/Enemy_Melee.prefab", 20f, 0.6f);     // 감염체 - 감염 + 출혈
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Enemy_Rogue.prefab") != null)
                Wounds($"{Prefabs}/Enemy_Rogue.prefab", 0f, 0.5f);  // 도적 단검 - 출혈만

            var hud = PrefabUtility.LoadPrefabContents($"{Prefabs}/UI/HUD.prefab");
            try
            {
                // 체력바 위에 상처, 무게 위에 볼트, 화면 아래 가운데 알림
                HudText(hud.transform, "AilmentText", new Vector2(0f, 0f), new Vector2(40f, 160f), new Vector2(700f, 40f), 28f, TextAlignmentOptions.Left);
                HudText(hud.transform, "AmmoText", new Vector2(1f, 0f), new Vector2(-40f, 120f), new Vector2(400f, 40f), 30f, TextAlignmentOptions.Right);
                HudText(hud.transform, "ToastText", new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(1000f, 44f), 30f, TextAlignmentOptions.Center);
                PrefabUtility.SaveAsPrefabAsset(hud, $"{Prefabs}/UI/HUD.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
        }

        private static void Wounds(string path, float infection, float bleed)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var m = root.GetComponent<MeleeEnemy>();
                Set(m, "infectionPerHit", infection);
                Set(m, "bleedChance", bleed);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void HudText(Transform hud, string name, Vector2 corner, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
        {
            var t = hud.Find(name);
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(hud, false);
                t = go.transform;
            }
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var text = t.GetComponent<TextMeshProUGUI>();
            if (text == null) text = t.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = new Color(0.86f, 0.84f, 0.78f);
            text.raycastTarget = false;
            text.richText = true;
            text.text = "";
            // 페이드 판보다는 아래, 나머지보다는 위
            var fade = hud.Find("SceneFade");
            t.SetSiblingIndex(fade != null ? fade.GetSiblingIndex() : hud.childCount - 1);
        }

        /// <summary>타이틀의 시작 버튼이 마을로 가게 한다 - 첫 판도 마을에서 준비하고 나간다.</summary>
        [MenuItem("Capstone/피드백 적용/타이틀 → 마을")]
        public static void TitleToTown()
        {
            const string intro = "Assets/_Game/Scenes/Intro.unity";
            var prev = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(intro);
            foreach (var menu in Object.FindObjectsByType<TitleMenu>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(menu);
                so.FindProperty("gameSceneName").stringValue = "Town";
                so.ApplyModifiedPropertiesWithoutUndo();
                // 버튼 글자도 - 이제 바로 레이드가 아니라 마을부터 간다
                var btn = so.FindProperty("startButton").objectReferenceValue as UnityEngine.UI.Button;
                var label = btn != null ? btn.GetComponentInChildren<TMP_Text>(true) : null;
                if (label != null) label.text = "게임 시작";
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            if (!string.IsNullOrEmpty(prev) && prev != intro) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(prev);
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ---------- 직렬화 도우미 ----------
        private static Transform Child(GameObject root, string name)
        {
            var t = root.transform.Find(name);
            if (t == null) throw new System.Exception($"{root.name} 에 {name} 자식이 없다");
            return t;
        }

        private static void Set(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"{target.GetType().Name}.{field} 없음"); return; }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"{target.GetType().Name}.{field} 없음"); return; }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"{target.GetType().Name}.{field} 없음"); return; }
            p.enumValueIndex = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"{target.GetType().Name}.{field} 없음"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetClips(Object target, string field, string prefix)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = 4;
            for (int i = 0; i < 4; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{SoundBaker.Folder}/{prefix}{i + 1}.wav");
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
