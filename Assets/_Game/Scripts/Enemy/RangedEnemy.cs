using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 이교도 / 도적단 계열 - 거리를 두고 쏜다 (기획서 7.1).
    /// 너무 가까우면 물러나고 너무 멀면 붙어서 선호 사거리를 유지한다.
    /// 평소에는 순찰로를 돈다. 이교도는 2명이 한 조 - 한 명이 길을 잡고(leader) 다른 한 명이 뒤를 따른다.
    /// </summary>
    public class RangedEnemy : EnemyAI
    {
        [Header("원거리 공격")]
        [SerializeField] private Combat.EnemyProjectile projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField] private float projectileDamage = 10f;
        [SerializeField] private float fireCooldown = 2.0f;
        [Tooltip("발사 전 조준 시간. 이 사이에 엄폐하면 피할 수 있다")]
        [SerializeField] private float aimTime = 0.6f;
        [Tooltip("탄착 오차 (도)")]
        [SerializeField] private float spreadDegrees = 5f;

        [Header("거리 유지")]
        [Tooltip("이 거리를 유지하려 한다 (m)")]
        [SerializeField] private float preferredRange = 8f;
        [Tooltip("선호 사거리에서 허용하는 여유 (m)")]
        [SerializeField] private float rangeTolerance = 1.5f;
        [Tooltip("이 거리보다 가까워지면 물러난다 (m)")]
        [SerializeField] private float retreatRange = 4f;

        [Header("순찰 (피드백 - 교회 주변 2인 1조)")]
        [Tooltip("돌아다닐 지점들 (월드 좌표). 끝에 닿으면 처음으로 돌아간다. 비우면 제자리에 선다")]
        [SerializeField] private Vector2[] patrolPoints;
        [Tooltip("조장. 채워져 있으면 순찰로 대신 조장의 뒤를 따른다")]
        [SerializeField] private RangedEnemy patrolLeader;
        [Tooltip("조장 뒤로 떨어지는 거리 (m)")]
        [SerializeField] private float followDistance = 1.8f;
        [SerializeField, Range(0.1f, 1f)] private float patrolSpeed = 0.45f;
        [Tooltip("지점마다 서서 둘러보는 시간 (초)")]
        [SerializeField] private float patrolPause = 1.5f;

        [Header("대응수단 (피드백 - 감염자에게 대응수단이 있으나 모두가 쓰는 건 아니다)")]
        [Tooltip("횃불을 든 이교도만 켠다. 감염자를 만나면 석궁 대신 화염병을 던진다")]
        [SerializeField] private bool throwsFireFlask = false;
        [SerializeField] private float flaskDamage = 30f;
        [SerializeField] private float flaskCooldown = 5.5f;
        [SerializeField] private Sprite flaskSprite;

        private int _patrolIndex;
        private float _patrolWaitUntil;
        private float _nextFlaskAt;

        // ---------- 명령 (교대 · 후퇴 · 증원 - CultistSquad 가 내린다) ----------
        private bool _hasOrder;
        private Vector2 _orderGoal;
        private float _orderSpeed;
        private bool _orderIgnoresCombat;
        private System.Action<RangedEnemy> _onArrive;

        /// <summary>그 자리로 가라. ignoreCombat 이면 싸움보다 이동이 먼저다 (후퇴).</summary>
        public void Order(Vector2 goal, float speed, bool ignoreCombat, System.Action<RangedEnemy> onArrive)
        {
            _hasOrder = true;
            _orderGoal = goal;
            _orderSpeed = speed;
            _orderIgnoresCombat = ignoreCombat;
            _onArrive = onArrive;
        }
        public void ClearOrder() { _hasOrder = false; _onArrive = null; }
        public bool HasOrder => _hasOrder;
        public Vector2[] PatrolPoints => patrolPoints;
        public RangedEnemy PatrolLeader => patrolLeader;
        public bool ThrowsFireFlask { get => throwsFireFlask; set => throwsFireFlask = value; }
        public bool InCombat => State == AlertState.Alerted;
        public new Vector2 Position => base.Position;

        private bool TickOrder()
        {
            if (!_hasOrder) return false;
            if (!_orderIgnoresCombat && State == AlertState.Alerted) return false;   // 싸움이 나면 명령은 잠시 미룬다
            if (Vector2.Distance(Position, _orderGoal) <= 0.8f)
            {
                Stop();
                var cb = _onArrive;
                _hasOrder = false;
                _onArrive = null;
                cb?.Invoke(this);
                return true;
            }
            MoveAlongPathTo(_orderGoal, _orderSpeed);
            return true;
        }

        /// <summary>순찰로 설정 (맵 빌더가 부른다).</summary>
        public void SetPatrol(Vector2[] points, RangedEnemy leader, float side = 1f)
        {
            patrolPoints = points;
            patrolLeader = leader;
            followSide = side;
            _patrolIndex = 0;
        }

        [Tooltip("조장 뒤에서 어느 쪽 어깨에 서는가 (+1 / -1). 셋이 다닐 때 겹치지 않게")]
        [SerializeField] private float followSide = 1f;

        /// <summary>조준 진행도 0~1. 조준 중이 아니면 -1.</summary>
        public float AimProgress
            => _fireAt < 0f ? -1f : Mathf.Clamp01(1f - (_fireAt - Time.time) / Mathf.Max(0.01f, aimTime));
        /// <summary>조준을 시작한 순간 기억한 표적 위치.</summary>
        public Vector2 AimTarget => _aimSnapshot;

        private float _nextFireAt;
        private float _fireAt = -1f;
        private Vector2 _aimSnapshot;

        protected override void Think()
        {
            if (_fireAt > 0f)
            {
                Stop();
                if (Time.time >= _fireAt) Fire();
                return;
            }

            if (TickOrder()) return;

            if (State != AlertState.Alerted)
            {
                if (State == AlertState.Suspicious) MoveAlongPathTo(ChaseTarget, 0.4f);
                else Patrol();
                return;
            }

            // 쏠 자리가 안 나오면 사거리 유지보다 시야 확보가 먼저다.
            // 벽 뒤에서 거리만 맞춰 놓고 벽에 대고 겨누는 게 예전 동작이었다
            if (!CanSeeTarget) { MoveAlongPathTo(ChaseTarget); return; }

            // 감염자 - 화살은 거의 안 먹는다. 화염병을 가진 이교도만 맞서고, 나머지는 거리를 벌리며 쏜다
            if (!TargetIsPlayer && Foe != null && Foe.Faction == Core.Faction.Infected && throwsFireFlask && Time.time >= _nextFlaskAt && DistanceToTarget <= 9f)
            {
                ThrowFlask();
                return;
            }

            // 거리 관리
            if (DistanceToTarget < retreatRange)
            {
                // 붙었다고 물러나기만 하면 그냥 맞아준다.
                // 물러나되 쏠 때가 되면 쏜다. 물러날 곳이 없으면 그 자리에서 쏜다
                Vector2 away = ((Vector2)transform.position - TargetPosition).normalized;
                if (!MoveAwayFrom(away, 0.9f)) Stop();
                TryStartAim();
                return;
            }
            else if (DistanceToTarget > preferredRange + rangeTolerance)
            {
                MoveAlongPathTo(TargetPosition);
            }
            else
            {
                Stop();
                TryStartAim();
            }
        }

        /// <summary>
        /// 물러난다. 갈 곳이 없으면 false 를 돌려주고 - 그때는 물러나는 시늉 대신 쏘는 게 낫다.
        /// 뒤가 막혔는데도 계속 밀면 벽에 등을 대고 비비는 꼴이 된다.
        /// </summary>
        private bool MoveAwayFrom(Vector2 away, float speedMultiplier)
        {
            Vector2 spot = Position + away * 2f;
            if (HasClearPath(Position, spot)) { MoveToward(spot, speedMultiplier); return true; }

            // 뒤가 막혔으면 옆으로. 두 방향 중 트인 쪽을 고른다
            Vector2 left = Vector2.Perpendicular(away);
            Vector2 right = -left;
            bool leftOpen = HasClearPath(Position, Position + left * 2f);
            bool rightOpen = HasClearPath(Position, Position + right * 2f);

            if (!leftOpen && !rightOpen) return false;
            Vector2 pick = leftOpen && rightOpen
                ? (Random.value < 0.5f ? left : right)
                : (leftOpen ? left : right);
            MoveToward(Position + pick * 2f, speedMultiplier);
            return true;
        }

        private void Patrol()
        {
            // 조원 - 조장이 살아 있으면 그 뒤를 따른다. 조장이 죽으면 혼자 순찰로를 이어 돈다
            if (patrolLeader != null && patrolLeader.isActiveAndEnabled)
            {
                Vector2 lp = patrolLeader.Position;
                Vector2 heading = patrolLeader.Body != null && patrolLeader.Body.linearVelocity.sqrMagnitude > 0.01f
                    ? patrolLeader.Body.linearVelocity.normalized
                    : (lp - Position).normalized;
                Vector2 slot = lp - heading * followDistance + Vector2.Perpendicular(heading) * 0.9f * followSide;

                if (Vector2.Distance(Position, slot) < 0.4f) Stop();
                else MoveAlongPathTo(slot, Vector2.Distance(Position, slot) > followDistance * 2f ? patrolSpeed * 1.5f : patrolSpeed);
                return;
            }

            if (patrolPoints == null || patrolPoints.Length == 0) { Stop(); return; }
            if (Time.time < _patrolWaitUntil) { Stop(); return; }

            Vector2 goal = patrolPoints[_patrolIndex % patrolPoints.Length];
            if (Vector2.Distance(Position, goal) <= 0.5f)
            {
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                _patrolWaitUntil = Time.time + patrolPause;
                Stop();
                return;
            }
            MoveAlongPathTo(goal, patrolSpeed);
        }

        private void TryStartAim()
        {
            if (Time.time < _nextFireAt) return;
            if (!CanSeeTarget) return;                       // 벽에 대고 겨누지 않는다
            _aimSnapshot = TargetPosition + Vector2.up * 0.9f;   // 조준 시작 시점의 위치를 기억 (가슴 높이)
            _fireAt = Time.time + aimTime;
            _nextFireAt = Time.time + fireCooldown + aimTime;
        }

        private void ThrowFlask()
        {
            _nextFlaskAt = Time.time + flaskCooldown;
            Stop();
            EnemyBark.Say(this, "불로 태워라!", new Color(1f, 0.55f, 0.3f), 4f);
            Combat.ThrownFlask.Throw(new Combat.ThrownFlask.Spec
            {
                type = Combat.DamageType.Fire, damage = flaskDamage, radius = 2.2f,
                glow = new Color(1f, 0.45f, 0.18f), lingerSeconds = 2.4f, noise = 8f, sprite = flaskSprite,
            }, Position + Vector2.up * 1.3f, TargetPosition, gameObject);
        }

        private void Fire()
        {
            _fireAt = -1f;
            if (!projectilePrefab) return;

            // 적 총구도 같은 함정을 안고 있다 - 발밑에서 북쪽으로 0.55m 올려둔 자리라
            // 북쪽에 벽이 있으면 벽 속에서 투사체가 생긴다
            Vector2 body = (Vector2)transform.position + Vector2.up * 0.19f;
            Vector2 desired = muzzle ? (Vector2)muzzle.position : body;
            Vector2 origin = Combat.ProjectileOrigin.Resolve(body, desired, sightBlockMask);

            // 겨누는 사이에 벽이 끼어들었으면 쏘지 않는다.
            // 기억해 둔 자리로 쏘는 규칙은 그대로 두되, 총구에서 그 자리가 보일 때만이다
            if (Physics2D.Linecast(origin, _aimSnapshot, sightBlockMask).collider != null) return;
            // 기억해둔 위치로 쏜다 - 움직이면 피할 수 있다
            Vector2 dir = (_aimSnapshot - origin).normalized;
            float offset = Random.Range(-spreadDegrees, spreadDegrees);
            dir = (Quaternion.Euler(0f, 0f, offset) * dir).normalized;

            var proj = Instantiate(projectilePrefab, origin, Quaternion.identity);
            proj.Launch(dir, projectileDamage, gameObject);
            FX.SfxBank.Play("enemy_shot", origin, 0.75f);
            EmitCombatNoise();
        }
    }
}
