using UnityEngine;

namespace Capstone.Enemy
{
    public enum AlertState { Idle, Suspicious, Alerted }

    /// <summary>
    /// 적 공통 AI (기획서 7.2).
    /// 탐지는 거리로만 판정하지 않는다. 플레이어가 불을 켜고 있으면 25m 밖에서도 의심하고
    /// 15m 안에서 발각되지만, 불을 끄면 탐지 거리가 크게 줄어든다.
    /// 이게 "빛 = 시야이자 노출"이라는 기획의 핵심 긴장을 만든다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class EnemyAI : MonoBehaviour
    {
        [Header("탐지 (기획서 7.2)")]
        [Tooltip("빛을 감지해 의심 상태가 되는 거리 (m)")]
        [SerializeField] protected float suspectRange = 25f;
        [Tooltip("발각 상태로 전환되는 거리 (m)")]
        [SerializeField] protected float detectRange = 15f;
        [Tooltip("플레이어가 불을 껐을 때 위 두 거리에 곱해지는 배율")]
        [SerializeField, Range(0.05f, 1f)] protected float unlitRangeMultiplier = 0.25f;
        [Tooltip("시야가 벽에 막히는지 검사할 레이어")]
        [SerializeField] protected LayerMask sightBlockMask;
        [Tooltip("발각 후 놓쳤을 때 추격을 유지하는 시간 (초)")]
        [SerializeField] protected float loseInterestTime = 4f;

        [Header("이동")]
        [SerializeField] protected float moveSpeed = 2.4f;
        [SerializeField] protected float turnSmoothing = 12f;

        [Header("참조")]
        [SerializeField] protected SpriteRenderer spriteRenderer;

        public AlertState State { get; protected set; } = AlertState.Idle;
        public float DistanceToPlayer { get; private set; } = float.MaxValue;

        protected Transform Player;
        protected Combat.PlayerStealth PlayerStealth;
        protected Player.TorchFuel PlayerTorch;
        protected Rigidbody2D Body;
        protected Vector2 LastKnownPosition;

        private float _loseInterestAt;
        private bool _countedAsDetector;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Body.gravityScale = 0f;
            Body.freezeRotation = true;
            if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        protected virtual void Start()
        {
            var pc = FindFirstObjectByType<Capstone.Player.PlayerController>();
            if (pc == null) return;
            Player = pc.transform;
            PlayerStealth = pc.GetComponent<Combat.PlayerStealth>();
            PlayerTorch = pc.GetComponent<Player.TorchFuel>();
        }

        protected virtual void Update()
        {
            if (Player == null) return;
            UpdateDetection();
            Think();
            FacePlayerIfAware();
        }

        private void UpdateDetection()
        {
            DistanceToPlayer = Vector2.Distance(transform.position, Player.position);

            bool lit = PlayerTorch != null && PlayerTorch.IsLit;
            float mult = lit ? 1f : unlitRangeMultiplier;
            float suspect = suspectRange * mult;
            float detect  = detectRange  * mult;

            bool hasLineOfSight = HasLineOfSight();

            if (hasLineOfSight && DistanceToPlayer <= detect)
            {
                SetState(AlertState.Alerted);
                LastKnownPosition = Player.position;
                _loseInterestAt = Time.time + loseInterestTime;
            }
            else if (hasLineOfSight && DistanceToPlayer <= suspect)
            {
                // 이미 추격 중이면 의심으로 내려오지 않는다
                if (State != AlertState.Alerted || Time.time >= _loseInterestAt)
                    SetState(AlertState.Suspicious);
            }
            else if (State == AlertState.Alerted && Time.time >= _loseInterestAt)
            {
                SetState(AlertState.Idle);
            }
            else if (State == AlertState.Suspicious)
            {
                SetState(AlertState.Idle);
            }
        }

        private bool HasLineOfSight()
        {
            if (sightBlockMask.value == 0) return true;
            Vector2 origin = transform.position;
            Vector2 delta = (Vector2)Player.position - origin;
            var hit = Physics2D.Raycast(origin, delta.normalized, delta.magnitude, sightBlockMask);
            return hit.collider == null;
        }

        private void SetState(AlertState next)
        {
            if (State == next) return;
            State = next;

            // 발각 중인 적이 하나라도 있으면 플레이어는 은신할 수 없다
            bool shouldCount = next == AlertState.Alerted;
            if (shouldCount != _countedAsDetector && PlayerStealth != null)
            {
                PlayerStealth.SetDetectedBy(shouldCount);
                _countedAsDetector = shouldCount;
            }

            OnStateChanged(next);
        }

        protected virtual void OnStateChanged(AlertState next) { }

        /// <summary>상태별 행동. 파생 클래스가 구현한다.</summary>
        protected abstract void Think();

        protected void MoveToward(Vector2 target, float speedMultiplier = 1f)
        {
            Vector2 dir = (target - (Vector2)transform.position).normalized;
            Body.linearVelocity = dir * moveSpeed * speedMultiplier;
        }

        protected void Stop() => Body.linearVelocity = Vector2.zero;

        private void FacePlayerIfAware()
        {
            if (!spriteRenderer || State == AlertState.Idle || Player == null) return;
            float dx = Player.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.1f) spriteRenderer.flipX = dx < 0f;
        }

        private void OnDestroy()
        {
            // 죽으면서 탐지 카운트를 반드시 되돌려야 은신이 영구히 막히지 않는다
            if (_countedAsDetector && PlayerStealth != null)
                PlayerStealth.SetDetectedBy(false);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, suspectRange);
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, detectRange);
        }
    }
}
