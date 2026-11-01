using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Capstone.FX
{
    /// <summary>
    /// 화면 효과 (2026-10-29, 게임 손맛 정리). 분위기가 무겁고 긴장된 쪽이라 짧고 무겁게, 늘 제자리로 돌아온다.
    ///   번쩍임   - 빛 플라스크 · 화염병이 시야 안에서 터질 때 (금빛 / 주황). 가장 큰 사건에만
    ///   발각     - 처음 들킨 순간 화면 가장자리가 붉게 한 번 맥동 (상태 변화)
    ///   빈사     - 체력 30% 아래면 가장자리가 심장 박동처럼 뛴다. 낮을수록 빠르게
    ///   화면 멈춤 - HitStop(초). 겹쳐도 길어지지 않고, 일시정지 · 사망으로 바뀐 시간은 건드리지 않는다
    ///   피격     - (2026-10-31 공포 쪽으로) 붉은 화면 대신 한순간 어둠이 덮쳤다 걷히고, 색이 빠졌다 돌아온다.
    ///              붉은색은 핏자국(맞은 쪽 가장자리)에만 쓴다 - 화면 전체가 붉으면 무섭지 않고 '게임'처럼 보인다
    ///   맞은 방향 - 때린 쪽 화면 가장자리에 붉은 쐐기가 잠깐 뜬다 (시야 밖에서 맞아도 어디서 왔는지 안다)
    ///   빈사 채도 - 체력이 낮을수록 세상의 색이 빠진다 (Ghost of Yōtei · 어쌔신 크리드 식). 등불 빛은 남는다
    /// 늘이기 · 찌그러뜨리기 · 화면 확대는 쓰지 않는다 - 도트 격자가 깨진다.
    /// </summary>
    public class ScreenFX : MonoBehaviour
    {
        [SerializeField] private Sprite edgeSprite;
        [Tooltip("이 체력 비율 아래면 심장 박동")]
        [SerializeField, Range(0f, 1f)] private float lowHealth = 0.3f;

        private static ScreenFX _inst;
        private Image _flash, _edge;
        private float _flashA, _flashDecay;
        private float _spotPulse;
        private bool _wasDetected;
        private Combat.Health _hp;
        private Combat.PlayerStealth _stealth;
        private static float _stopUntil;
        private bool _ordered;
        private ColorAdjustments _grade;
        private float _gradeSat0, _gradeExp0;
        private Color _gradeTint0 = Color.white;
        private float _hurt;                 // 0~1 피격 색 쏠림
        private RectTransform _dirRoot;
        private Image[] _dirs;               // 쐐기 풀
        private float[] _dirA;
        private Vector2[] _dirV;

        private void Awake()
        {
            _inst = this;
            _flash = Full("ScreenFlash", null);
            _edge = Full("ScreenEdge", edgeSprite);
            // HUD 뒤 - 번쩍여도 체력 · 시간은 읽혀야 한다
            _edge.transform.SetAsFirstSibling();
            _flash.transform.SetAsFirstSibling();
            BuildDirectional();
            var vol = FindFirstObjectByType<Volume>();
            if (vol != null && vol.sharedProfile != null && vol.sharedProfile.TryGet(out _grade))
            {
                // 굽힌 프로필 자체를 고치면 에셋이 바뀐다 - 실행 중 사본을 쓴다.
                // Instantiate 는 얕은 복사라 안의 효과(ColorAdjustments 등)는 원본을 그대로 가리킨다 -
                // 효과까지 하나씩 복제하지 않으면 빈사 · 피격 채도가 에셋에 남아 다음 판이 흑백으로 시작한다 (2026-10-31)
                var copy = Instantiate(vol.sharedProfile);
                for (int i = 0; i < copy.components.Count; i++)
                    if (copy.components[i] != null) copy.components[i] = Instantiate(copy.components[i]);
                vol.profile = copy;
                vol.profile.TryGet(out _grade);
                _gradeSat0 = _grade.saturation.value; _gradeExp0 = _grade.postExposure.value; _gradeTint0 = _grade.colorFilter.value;
                _grade.saturation.overrideState = true; _grade.colorFilter.overrideState = true; _grade.postExposure.overrideState = true;
            }
        }

        private void BuildDirectional()
        {
            _dirRoot = (RectTransform)new GameObject("HitDirection", typeof(RectTransform)).transform;
            _dirRoot.SetParent(transform, false);
            _dirRoot.anchorMin = _dirRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _dirRoot.sizeDelta = Vector2.zero;
            _dirs = new Image[4]; _dirA = new float[4]; _dirV = new Vector2[4];
            var tex = new Texture2D(24, 12, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[24 * 12];
            for (int y = 0; y < 12; y++) for (int x = 0; x < 24; x++)
            {
                // 바깥을 향한 도트 쐐기 (아래가 넓고 위로 뾰족)
                int half = (12 - y);
                bool inside = Mathf.Abs(x - 11.5f) < half;
                bool edge = inside && Mathf.Abs(x - 11.5f) >= half - 1.5f;
                if (inside) px[y * 24 + x] = edge ? new Color32(255, 90, 70, 255) : new Color32(180, 20, 18, (byte)((x + y) % 2 == 0 ? 220 : 150));
            }
            tex.SetPixels32(px); tex.Apply();
            var spr = Sprite.Create(tex, new Rect(0, 0, 24, 12), new Vector2(0.5f, 0f), 100f);
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Wedge", typeof(RectTransform));
                go.transform.SetParent(_dirRoot, false);
                var img = go.AddComponent<Image>(); img.sprite = spr; img.raycastTarget = false;
                ((RectTransform)go.transform).sizeDelta = new Vector2(96, 48);   // 4배 도트
                img.enabled = false; _dirs[i] = img;
            }
        }

        /// <summary>플레이어가 맞았다 - 색 쏠림 · 맞은 방향 표시. from = 때린 쪽에서 플레이어로 오는 방향.</summary>
        public static void PlayerHurt(float amount, Vector2 hitDirection)
        {
            if (_inst == null) return;
            _inst._hurt = Mathf.Clamp01(Mathf.Max(_inst._hurt, 0.35f + amount / 30f));
            Flash(Color.black, Mathf.Clamp(0.35f + amount / 60f, 0.35f, 0.7f), 0.18f);     // 눈앞이 한순간 캄캄해진다
            if (hitDirection.sqrMagnitude < 0.0001f) return;
            // 쐐기는 때린 쪽을 가리킨다 (맞은 방향의 반대)
            Vector2 toward = -hitDirection.normalized;
            int slot = 0; float best = 9f;
            for (int i = 0; i < 4; i++) if (_inst._dirA[i] < best) { best = _inst._dirA[i]; slot = i; }
            _inst._dirA[slot] = 1f; _inst._dirV[slot] = toward;
        }

        private Image Full(string name, Sprite s)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = s; img.raycastTarget = false; img.color = new Color(1, 1, 1, 0);
            return img;
        }

        /// <summary>화면 전체 번쩍임. 큰 사건에만 - 매번 쓰면 아무것도 크지 않다.</summary>
        public static void Flash(Color c, float alpha, float seconds)
        {
            if (_inst == null) return;
            if (alpha < _inst._flashA) return;
            _inst._flash.color = new Color(c.r, c.g, c.b, 0f);
            _inst._flashA = alpha;
            _inst._flashDecay = alpha / Mathf.Max(0.02f, seconds);
        }

        /// <summary>화면 멈춤. 겹쳐 맞아도 더 길게 멈추지 않는다.</summary>
        public static void HitStop(float seconds)
        {
            if (_inst == null || seconds <= 0f) return;
            if (Time.timeScale < 0.5f || Time.unscaledTime < _stopUntil) return;
            _inst.StartCoroutine(StopRoutine(seconds));
        }

        private static System.Collections.IEnumerator StopRoutine(float seconds)
        {
            _stopUntil = Time.unscaledTime + seconds;
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(seconds);
            if (Mathf.Approximately(Time.timeScale, 0.05f)) Time.timeScale = 1f;
        }

        private void LateUpdate()
        {
            if (!_ordered)
            {
                // 화면 전체를 덮는 것들은 전부 도트 HUD 뒤로 - 맞아도 체력 · 시간은 읽혀야 한다
                int i = 0;
                foreach (var n in new[] { "ScreenFlash", "ScreenEdge", "DamageVignette", "DarknessVignette", "HitDirection" })
                {
                    var t = transform.Find(n);
                    if (t) t.SetSiblingIndex(i++);
                }
                _ordered = true;
            }   // 도트 HUD 가 만들어진 뒤 그 뒤로
            float dt = Time.unscaledDeltaTime;
            // 번쩍임
            if (_flashA > 0f)
            {
                _flashA = Mathf.Max(0f, _flashA - _flashDecay * dt);
                var c = _flash.color; c.a = Mathf.Round(_flashA * 20f) / 20f; _flash.color = c;   // 계단으로 빠진다
            }

            if (_hp == null)
            {
                var pc = FindFirstObjectByType<Player.PlayerController>();
                if (pc) { _hp = pc.GetComponent<Combat.Health>(); _stealth = pc.GetComponent<Combat.PlayerStealth>(); }
                if (_hp == null) return;
            }

            // 처음 들킨 순간
            bool det = _stealth && _stealth.IsDetected;
            if (det && !_wasDetected) _spotPulse = 1f;
            _wasDetected = det;
            _spotPulse = Mathf.MoveTowards(_spotPulse, 0f, dt * 2.2f);

            // 빈사 - 심장 박동 (두 번 뛰고 쉰다)
            float beat = 0f;
            float hpN = _hp.IsDead ? 0f : _hp.Normalized;
            if (!_hp.IsDead && hpN < lowHealth)
            {
                float rate = Mathf.Lerp(1.6f, 0.9f, hpN / lowHealth);       // 낮을수록 빠르다
                float ph = Mathf.Repeat(Time.unscaledTime / rate, 1f);
                beat = Mathf.Max(Pulse(ph, 0f), Pulse(ph, 0.18f) * 0.7f) * Mathf.Lerp(0.55f, 0.25f, hpN / lowHealth);
            }
            float a = Mathf.Max(Mathf.Max(_spotPulse * _spotPulse * 0.45f, beat), _hurt * 0.5f);

            // 피격 색 쏠림 · 빈사 채도
            _hurt = Mathf.MoveTowards(_hurt, 0f, dt * 2.6f);
            if (_grade != null)
            {
                float low = _hp.IsDead ? 1f : Mathf.Clamp01(1f - hpN / 0.5f);          // 체력 50% 아래부터 색이 빠진다
                _grade.saturation.value = Mathf.Lerp(_gradeSat0, -90f, Mathf.Max(low * 0.75f, _hurt));   // 맞으면 색이 확 빠진다
                _grade.colorFilter.value = _gradeTint0;
                _grade.postExposure.value = _gradeExp0 - _hurt * 0.35f - low * 0.15f;
            }

            // 맞은 방향 쐐기 - 화면 가장자리 타원 위, 바깥을 향해
            if (_dirs != null)
            {
                var canvasRect = (RectTransform)transform;
                Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(70f, 60f);
                for (int i = 0; i < _dirs.Length; i++)
                {
                    _dirA[i] = Mathf.MoveTowards(_dirA[i], 0f, dt * 1.6f);
                    var img = _dirs[i];
                    img.enabled = _dirA[i] > 0.01f;
                    if (!img.enabled) continue;
                    Vector2 v = _dirV[i];
                    float k = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(v.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(v.y)));
                    var rt = img.rectTransform;
                    rt.anchoredPosition = v * k * 0.92f;
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Round((Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f) / 45f) * 45f);   // 8방향 - 도트가 비틀리지 않게
                    img.color = new Color(1f, 1f, 1f, _dirA[i] > 0.7f ? 1f : _dirA[i] / 0.7f);
                }
            }
            _edge.color = new Color(0.02f, 0.01f, 0.01f, Mathf.Min(1f, a * 1.5f));     // 가장자리는 붉지 않고 검게 조여 온다
            _edge.enabled = a > 0.01f && edgeSprite != null;
        }

        private static float Pulse(float ph, float at)
        {
            float d = ph - at;
            return d < 0f || d > 0.16f ? 0f : (d < 0.04f ? d / 0.04f : 1f - (d - 0.04f) / 0.12f);
        }
    }
}
