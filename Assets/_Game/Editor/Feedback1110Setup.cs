using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using Capstone.Core;
using Capstone.Items;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 수정사항 2026-11-10 (카톡 PDF) 중 에셋 · 씬에 들어가는 것을 한 번에 굽는다. 여러 번 눌러도 결과가 같다.
    ///   1. 맵 밝기 +50%       - 레이드 · 마을 후처리 노출을 log2(1.5) = +0.585 스톱 올린다
    ///   2. 마을 등급별 상자    - 일반 · 레어 · 유니크 · 전설 견본 상자 4개 (감정 소리 확인용, 꺼낼 수 없음)
    ///   5. 적 실루엣 재질      - Resources/EnemySilhouette.mat
    ///   6. 적 시야            - 모든 적 전방 180도, 반경 = 등불 범위(6.5m)와 근접(1.5m)의 중간 4m
    /// 코드로만 바뀐 것 (여기서 할 일 없음): 인벤토리 Alt+우클릭, 출혈 5초, 도적 회피 40%, 적 횃불 방향 · 도적 등불, 정조준 감속.
    /// </summary>
    public static class Feedback1110Setup
    {
        public const float EnemySightRange = 4f;
        public const float EnemySightAngle = 180f;
        private const float ExposureUp = 0.585f;            // 밝기 x1.5
        public const float RaidExposure = 0.15f + ExposureUp;
        public const float TownExposure = 0.10f + ExposureUp;

        private const string Prefabs = "Assets/_Game/Prefabs/";
        private const string TownScene = "Assets/_Game/Scenes/Town.unity";

        [MenuItem("Capstone/피드백 적용/2026-11-10 수정사항 (전부)")]
        public static void ApplyAll()
        {
            SilhouetteMaterial();
            EnemySight();
            RogueLantern();
            Brightness();
            TownSoundChests();
            AssetDatabase.SaveAssets();
            Debug.Log("[Feedback1110] 2026-11-10 수정사항을 반영했다");
        }

        [MenuItem("Capstone/피드백 적용/2026-11-10 - 적 실루엣 재질")]
        public static void SilhouetteMaterial()
        {
            const string path = "Assets/_Game/Resources/EnemySilhouette.mat";
            var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Game/Art/Shaders/EnemySilhouette.shader");
            if (sh == null) { Debug.LogError("[Feedback1110] 실루엣 셰이더가 없다"); return; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            mat.SetColor("_Color", new Color(0.2f, 0.21f, 0.24f, 0.8f));   // 후처리 노출(+50%) · 블룸을 지나도 회색으로 남는 값
            mat.SetFloat("_Detail", 0.25f);
            EditorUtility.SetDirty(mat);
        }

        [MenuItem("Capstone/피드백 적용/2026-11-10 - 적 시야 180도")]
        public static void EnemySight()
        {
            foreach (var name in new[] { "Enemy_Melee", "Enemy_Ranged", "Enemy_Rogue" })
            {
                string path = Prefabs + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var ai = root.GetComponent<Enemy.EnemyAI>();
                    var so = new SerializedObject(ai);
                    so.FindProperty("sightConeRange").floatValue = EnemySightRange;
                    so.FindProperty("sightConeAngle").floatValue = EnemySightAngle;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>도적 허리 등불 값 (2026-11-11 - 노란 점 하나라 이상하다는 피드백: 7x10 도트 등불 + 몸이 비칠 만큼 밝게).</summary>
        [MenuItem("Capstone/피드백 적용/2026-11-10 - 도적 허리 등불")]
        public static void RogueLantern()
        {
            string path = Prefabs + "Enemy_Rogue.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var so = new SerializedObject(root.GetComponent<Enemy.RogueEnemy>());
                so.FindProperty("waistLantern").boolValue = true;
                so.FindProperty("lanternColor").colorValue = new Color(1f, 0.66f, 0.34f);
                so.FindProperty("lanternRadius").floatValue = 3.6f;
                so.FindProperty("lanternIntensity").floatValue = 1.05f;
                so.FindProperty("lanternHip").vector2Value = new Vector2(0.2f, 0.52f);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Capstone/피드백 적용/2026-11-10 - 맵 밝기 +50%")]
        public static void Brightness()
        {
            SetExposure("Assets/_Game/Settings/PlagueNight.asset", RaidExposure);
            SetExposure("Assets/_Game/Settings/TownDusk.asset", TownExposure);
        }

        private static void SetExposure(string path, float value)
        {
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (prof == null) { Debug.LogWarning($"[Feedback1110] {path} 없음"); return; }
            if (!prof.TryGet<ColorAdjustments>(out var ca)) return;
            ca.postExposure.Override(value);
            EditorUtility.SetDirty(ca);
            EditorUtility.SetDirty(prof);
        }

        // ---------- 마을 등급별 견본 상자 ----------
        private static readonly (ItemGrade grade, string box, string label)[] Chests =
        {
            (ItemGrade.Common, "Sack", "일반"), (ItemGrade.Rare, "Chest", "레어"),
            (ItemGrade.Unique, "Strongbox", "유니크"), (ItemGrade.Legendary, "Reliquary", "전설"),
        };

        [MenuItem("Capstone/피드백 적용/2026-11-10 - 마을 등급별 상자")]
        public static void TownSoundChests()
        {
            var prev = EditorSceneManager.GetActiveScene().path;
            if (EditorSceneManager.GetActiveScene().isDirty && prev != TownScene && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = prev == TownScene ? EditorSceneManager.GetActiveScene() : EditorSceneManager.OpenScene(TownScene);

            var items = new List<ItemDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/_Game/Items" }))
                items.Add(AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)));

            var old = GameObject.Find("SoundTestChests");
            if (old) Object.DestroyImmediate(old);
            var root = new GameObject("SoundTestChests").transform;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Game/Art/Fonts/Galmuri11-Bold SDF.asset");
            for (int i = 0; i < Chests.Length; i++)
            {
                var c = Chests[i];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "LootBox_" + c.box + ".prefab");
                if (prefab == null) { Debug.LogWarning($"[Feedback1110] LootBox_{c.box} 없음"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(-19f + i * 3f, -10f, 0f);
                go.name = $"SoundTest_{c.grade}";
                var box = go.GetComponent<LootContainer>();
                var pool = items.FindAll(d => d != null && d.grade == c.grade);
                box.SetupDisplay($"{c.label} 견본 상자", c.grade, pool, 3, 3f);
                EditorUtility.SetDirty(box);
                Label(root, (Vector2)go.transform.position + Vector2.up * 2.3f, c.label, c.grade.ToColor(), font);
            }
            Label(root, new Vector2(-14.5f, -7.4f), "등급별 감정 소리 확인 (꺼낼 수 없음)", new Color(0.86f, 0.84f, 0.78f), font);

            LootHud();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!string.IsNullOrEmpty(prev) && prev != TownScene) EditorSceneManager.OpenScene(prev);
        }

        private static void Label(Transform parent, Vector2 pos, string text, Color color, TMP_FontAsset font)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var t = go.AddComponent<TextMeshPro>();
            if (font) t.font = font;
            t.text = text;
            t.fontSize = 4f;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = new Vector2(12f, 1.4f);
            t.sortingOrder = 30;
        }

        /// <summary>
        /// 마을에는 루팅 화면이 없었다 (가게 · 창고 화면만). 레이드 HUD 프리팹을 얹되 루팅 화면 · 설명창만 남기고 끈다.
        /// 프리팹 인스턴스라 레이드 HUD 의 루팅 화면을 고치면 마을 것도 따라온다.
        /// </summary>
        private static void LootHud()
        {
            var existing = GameObject.Find("LootHUD");
            if (existing) Object.DestroyImmediate(existing);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "UI/HUD.prefab");
            var hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            hud.name = "LootHUD";
            foreach (var mb in hud.GetComponents<MonoBehaviour>())
            {
                bool keep = mb is UI.LootScreenUI || mb is UI.ItemTooltip
                         || mb is UnityEngine.UI.CanvasScaler || mb is UnityEngine.UI.GraphicRaycaster;
                if (!keep) mb.enabled = false;
            }
            foreach (Transform t in hud.transform)
                if (t.name != "LootScreen" && t.name != "ItemTooltip" && t.name != "ToastText") t.gameObject.SetActive(false);
            var ui = hud.GetComponent<UI.LootScreenUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("bagHotkey").boolValue = false;
            var pc = Object.FindFirstObjectByType<Player.PlayerController>();
            if (pc != null)
            {
                so.FindProperty("interactor").objectReferenceValue = pc.GetComponent<Interactor>();
                so.FindProperty("inventory").objectReferenceValue = pc.GetComponent<PlayerInventory>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            hud.GetComponent<Canvas>().sortingOrder = 20;        // 마을 화면(가게 · 안내 글) 위
        }
    }
}
