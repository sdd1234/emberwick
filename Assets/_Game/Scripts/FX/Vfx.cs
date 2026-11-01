using System.Collections.Generic;
using UnityEngine;

namespace Capstone.FX
{
    /// <summary>
    /// 도트 이펙트 (2026-10-29 - GPT 도트로 뽑은 프레임 시트).
    /// Vfx.Play("vfx_impact", 위치, 왼쪽을 보나) 로 부른다. 회전하지 않는다 - 도트가 비틀리면 격자가 깨진다.
    /// 왼쪽으로 가는 공격은 좌우만 뒤집는다. 조명을 안 받는 재질이라 어둠 속에서도 보인다.
    /// 위치는 도트 격자(1/38m)에 맞춰 찍는다.
    /// </summary>
    public class VfxBank : MonoBehaviour
    {
        [System.Serializable]
        public class Clip
        {
            public string name;
            public Sprite[] frames;
            public float fps = 18f;
            [Tooltip("살짝 위로 떠오르는 속도 (m/s) - 연기 · 반짝임")]
            public float rise;
        }

        [SerializeField] private List<Clip> clips = new();
        public void SetClips(List<Clip> c) => clips = c;

        private static VfxBank _inst;
        private readonly Dictionary<string, Clip> _map = new();
        private readonly Stack<SpriteRenderer> _pool = new();

        private struct Live { public SpriteRenderer sr; public Clip clip; public float t; public Vector2 pos; public float speed; }
        private readonly List<Live> _live = new();

        private void Awake()
        {
            _inst = this;
            foreach (var c in clips) if (c != null && c.frames != null && c.frames.Length > 0) _map[c.name] = c;
        }

        public static void Play(string name, Vector2 at, bool faceLeft = false, int sortingOrder = 45, float speed = 1f)
        {
            if (_inst == null || !_inst._map.TryGetValue(name, out var clip)) return;
            var sr = _inst._pool.Count > 0 ? _inst._pool.Pop() : _inst.NewRenderer();
            sr.gameObject.SetActive(true);
            sr.sprite = clip.frames[0];
            sr.flipX = faceLeft;
            sr.sortingOrder = sortingOrder;
            sr.color = Color.white;
            _inst._live.Add(new Live { sr = sr, clip = clip, t = 0f, pos = at, speed = speed });
            Snap(sr.transform, at);
        }

        private SpriteRenderer NewRenderer()
        {
            var go = new GameObject("Vfx");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = PixelFX.UnlitMaterial;
            return sr;
        }

        private static void Snap(Transform t, Vector2 p) => t.position = new Vector3(Mathf.Round(p.x * 38f) / 38f, Mathf.Round(p.y * 38f) / 38f, 0f);

        private void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var l = _live[i];
                l.t += Time.deltaTime * l.speed;
                int f = (int)(l.t * l.clip.fps);
                if (f >= l.clip.frames.Length || l.sr == null)
                {
                    if (l.sr != null) { l.sr.gameObject.SetActive(false); _pool.Push(l.sr); }
                    _live.RemoveAt(i);
                    continue;
                }
                l.sr.sprite = l.clip.frames[f];
                if (l.clip.rise != 0f) { l.pos.y += l.clip.rise * Time.deltaTime; Snap(l.sr.transform, l.pos); }
                _live[i] = l;
            }
        }
    }
}

namespace Capstone.FX
{
    /// <summary>짧게 부르는 이름 - Vfx.Play(...).</summary>
    public static class Vfx
    {
        public static void Play(string name, UnityEngine.Vector2 at, bool faceLeft = false, int sortingOrder = 45, float speed = 1f)
            => VfxBank.Play(name, at, faceLeft, sortingOrder, speed);
    }
}
