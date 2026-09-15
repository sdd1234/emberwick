using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>감염체 계열 - 붙어서 때린다. 소리와 빛에 반응해 몰려든다 (기획서 7.1).</summary>
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
                if (Time.time >= _swingAt) Swing();
                return;
            }

            switch (State)
            {
                case AlertState.Alerted:
                    // 붙어 있어도 벽 너머면 때릴 수 없다. 돌아 들어가야 한다
                    if (DistanceToPlayer <= attackRange && CanSeePlayer) { Stop(); TryStartAttack(); }
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
            if (!CanSeePlayer) return;                       // 벽을 사이에 두고 휘두르지 않는다

            // 휘두를 방향을 예비 동작 시작 시점에 고정한다.
            // 이래야 플레이어가 옆으로 돌아 들어가 피할 수 있다.
            Vector2 toPlayer = (Vector2)Player.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.0001f) _attackFacing = toPlayer.normalized;

            _swingAt = Time.time + windUp;
            _nextAttackAt = Time.time + attackCooldown + windUp;
        }

        private void Swing()
        {
            _swingAt = -1f;
            if (Player == null) return;

            Vector2 toPlayer = (Vector2)Player.position - (Vector2)transform.position;

            // 예비 동작 사이에 벗어났으면 빗나간다
            if (toPlayer.magnitude > attackRange * 1.25f) return;

            // 휘두르는 사이에 벽 뒤로 숨었으면 벽을 때린 것이다
            if (!HasLineOfSightTo(Player.position)) return;

            // 앞쪽 부채꼴 밖이면 빗나간다. 옆이나 뒤로 돌아 들어가면 피할 수 있다.
            if (Vector2.Angle(_attackFacing, toPlayer) > attackArcDegrees * 0.5f) return;

            var health = Player.GetComponent<Combat.Health>();
            if (health == null || health.IsDead) return;
            health.TakeDamage(attackDamage, toPlayer.normalized);
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
