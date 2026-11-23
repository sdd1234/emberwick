#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Capstone.Dev
{
    /// <summary>
    /// 수정사항 2026-11-10 시연 영상 촬영 (에디터 전용). 마을에서 시작해 항목마다 자막을 띄우고 실제 입력(InputPuppet)으로 시연한다.
    /// GameplayRecorder 가 화면과 게임 소리를 같이 찍는다. 끝나면 Done = true.
    /// </summary>
    public class FeatureShowcase : MonoBehaviour
    {
        public static bool Done { get; private set; }
        public static string Status { get; private set; } = "";

        private string _dir;
        private readonly System.Text.StringBuilder _results = new();
        /// <summary>측정값 한 줄 - 요구사항과 대조할 수 있게 results.tsv 에 남긴다.</summary>
        private void Check(string item, string measured, bool pass)
        {
            _results.AppendLine($"{(pass ? "PASS" : "FAIL")}\t{item}\t{measured}");
            Debug.Log($"[Showcase] {(pass ? "PASS" : "FAIL")} {item} - {measured}");
        }
        private TMP_Text _title, _sub, _card;
        private Image _cardBg;
        private Func<string> _live;
        private Player.PlayerController _pc;
        private Rigidbody2D _rb;
        private ColorAdjustments _ca;
        private float _origExposure = float.NaN;

        public static void Begin(string dir)
        {
            if (FindFirstObjectByType<FeatureShowcase>() != null) { Debug.LogWarning("[Showcase] 이미 촬영 중"); return; }
            Done = false;
            var go = new GameObject("FeatureShowcase");
            DontDestroyOnLoad(go);
            go.AddComponent<FeatureShowcase>()._dir = dir;
        }

        private void Start()
        {
            InputPuppet.Ensure();
            InputPuppet.Clear();
            BuildCaptions();
            StartCoroutine(Run());
        }

        // ---------- 자막 ----------
        private void BuildCaptions()
        {
            var cgo = new GameObject("ShowcaseCaptions");
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            var bg = new GameObject("Band", typeof(RectTransform)).GetComponent<RectTransform>();
            bg.SetParent(cgo.transform, false);
            bg.anchorMin = new Vector2(0f, 1f); bg.anchorMax = new Vector2(1f, 1f); bg.pivot = new Vector2(0.5f, 1f);
            bg.anchoredPosition = new Vector2(0f, -120f); bg.sizeDelta = new Vector2(0f, 130f);
            bg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            _title = MakeText(cgo.transform, new Vector2(0f, -132f), 46f, new Color(1f, 0.86f, 0.5f));
            _sub = MakeText(cgo.transform, new Vector2(0f, -192f), 32f, new Color(0.92f, 0.9f, 0.85f));

            var card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(cgo.transform, false);
            card.anchorMin = Vector2.zero; card.anchorMax = Vector2.one; card.offsetMin = card.offsetMax = Vector2.zero;
            _cardBg = card.gameObject.AddComponent<Image>();
            _cardBg.color = new Color(0.03f, 0.03f, 0.04f, 1f);
            _card = MakeText(card, new Vector2(0f, 0f), 60f, new Color(1f, 0.86f, 0.5f));
            var crt = _card.rectTransform; crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(1700f, 400f);
            card.gameObject.SetActive(false);
        }

        private TMP_Text MakeText(Transform parent, Vector2 pos, float size, Color color)
        {
            var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(1800f, 70f);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = FindFont();
            if (font) t.font = font;
            t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Center;
            t.outlineWidth = 0.2f; t.outlineColor = Color.black;
            t.raycastTarget = false;
            return t;
        }

        private static TMP_FontAsset _font;
        private static TMP_FontAsset FindFont()
        {
            if (_font) return _font;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f.name.Contains("Galmuri")) { _font = f; break; }
            return _font;
        }

        private void Caption(string title, string sub = "", Func<string> live = null)
        {
            _title.text = title; _sub.text = sub; _live = live;
            Status = title;
            try { GameplayRecorder.Mark(title); } catch (Exception) { }
        }

        private IEnumerator Card(string text, float seconds)
        {
            _card.text = text;
            _cardBg.gameObject.SetActive(true);
            yield return new WaitForSeconds(seconds);
            _cardBg.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_live != null) { try { _sub.text = _live(); } catch { } }
        }

        // ---------- 도우미 ----------
        private void FindPlayer()
        {
            _pc = FindFirstObjectByType<Player.PlayerController>();
            _rb = _pc ? _pc.GetComponent<Rigidbody2D>() : null;
        }

        private void Put(Vector2 p) { _rb.position = p; _rb.linearVelocity = Vector2.zero; _pc.transform.position = p; }
        private void Lit(bool on) => _pc.GetComponent<Player.TorchFuel>().SetLit(on);
        private static Vector2 Screen01(float x, float y) => new(Screen.width * x, Screen.height * y);
        private void AimScreen(float x, float y) => InputPuppet.MouseScreen = Screen01(x, y);

        /// <summary>월드 점을 화면 좌표로 - 마우스를 그 적에게 겨눌 때.</summary>
        private void AimWorld(Vector2 w)
        {
            var cam = Camera.main;
            if (cam) InputPuppet.MouseScreen = cam.WorldToScreenPoint(new Vector3(w.x, w.y + 1f, 0f));
        }

        private float Speed => _rb ? _rb.linearVelocity.magnitude : 0f;

        private static readonly List<Enemy.EnemyAI> _frozen = new();
        private void FreezeAll()
        {
            foreach (var sq in FindObjectsByType<Enemy.CultistSquad>(FindObjectsSortMode.None)) sq.enabled = false;
            foreach (var ai in FindObjectsByType<Enemy.EnemyAI>(FindObjectsSortMode.None)) Freeze(ai);
        }
        private static void Freeze(Enemy.EnemyAI ai)
        {
            if (ai == null) return;
            ai.enabled = false;
            var b = ai.GetComponent<Rigidbody2D>(); if (b) b.linearVelocity = Vector2.zero;
        }
        private static void SetFacing(Enemy.EnemyAI ai, Vector2 f)
            => typeof(Enemy.EnemyAI).GetField("_facing", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ai, f.normalized);

        /// <summary>앞뒤로 dist 만큼 벽 없이 트인 방향을 고른다 (적을 그쪽으로 세운다).</summary>
        private static Vector2 OpenAxis(Vector2 at, float dist)
        {
            var walls = Combat.ProjectileOrigin.DefaultWallMask(0);
            foreach (var d in new[] { Vector2.right, Vector2.left, Vector2.down, Vector2.up })
                if (!Physics2D.Linecast(at, at + d * dist, walls) && !Physics2D.Linecast(at, at - d * dist, walls)
                    && !Physics2D.OverlapCircle(at + d * dist, 0.6f, walls) && !Physics2D.OverlapCircle(at - d * dist, 0.6f, walls)) return d;
            return Vector2.right;
        }

        private static T Find<T>(string name) where T : Component
        {
            var g = GameObject.Find(name);
            return g ? g.GetComponentInChildren<T>() : null;
        }

        private static string Kor(Enemy.AlertState s) => s switch
        {
            Enemy.AlertState.Alerted => "<color=#FF5544>전투</color>",
            Enemy.AlertState.Suspicious => "<color=#FFD055>의심</color>",
            _ => "대기",
        };

        private static string Kor(Enemy.EnemyVisibility.Look l) => l switch
        {
            Enemy.EnemyVisibility.Look.Full => "<color=#FFFFFF>모습이 다 보임</color>",
            Enemy.EnemyVisibility.Look.Silhouette => "<color=#9AA0AA>회색 실루엣</color>",
            _ => "<color=#777777>안 보임</color>",
        };

        private void SetExposure(float v)
        {
            InitExposure();
            if (_ca != null) _ca.postExposure.value = v;
        }

        private void InitExposure()
        {
            if (_ca == null)
            {
                var vol = FindFirstObjectByType<Volume>();
                if (vol && vol.sharedProfile) vol.sharedProfile.TryGet(out _ca);
                if (_ca != null) _origExposure = _ca.postExposure.value;
            }
        }

        private void RestoreExposure()
        {
            if (_ca != null && !float.IsNaN(_origExposure)) _ca.postExposure.value = _origExposure;
        }

        private void OnDestroy() { RestoreExposure(); InputPuppet.Clear(); }

        // ---------- 시연 ----------
        private IEnumerator Run()
        {
            while (FindFirstObjectByType<Player.PlayerController>() == null) yield return null;
            yield return new WaitForSeconds(1.5f);
            FindPlayer();
            GameplayRecorder.Begin(_dir);
            yield return Card("수정사항 2026-11-10 반영 시연\n<size=60%>정조준 감속 · 밝기 · 등급별 상자 · 인벤토리 · 출혈 · 회피 · 적 표시 · 적 감지 · 적 광원</size>", 3.5f);

            yield return TownChests();

            Caption("마을 → 레이드", "출구에서 이동");
            var gate = FindFirstObjectByType<Town.RaidGate>();
            if (gate) gate.Accept();
            string town = SceneManager.GetActiveScene().name;
            float until = Time.time + 15f;
            while (SceneManager.GetActiveScene().name == town && Time.time < until) yield return null;
            while (FindFirstObjectByType<Player.PlayerController>() == null) yield return null;
            yield return new WaitForSeconds(2.5f);
            FindPlayer();
            _pc.GetComponent<Combat.Health>().Invulnerable = true;
            FreezeAll();
            Vector2 start = _rb.position;

            yield return Brightness(start);
            yield return AimSlow(start);
            yield return EnemyDisplay();
            yield return Detection();
            yield return EnemyLights();
            yield return Bleed();
            yield return Dodge();
            yield return Inventory();

            Caption("", "");
            yield return Card("시연 끝\n<size=55%>Assets/_Game/Editor/Feedback1110Setup.cs · 메뉴 Capstone/피드백 적용/2026-11-10 수정사항 (전부)</size>", 3f);
            RestoreExposure();
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, "results.tsv"), _results.ToString(), new System.Text.UTF8Encoding(false)); } catch (Exception e) { Debug.LogWarning(e.Message); }
            GameplayRecorder.Stop();
            InputPuppet.Clear();
            Done = true;
            Status = "done";
            Debug.Log("[Showcase] 촬영 끝 - " + _dir);
        }

        private IEnumerator TownChests()
        {
            string[] names = { "SoundTest_Common", "SoundTest_Rare", "SoundTest_Unique", "SoundTest_Legendary" };
            string[] kor = { "일반", "레어", "유니크", "전설" };
            for (int i = 0; i < names.Length; i++)
            {
                var box = GameObject.Find(names[i]);
                if (box == null) continue;
                Put((Vector2)box.transform.position + new Vector2(0f, -1.2f));
                Caption("2. 마을 등급별 상자 (사운드 테스트)", $"{kor[i]} 견본 상자 - 감정될 때마다 {kor[i]} 등급 소리");
                yield return new WaitForSeconds(0.8f);
                string grade = names[i].Substring("SoundTest_".Length).ToLowerInvariant();
                int heard = 0;
                Action<string, float> ear = (c, v) => { if (c == "item_get_" + grade) heard++; };
                FX.SfxBank.Played += ear;
                InputPuppet.Tap.Add(Key.F);
                yield return new WaitForSeconds(3.6f);
                FX.SfxBank.Played -= ear;
                var lc = box.GetComponent<Items.LootContainer>();
                bool locked = lc != null && lc.DisplayOnly;
                Check($"2. 마을 {kor[i]} 상자 감정음", $"item_get_{grade} {heard}번, 꺼내기 막힘={locked}", heard >= 1 && locked);
                InputPuppet.Tap.Add(Key.Escape);
                yield return new WaitForSeconds(0.6f);
            }
        }

        private IEnumerator Brightness(Vector2 start)
        {
            Put(start); Lit(true); AimScreen(0.7f, 0.5f);
            InitExposure();
            SetExposure(_origExposure - 0.585f);
            Caption("1. 맵 밝기", "보정 전");
            yield return new WaitForSeconds(2.5f);
            RestoreExposure();
            Caption("1. 맵 밝기", "밝기 +50% (화면 노출 x1.5)");
            float gain = _ca != null ? Mathf.Pow(2f, _ca.postExposure.value - 0.15f) : 0f;
            Check("1. 맵 밝기 +50%", $"노출 {(_ca != null ? _ca.postExposure.value : -1f):0.000} = 이전 대비 x{gain:0.00}", Mathf.Abs(gain - 1.5f) < 0.02f);
            yield return new WaitForSeconds(3f);
        }

        private IEnumerator AimSlow(Vector2 start)
        {
            Put(start); Lit(true);
            Func<string> live = () => $"이동 속도 <color=#FFD055>{Speed:0.00} m/s</color>   ·   {(_pc.IsAiming ? "정조준(우클릭)" : "보통")}{(_pc.IsSprinting ? " · 달리기" : "")}";
            Caption("정조준 감속", "", live);
            AimScreen(0.85f, 0.5f);
            InputPuppet.Held.Add(Key.D);
            yield return new WaitForSeconds(2.2f);
            float walk = Speed;
            InputPuppet.Right = true;
            yield return new WaitForSeconds(2.6f);
            float aimV = Speed;
            Check("정조준 감속", $"보통 {walk:0.00} → 정조준 {aimV:0.00} m/s (x{aimV / Mathf.Max(0.01f, walk):0.00})", walk > 3f && aimV < walk * 0.6f && aimV > 0.5f);
            InputPuppet.Held.Remove(Key.D); InputPuppet.Held.Add(Key.A); AimScreen(0.15f, 0.5f);
            InputPuppet.Held.Add(Key.LeftShift);
            Caption("정조준 감속", "", () => live() + "   <color=#FF8866>(Shift 눌러도 정조준 중엔 못 달린다)</color>");
            yield return new WaitForSeconds(2.6f);
            float aimShift = Speed; bool sprinted = _pc.IsSprinting;
            Check("정조준 중 달리기 불가", $"정조준+Shift {aimShift:0.00} m/s, 달리기={sprinted}", !sprinted && aimShift < 2f);
            InputPuppet.Right = false;
            Caption("정조준 감속", "", live);
            yield return new WaitForSeconds(1.6f);
            Check("정조준 풀면 달리기", $"Shift {Speed:0.00} m/s", Speed > 5f);
            InputPuppet.Clear();
            yield return new WaitForSeconds(0.4f);
        }

        private IEnumerator EnemyDisplay()
        {
            var inf = Find<Enemy.EnemyAI>("Infected_1");
            if (inf != null)
            {
                var vis = inf.GetComponent<Enemy.EnemyVisibility>();
                Vector2 e = inf.transform.position;
                Func<string> live = () => "감염자 → " + Kor(vis.Current);
                Put(e + new Vector2(-4f, 0f)); Lit(false); AimScreen(0.8f, 0.5f);
                Caption("5. 적 표시 - B. 시야각 안, 빛 없음", "", live);
                yield return new WaitForSeconds(3.2f);
                Check("5. B 시야각+빛 없음 → 실루엣", vis.Current.ToString(), vis.Current == Enemy.EnemyVisibility.Look.Silhouette);
                Lit(true);
                Caption("5. 적 표시 - A. 등불 범위 안", "", live);
                yield return new WaitForSeconds(3f);
                Check("5. A 등불 범위 → 다 보임", vis.Current.ToString(), vis.Current == Enemy.EnemyVisibility.Look.Full);
                Lit(false); AimScreen(0.15f, 0.5f);
                Caption("5. 적 표시 - F. 등 뒤 어둠", "", live);
                yield return new WaitForSeconds(3f);
                Check("5. F 등 뒤 어둠 → 안 보임", vis.Current.ToString(), vis.Current == Enemy.EnemyVisibility.Look.Hidden);
                Put(e + new Vector2(-1.2f, 0f));
                Caption("5. 적 표시 - C. 1.5m 이내", "", live);
                yield return new WaitForSeconds(2.8f);
                Check("5. C 1.5m 이내 → 다 보임 (등 돌려도)", vis.Current.ToString(), vis.Current == Enemy.EnemyVisibility.Look.Full);
            }
            var rg = Find<Enemy.EnemyAI>("Rogue_9");
            if (rg != null)
            {
                var vis = rg.GetComponent<Enemy.EnemyVisibility>();
                Put((Vector2)rg.transform.position + new Vector2(-7.5f, -1.5f)); Lit(false); AimScreen(0.2f, 0.2f);
                Caption("5. 적 표시 - D. 적이 든 불빛", "", () => "등불을 든 도적 (7.5m, 시야각 밖) → " + Kor(vis.Current));
                yield return new WaitForSeconds(3.2f);
                Check("5. D 광원 안의 적 → 멀리서도 보임", vis.Current.ToString(), vis.Current == Enemy.EnemyVisibility.Look.Full);
            }
        }

        private IEnumerator Detection()
        {
            var a = Find<Enemy.EnemyAI>("Cultist_Reserve_1");
            if (a != null)
            {
                Vector2 ep = a.transform.position;
                Vector2 fd = OpenAxis(ep, 6.5f);
                Put(ep - fd * 6f); Lit(true); AimWorld(ep);
                a.enabled = true; SetFacing(a, fd);
                Caption("6. 적 감지 - 등 뒤 6m 에서 등불 켬", "", () => "이교도 → " + Kor(a.State) + "   (등 뒤의 불빛은 못 본다)");
                yield return new WaitForSeconds(3.2f);
                Check("6. 등 뒤 불빛 → 전투 안 됨", a.State.ToString(), a.State != Enemy.AlertState.Alerted);
                Put(ep + fd * 6f); AimWorld(ep);
                Caption("6. 적 감지 - 앞쪽 180도 + 빛 탐지 거리", "", () => "이교도 → " + Kor(a.State));
                yield return new WaitForSeconds(3f);
                Check("6. 시야각 안 + 빛 거리 → 전투", a.State.ToString(), a.State == Enemy.AlertState.Alerted);
                Freeze(a);
            }
            var b = Find<Enemy.EnemyAI>("Cultist_Reserve_3");
            if (b != null)
            {
                Vector2 ep = b.transform.position;
                Vector2 fd = OpenAxis(ep, 7f);
                Put(ep - fd * 6.5f); Lit(false); AimWorld(ep);
                b.enabled = true; SetFacing(b, fd);
                Caption("6. 적 감지 - 소음", "", () => "이교도 → " + Kor(b.State));
                yield return new WaitForSeconds(1.8f);
                Core.NoiseSystem.Emit(_rb.position, 10f, _pc.gameObject);
                yield return null;
                var afterNoise = b.State;
                Caption("6. 적 감지 - 소음 거리 안 → 무조건 의심", "", () => "이교도 → " + Kor(b.State) + "   (소리 난 쪽으로 돌아본다)");
                yield return new WaitForSeconds(0.6f);
                float turn = Vector2.Angle(b.Facing, _rb.position - (Vector2)b.transform.position);
                Check("6. 소음 → 의심 (전투 아님)", $"{afterNoise}, 소리 쪽과 시선 각 {turn:0}°", afterNoise == Enemy.AlertState.Suspicious && turn < 45f);
                yield return new WaitForSeconds(2f);
                Put(ep - fd * 3f);
                Caption("6. 적 감지 - 의심 중 시야각 안에 보임 → 전투", "", () => "이교도 → " + Kor(b.State));
                yield return new WaitForSeconds(2.8f);
                Check("6. 의심 중 시야각 안에 보임 → 전투", b.State.ToString(), b.State == Enemy.AlertState.Alerted);
                Freeze(b);
            }
        }

        private IEnumerator EnemyLights()
        {
            var rg = Find<Enemy.EnemyAI>("Rogue_9");
            if (rg == null) yield break;
            var body = rg.GetComponent<Rigidbody2D>();
            Vector2 home = body.position;
            // 도적이 화면 가운데 아래쪽에 오게 - 위로 걸어도 자막 띠에 안 가린다
            Put(home + new Vector2(-2.6f, 1.2f)); Lit(false); AimWorld(home);
            (Vector2 dir, string name)[] legs = { (Vector2.right, "오른쪽"), (Vector2.up, "위"), (Vector2.left, "왼쪽"), (Vector2.down, "아래") };
            foreach (var leg in legs)
            {
                body.position = home - leg.dir * 0.6f;                   // 걸음마다 같은 자리 근처에서
                Caption("7. 인간형 적 광원 - 도적 허리 등불", $"바라보는 방향: {leg.name} → 등불도 {leg.name}");
                float end = Time.time + 2.2f;
                while (Time.time < end) { body.linearVelocity = leg.dir * 0.55f; yield return new WaitForFixedUpdate(); }
                var lantern = rg.transform.Find("WaistLantern");
                Vector2 off = lantern != null ? (Vector2)lantern.position - (Vector2)rg.transform.position - new Vector2(0f, 1.04f) : Vector2.zero;
                bool sprite = lantern != null && lantern.GetComponent<SpriteRenderer>() != null;
                Check($"7. 도적 등불 - {leg.name}", $"빛 중심 오프셋 ({off.x:0.00}, {off.y:0.00})m, 노란 그림={sprite}", lantern != null && !sprite && Vector2.Dot(off.normalized, leg.dir) > 0.9f && off.magnitude > 0.6f);
                body.linearVelocity = Vector2.zero;
            }
            body.position = home;
        }

        private IEnumerator Bleed()
        {
            var ail = _pc.GetComponent<Combat.PlayerAilments>();
            var hp = _pc.GetComponent<Combat.Health>();
            hp.Invulnerable = false;
            Func<string> live = () => ail.IsBleeding ? $"출혈 남은 시간 <color=#FF5544>{ail.BleedRemaining:0.0}초</color>   체력 {hp.Current:0}" : $"출혈 없음   체력 {hp.Current:0}";
            ail.Wound(0f, 1f);
            Caption("4. 출혈 최대 5초", "", live);
            yield return new WaitForSeconds(2f);
            ail.Wound(0f, 1f); ail.Wound(0f, 1f);
            float stacked = ail.BleedRemaining;
            Check("4. 출혈 겹쳐도 5초 이하", $"2초 뒤 두 번 더 베임 → 남은 {stacked:0.00}초", stacked <= 5.001f && stacked > 4.5f);
            Caption("4. 출혈 - 두 번 더 베여도 5초를 넘지 않는다", "", live);
            float t0 = Time.time, until = Time.time + 6f;
            while (ail.IsBleeding && Time.time < until) yield return null;
            Check("4. 출혈 5초 뒤 저절로 멎음", $"{Time.time - t0:0.00}초 뒤 멎음={!ail.IsBleeding}", !ail.IsBleeding && Time.time - t0 <= 5.2f);
            Caption("4. 출혈 - 5초가 지나면 저절로 멎는다", "", live);
            yield return new WaitForSeconds(1.8f);
            hp.Invulnerable = true;
        }

        private IEnumerator Dodge()
        {
            Caption("4. 도적 석궁 회피율 상한 60% → 40%", "", () => $"도적 회피율 <color=#FFD055>{Enemy.RogueLearning.DodgeChance * 100f:0}%</color>   (볼트 {Enemy.RogueLearning.BoltsSeen}발 봄)");
            for (int i = 0; i < 30; i++) { Enemy.RogueLearning.SawBolt(); yield return new WaitForSeconds(0.1f); }
            Check("4. 도적 회피율 최대 40%", $"볼트 {Enemy.RogueLearning.BoltsSeen}발 → {Enemy.RogueLearning.DodgeChance * 100f:0}%", Mathf.Approximately(Enemy.RogueLearning.DodgeChance, 0.4f));
            yield return new WaitForSeconds(2f);
        }

        private IEnumerator Inventory()
        {
            var cart = GameObject.Find("Crossroads_Cart");
            if (cart == null) yield break;
            Put((Vector2)cart.transform.position + new Vector2(0f, -1.3f)); Lit(true);
            var bag = _pc.GetComponent<Items.PlayerInventory>();
            var bolt = FindBolt(bag);
            Func<string> live = () => $"가방의 볼트 {CountIn(bag.Grid, bolt)}개";
            Caption("3. 인벤토리 - 상자 감정", "", live);
            yield return new WaitForSeconds(0.6f);
            InputPuppet.Tap.Add(Key.F);
            yield return new WaitForSeconds(0.5f);
            var box = _pc.GetComponent<Items.Interactor>().Searching;
            if (box != null && bolt != null) box.Grid.TryAutoPlace(new Items.GridItem { Def = bolt, Count = 3, Identified = false, RevealAt = 1.5f });
            yield return new WaitForSeconds(6.5f);

            var view = FindView(bolt, true);
            if (view != null)
            {
                Caption("3. 인벤토리 - Alt + 우클릭", "", () => "상자의 볼트 3개 → 가방의 볼트 더미에 겹친다   ·   " + live());
                yield return new WaitForSeconds(1.5f);
                InputPuppet.MouseScreen = Center(view);
                InputPuppet.Held.Add(Key.LeftAlt);
                yield return new WaitForSeconds(0.3f);
                InputPuppet.RightClickOnce = true;
                yield return new WaitForSeconds(0.4f);
                InputPuppet.Held.Remove(Key.LeftAlt);
                yield return new WaitForSeconds(2f);
                int inBag = CountIn(bag.Grid, bolt);
                int stacks = 0; foreach (var it in bag.Grid.Items) if (it.Def == bolt) stacks++;
                Check("3. Alt+우클릭 + 겹치기", $"가방 볼트 {inBag}개, 더미 {stacks}개, 상자 남은 볼트 {(box != null ? CountIn(box.Grid, bolt) : -1)}", inBag == 13 && stacks == 1);
            }
            GridItemViewAny(out var other);
            if (other != null)
            {
                var otherItem = other.Item;
                Caption("3. 인벤토리 - Alt + 우클릭", otherItem.Def.displayName + " → 가방 빈 칸으로 바로 이동");
                InputPuppet.MouseScreen = Center(other);
                InputPuppet.Held.Add(Key.LeftAlt);
                yield return new WaitForSeconds(0.3f);
                InputPuppet.RightClickOnce = true;
                yield return new WaitForSeconds(0.4f);
                InputPuppet.Held.Remove(Key.LeftAlt);
                yield return new WaitForSeconds(2.2f);
                bool moved = false; foreach (var it in bag.Grid.Items) if (it == otherItem || (it.Def == otherItem.Def && otherItem.Count <= 0)) moved = true;
                Check("3. Alt+우클릭 - 다른 물건", $"{otherItem.Def.displayName} 가방으로 이동={moved}", moved);
            }
            InputPuppet.Tap.Add(Key.Escape);
            yield return new WaitForSeconds(0.6f);
        }

        private static Items.ItemDefinition FindBolt(Items.PlayerInventory bag)
        {
            if (bag != null) foreach (var it in bag.Grid.Items) if (it.Def != null && it.Def.name == "Item_Bolt") return it.Def;
            foreach (var d in Resources.FindObjectsOfTypeAll<Items.ItemDefinition>()) if (d.name == "Item_Bolt") return d;
            return null;
        }

        private static int CountIn(Items.ItemGrid g, Items.ItemDefinition def)
        {
            int n = 0;
            if (g != null) foreach (var it in g.Items) if (it.Def == def) n += it.Count;
            return n;
        }

        private static UI.GridItemView FindView(Items.ItemDefinition def, bool inContainer)
        {
            foreach (var v in FindObjectsByType<UI.GridItemView>(FindObjectsSortMode.None))
            {
                var p = v.GetComponentInParent<UI.GridPanelUI>();
                if (v.Item != null && v.Item.Def == def && p != null && (p.Container != null) == inContainer) return v;
            }
            return null;
        }

        private static void GridItemViewAny(out UI.GridItemView view)
        {
            view = null;
            foreach (var v in FindObjectsByType<UI.GridItemView>(FindObjectsSortMode.None))
            {
                var p = v.GetComponentInParent<UI.GridPanelUI>();
                if (v.Item != null && v.Item.Identified && p != null && p.Container != null) { view = v; return; }
            }
        }

        private static Vector2 Center(Component c)
        {
            var corners = new Vector3[4];
            ((RectTransform)c.transform).GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }
    }
}
#endif
