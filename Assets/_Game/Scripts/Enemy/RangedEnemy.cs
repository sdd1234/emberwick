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
                if (State == AlertState.Suspicious) MoveToward(Player.position, 0.4f);
                else Stop();
                return;
            }

            // 거리 관리
            if (DistanceToPlayer < retreatRange)
            {
                Vector2 away = (Vector2)transform.position - (Vector2)Player.position;
                MoveToward((Vector2)transform.position + away.normalized, 0.9f);
            }
            else if (DistanceToPlayer > preferredRange + rangeTolerance)
            {
                MoveToward(Player.position);
            }
            else
            {
                Stop();
                TryStartAim();
            }
        }

        private void TryStartAim()
        {
            if (Time.time < _nextFireAt) return;
            _aimSnapshot = Player.position;                  // 조준 시작 시점의 위치를 기억
            _fireAt = Time.time + aimTime;
            _nextFireAt = Time.time + fireCooldown + aimTime;
        }

        private void Fire()
        {
            _fireAt = -1f;
            if (!projectilePrefab) return;

            Vector2 origin = muzzle ? (Vector2)muzzle.position : (Vector2)transform.position;
            // 기억해둔 위치로 쏜다 - 움직이면 피할 수 있다
            Vector2 dir = (_aimSnapshot - origin).normalized;
            float offset = Random.Range(-spreadDegrees, spreadDegrees);
            dir = (Quaternion.Euler(0f, 0f, offset) * dir).normalized;

            var proj = Instantiate(projectilePrefab, origin, Quaternion.identity);
            proj.Launch(dir, projectileDamage, gameObject);
        }
    }
}
