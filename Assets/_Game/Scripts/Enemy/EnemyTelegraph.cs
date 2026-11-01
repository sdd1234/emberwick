using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 공격 예고 (2026-10-29 - 도트로 다시 그림).
    /// 근접(감염자 · 도적)은 예비 동작 동안 앞쪽 부채꼴이 도트 계단으로 뻗어 나오고, 다 뻗으면 휘두른다.
    /// 원거리(이교도)는 몸 둘레 고리가 조여들다가 다 조이면 쏜다.
    /// 부채꼴이라 옆이나 뒤로 돌아 들어가면 피할 수 있다 - 원형이면 피할 방법이 없다.
    ///
    /// 매끈한 UI 원 대신 월드 도트(38px/m)로 그린다: 안쪽은 두 칸 체크 디더, 가장자리는 밝은 1px 테두리.
    /// 조명을 안 받는 재질이라 어둠 속에서도 위험이 보인다. 그림은 (반경 · 방향 · 각도)별로 한 번만 만든다.
    /// </summary>
    public class EnemyTelegraph : MonoBehaviour
    {
        [Header("근접 - 앞쪽 공격 범위")]
        [SerializeField] private Color meleeZoneColor = new(0.85f, 0.22f, 0.20f, 0.35f);
        [SerializeField] private Color meleeStrikeColor = new(1f, 0.36f, 0.24f, 0.95f);
        [Tooltip("예비 동작 시작 시 부채꼴이 뻗어나온 비율")]
        [SerializeField, Range(0f, 1f)] private float strikeStartScale = 0.25f;
        [Tooltip("뻗어 나오는 계단 수 - 도트답게 끊어서")]
        [SerializeField] private int reachSteps = 6;

        [Header("AI 도트 그림 (2026-10-29)")]
        [Tooltip("위험 지대 무늬 (이음매 없는 타일, 읽기 가능). 부채꼴 모양으로 오려 쓴다 - 그림은 돌리지 않는다")]
        [SerializeField] private Texture2D pattern;
        [Tooltip("원거리 - 노리는 자리에 조여드는 조준점 (넓음 → 좁음)")]
        [SerializeField] private Sprite[] reticle;

        [Header("원거리 - 조여드는 고리")]
        [SerializeField] private Color rangedRingColor = new(1f, 0.32f, 0.26f, 0.95f);
        [SerializeField] private float ringStartScale = 3.4f;
        [SerializeField] private float ringEndRadius = 0.75f;

        private const float PPU = 38f;
        private const int Dirs = 16;

        private MeleeEnemy _melee;
        private RangedEnemy _ranged;
        private RogueEnemy _rogue;
        private SpriteRenderer _zone, _strike, _ring, _aim;

        private static readonly Dictionary<long, Sprite> Cache = new();

        private void Awake()
        {
            _melee = GetComponentInParent<MeleeEnemy>();
            _ranged = GetComponentInParent<RangedEnemy>();
            _rogue = GetComponentInParent<RogueEnemy>();
            // 예전 UI 캔버스 예고는 끈다
            var old = transform.Find("TelegraphCanvas");
            if (old) old.gameObject.SetActive(false);
            _zone = Layer("Zone", -20);
            _strike = Layer("Strike", -19);
            _ring = Layer("Ring", -19);
            _aim = Layer("Aim", 52);
        }

        private SpriteRenderer Layer(string name, int order)
        {
            var go = new GameObject("Pixel" + name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = FX.PixelFX.UnlitMaterial;
            sr.sortingOrder = order;
            sr.enabled = false;
            return sr;
        }

        private void LateUpdate()
        {
            // 부모가 2배여도 월드 도트 크기 그대로 - 발밑에 깐다
            float ps = transform.parent ? Mathf.Abs(transform.parent.lossyScale.x) : 1f;
            transform.localScale = Vector3.one / Mathf.Max(0.0001f, ps);
            transform.rotation = Quaternion.identity;
            Vector3 feet = transform.parent ? transform.parent.position : transform.position;
            transform.position = new Vector3(Mathf.Round(feet.x * PPU) / PPU, Mathf.Round((feet.y + 0.15f) * PPU) / PPU, 0f);

            float wind = -1f, range = 1f, arc = 100f; Vector2 facing = Vector2.right;
            if (_melee != null) { wind = _melee.WindupProgress; range = _melee.AttackRange; arc = _melee.AttackArcDegrees; facing = _melee.AttackFacing; }
            else if (_rogue != null) { wind = _rogue.WindupProgress; range = _rogue.AttackRange; arc = _rogue.AttackArcDegrees; facing = _rogue.AttackFacing; }
            Melee(wind, range, arc, facing);

            Ranged(_ranged != null ? _ranged.AimProgress : -1f);
        }

        private void Melee(float p, float range, float arc, Vector2 facing)
        {
            bool show = p >= 0f;
            _zone.enabled = _strike.enabled = show;
            if (!show) return;
            int dir = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg, 360f) / (360f / Dirs)) % Dirs;
            int full = Mathf.RoundToInt(range * PPU);
            _zone.sprite = Sector(full, dir, Mathf.RoundToInt(arc), false, pattern);
            _zone.color = pattern != null ? new Color(1f, 1f, 1f, 0.45f) : meleeZoneColor;
            float reach = Mathf.Lerp(strikeStartScale, 1f, Mathf.Floor(p * reachSteps) / reachSteps);
            _strike.sprite = Sector(Mathf.Max(4, Mathf.RoundToInt(full * reach)), dir, Mathf.RoundToInt(arc), true, pattern);
            var c = pattern != null ? Color.white : meleeStrikeColor;
            c.a = p > 0.85f ? (Mathf.Repeat(Time.time * 16f, 1f) > 0.5f ? 1f : 0.55f) : Mathf.Lerp(0.5f, 0.9f, p);   // 휘두르기 직전엔 깜빡인다
            _strike.color = c;
        }

        private void Ranged(float p)
        {
            bool show = p >= 0f;
            _ring.enabled = show && (reticle == null || reticle.Length == 0);
            _aim.enabled = show && reticle != null && reticle.Length > 0;
            if (!show) return;
            if (_aim.enabled)
            {
                // 노리는 자리 (조준을 시작한 순간 기억한 곳) - 거기서 비키면 피한다
                int f = Mathf.Clamp((int)(p * reticle.Length), 0, reticle.Length - 1);
                _aim.sprite = reticle[f];
                Vector2 at = _ranged.AimTarget;
                _aim.transform.position = new Vector3(Mathf.Round(at.x * PPU) / PPU, Mathf.Round(at.y * PPU) / PPU, 0f);
                _aim.color = new Color(1f, 1f, 1f, p > 0.85f && Mathf.Repeat(Time.time * 16f, 1f) > 0.5f ? 0.6f : 1f);
                return;
            }
            float radius = Mathf.Lerp(ringEndRadius * ringStartScale, ringEndRadius, p);
            int r = Mathf.RoundToInt(radius * PPU / 2f) * 2;
            _ring.sprite = Ring(Mathf.Max(6, r));
            var c = rangedRingColor;
            c.a = Mathf.Lerp(0.45f, 1f, p);
            _ring.color = c;
        }

        // ---------- 도트 그림 ----------
        /// <summary>부채꼴. 안쪽은 체크 디더, 가장자리 1px 는 꽉 채운다. strong 이면 안쪽을 더 진하게.</summary>
        private static Sprite Sector(int r, int dir, int arc, bool strong, Texture2D pat = null)
        {
            long key = ((long)r << 32) | ((long)dir << 20) | ((long)arc << 2) | (strong ? 1L : 0L) | (pat != null ? 1L << 62 : 0L);
            if (Cache.TryGetValue(key, out var s)) return s;
            int size = r * 2 + 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float center = Mathf.Repeat(dir * (360f / Dirs), 360f), half = arc * 0.5f;
            bool Inside(int x, int y)
            {
                float dx = x - (r + 1), dy = y - (r + 1);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r + 0.3f) return false;
                if (d < 1.5f) return true;
                float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                return Mathf.Abs(Mathf.DeltaAngle(a, center)) <= half;
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x + 1, y) || !Inside(x - 1, y) || !Inside(x, y + 1) || !Inside(x, y - 1);
                if (pat != null)
                {
                    // AI 무늬를 그대로 찍는다 - 가장자리만 밝은 1px 테두리
                    if (edge) { px[y * size + x] = new Color32(255, 110, 80, 255); continue; }
                    Color32 c = PatternPixel(pat, x, y);
                    c.a = strong ? (byte)230 : (byte)170;
                    px[y * size + x] = c;
                    continue;
                }
                byte a = edge ? (byte)255 : ((x + y) % 2 == 0 ? (strong ? (byte)170 : (byte)110) : (strong ? (byte)90 : (byte)40));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PPU);
            Cache[key] = s;
            return s;
        }

        private static Color32[] _patPx; private static int _patW, _patH; private static Texture2D _patSrc;
        private static Color32 PatternPixel(Texture2D pat, int x, int y)
        {
            if (_patSrc != pat) { _patSrc = pat; _patPx = pat.GetPixels32(); _patW = pat.width; _patH = pat.height; }
            return _patPx[(y % _patH) * _patW + (x % _patW)];
        }

        /// <summary>고리. 두께 2px, 안쪽으로 한 칸 건너 디더 한 줄.</summary>
        private static Sprite Ring(int r)
        {
            long key = -1 - r;
            if (Cache.TryGetValue(key, out var s)) return s;
            int size = r * 2 + 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - (r + 1), dy = y - (r + 1);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = 0;
                if (d <= r + 0.3f && d > r - 1.7f) a = 255;
                else if (d <= r - 2.7f && d > r - 3.7f && (x + y) % 2 == 0) a = 120;
                if (a > 0) px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PPU);
            Cache[key] = s;
            return s;
        }
    }
}
