using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Capstone.UI
{
    /// <summary>
    /// 도트 고딕 HUD (2026-10-29 - 피그마 시안 "Emberwick HUD - 역병의 시대" 를 그대로 옮긴 것).
    ///   왼쪽 위   - 초상 프레임 + 체력 · 기력 바 + 상태 칩 (출혈 · 감염 · 은신)
    ///   오른쪽 위 - 남은 시간 장식판 (랜턴은 LanternHUD)
    ///   오른쪽 아래 - 빛 플라스크 개수 + 석궁 볼트
    ///   가운데 아래 - 상호작용 안내 (키캡 + 문구)
    ///   왼쪽 아래 - 조작키
    /// 모든 도트는 원본의 2배 (월드와 같은 도트 크기). 시안의 좌표를 1920x1080 기준으로 그대로 쓴다.
    /// 조각은 처음 켜질 때 코드로 만든다 - 프리팹을 고치지 않고 레이드 씬마다 같은 HUD 가 뜬다.
    /// </summary>
    public class PixelHUD : MonoBehaviour
    {
        [System.Serializable]
        public class Sprites
        {
            public Sprite portraitFrame, portrait, healthBar, healthMask, staminaBar, staminaMask, chip, panel, keycap, plaque;
            public Sprite iconBleed, iconInfect, iconHidden, iconSpotted, iconFlask, iconCrossbow;
        }

        [SerializeField] private Sprites art = new();
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("작은 설명 글자 (Galmuri9). 비우면 본문 글꼴")]
        [SerializeField] private TMP_FontAsset smallFont;

        /// <summary>도트 글꼴 - 머리 위 외침 · 피해 글자 · 랜턴이 같이 쓴다.</summary>
        public static TMP_FontAsset MainFont { get; private set; }
        public static TMP_FontAsset SmallFont { get; private set; }
        [Tooltip("바 그림 안에서 채움이 들어가는 칸 (2배 px, 왼쪽 위 기준). 맵 빌더가 마스크에서 읽어 넣는다")]
        [SerializeField] private Rect healthFillRect = new(86, 48, 410, 20);
        [SerializeField] private Rect staminaFillRect = new(78, 34, 260, 16);
        [SerializeField] private Rect portraitInnerRect = new(14, 14, 60, 58);

        [Header("대상 (비우면 찾는다)")]
        [SerializeField] private Combat.Health health;
        [SerializeField] private Player.PlayerStamina stamina;
        [SerializeField] private Combat.PlayerAilments ailments;
        [SerializeField] private Combat.PlayerStealth stealth;
        [SerializeField] private Combat.Crossbow crossbow;
        [SerializeField] private Player.TorchFuel torch;
        [SerializeField] private Player.LightFlaskThrower flask;
        [SerializeField] private Items.Interactor interactor;
        [SerializeField] private Core.RaidManager raid;

        private static readonly Color Ivory = new(0.90f, 0.88f, 0.82f), Gold = new(1f, 0.88f, 0.54f), Blood = new(0.77f, 0.15f, 0.14f),
                                      Dim = new(0.59f, 0.55f, 0.49f), Ink = new(0.03f, 0.02f, 0.04f);

        private bool _ready;
        private RectTransform _root;
        private Image _hpFill, _hpShine, _stFill;
        private TMP_Text _hpText, _timer, _flaskCount, _bolts, _promptKey, _promptText, _promptExtra, _promptCycle;
        private RectTransform _promptRoot, _promptBox;
        private GameObject _prompt;
        private readonly List<Image> _pips = new();
        private GameObject _chipBleed, _chipInfect, _chipHidden;
        private TMP_Text _bleedText;
        private TMP_Text _capFlask, _capBandage;
        private TMP_Text _infectText, _hiddenText;
        private RectTransform _chips;
        private float _hpShown = 1f, _stShown = 1f;
        private RectTransform _toast;
        private TMP_Text _toastText;
        private Image _toastPlate;

        private void Awake() => Build();
        private void OnEnable() => Build();

        // ---------- 만들기 ----------
        private void Build()
        {
            if (_ready || art.healthBar == null) return;
            _ready = true;
            Find();
            if (font == null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) if (t.font) { font = t.font; break; }
            MainFont = font;
            SmallFont = smallFont != null ? smallFont : font;

            _root = New("PixelHUD", transform);
            Stretch(_root);
            _root.SetAsFirstSibling();                  // 루팅 화면 · 페이드가 위에 오도록

            // 왼쪽 위
            var tl = Panel("TopLeft", _root, new Vector2(0, 1), new Vector2(20, -14), new Vector2(720, 230));
            Vector2 fs = Size2(art.portraitFrame), ps = Size2(art.portrait);
            var pi = portraitInnerRect;
            // 초상은 틀 안쪽 칸 가운데, 바닥에 붙인다 (어깨가 틀 아래로 잘리게)
            Img("PortraitBack", tl, null, pi.position, pi.size);
            Fill("PortraitBg", tl, pi.position, pi.size, new Color(0.10f, 0.09f, 0.11f));
            Img("Portrait", tl, art.portrait, new Vector2(pi.center.x - ps.x * 0.5f, pi.yMax - ps.y + 6f), ps);
            Img("PortraitFrame", tl, art.portraitFrame, Vector2.zero, fs);
            float bx = fs.x + 6f;
            var hr = healthFillRect; var sr = staminaFillRect;
            float hb = Size2(art.healthBar).y + 2f;
            // 채움은 바 틀 뒤에 깐다 - 틀의 안쪽 테두리가 채움 끝을 덮어 깔끔하게 끊긴다
            var hpBack = Fill("HealthBack", tl, new Vector2(bx + hr.x, hr.y), hr.size, new Color(0.08f, 0.05f, 0.05f, 0.9f));
            _hpFill = Fill("HealthFill", tl, new Vector2(bx + hr.x, hr.y), hr.size, Blood);
            _hpShine = Fill("HealthShine", tl, new Vector2(bx + hr.x, hr.y), new Vector2(hr.width, Mathf.Max(2, Mathf.Round(hr.height * 0.2f / 2) * 2)), new Color(0.94f, 0.43f, 0.39f, 0.8f));
            Img("HealthBar", tl, art.healthBar, new Vector2(bx, 0), Size2(art.healthBar));
            _hpText = Text("HealthText", tl, new Vector2(bx + hr.x, hr.y + hr.height * 0.5f - 14), new Vector2(hr.width, 28), 18, Ivory, TextAlignmentOptions.Center);
            float sy = hb - 8;
            Fill("StaminaBack", tl, new Vector2(bx + sr.x, sy + sr.y), sr.size, new Color(0.08f, 0.06f, 0.04f, 0.9f));
            _stFill = Fill("StaminaFill", tl, new Vector2(bx + sr.x, sy + sr.y), sr.size, new Color(0.84f, 0.72f, 0.43f));
            Img("StaminaBar", tl, art.staminaBar, new Vector2(bx, sy), Size2(art.staminaBar));
            _chips = New("Chips", tl);
            Place(_chips, new Vector2(bx, sy + Size2(art.staminaBar).y + 2), new Vector2(600, 58));
            var hl = _chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childControlWidth = hl.childControlHeight = false; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            _chipBleed = Chip("Bleed", art.iconBleed, "출혈", new Color(0.88f, 0.35f, 0.31f), out _bleedText);
            _chipInfect = Chip("Infect", art.iconInfect, "감염", new Color(0.56f, 0.80f, 0.35f), out _infectText);
            _chipHidden = Chip("Hidden", art.iconHidden, "은신", new Color(0.59f, 0.75f, 0.90f), out _hiddenText);

            // 오른쪽 위 - 남은 시간 (랜턴은 LanternHUD 가 그 오른쪽에 있다)
            var tr = Panel("TopRight", _root, new Vector2(1, 1), new Vector2(-120, -14), new Vector2(200, 110));
            Vector2 pls = Size2(art.plaque);
            Img("Plaque", tr, art.plaque, new Vector2(0, 14), pls);
            // 도트 글꼴은 크기 12 의 정수배 · 정수 좌표에서만 격자에 맞는다. 자간 8.333 = 글꼴 1픽셀 (크기 24 에서 2px)
            float timerTop = Mathf.Round(14 + pls.y * 0.5f);
            Text("TimerCaption", tr, new Vector2(0, timerTop - 26), new Vector2(pls.x, 20), 12, Dim, TextAlignmentOptions.Center, 0f).text = "남은 시간";
            _timer = Text("Timer", tr, new Vector2(0, timerTop - 8), new Vector2(pls.x, 34), 24, Ivory, TextAlignmentOptions.Center);
            _timer.characterSpacing = 8.333f;

            // 오른쪽 아래 - 빛 플라스크 · 석궁
            var br = Panel("BottomRight", _root, new Vector2(1, 0), new Vector2(-20, 20), new Vector2(300, 200));
            Panel9("FlaskPanel", br, new Vector2(36, 14), new Vector2(264, 80));
            Img("FlaskIcon", br, art.iconFlask, new Vector2(62, 30), new Vector2(30, 42));
            Text("FlaskLabel", br, new Vector2(100, 28), new Vector2(130, 22), 15, Dim, TextAlignmentOptions.Left, 0.2f).text = "빛 플라스크";
            _flaskCount = Text("FlaskCount", br, new Vector2(100, 46), new Vector2(100, 30), 22, Gold, TextAlignmentOptions.Left);
            Key(br, "G", new Vector2(236, 30), 0.5f);
            Panel9("BowPanel", br, new Vector2(36, 108), new Vector2(264, 80));
            Img("BowIcon", br, art.iconCrossbow, new Vector2(58, 124), Size2(art.iconCrossbow));
            Text("BowLabel", br, new Vector2(132, 118), new Vector2(80, 22), 15, Dim, TextAlignmentOptions.Left, 0.2f).text = "석궁";
            _bolts = Text("Bolts", br, new Vector2(132, 134), new Vector2(64, 36), 28, Ivory, TextAlignmentOptions.Left);
            for (int i = 0; i < 10; i++)
                _pips.Add(Fill("Pip" + i, br, new Vector2(198 + (i % 5) * 14, 132 + (i / 5) * 16), new Vector2(8, 12), new Color(0.84f, 0.78f, 0.67f)));

            // 가운데 아래 - 상호작용
            var bc = Panel("Prompt", _root, new Vector2(0.5f, 0), new Vector2(0, 200), new Vector2(600, 64));
            bc.pivot = new Vector2(0.5f, 0f);
            _prompt = bc.gameObject;
            _promptRoot = bc;
            _promptKey = Key(bc, "F", new Vector2(0, 2), 1f);
            _promptBox = Panel9("PromptBox", bc, new Vector2(70, 0), new Vector2(530, 64)).rectTransform;
            _promptText = Text("PromptText", bc, new Vector2(94, 12), new Vector2(330, 40), 24, Ivory, TextAlignmentOptions.Left);
            _promptExtra = Text("PromptExtra", bc, new Vector2(400, 12), new Vector2(186, 40), 24, Gold, TextAlignmentOptions.Right);
            // 겹친 대상 전환 안내는 판자 위 한 줄 - 판자 안에 같이 넣으면 긴 문구와 겹친다
            _promptCycle = Text("PromptCycle", bc, new Vector2(70, -34), new Vector2(530, 30), 24, Gold, TextAlignmentOptions.Right);

            // 왼쪽 아래 - 조작키
            var bl = Panel("Hints", _root, new Vector2(0, 0), new Vector2(22, 22), new Vector2(260, 210));
            string[,] hints = { { "E", "등불 켜기 · 끄기" }, { "휠", "등불 밝기 (강 / 약)" }, { "G", "빛 플라스크" }, { "H", "붕대" }, { "Tab", "가방" } };
            for (int i = 0; i < hints.GetLength(0); i++)
            {
                float y = i * 41;
                var cap = Key(bl, hints[i, 0], new Vector2(0, y + 2), 0.5f);   // 정확히 반 - 글자가 12px 정수 크기로 떨어진다
                if (hints[i, 0] == "G") _capFlask = cap;
                if (hints[i, 0] == "H") _capBandage = cap;
                Text("Hint" + i, bl, new Vector2(46, y + 4), new Vector2(260, 30), 16, Dim, TextAlignmentOptions.Left, 0.2f).text = hints[i, 1];
            }

            HideOld();
            PixelSizes();

            gameObject.AddComponent<QuickBarUI>().Build(font);
        }

        /// <summary>
        /// 도트 글꼴은 원래 크기의 정수배에서만 또렷하다 (Galmuri11 = 12, Galmuri9 = 10).
        /// 큰 글자(22 이상)는 본문 글꼴 24 · 36, 나머지는 작은 글꼴 20 으로 맞춘다.
        /// </summary>
        private void PixelSizes()
        {
            foreach (var t in _root.GetComponentsInChildren<TMP_Text>(true))
            {
                // 12 의 정수배만 쓴다 - 1.5배 같은 어중간한 크기는 도트가 두께가 들쭉날쭉해진다
                t.font = font;
                t.fontSize = t.fontSize >= 26f ? 36f : t.fontSize >= 16f ? 24f : 12f;
                t.characterSpacing = 0f;
            }
        }

        /// <summary>예전 HUD 조각 - 새 HUD 가 같은 정보를 보여 준다.</summary>
        private void HideOld()
        {
            // 무게 - 무기 카드 위로 비킨다
            var w = transform.Find("WeightText") as RectTransform;
            if (w) { w.anchoredPosition = new Vector2(-40, 236); var wt = w.GetComponent<TMP_Text>(); if (wt) { wt.fontSize = 16; wt.color = Dim; wt.alignment = TextAlignmentOptions.Right; } }
            foreach (var n in new[] { "HealthBG", "AilmentText", "AmmoText", "StealthIndicator", "PromptText", "TimerText", "FuelBG", "LitIndicator" })
            {
                var t = transform.Find(n);
                if (t) t.gameObject.SetActive(false);
            }
            // 알림은 위쪽 가운데로 - 판자 위에
            var toast = transform.Find("ToastText") as RectTransform;
            if (toast)
            {
                toast.anchorMin = toast.anchorMax = new Vector2(0.5f, 1f);
                toast.pivot = new Vector2(0.5f, 1f);
                toast.anchoredPosition = new Vector2(0, -214);
                var tt = toast.GetComponent<TMP_Text>();
                if (tt) { tt.fontSize = 20; tt.color = Ivory; tt.outlineWidth = 0.2f; tt.outlineColor = Ink; }
                // 판자는 글자 뒤(앞 형제)에 둔다 - 글자 자식으로 두면 글자를 덮는다
                if (_toastPlate == null)
                {
                    _toast = toast;
                    _toastText = tt;
                    _toastPlate = Panel9("ToastPlate", (RectTransform)transform, Vector2.zero, Vector2.zero);
                    _toastPlate.transform.SetSiblingIndex(toast.GetSiblingIndex());
                }
            }
        }

        private void Find()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            if (pc == null) return;
            if (!health) health = pc.GetComponent<Combat.Health>();
            if (!stamina) stamina = pc.GetComponent<Player.PlayerStamina>();
            if (!ailments) ailments = pc.GetComponent<Combat.PlayerAilments>();
            if (!stealth) stealth = pc.GetComponent<Combat.PlayerStealth>();
            if (!crossbow) crossbow = pc.GetComponentInChildren<Combat.Crossbow>();
            if (!torch) torch = pc.GetComponent<Player.TorchFuel>();
            if (!flask) flask = pc.GetComponent<Player.LightFlaskThrower>();
            if (!interactor) interactor = pc.GetComponent<Items.Interactor>();
            if (!raid) raid = FindFirstObjectByType<Core.RaidManager>();
        }

        // ---------- 갱신 ----------
        private void LateUpdate()
        {
            if (!_ready) { Build(); if (!_ready) return; }
            float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);

            if (health)
            {
                _hpShown = Mathf.Lerp(_hpShown, health.Normalized, k);
                SetWidth(_hpFill, healthFillRect.width * _hpShown);
                SetWidth(_hpShine, healthFillRect.width * _hpShown);
                _hpText.text = $"{health.Current:0} / {health.Max:0}";
            }
            if (stamina) { _stShown = Mathf.Lerp(_stShown, stamina.Normalized, k); SetWidth(_stFill, staminaFillRect.width * _stShown); }

            _chipBleed.SetActive(ailments && ailments.IsBleeding);
            if (_bleedText && ailments && ailments.IsBleeding) _bleedText.text = $"출혈 {Mathf.CeilToInt(ailments.BleedRemaining)}초";   // 2026-11-10 - 최대 5초
            _chipInfect.SetActive(ailments && ailments.IsInfected);
            if (ailments && ailments.IsInfected) _infectText.text = $"감염 {ailments.Infection:0}%";
            _chipHidden.SetActive(stealth && (stealth.IsStealthed || stealth.IsHidden));
            if (stealth) _hiddenText.text = stealth.IsHidden ? "숨음" : "은신";

            if (_capFlask) _capFlask.text = Core.KeyBindings.Label(Core.GameAction.Flask);
            if (_capBandage) _capBandage.text = Core.KeyBindings.Label(Core.GameAction.Bandage);
            if (raid) { float t = raid.TimeRemaining; _timer.text = $"{Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}"; _timer.color = t < 120f ? new Color(1f, 0.5f, 0.42f) : Ivory; }

            if (torch && flask) _flaskCount.text = $"× {Mathf.FloorToInt(torch.Fuel / Mathf.Max(1f, flask.FuelCost))}";
            if (crossbow)
            {
                int n = crossbow.BoltsLeft;
                _bolts.text = n < 0 ? "∞" : n.ToString();
                _bolts.color = n == 0 ? new Color(0.9f, 0.35f, 0.3f) : Ivory;
                for (int i = 0; i < _pips.Count; i++)
                    _pips[i].color = (n < 0 || i < n) ? (crossbow.IsLoaded || i > 0 ? new Color(0.84f, 0.78f, 0.67f) : new Color(0.55f, 0.5f, 0.42f)) : new Color(0.25f, 0.23f, 0.21f);
            }

            if (_toastPlate)
            {
                bool on = _toast.gameObject.activeInHierarchy && _toastText && !string.IsNullOrEmpty(_toastText.text);
                _toastPlate.enabled = on;
                if (on)
                {
                    var pr = _toastPlate.rectTransform;
                    pr.anchorMin = _toast.anchorMin; pr.anchorMax = _toast.anchorMax; pr.pivot = new Vector2(0.5f, 1f);
                    float wdt = _toastText.preferredWidth + 72f, hgt = Mathf.Max(60f, _toastText.preferredHeight + 28f);
                    pr.sizeDelta = new Vector2(Mathf.Round(wdt / 2) * 2, Mathf.Round(hgt / 2) * 2);
                    pr.anchoredPosition = _toast.anchoredPosition + new Vector2(0, 14);
                    var c = _toastText.color; _toastPlate.color = new Color(1f, 1f, 1f, c.a);
                }
            }

            string p = interactor ? interactor.CurrentPrompt : "";
            bool show = !string.IsNullOrEmpty(p) && !LootScreenUI.AnyScreenOpen;
            if (_prompt.activeSelf != show) _prompt.SetActive(show);
            if (show) SplitPrompt(p);
        }

        /// <summary>"[F] 등불의 빛을 거둔다  (불씨 +18%)    [Q] 다음 대상 (1/2)" -> 키 · 문구 · 덧붙임.</summary>
        private void SplitPrompt(string p)
        {
            string key = "F", main = p, extra = "";
            if (p.StartsWith("["))
            {
                int close = p.IndexOf(']');
                if (close > 0) { key = p.Substring(1, close - 1); main = p.Substring(close + 1).Trim(); }
            }
            else key = "";
            string cycle = "";
            int q = main.IndexOf("[Q]");
            if (q >= 0) { cycle = main.Substring(q).Trim(); main = main.Substring(0, q).Trim(); }
            int paren = main.LastIndexOf('(');
            if (paren > 0 && main.EndsWith(")")) { extra = main.Substring(paren + 1, main.Length - paren - 2); main = main.Substring(0, paren).Trim(); }
            _promptKey.text = key;
            _promptKey.transform.parent.gameObject.SetActive(key.Length > 0);
            _promptText.text = main;
            _promptExtra.text = extra;
            _promptCycle.text = cycle.Replace("[Q]", "Q");
            FitPrompt(main, extra);
        }

        /// <summary>판자를 글자 길이에 맞춰 늘린다 - 문구와 덧붙임이 서로 덮지 않게.</summary>
        private void FitPrompt(string main, string extra)
        {
            const float boxX = 70f, pad = 24f, padR = 36f, gap = 24f;      // 오른쪽은 리벳을 피해 더 띄운다
            float mw = Mathf.Ceil(_promptText.GetPreferredValues(main).x);
            float ew = extra.Length > 0 ? Mathf.Ceil(_promptExtra.GetPreferredValues(extra).x) : 0f;
            float bw = Mathf.Max(300f, pad + mw + (ew > 0f ? gap + ew : 0f) + padR);
            bw = Mathf.Ceil(bw / 4f) * 4f;                                   // 2배 도트 칸에 맞춘다
            _promptBox.sizeDelta = new Vector2(bw, _promptBox.sizeDelta.y);
            Place(_promptText.rectTransform, new Vector2(boxX + pad, 12), new Vector2(mw + 4f, 40));
            Place(_promptExtra.rectTransform, new Vector2(boxX + bw - padR - ew - 4f, 12), new Vector2(ew + 4f, 40));
            Place(_promptCycle.rectTransform, new Vector2(boxX, -34), new Vector2(bw, 30));
            _promptRoot.sizeDelta = new Vector2(boxX + bw, _promptRoot.sizeDelta.y);
        }

        // ---------- 도우미 ----------
        private GameObject Chip(string name, Sprite icon, string label, Color color, out TMP_Text text)
        {
            var c = New("Chip" + name, _chips);
            Vector2 cs = Size2(art.chip);
            bool sliced = art.chip != null && art.chip.border.sqrMagnitude > 0f;
            if (sliced) cs = new Vector2(190f, cs.y);
            c.sizeDelta = cs;
            var frame = Img("Frame", c, art.chip, Vector2.zero, cs);
            if (sliced) { frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 0.5f; }   // 테두리도 2배 도트로
            // 칩에 2배가 안 들어가면 1배로 - 어중간한 배율은 도트가 뭉개진다
            Vector2 isz = Size2(icon).y <= cs.y - 8f ? Size2(icon) : (icon ? icon.rect.size : new Vector2(24, 24));
            Img("Icon", c, icon, new Vector2(16f, (cs.y - isz.y) * 0.5f), isz);
            text = Text("Label", c, new Vector2(16f + isz.x + 8, cs.y * 0.5f - 15), new Vector2(cs.x - isz.x - 30, 30), 19, color, TextAlignmentOptions.Left);
            text.text = label;
            return c.gameObject;
        }

        private TMP_Text Key(RectTransform parent, string key, Vector2 pos, float scale)
        {
            var k = New("Key_" + key, parent);
            Place(k, pos, new Vector2(62, 60));
            k.localScale = Vector3.one * scale;
            // 밝은 돌 면은 글자를 먹는다 - 돌을 반쯤 어둡게 물들이고 상아색 글자에 검은 외곽선
            Img("Cap", k, art.keycap, Vector2.zero, Size2(art.keycap)).color = new Color(0.58f, 0.56f, 0.54f);
            var t = Text("Label", k, new Vector2(0, 12), new Vector2(62, 36), key.Length > 1 ? 19 : 26, Ivory, TextAlignmentOptions.Center, 0.32f);
            t.text = key;
            return t;
        }

        /// <summary>판자 - 모서리 리벳은 2배 도트 그대로, 가운데만 늘린다.</summary>
        private Image Panel9(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var img = Img(name, parent, art.panel, pos, size);
            if (art.panel != null && art.panel.border.sqrMagnitude > 0f) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.5f; }
            return img;
        }

        private static Vector2 Size2(Sprite s) => s ? s.rect.size * 2f : new Vector2(32, 32);

        private RectTransform Panel(string name, RectTransform parent, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var r = New(name, parent);
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = offset;
            r.sizeDelta = size;
            return r;
        }

        private static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        /// <summary>시안 좌표 (부모 왼쪽 위 기준, 아래로 +) 로 놓는다.</summary>
        private static void Place(RectTransform r, Vector2 topLeft, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            r.sizeDelta = size;
        }

        private Image Img(string name, RectTransform parent, Sprite s, Vector2 topLeft, Vector2 size)
        {
            var r = New(name, parent);
            Place(r, topLeft, size);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.raycastTarget = false;
            if (s == null) img.enabled = false;
            return img;
        }

        private Image Fill(string name, Transform parent, Vector2 topLeft, Vector2 size, Color c)
        {
            var r = New(name, parent);
            Place(r, topLeft, size);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static void SetWidth(Image img, float w)
        {
            var r = img.rectTransform;
            r.sizeDelta = new Vector2(Mathf.Round(Mathf.Max(0f, w) / 2f) * 2f, r.sizeDelta.y);   // 2px 계단 - 도트 한 칸씩 준다
        }

        private TMP_Text Text(string name, RectTransform parent, Vector2 topLeft, Vector2 size, float fontSize, Color c, TextAlignmentOptions align, float outline = 0.28f)
        {
            var r = New(name, parent);
            Place(r, topLeft, size);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.fontSize = fontSize;
            t.color = c;
            t.alignment = align;
            t.outlineWidth = outline;
            t.outlineColor = Ink;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }
    }
}
