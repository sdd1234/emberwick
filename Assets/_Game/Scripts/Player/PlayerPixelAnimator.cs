using UnityEngine;

namespace Capstone.Player
{
    /// <summary>
    /// 도트 주인공 애니메이션 (2026-10-29 - 도트 통일).
    /// 프레임이 몇 장 안 돼서 Animator 대신 직접 넘긴다 - EnemySpriteAnimator 와 같은 방식.
    ///   대기 / 걷기(4) / 달리기(같은 4장을 빠르게) / 조준(석궁을 당기거나 우클릭)
    /// 좌우는 PlayerController.FaceAim 이 뒤집는다. 원화는 오른쪽을 본다.
    /// 대기 중엔 1도트씩 숨을 쉰다 - 정지 화면처럼 굳어 보이지 않게.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerPixelAnimator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer target;
        [SerializeField] private Sprite[] idle;
        [SerializeField] private Sprite[] walk;
        [SerializeField] private Sprite aim;
        [Tooltip("구르기 - 웅크림 · 말림 · 뒤집힘 · 일어남")]
        [SerializeField] private Sprite[] roll;
        [SerializeField] private float rollSeconds = 0.35f;
        [Tooltip("맞았을 때 - 움찔 · 비틀")]
        [SerializeField] private Sprite[] hurt;
        [SerializeField] private float hurtSeconds = 0.22f;
        [SerializeField] private float walkFps = 7f;
        [SerializeField] private float runFps = 11f;
        [Tooltip("한 걸음 사이클(두 발)에 나아가는 거리 (m). 프레임 속도를 실제 속도에 맞춰 발이 미끄러지지 않게 한다. 0 이면 고정 fps")]
        [SerializeField] private float cycleMeters = 2.2f;
        [Tooltip("프레임마다 몸이 위아래로 움직이는 도트 수 (디딤 = -1, 지나감 = +1). 걷기 6장 그림에 이미 오르내림이 그려져 있어 기본은 0")]
        [SerializeField] private int[] walkBob = { 0, 0, 0, 0, 0, 0 };
        [SerializeField] private float breathPeriod = 1.4f;

        private PlayerController _pc;
        private Combat.Crossbow _bow;
        private Combat.Health _hp;
        private float _clock;
        private Vector3 _base;
        private float _rollStart = -1f;
        private float _walkPhase;
        private FX.HitFeel _feel;
        private Rigidbody2D _rb;

        private void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _bow = GetComponent<Combat.Crossbow>();
            _hp = GetComponent<Combat.Health>();
            _rb = GetComponent<Rigidbody2D>();
            if (!target) target = GetComponentInChildren<SpriteRenderer>();
            if (target) _base = target.transform.localPosition;
            var anim = GetComponentInChildren<Animator>();
            if (anim) anim.enabled = false;                  // 예전 원화 클립이 그림을 덮어쓰지 않게
        }

        private void LateUpdate()
        {
            if (!target || (_hp != null && _hp.IsDead)) return;
            _clock += Time.deltaTime;
            bool aiming = (_bow != null && _bow.IsCharging) || _pc.IsAiming;
            Vector3 offset = Vector3.zero;

            if (_pc.IsRolling && _rollStart < 0f) _rollStart = Time.time;
            if (!_pc.IsRolling) _rollStart = -1f;

            if (_feel == null) _feel = GetComponent<FX.HitFeel>();
            float since = _feel ? Time.time - _feel.LastHitAt : 9f;
            if (!_pc.IsRolling && hurt != null && hurt.Length > 0 && since < hurtSeconds)
            {
                target.sprite = hurt[Mathf.Min(hurt.Length - 1, (int)(since / hurtSeconds * hurt.Length))];
                offset.x = since < 0.06f ? Mathf.Sign(_feel.LastHitDir.x) * (target.flipX ? -1f : 1f) / 38f : 0f;   // 1도트 밀림
            }
            else if (_pc.IsRolling && roll != null && roll.Length > 0)
            {
                int f = Mathf.Clamp((int)((Time.time - _rollStart) / rollSeconds * roll.Length), 0, roll.Length - 1);
                target.sprite = roll[f];
            }
            else if (aiming && aim != null) target.sprite = aim;
            else if (_pc.IsMoving && walk != null && walk.Length > 0)
            {
                // 이동한 거리로 프레임을 넘긴다 - 빨리 걸으면 빨리, 감염으로 느려지면 느리게
                float speed = _rb ? _rb.linearVelocity.magnitude : 3f;
                if (cycleMeters > 0f) _walkPhase += speed / cycleMeters * Time.deltaTime;
                else _walkPhase += (_pc.IsSprinting ? runFps : walkFps) / walk.Length * Time.deltaTime;
                int f = (int)(Mathf.Repeat(_walkPhase, 1f) * walk.Length) % walk.Length;
                target.sprite = walk[f];
                if (walkBob != null && walkBob.Length == walk.Length) offset.y = walkBob[f] / 38f;
            }
            else if (idle != null && idle.Length > 0)
            {
                target.sprite = idle[(int)(_clock / breathPeriod * idle.Length) % idle.Length];
                if (idle.Length == 1 && (_clock % breathPeriod) > breathPeriod * 0.5f) offset.y = -1f / 38f;
            }
            target.transform.localPosition = _base + offset;
        }
    }
}
