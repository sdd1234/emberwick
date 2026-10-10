using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 적 원화 애니메이션 (2026-10 원화 반영). 프레임이 2장 안팎이라 Animator 대신 직접 넘긴다.
    ///   대기 → 걷기 → 공격(근접은 예비 동작 · 내려치기, 원거리는 조준 · 장전) 중 지금 상태에 맞는 걸 고른다.
    /// 원화는 왼쪽을 보고 그려져 있다. 오른쪽을 볼 때 뒤집는다 (artFacesLeft).
    /// EnemyAI 의 좌우 뒤집기보다 뒤(LateUpdate)에 돌아서 이쪽이 최종 결정을 한다.
    /// </summary>
    [RequireComponent(typeof(EnemyAI))]
    public class EnemySpriteAnimator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer target;
        [Tooltip("원화가 왼쪽을 보고 있으면 체크")]
        [SerializeField] private bool artFacesLeft = true;

        [Header("프레임")]
        [SerializeField] private Sprite[] idle;
        [SerializeField] private Sprite[] walk;
        [Tooltip("근접 - [0] 예비 동작, [1] 내려치기 / 원거리 - [0] 조준(사격)")]
        [SerializeField] private Sprite[] attack;
        [Tooltip("원거리 - 쏜 뒤 잠깐 보여줄 장전 자세")]
        [SerializeField] private Sprite[] reload;

        [Header("속도")]
        [SerializeField] private float idleFps = 2f;
        [SerializeField] private float walkFps = 5f;
        [Tooltip("이 속도(m/s)보다 빠르면 걷기")]
        [SerializeField] private float walkThreshold = 0.25f;
        [Tooltip("근접 - 예비 동작 진행도가 이 값을 넘으면 내려치는 프레임")]
        [SerializeField, Range(0f, 1f)] private float strikeAt = 0.7f;
        [Tooltip("원거리 - 쏜 뒤 장전 자세를 보여주는 시간 (초)")]
        [SerializeField] private float reloadPoseSeconds = 0.9f;

        private EnemyAI _ai;
        private MeleeEnemy _melee;
        private RangedEnemy _ranged;
        private Rigidbody2D _body;
        private Combat.Health _health;
        private float _clock;
        private bool _wasAiming;
        private float _reloadUntil;
        private Vector2 _lastFace = Vector2.left;

        private void Awake()
        {
            _ai = GetComponent<EnemyAI>();
            _melee = _ai as MeleeEnemy;
            _ranged = _ai as RangedEnemy;
            _body = GetComponent<Rigidbody2D>();
            _health = GetComponent<Combat.Health>();
            if (!target) target = GetComponentInChildren<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            if (target == null) return;
            if (_health != null && _health.IsDead) return;      // 쓰러진 뒤엔 마지막 그림 그대로 (시체가 기울인다)

            _clock += Time.deltaTime;
            Vector2 v = _body ? _body.linearVelocity : Vector2.zero;

            Sprite pick = Pick(v);
            if (pick != null) target.sprite = pick;

            // 바라보는 쪽 - 움직이면 가는 쪽, 아니면 AI 가 보는 쪽
            Vector2 face = v.sqrMagnitude > walkThreshold * walkThreshold ? v : _ai.Facing;
            if (_melee != null && _melee.WindupProgress >= 0f) face = _melee.AttackFacing;
            if (_ranged != null && _ranged.AimProgress >= 0f) face = _ranged.AimTarget - (Vector2)transform.position;
            if (Mathf.Abs(face.x) > 0.05f) _lastFace = face;
            bool lookRight = _lastFace.x > 0f;
            target.flipX = artFacesLeft ? lookRight : !lookRight;
        }

        private Sprite Pick(Vector2 v)
        {
            if (_melee != null)
            {
                float w = _melee.WindupProgress;
                if (w >= 0f && Has(attack))
                    return attack[Mathf.Min(attack.Length - 1, w >= strikeAt ? 1 : 0)];
            }
            if (_ranged != null)
            {
                bool aiming = _ranged.AimProgress >= 0f;
                if (_wasAiming && !aiming) _reloadUntil = Time.time + reloadPoseSeconds;   // 방금 쐈다
                _wasAiming = aiming;
                if (aiming && Has(attack)) return attack[0];
                if (Time.time < _reloadUntil && Has(reload)) return reload[0];
            }

            bool moving = v.sqrMagnitude > walkThreshold * walkThreshold;
            if (moving && Has(walk)) return Cycle(walk, walkFps * Mathf.Clamp(v.magnitude / 2.4f, 0.6f, 1.8f));
            return Has(idle) ? Cycle(idle, idleFps) : null;
        }

        private Sprite Cycle(Sprite[] frames, float fps)
            => frames[(int)(_clock * fps) % frames.Length];

        private static bool Has(Sprite[] a) => a != null && a.Length > 0 && a[0] != null;
    }
}
