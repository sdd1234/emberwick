using UnityEngine;

namespace Capstone.Map
{
    /// <summary>
    /// 숨는 덤불 (2026-10-29).
    /// 불을 끈 채 안에 들어가면 바로 옆이 아닌 적에게는 안 보인다 (PlayerStealth.IsHidden).
    /// 대신 들어갈 때 · 안에서 움직일 때 잎이 바스락거린다 - 소리(작은 소음)와 흔들림으로 들킬 수 있다.
    /// 숨어 있는 동안 주인공은 반투명해져 본인이 숨은 걸 안다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class HideBush : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private Sprite rustleSprite;
        [Tooltip("같은 덤불을 기울인 그림 - 왼쪽 2단 · 왼쪽 1단 · 오른쪽 1단 · 오른쪽 2단 (Tools/art/bush_lean.py)")]
        [SerializeField] private Sprite[] leanFrames;
        [Tooltip("바스락 소리가 퍼지는 반경 (m) - 걷기 발소리(3m)보다 작다")]
        [SerializeField] private float rustleNoise = 2.2f;

        private Combat.PlayerStealth _inside;
        private float _rustleStart = -9f, _rustleDir = 1f, _rustleStrength;
        private SpriteRenderer _playerSprite;
        private float _nextRustle;
        private Vector3 _basePos;

        private void Awake()
        {
            if (!body) body = GetComponent<SpriteRenderer>();
            _basePos = body ? body.transform.localPosition : Vector3.zero;
            GetComponent<Collider2D>().isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var st = other.GetComponentInParent<Combat.PlayerStealth>();
            if (st == null || st == _inside) return;
            _inside = st;
            st.EnterBush();
            var pc = st.GetComponent<Player.PlayerController>();
            _playerSprite = pc ? pc.GetComponentInChildren<SpriteRenderer>() : null;
            Rustle(1f);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var st = other.GetComponentInParent<Combat.PlayerStealth>();
            if (st == null || st != _inside) return;
            st.ExitBush();
            if (_playerSprite) { var c = _playerSprite.color; c.a = 1f; _playerSprite.color = c; }
            _inside = null;
            Rustle(0.7f);
        }

        private void Update()
        {
            if (_inside != null)
            {
                var pc = _inside.GetComponent<Player.PlayerController>();
                if (pc && pc.IsMoving && Time.time >= _nextRustle) Rustle(pc.IsSprinting ? 1f : 0.5f);
                if (_playerSprite) { var c = _playerSprite.color; c.a = _inside.IsHidden ? 0.55f : 1f; _playerSprite.color = c; }
            }
            if (body == null) return;
            body.sortingOrder = _inside != null ? 3 : 0;      // 안에 있으면 덤불이 몸을 덮는다 (Y 정렬보다 앞선다)

            // 쓸림 - 밑동은 박힌 채 위만 밀린 쪽으로 휘었다가, 반대로 살짝 넘어가고 선다
            float t = Time.time - _rustleStart;
            if (leanFrames != null && leanFrames.Length == 4)
            {
                body.transform.localPosition = _basePos;
                body.sprite = LeanAt(t);
                return;
            }
            const float dur = 0.55f;
            if (t < dur)
            {
                float k = t / dur;
                float sway = Mathf.Sin(k * Mathf.PI * 2.2f) * (1f - k) * 2f * _rustleStrength;   // 2도트 → 0
                body.transform.localPosition = _basePos + Vector3.right * Mathf.Round(sway * _rustleDir) / 38f;
                if (rustleSprite) body.sprite = t < 0.2f ? rustleSprite : idleSprite;
            }
            else
            {
                body.transform.localPosition = _basePos;
                if (rustleSprite) body.sprite = idleSprite;
            }
        }

        // 기울기 순서 (초, 단 - 양수 = 밀린 쪽). 세게 쓸리면 2단까지 휜다
        private static readonly float[] LeanTimes = { 0.05f, 0.15f, 0.23f, 0.29f, 0.41f, 0.47f };
        private static readonly int[] LeanStrong = { 1, 2, 1, 0, -1, 0 };
        private static readonly int[] LeanSoft   = { 1, 1, 1, 0, -1, 0 };

        private Sprite LeanAt(float t)
        {
            int step = 0;
            var seq = _rustleStrength > 0.75f ? LeanStrong : LeanSoft;
            for (int i = 0; i < LeanTimes.Length; i++) if (t < LeanTimes[i]) { step = seq[i]; break; }
            if (step == 0) return idleSprite;
            float dir = _rustleDir * (body.flipX ? -1f : 1f);           // 뒤집힌 덤불은 그림 속 방향이 반대
            int lean = step * (dir >= 0f ? 1 : -1);                      // -2 -1 1 2
            return leanFrames[lean < 0 ? lean + 2 : lean + 1];
        }

        private void Rustle(float strength)
        {
            // 이미 쓸리는 중이면 다시 시작하지 않는다 - 걸을 때마다 처음부터 튀면 경련처럼 보인다
            if (Time.time - _rustleStart < 0.4f) return;
            _rustleStart = Time.time;
            _rustleStrength = Mathf.Clamp01(strength);
            if (_inside != null) _rustleDir = Mathf.Sign(transform.position.x - _inside.transform.position.x + 0.001f);   // 몸에서 먼 쪽으로 밀린다
            _nextRustle = Time.time + 0.9f;
            FX.SfxBank.Play("bush", transform.position, 0.5f * strength + 0.2f, 0.12f);
            if (_inside != null) Core.NoiseSystem.Emit(transform.position, rustleNoise * strength, _inside.gameObject);
            FX.PixelFX.Burst((Vector2)transform.position + Vector2.up * 0.8f, Vector2.up, new Color(0.18f, 0.32f, 0.16f), new Color(0.3f, 0.45f, 0.22f), Mathf.RoundToInt(4 * strength) + 1, 2.2f, 70f, true, 0.5f);
        }
    }
}
