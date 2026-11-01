using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 감염체 계열 - 붙어서 때린다 (기획서 7.1).
    /// 피드백 이후로는 빛이 아니라 소리에 반응한다 (senseMode = Hearing). 3m 안이면 알아차리고 그 너머는 못 본다.
    /// </summary>
    public class MeleeEnemy : EnemyAI
    {
        [Header("근접 공격")]
        [SerializeField] private float attackRange = 1.2f;
        [Tooltip("앞쪽 부채꼴 각도. 이 범위 밖에 있으면 맞지 않는다")]
        [SerializeField] private float attackArcDegrees = 110f;
        [SerializeField] private float attackDamage = 12f;
        [SerializeField] private float attackCooldown = 1.1f;
        [Tooltip("내려치기 전 예비 동작 시간. 플레이어가 구르기로 피할 여지를 준다")]
        [SerializeField] private float windUp = 0.35f;

        [Header("덮치기 (2026-10-29 - 옆으로 돌기만 하면 다 피해지던 문제)")]
        [Tooltip("예비 동작 중 이 비율까지는 대상 쪽으로 방향을 계속 튼다. 이후엔 고정 - 끝까지 보고 피해야 한다")]
        [SerializeField, Range(0f, 1f)] private float trackUntil = 0.5f;
        [Tooltip("내려칠 때 앞으로 덮치는 최대 거리 (m). 벽 앞에서 멈춘다")]
        [SerializeField] private float lungeDistance = 0f;

        [Header("상처 (피드백 - 감염체에게 맞으면 감염 · 출혈)")]
        [Tooltip("한 대 맞힐 때 쌓이는 감염 (0~100). 감염체만 준다")]
        [SerializeField] private float infectionPerHit = 0f;
        [Tooltip("맞혔을 때 피가 나기 시작할 확률")]
        [SerializeField, Range(0f, 1f)] private float bleedChance = 0f;

        [Header("배회")]
        [SerializeField] private float wanderRadius = 3f;
        [SerializeField] private float wanderInterval = 2.5f;

        /// <summary>예비 동작 진행도 0~1. 공격 예고 표시에 쓴다. 휘두르지 않는 동안엔 -1.</summary>
        public float WindupProgress
            => _swingAt < 0f ? -1f : Mathf.Clamp01(1f - (_swingAt - Time.time) / Mathf.Max(0.01f, windUp));
        public float AttackRange => attackRange;
        public float AttackArcDegrees => attackArcDegrees;
        /// <summary>공격이 향하는 방향. 예비 동작에 들어간 순간의 방향으로 고정된다.</summary>
        public Vector2 AttackFacing => _attackFacing;

        private float _nextAttackAt;
        private float _swingAt = -1f;
        private Vector2 _attackFacing = Vector2.right;
        private Vector2 _wanderTarget;
        private float _nextWanderAt;
        private Vector2 _home;
        private Transform _swingTarget;

        protected override void Start()
        {
            base.Start();
            _home = transform.position;
            _wanderTarget = _home;
        }

        protected override void Think()
        {
            // 예비 동작 중이면 멈춰서 휘두를 때를 기다린다
            if (_swingAt > 0f)
            {
                Stop();
                // 앞 절반은 대상을 따라 돈다
                if (_swingTarget != null && WindupProgress < trackUntil)
                {
                    Vector2 to = (Vector2)_swingTarget.position - Position;
                    if (to.sqrMagnitude > 0.0001f) _attackFacing = Vector2.Lerp(_attackFacing, to.normalized, 10f * Time.deltaTime).normalized;
                }
                if (Time.time >= _swingAt) Swing();
                return;
            }

            switch (State)
            {
                case AlertState.Alerted:
                    // 붙어 있어도 벽 너머면 때릴 수 없다. 돌아 들어가야 한다
                    // 덮칠 수 있는 거리면 거기서부터 휘두르기 시작한다 - 사거리 바로 밖을 도는 것만으로는 못 피한다
                    if (DistanceToTarget <= attackRange + lungeDistance * 0.85f && CanSeeTarget) { Stop(); TryStartAttack(); }
                    else MoveAlongPathTo(ChaseTarget);
                    break;

                case AlertState.Suspicious:
                    MoveAlongPathTo(ChaseTarget, 0.5f);      // 확인하러 천천히 접근
                    break;

                default:
                    Wander();
                    break;
            }
        }

        private void TryStartAttack()
        {
            if (Time.time < _nextAttackAt) return;
            if (!CanSeeTarget) return;                       // 벽을 사이에 두고 휘두르지 않는다

            // 휘두를 방향을 예비 동작 시작 시점에 고정한다.
            // 이래야 플레이어가 옆으로 돌아 들어가 피할 수 있다.
            _swingTarget = Target;
            Vector2 toPlayer = TargetPosition - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.0001f) _attackFacing = toPlayer.normalized;

            _swingAt = Time.time + windUp;
            _nextAttackAt = Time.time + attackCooldown + windUp;
        }

        private void Swing()
        {
            _swingAt = -1f;
            var victim = _swingTarget;
            _swingTarget = null;
            if (victim == null) return;
            EmitCombatNoise();                               // 내려치는 소리 - 감염자가 듣는다
            FX.SfxBank.Play("slam", Position, 1f, 0.1f);
            Vector2 hitAt = Position + _attackFacing * attackRange * 0.9f;
            FX.Vfx.Play("vfx_dust", hitAt + Vector2.up * 0.2f, _attackFacing.x < 0f, 20);
            FX.Vfx.Play("vfx_slash", hitAt + Vector2.up * 0.8f, _attackFacing.x < 0f, 45, 0.7f);

            // 덮치기 - 대상 쪽(고정된 방향)으로 몸을 던진다. 벽이 있으면 그 앞까지
            if (lungeDistance > 0f)
            {
                float want = Mathf.Clamp(Vector2.Distance(Position, victim.position) - attackRange * 0.6f, 0f, lungeDistance);
                if (want > 0.05f)
                {
                    var hit = Physics2D.CircleCast(Position, agentRadius, _attackFacing, want, obstacleMask.value != 0 ? obstacleMask : sightBlockMask);
                    float go = hit.collider != null ? Mathf.Max(0f, hit.distance - 0.05f) : want;
                    Body.position += _attackFacing * go;
                    transform.position = Body.position;
                }
            }

            Vector2 toPlayer = (Vector2)victim.position - (Vector2)transform.position;

            // 예비 동작 사이에 벗어났으면 빗나간다
            if (toPlayer.magnitude > attackRange * 1.25f) return;

            // 휘두르는 사이에 벽 뒤로 숨었으면 벽을 때린 것이다
            if (!HasLineOfSightTo(victim.position)) return;

            // 앞쪽 부채꼴 밖이면 빗나간다. 옆이나 뒤로 돌아 들어가면 피할 수 있다.
            if (Vector2.Angle(_attackFacing, toPlayer) > attackArcDegrees * 0.5f) return;

            var health = victim.GetComponent<Combat.Health>();
            if (health == null || health.IsDead) return;
            health.TakeDamage(attackDamage, toPlayer.normalized, Combat.DamageType.Physical, gameObject);
            var ailments = victim.GetComponent<Combat.PlayerAilments>();
            if (ailments != null && !health.IsDead) ailments.Wound(infectionPerHit, bleedChance);
        }

        /// <summary>
        /// 놓치면 그 자리를 새 거점으로 삼아 서성인다.
        /// 원래 자리로 걸어 돌아가면 소리를 따라왔다가 맥없이 퇴근하는 그림이 된다.
        /// </summary>
        protected override void OnStateChanged(AlertState next)
        {
            if (next == AlertState.Idle && Body != null) { _home = Body.position; _nextWanderAt = Time.time + 1f; }
        }

        private void Wander()
        {
            if (Time.time >= _nextWanderAt)
            {
                _wanderTarget = PickWanderTarget();
                _nextWanderAt = Time.time + wanderInterval;
            }

            if (Vector2.Distance(transform.position, _wanderTarget) < 0.2f) Stop();
            else MoveAlongPathTo(_wanderTarget, 0.35f);
        }

        /// <summary>
        /// 벽 속을 찍지 않도록 몇 번 다시 뽑는다.
        /// 예전에는 아무 데나 찍어 놓고 그쪽으로 밀기만 해서, 벽을 고르면 다음 갱신까지 비비고 있었다.
        /// </summary>
        private Vector2 PickWanderTarget()
        {
            for (int i = 0; i < 6; i++)
            {
                Vector2 candidate = _home + Random.insideUnitCircle * wanderRadius;
                if (IsStandable(candidate)) return candidate;
            }
            return IsStandable(_home) ? _home : (Vector2)transform.position;
        }
    }
}
