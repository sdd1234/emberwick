using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Capstone.FX
{
    /// <summary>
    /// 도트 파편 · 떠오르는 글자 (FEEL 에셋이 들어오기 전까지의 자체 타격감).
    /// 파편은 2x2 도트 사각형이라 원화의 도트 크기와 맞는다. 회전하지 않는다 - 도트가 비틀리면 격자가 깨진다.
    /// 수가 적어서(한 번에 스무 개 안팎) 파티클 시스템 대신 직접 움직인다.
    /// </summary>
    public class PixelFX : MonoBehaviour
    {
        private struct Bit { public Transform t; public SpriteRenderer sr; public Vector2 v; public float life, max; public float drag; public bool gravity; public float floorY; }

        private static PixelFX _inst;
        private static Sprite _dot;
        private static TMP_FontAsset _font;
        private readonly List<Bit> _bits = new();
        private readonly Stack<GameObject> _pool = new();

        private const float PPU = 38f;

        private static PixelFX Inst
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("PixelFX");
                    _inst = go.AddComponent<PixelFX>();
                }
                return _inst;
            }
        }

        public static Sprite Dot
        {
            get
            {
                if (_dot == null)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                    tex.Apply();
                    _dot = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), PPU);
                }
                return _dot;
            }
        }

        /// <summary>파편을 흩뿌린다. 피 · 쇳조각 · 빛가루.</summary>
        public static void Burst(Vector2 at, Vector2 dir, Color a, Color b, int count, float speed, float spread = 70f, bool gravity = true, float life = 0.45f, bool unlit = true)
        {
            var fx = Inst;
            float baseAng = dir.sqrMagnitude > 0.001f ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg : 90f;
            for (int i = 0; i < count; i++)
            {
                float ang = (baseAng + Random.Range(-spread, spread)) * Mathf.Deg2Rad;
                float sp = speed * Random.Range(0.45f, 1.1f);
                var go = fx.Get();
                go.transform.position = at + Random.insideUnitCircle * 0.1f;
                var sr = go.GetComponent<SpriteRenderer>();
                sr.color = Color.Lerp(a, b, Random.value);
                sr.sortingOrder = 40;
                int px = Random.value < 0.3f ? 2 : 1;
                go.transform.localScale = Vector3.one * px;
                float l = life * Random.Range(0.7f, 1.2f);
                fx._bits.Add(new Bit
                {
                    t = go.transform, sr = sr, v = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * sp,
                    life = l, max = l, drag = 3.5f, gravity = gravity, floorY = at.y - Random.Range(0.3f, 0.9f)
                });
            }
        }

        /// <summary>떠오르는 짧은 글자 ("저항", "약점!"). TextMeshPro 는 조명을 안 받아 어둠 속에서도 읽힌다.</summary>
        public static void Text(Vector2 at, string text, Color color, float size = 3.2f)
        {
            if (_font == null) _font = UI.PixelHUD.MainFont;
            if (_font == null)
            {
                foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.font != null) { _font = t.font; break; }
            }
            var go = new GameObject("FloatText");
            go.transform.position = at;
            var tmp = go.AddComponent<TextMeshPro>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color32(10, 8, 12, 255);
            tmp.sortingOrder = 60;
            tmp.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
            go.AddComponent<FloatUp>();
        }

        private GameObject Get()
        {
            GameObject go;
            if (_pool.Count > 0) { go = _pool.Pop(); go.SetActive(true); }
            else
            {
                go = new GameObject("Bit");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Dot;
                // 조명을 안 받는 기본 스프라이트 재질 - 어둠 속 피격도 보여야 한다
                sr.sharedMaterial = UnlitMaterial;
            }
            return go;
        }

        private static Material _unlit;
        public static Material UnlitMaterial
        {
            get
            {
                if (_unlit == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (sh == null) sh = Shader.Find("Sprites/Default");
                    _unlit = new Material(sh);
                }
                return _unlit;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _bits.Count - 1; i >= 0; i--)
            {
                var b = _bits[i];
                b.life -= dt;
                if (b.life <= 0f || b.t == null)
                {
                    if (b.t != null) { b.t.gameObject.SetActive(false); _pool.Push(b.t.gameObject); }
                    _bits.RemoveAt(i);
                    continue;
                }
                if (b.gravity) b.v.y -= 14f * dt;
                b.v *= Mathf.Exp(-b.drag * dt);
                Vector3 p = b.t.position + (Vector3)(b.v * dt);
                if (b.gravity && p.y < b.floorY) { p.y = b.floorY; b.v = new Vector2(b.v.x * 0.4f, 0f); }
                // 도트 격자에 맞춰 찍는다
                p.x = Mathf.Round(p.x * PPU) / PPU; p.y = Mathf.Round(p.y * PPU) / PPU;
                b.t.position = p;
                var c = b.sr.color; c.a = Mathf.Clamp01(b.life / (b.max * 0.5f)); b.sr.color = c;
                _bits[i] = b;
            }
        }
    }

    /// <summary>떠올랐다가 사라지는 글자.</summary>
    public class FloatUp : MonoBehaviour
    {
        private TextMeshPro _t;
        private float _age;
        private Vector3 _from;
        private void Start() { _t = GetComponent<TextMeshPro>(); _from = transform.position; }
        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float k = _age / 0.9f;
            transform.position = _from + Vector3.up * (0.9f * (1f - (1f - k) * (1f - k)));
            float pop = k < 0.12f ? Mathf.Lerp(1.5f, 1f, k / 0.12f) : 1f;
            transform.localScale = Vector3.one * pop;
            if (_t) { var c = _t.color; c.a = k < 0.6f ? 1f : Mathf.Clamp01(1f - (k - 0.6f) / 0.4f); _t.color = c; }
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
