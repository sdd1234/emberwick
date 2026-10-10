using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Capstone.Enemy;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 2026-10-10 에 받은 원화를 게임에 연결한다 (메뉴 Capstone/아트 반영/2026-10 원화).
    /// 원본 가공(배경 지우기 · 자르기 · 걷기 선화 채색)은 Tools/art/process_art.py 가 하고,
    /// 여기서는 그 결과를 임포트 설정 · 애니메이션 · 프리팹에 꽂는다. 여러 번 눌러도 같은 결과.
    ///
    /// 크기 기준 - 원화들은 같은 배율로 그려져 있어 세트마다 같은 PPU 를 쓴다.
    ///   적: 루트 스케일 2 에서 이교도가 약 2.2m (PPU 640, 원본을 반으로 줄였으므로)
    ///   주인공: Sprite 자식 스케일 2 에서 약 2.5m (PPU 528)
    ///   배경: PPU 128 (큰 가문비 9m · 덤불 3m)
    /// </summary>
    public static class NewArtImporter
    {
        private const string Art = "Assets/_Game/Art";
        private const string Prefabs = "Assets/_Game/Prefabs";
        public const float EnemyPpu = 640f;
        public const float HeroPpu = 528f;
        public const float EnvPpu = 128f;

        [MenuItem("Capstone/아트 반영/2026-10 원화")]
        public static void Apply()
        {
            ConfigureImports();
            AssetDatabase.Refresh();
            ApplyPlayer();
            ApplyEnemy("Enemy_Melee", "Enemies/Infected/infected",
                       new[] { "idle_0", "idle_1" }, new[] { "walk_0", "walk_1" }, new[] { "attack_0", "attack_1" }, null);
            ApplyEnemy("Enemy_Ranged", "Enemies/Cultist/cultist",
                       new[] { "idle" }, new[] { "walk_0", "walk_1" }, new[] { "shoot" }, new[] { "reload" });
            MakeRogue();
            AssetDatabase.SaveAssets();
            Debug.Log("[NewArtImporter] 2026-10 원화를 반영했다.");
        }

        // ---------- 임포트 설정 ----------
        private static void ConfigureImports()
        {
            var metaPath = Path.Combine(Application.dataPath, "_Game/Art/art_import_meta.json");
            // JsonUtility 가 딕셔너리를 못 읽어서 { "경로": {"pivot": [x, y], ...} } 를 직접 훑는다
            foreach (var kv in ReadPivots(File.ReadAllText(metaPath))) Configure(kv.Key, kv.Value);
        }

        private static Dictionary<string, Vector2> ReadPivots(string json)
        {
            var result = new Dictionary<string, Vector2>();
            int i = 0;
            while (true)
            {
                int k = json.IndexOf("\": {", i);
                if (k < 0) break;
                int ks = json.LastIndexOf('"', k - 1);
                string rel = json.Substring(ks + 1, k - ks - 1);
                int p = json.IndexOf('[', k);
                int pe = json.IndexOf(']', p);
                var parts = json.Substring(p + 1, pe - p - 1).Split(',');
                result[rel] = new Vector2(float.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                                          float.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture));
                i = pe;
            }
            return result;
        }

        private static void Configure(string rel, Vector2 pivot)
        {
            string path = $"{Art}/{rel}";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { AssetDatabase.ImportAsset(path); imp = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (imp == null) { Debug.LogWarning($"[NewArtImporter] 없음: {path}"); return; }

            float ppu = rel.StartsWith("Enemies") ? EnemyPpu : rel.StartsWith("Characters") ? HeroPpu : EnvPpu;
            // 주인공 영상은 캐릭터가 화면 가운데에 있지 않아서 가공 단계의 x 피벗이 틀리다. 가운데로 둔다
            if (rel.StartsWith("Characters")) pivot.x = 0.5f;

            // PPU · 타입 · 필터는 importer 에 직접 (TextureImporterSettings 경유는 조용히 무시된다)
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = ppu;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            // 바닥 · 길 · 길 가장자리는 SpriteRenderer 의 Tiled 로 이어 붙인다 - FullRect 라야 Tiled 가 된다
            bool tiled = rel.Contains("ground_") || rel.Contains("road_edge");
            imp.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = pivot;
            s.spriteMeshType = tiled ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            imp.SetTextureSettings(s);
            imp.spritePixelsPerUnit = ppu;
            imp.SaveAndReimport();
        }

        private static Sprite S(string rel) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Art}/{rel}.png");

        // ---------- 주인공 ----------
        /// <summary>
        /// 기존 애니메이션 클립(대기 · 걷기 · 달리기 · 조준 · 사격)의 스프라이트만 새 원화로 갈아끼운다.
        /// 컨트롤러 · 파라미터 · PlayerAnimatorDriver 는 그대로 둔다.
        /// 새 원화는 발밑 피벗이라 Sprite 자식을 올려 둘 필요가 없어진다 (예전 그림은 중앙 피벗이었다).
        /// </summary>
        private static void ApplyPlayer()
        {
            var idle = new[] { S("Characters/Hero/player_idle_0"), S("Characters/Hero/player_idle_1") };
            var walk = new[] { S("Characters/Hero/player_walk_0"), S("Characters/Hero/player_walk_1"),
                               S("Characters/Hero/player_walk_2"), S("Characters/Hero/player_walk_3") };
            const string anim = "Assets/_Game/Animations/Player";
            SetClip($"{anim}/Player_Idle.anim", idle, 2f);
            SetClip($"{anim}/Player_Walk.anim", walk, 6f);
            SetClip($"{anim}/Player_Run.anim", walk, 9f);
            // 조준 - 원화가 없어 대기 그림에 이교도 석궁을 쥐여 합성한 1프레임 (process_art.py make_aim)
            var aim = S("Characters/Hero/player_aim") ?? idle[0];
            SetClip($"{anim}/Player_Aim.anim", new[] { aim }, 1f);
            SetClip($"{anim}/Player_Shoot.anim", new[] { aim }, 1f);

            var root = PrefabUtility.LoadPrefabContents($"{Prefabs}/Player.prefab");
            try
            {
                var sprite = root.transform.Find("Sprite");
                sprite.localPosition = Vector3.zero;
                sprite.GetComponent<SpriteRenderer>().sprite = idle[0];
                PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetClip(string path, Sprite[] frames, float fps)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { Debug.LogWarning($"[NewArtImporter] 클립 없음: {path}"); return; }
            var binding = EditorCurveBinding.PPtrCurve("Sprite", typeof(SpriteRenderer), "m_Sprite");
            // 마지막 프레임을 한 칸 더 두어야 마지막 그림도 제 시간만큼 보인다
            var keys = new ObjectReferenceKeyframe[frames.Length + 1];
            for (int i = 0; i < frames.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
            keys[frames.Length] = new ObjectReferenceKeyframe { time = frames.Length / fps, value = frames[frames.Length - 1] };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            clip.frameRate = Mathf.Max(12f, fps);
            EditorUtility.SetDirty(clip);
        }

        // ---------- 적 ----------
        private static void ApplyEnemy(string prefab, string prefix, string[] idle, string[] walk, string[] attack, string[] reload)
        {
            string path = $"{Prefabs}/{prefab}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Wire(root, prefix, idle, walk, attack, reload);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Wire(GameObject root, string prefix, string[] idle, string[] walk, string[] attack, string[] reload)
        {
            var sr = root.GetComponent<SpriteRenderer>();
            sr.sprite = S($"{prefix}_{idle[0]}");
            sr.spriteSortPoint = SpriteSortPoint.Pivot;

            var anim = root.GetComponent<EnemySpriteAnimator>();
            if (anim == null) anim = root.AddComponent<EnemySpriteAnimator>();
            var so = new SerializedObject(anim);
            so.FindProperty("target").objectReferenceValue = sr;
            Fill(so.FindProperty("idle"), prefix, idle);
            Fill(so.FindProperty("walk"), prefix, walk);
            Fill(so.FindProperty("attack"), prefix, attack);
            Fill(so.FindProperty("reload"), prefix, reload);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Fill(SerializedProperty arr, string prefix, string[] names)
        {
            names ??= new string[0];
            arr.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = S($"{prefix}_{names[i]}");
        }

        /// <summary>
        /// 도적 - 근접 적의 변형. 감염체와 달리 사람이라 눈으로 찾는다(Sight). 더 빠르고 아프다.
        /// 지금 레이드 맵(피드백 레이아웃)에는 도적이 없어서 프리팹만 만들어 둔다.
        /// </summary>
        private static void MakeRogue()
        {
            string src = $"{Prefabs}/Enemy_Melee.prefab", dst = $"{Prefabs}/Enemy_Rogue.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) == null) AssetDatabase.CopyAsset(src, dst);
            var root = PrefabUtility.LoadPrefabContents(dst);
            try
            {
                root.name = "Enemy_Rogue";
                Wire(root, "Enemies/Rogue/rogue", new[] { "idle" }, new[] { "walk_0", "walk_1" }, new[] { "idle", "attack" }, null);
                var ai = new SerializedObject(root.GetComponent<MeleeEnemy>());
                ai.FindProperty("senseMode").enumValueIndex = (int)SenseMode.Sight;
                ai.FindProperty("moveSpeed").floatValue = 3.0f;
                ai.FindProperty("attackDamage").floatValue = 15f;
                ai.FindProperty("windUp").floatValue = 0.3f;
                ai.ApplyModifiedPropertiesWithoutUndo();
                var corpse = root.GetComponent<EnemyCorpse>();
                if (corpse != null)
                {
                    var c = new SerializedObject(corpse);
                    c.FindProperty("corpseLabel").stringValue = "도적의 시체";
                    c.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, dst);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
