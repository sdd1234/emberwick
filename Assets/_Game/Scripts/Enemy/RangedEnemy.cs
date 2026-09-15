using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 이교도 / 도적단 계열 - 거리를 두고 쏜다 (기획서 7.1).
    /// 너무 가까우면 물러나고 너무 멀면 붙어서 선호 사거리를 유지한다.
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

            if (State != AlertState.Alerted)
            {
                if (State == AlertState.Suspicious) MoveAlongPathTo(ChaseTarget, 0.4f);
                else Stop();
                return;
            }

            // 쏠 자리가 안 나오면 사거리 유지보다 시야 확보가 먼저다.
            // 벽 뒤에서 거리만 맞춰 놓고 벽에 대고 겨누는 게 예전 동작이었다
            if (!CanSeePlayer) { MoveAlongPathTo(ChaseTarget); return; }

            // 거리 관리
            if (DistanceToPlayer < retreatRange)
            {
                // 붙었다고 물러나기만 하면 그냥 맞아준다.
                // 물러나되 쏠 때가 되면 쏜다. 물러날 곳이 없으면 그 자리에서 쏜다
                Vector2 away = ((Vector2)transform.position - (Vector2)Player.position).normalized;
                if (!MoveAwayFrom(away, 0.9f)) Stop();
                TryStartAim();
                return;
            }
            else if (DistanceToPlayer > preferredRange + rangeTolerance)
            {
                MoveAlongPathTo(Player.position);
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

        private void TryStartAim()
        {
            if (Time.time < _nextFireAt) return;
            if (!CanSeePlayer) return;                       // 벽에 대고 겨누지 않는다
            _aimSnapshot = Player.position;                  // 조준 시작 시점의 위치를 기억
            _fireAt = Time.time + aimTime;
            _nextFireAt = Time.time + fireCooldown + aimTime;
        }

        private void Fire()
        {
            _fireAt = -1f;
            if (!projectilePrefab) return;

            Vector2 origin = muzzle ? (Vector2)muzzle.position : (Vector2)transform.position;

            // 겨누는 사이에 벽이 끼어들었으면 쏘지 않는다.
            // 기억해 둔 자리로 쏘는 규칙은 그대로 두되, 총구에서 그 자리가 보일 때만이다
            if (Physics2D.Linecast(origin, _aimSnapshot, sightBlockMask).collider != null) return;
            // 기억해둔 위치로 쏜다 - 움직이면 피할 수 있다
            Vector2 dir = (_aimSnapshot - origin).normalized;
            float offset = Random.Range(-spreadDegrees, spreadDegrees);
            dir = (Quaternion.Euler(0f, 0f, offset) * dir).normalized;

            var proj = Instantiate(projectilePrefab, origin, Quaternion.identity);
            proj.Launch(dir, projectileDamage, gameObject);
        }
    }
}
