using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Capstone.Enemy;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 도트 통일 반영 (메뉴 Capstone/도트 반영/2026-10-29 전체).
    ///   1) Art/Pixel 의 PNG 를 도트 설정으로 임포트 - 점 필터 · 무압축 · 발밑 피벗 · pixel_meta.json 의 PPU
    ///      적 · 상자는 루트가 2배라 PPU 를 두 배로 넣어 실제 크기를 맞춘다 (모든 도트가 같은 크기로 보이게)
    ///   2) 플레이어 · 적 · 상자 프리팹에 새 그림과 새 컴포넌트(진영 · 타격감 · 도적 AI · 감염자 내성 · 빛 플라스크)를 건다
    /// 여러 번 눌러도 같은 결과가 나온다.
    /// </summary>
    public static class PixelArtSetup
    {
        public const string Dir = "Assets/_Game/Art/Pixel/";
        private const string Prefabs = "Assets/_Game/Prefabs/";

        // 루트가 2배인 프리팹에 붙는 그림 - PPU 를 두 배로
        private static readonly string[] Doubled =
        {
            "cultist_", "rogue_", "infected_", "corpse_", "item_torch",
            "hurt_cultist", "hurt_rogue", "hurt_infected",          // 맞은 그림도 적 루트(2배)에 붙는다
            "prop_chest", "prop_strongbox", "prop_reliquary", "prop_apothecary", "prop_sack", "prop_rack", "prop_cart",
        };

        [MenuItem("Capstone/도트 반영/2026-10-29 전체")]
        public static void All()
        {
            ImportAll();
            ImportPixelUI();
            SetupItems();
            SetupPlayer();
            SetupEnemies();
            SetupLootBoxes();
            AssetDatabase.SaveAssets();
            Debug.Log("[PixelArtSetup] 도트 반영 완료");
        }

        [System.Serializable] private class Meta { public float ppu; public float[] pivot; }

        public static Dictionary<string, float> ReadPpu()
        {
            var d = new Dictionary<string, float>();
            string path = Dir + "pixel_meta.json";
            if (!File.Exists(path)) return d;
            // 간단 파서 - "name": {"ppu": 38.0, ...}
            var txt = File.ReadAllText(path);
            var rx = new System.Text.RegularExpressions.Regex("\"([a-z0-9_]+)\":\\s*\\{\\s*\"ppu\":\\s*([0-9.]+)");
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(txt))
                d[m.Groups[1].Value] = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return d;
        }

        public static void ImportAll()
        {
            var ppu = ReadPpu();
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool tile = name.StartsWith("tile_");
                bool ui = name.StartsWith("hud_") || name.StartsWith("vfx_") || name.StartsWith("tel_") || name.StartsWith("icon_") || name.StartsWith("ui_");   // 이펙트는 가운데 피벗
                float p = ppu.TryGetValue(name, out var v) ? v : 38f;
                foreach (var pre in Doubled) if (name.StartsWith(pre)) { p *= 2f; break; }

                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = p;
                ti.filterMode = name == "hud_halo" ? FilterMode.Bilinear : FilterMode.Point;   // 빛무리만 부드럽게
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.maxTextureSize = 2048;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteMeshType = SpriteMeshType.FullRect;
                s.spriteAlignment = (int)(tile || ui ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
                ti.SetTextureSettings(s);
                // 늘려 쓰는 UI 틀 - 모서리 리벳은 그대로 두고 가운데만 늘린다 (9-슬라이스)
                if (name == "ui_bar2_status") ti.spriteBorder = new Vector4(8, 8, 8, 8);
                ti.isReadable = name == "tel_pattern";   // 공격 범위가 이 무늬를 읽어 부채꼴로 오린다
                if (path.Contains("/Resources/PixelUI/")) continue;
                if (name == "ui_panel2") ti.spriteBorder = new Vector4(14, 14, 14, 14);   // 모서리 황동 덮개(13px) 바깥에서 자른다
                ti.SaveAndReimport();
            }
        }

        /// <summary>격자 칸 · 창 틀 (Resources/PixelUI) - 9-슬라이스 테두리를 건다.</summary>
        private static void ImportPixelUI()
        {
            foreach (var (n, b) in new[] { ("ui_slot", 4), ("ui_slot_hi", 4), ("ui_window", 12) })
            {
                string path = "Assets/_Game/Resources/PixelUI/" + n + ".png";
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                ti.filterMode = FilterMode.Point; ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false; ti.spritePixelsPerUnit = 100f; ti.spriteBorder = new Vector4(b, b, b, b);
                ti.SaveAndReimport();
            }
        }

        /// <summary>아이템 아이콘 - 칸 모양에 맞춘 AI 도트 (icon_item_*).</summary>
        private static void SetupItems()
        {
            var map = new Dictionary<string, string>
            {
                { "Item_Bandage", "bandage" }, { "Item_Bolt", "bolt" }, { "Item_Catalyst", "catalyst" }, { "Item_Gunpowder", "gunpowder" },
                { "Item_Oil", "oil" }, { "Item_PlagueHeart", "plagueheart" }, { "Item_SilverCross", "silvercross" },
            };
            foreach (var kv in map)
            {
                var def = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>($"Assets/_Game/Items/{kv.Key}.asset");
                var icon = S("icon_item_" + kv.Value);
                if (def == null || icon == null) continue;
                def.icon = icon;
                EditorUtility.SetDirty(def);
            }
        }

        public static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Dir + name + ".png");
        private static Sprite[] Frames(params string[] names)
        {
            var list = new List<Sprite>();
            foreach (var n in names) { var s = S(n); if (s != null) list.Add(s); }
            return list.ToArray();
        }

        private static void Set(Object o, string prop, object value)
        {
            var so = new SerializedObject(o);
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[PixelArtSetup] {o.GetType().Name}.{prop} 없음"); return; }
            switch (value)
            {
                case float f: p.floatValue = f; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case Object obj: p.objectReferenceValue = obj; break;
                case Sprite[] arr:
                    p.arraySize = arr.Length;
                    for (int k = 0; k < arr.Length; k++) p.GetArrayElementAtIndex(k).objectReferenceValue = arr[k];
                    break;
                case Color c: p.colorValue = c; break;
                case Vector2 v2: p.vector2Value = v2; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Ensure<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();

        // ---------- 플레이어 ----------
        private static void SetupPlayer()
        {
            string path = Prefabs + "Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            var sprite = root.transform.Find("Sprite");
            var sr = sprite.GetComponent<SpriteRenderer>();
            sr.sprite = S("hero_idle");
            sprite.localScale = Vector3.one;                 // 도트 PPU 가 실제 크기를 정한다
            var anim = sprite.GetComponent<Animator>() ?? root.GetComponent<Animator>();
            if (anim) anim.enabled = false;
            var drv = root.GetComponent<Player.PlayerAnimatorDriver>();
            if (drv) drv.enabled = false;

            var pa = Ensure<Player.PlayerPixelAnimator>(root);
            Set(pa, "target", sr);
            Set(pa, "idle", Frames("hero_idle"));
            Set(pa, "walk", S("hero_walk6_0") != null ? Frames("hero_walk6_0", "hero_walk6_1", "hero_walk6_2", "hero_walk6_3", "hero_walk6_4", "hero_walk6_5") : Frames("hero_walk_0", "hero_walk_1", "hero_walk_2", "hero_walk_3"));
            Set(pa, "aim", S("hero_aim"));
            Set(pa, "roll", Frames("hero_roll_0", "hero_roll_1", "hero_roll_2", "hero_roll_3"));
            Set(pa, "hurt", Frames("hurt_hero_0", "hurt_hero_1"));
            Ensure<FX.HitFeel>(root);          // 플레이어 피격 연출 (킥 · 색 쏠림 · 맞은 방향)

            Ensure<Core.FactionMember>(root).SetFaction(Core.Faction.Player);
            var fl = Ensure<Player.LightFlaskThrower>(root);
            if (S("item_flask")) Set(fl, "flaskSprite", S("item_flask"));
            if (S("item_wisp")) Set(fl, "wispSprite", S("item_wisp"));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------- 적 ----------
        private static void SetupEnemies()
        {
            int wall = LayerMask.NameToLayer(Core.GameLayers.Wall);
            int enemy = LayerMask.NameToLayer(Core.GameLayers.Enemy);
            int player = LayerMask.NameToLayer(Core.GameLayers.Player);

            // 이교도
            Edit("Enemy_Ranged", root =>
            {
                Ensure<Core.FactionMember>(root).SetFaction(Core.Faction.Cultist);
                Ensure<FX.HitFeel>(root);
                var an = root.GetComponent<EnemySpriteAnimator>();
                Set(an, "artFacesLeft", false);
                Set(an, "idle", Frames("cultist_idle"));
                Set(an, "walk", Frames("cultist_walk_0", "cultist_walk_1", "cultist_walk_2", "cultist_walk_3"));
                Set(an, "attack", Frames("cultist_aim"));
                Set(an, "reload", Frames("cultist_reload"));
                if (S("cultist_attack5_0")) Set(an, "shoot", Frames("cultist_attack5_0", "cultist_attack5_1", "cultist_attack5_2", "cultist_attack5_3", "cultist_attack5_4"));
                if (S("hurt_cultist")) Set(an, "hurt", S("hurt_cultist"));
                Set(an, "walkFps", 6f);
                root.GetComponent<SpriteRenderer>().sprite = S("cultist_idle");
                var ai = root.GetComponent<RangedEnemy>();
                Set(ai, "sightConeRange", Feedback1110Setup.EnemySightRange);
                Set(ai, "sightConeAngle", Feedback1110Setup.EnemySightAngle);
                if (S("item_flask")) Set(ai, "flaskSprite", S("item_flask"));
                if (S("corpse_cultist")) Set(root.GetComponent<EnemyCorpse>(), "corpseSprite", S("corpse_cultist"));
            });

            // 감염자 - 한 방이 세고 느리다. 물리에 강하고 빛 · 불에 약하다
            Edit("Enemy_Melee", root =>
            {
                Ensure<Core.FactionMember>(root).SetFaction(Core.Faction.Infected);
                Ensure<FX.HitFeel>(root);
                Ensure<Combat.DamageResistance>(root);
                var an = root.GetComponent<EnemySpriteAnimator>();
                Set(an, "artFacesLeft", false);
                Set(an, "idle", Frames("infected_idle"));
                Set(an, "walk", Frames("infected_walk_0", "infected_walk_1", "infected_walk_2", "infected_walk_3"));
                Set(an, "attack", Frames("infected_raise", "infected_slam"));
                Set(an, "walkFps", 4f);
                if (S("hurt_infected")) Set(an, "hurt", S("hurt_infected"));
                root.GetComponent<SpriteRenderer>().sprite = S("infected_idle");
                var ai = root.GetComponent<MeleeEnemy>();
                Set(ai, "attackDamage", 30f);
                Set(ai, "windUp", 0.55f);
                Set(ai, "attackCooldown", 1.4f);
                Set(ai, "attackRange", 2.2f);
                Set(ai, "attackArcDegrees", 140f);
                Set(ai, "moveSpeed", 2.6f);
                Set(ai, "trackUntil", 0.5f);
                Set(ai, "lungeDistance", 1.2f);
                Set(root.GetComponent<Combat.Health>(), "maxHealth", 140f);
                if (S("corpse_infected")) Set(root.GetComponent<EnemyCorpse>(), "corpseSprite", S("corpse_infected"));
            });

            // 도적 - 근접 AI 를 도적 AI 로 바꾼다
            Edit("Enemy_Rogue", root =>
            {
                Ensure<Core.FactionMember>(root).SetFaction(Core.Faction.Rogue);
                Ensure<FX.HitFeel>(root);
                var old = root.GetComponent<MeleeEnemy>();
                var rogue = Ensure<RogueEnemy>(root);
                if (old != null) Object.DestroyImmediate(old, true);
                var so = new SerializedObject(rogue);
                so.FindProperty("senseMode").enumValueIndex = (int)SenseMode.Sight;
                so.FindProperty("noticeRadius").floatValue = 3f;
                so.FindProperty("sightConeRange").floatValue = Feedback1110Setup.EnemySightRange;
                so.FindProperty("sightConeAngle").floatValue = Feedback1110Setup.EnemySightAngle;
                so.FindProperty("sightBlockMask").intValue = 1 << wall;
                so.FindProperty("moveSpeed").floatValue = 3.6f;
                so.FindProperty("loseInterestTime").floatValue = 5f;
                so.FindProperty("giveUpDistance").floatValue = 30f;
                so.FindProperty("spriteRenderer").objectReferenceValue = root.GetComponent<SpriteRenderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                var an = root.GetComponent<EnemySpriteAnimator>();
                Set(an, "artFacesLeft", false);
                Set(an, "idle", Frames("rogue_idle"));
                Set(an, "walk", Frames("rogue_walk_0", "rogue_walk_1", "rogue_walk_2", "rogue_walk_3"));
                Set(an, "attack", Frames("rogue_windup", "rogue_stab"));
                Set(an, "dodge", Frames("rogue_dodge"));
                Set(an, "walkFps", 8f);
                if (S("hurt_rogue")) Set(an, "hurt", S("hurt_rogue"));
                root.GetComponent<SpriteRenderer>().sprite = S("rogue_idle");
                Set(root.GetComponent<Combat.Health>(), "maxHealth", 55f);
                if (S("corpse_rogue")) Set(root.GetComponent<EnemyCorpse>(), "corpseSprite", S("corpse_rogue"));
            });

            // 적 투사체 - 적끼리도 맞는다 (같은 편은 투사체가 알아서 지나간다)
            Edit("EnemyProjectile", root =>
            {
                var p = root.GetComponent<Combat.EnemyProjectile>();
                Set(p, "hitMask", (1 << wall) | (1 << enemy) | (1 << player));
            });
        }

        private static void Edit(string prefab, System.Action<GameObject> act)
        {
            string path = Prefabs + prefab + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            act(root);
            // 공격 예고 - AI 무늬 · 조준점
            var tel = root.GetComponentInChildren<EnemyTelegraph>(true);
            if (tel != null && S("tel_pattern") != null)
            {
                var tso = new SerializedObject(tel);
                tso.FindProperty("pattern").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "tel_pattern.png");
                var rp = tso.FindProperty("reticle");
                var rf = Frames("tel_reticle_0", "tel_reticle_1", "tel_reticle_2", "tel_reticle_3");
                rp.arraySize = rf.Length; for (int k = 0; k < rf.Length; k++) rp.GetArrayElementAtIndex(k).objectReferenceValue = rf[k];
                tso.ApplyModifiedPropertiesWithoutUndo();
            }
            // 머리 위 ! ? - 도트 아이콘
            var icon = root.GetComponentInChildren<EnemyAlertIcon>(true);
            if (icon != null && S("alert_bang") != null)
            {
                Set(icon, "alertSprite", S("alert_bang"));
                Set(icon, "suspectSprite", S("alert_question"));
                Set(icon, "height", 2.5f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------- 상자 ----------
        private static void SetupLootBoxes()
        {
            var map = new Dictionary<string, string>
            {
                { "LootBox_Chest", "prop_chest" }, { "LootBox_Strongbox", "prop_strongbox" }, { "LootBox_Reliquary", "prop_reliquary" },
                { "LootBox_Apothecary", "prop_apothecary" }, { "LootBox_Sack", "prop_sack" }, { "LootBox_Armory", "prop_rack" },
                { "LootBox_Cart", "prop_cart" }, { "LootBox", "prop_chest" },
            };
            foreach (var kv in map)
            {
                var s = S(kv.Value);
                if (s == null || AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + kv.Key + ".prefab") == null) continue;
                Edit(kv.Key, root =>
                {
                    var sr = root.GetComponent<SpriteRenderer>();
                    sr.sprite = s;
                    sr.spriteSortPoint = SpriteSortPoint.Pivot;
                    var box = root.GetComponent<BoxCollider2D>();
                    if (box)
                    {
                        // 발밑 피벗 - 콜라이더는 물건 밑둥(바닥에 닿는 쪽)
                        Vector2 size = s.bounds.size;
                        box.size = new Vector2(size.x * 0.9f, Mathf.Min(size.y, size.x) * 0.5f);
                        box.offset = new Vector2(0f, box.size.y * 0.5f);
                    }
                });
            }
        }
    }
}
