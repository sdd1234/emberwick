using System.Collections.Generic;
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

        [Header("길찾기")]
        [Tooltip("길을 막는 것으로 볼 레이어. 비우면 시야 차단 레이어를 그대로 쓴다")]
        [SerializeField] protected LayerMask obstacleMask;
        [Tooltip("몸 반지름 (m). 길을 낼 때 벽에서 이만큼은 떨어뜨린다")]
        [SerializeField] protected float agentRadius = 0.42f;
        [Tooltip("벽이 이 거리 안으로 들어오면 밀려난다 (m). 어깨를 대고 비비지 않게 한다")]
        [SerializeField] protected float wallClearance = 0.55f;
        [Tooltip("길을 다시 계산하는 간격 (초)")]
        [SerializeField] protected float repathInterval = 0.4f;
        [Tooltip("중간 지점에 이만큼 붙으면 지난 것으로 친다 (m)")]
        [SerializeField] protected float waypointReach = 0.4f;

        [Header("끼임 탈출")]
        [Tooltip("이 시간 동안 명령한 만큼 못 움직이면 끼인 것으로 본다 (초)")]
        [SerializeField] protected float stuckGrace = 0.45f;
        [Tooltip("끼였을 때 옆으로 빠져나가는 시간 (초)")]
        [SerializeField] protected float unstickDuration = 0.5f;

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

        // 길찾기 상태
        private readonly List<Vector2> _path = new();
        private int _pathIndex;
        private float _nextRepathAt;
        private Vector2 _pathGoal;
        private LayerMask _obstacle;

        // 끼임 감지
        private Vector2 _stuckAnchor;
        private float _stuckSince = -1f;
        private float _unstickUntil;
        private Vector2 _unstickDir;

        /// <summary>물리 기준 위치. 콜라이더가 발밑에 있어 transform 과 미세하게 다르다.</summary>
        protected Vector2 Position => Body != null ? Body.position : (Vector2)transform.position;

        /// <summary>지금 플레이어가 보이는가. 벽 너머로 때리지 않으려면 공격 전에 반드시 묻는다.</summary>
        protected bool CanSeePlayer => Player != null && HasLineOfSightTo(Player.position);

        /// <summary>쫓아갈 곳. 보이면 플레이어, 놓쳤으면 마지막으로 본 자리.</summary>
        protected Vector2 ChaseTarget => CanSeePlayer ? (Vector2)Player.position : LastKnownPosition;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Body.gravityScale = 0f;
            Body.freezeRotation = true;
            if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            // 인스펙터에서 안 채웠으면 시야를 막는 것과 같은 레이어로 본다.
            // 벽은 눈도 몸도 똑같이 막으므로 기본값으로 맞는다
            _obstacle = obstacleMask.value != 0 ? obstacleMask : sightBlockMask;
            if (_obstacle.value == 0)
            {
                int wall = LayerMask.NameToLayer(Core.GameLayers.Wall);
                if (wall >= 0) _obstacle = 1 << wall;
            }
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
            TrackProgress();              // Think 이 새 속도를 넣기 전에 지난 프레임 결과를 본다
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

        private bool HasLineOfSight() => HasLineOfSightTo(Player.position);

        /// <summary>그 지점이 벽에 가리지 않고 보이는가.</summary>
        protected bool HasLineOfSightTo(Vector2 point)
        {
            if (sightBlockMask.value == 0) return true;

            Vector2 origin = Position;
            Vector2 delta = point - origin;
            float dist = delta.magnitude;
            if (dist < 0.01f) return true;
            return Physics2D.Raycast(origin, delta / dist, dist, sightBlockMask).collider == null;
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

        /// <summary>목표 쪽으로 곧장 민다. 벽을 신경 쓰지 않으므로 길이 뚫려 있을 때만 쓴다.</summary>
        protected void MoveToward(Vector2 target, float speedMultiplier = 1f)
        {
            Vector2 dir = (target - Position).normalized;

            // 끼여 있는 동안에는 목표를 무시하고 빠져나오는 쪽으로 민다
            if (Time.time < _unstickUntil) dir = _unstickDir;
            else dir = AvoidWalls(dir);

            Body.linearVelocity = dir * moveSpeed * speedMultiplier;
        }

        /// <summary>
        /// 벽으로 파고드는 속도 성분을 덜어낸다.
        ///
        /// 길찾기를 넣어도 "벽에 어깨를 대고 미는" 그림은 남는다. 경로 칸은 벽에서 몸 반지름만큼만
        /// 떨어져 있고, 옆에서 다른 적이 밀거나 모퉁이를 크게 돌면 결국 벽에 닿는다.
        /// 닿는 것 자체는 괜찮다. 문제는 벽 쪽으로 계속 미는 것이다 - 속도는 벽이 먹고
        /// 남는 건 비비는 소리뿐이다. 그래서 (1) 벽을 파고드는 성분을 지우고
        /// (2) 너무 붙었으면 살짝 떼어낸다. 벽을 따라가는 성분은 그대로 남으므로 속도는 안 죽는다.
        /// </summary>
        private Vector2 AvoidWalls(Vector2 dir)
        {
            if (_obstacle.value == 0 || dir.sqrMagnitude < 0.0001f) return dir;

            var ahead = Physics2D.CircleCast(Position, agentRadius, dir, wallClearance, _obstacle);
            if (ahead.collider != null)
            {
                float into = Vector2.Dot(dir, ahead.normal);
                if (into < 0f)
                {
                    Vector2 slide = dir - ahead.normal * into;
                    // 정면으로 부딪히면 접선 성분이 거의 없다. 그때는 원래 방향을 두고 밀어내기에 맡긴다
                    if (slide.sqrMagnitude > 0.04f) dir = slide.normalized;
                }
            }

            var near = Physics2D.OverlapCircle(Position, wallClearance, _obstacle);
            if (near != null)
            {
                Vector2 push = Position - near.ClosestPoint(Position);
                if (push.sqrMagnitude > 0.000001f) dir = (dir + push.normalized * 0.6f).normalized;
            }
            return dir;
        }

        /// <summary>
        /// 명령한 만큼 실제로 움직였는지 본다.
        ///
        /// 길찾기를 넣어도 비비는 경우는 남는다 - 앞선 적이 길을 막았거나, 플레이어와 부딪혔거나,
        /// 모퉁이에 어깨가 걸렸거나. 원인을 하나씩 다루는 대신 "밀고 있는데 안 나간다"는
        /// 결과만 보고 옆으로 빼낸다. 어떤 이유로 끼든 여기서 풀린다.
        /// </summary>
        private void TrackProgress()
        {
            float commanded = Body.linearVelocity.magnitude;
            if (commanded < 0.2f || Time.time < _unstickUntil)
            {
                _stuckSince = -1f;
                _stuckAnchor = Position;
                return;
            }

            if (_stuckSince < 0f) { _stuckSince = Time.time; _stuckAnchor = Position; return; }

            float elapsed = Time.time - _stuckSince;
            if (elapsed < stuckGrace) return;

            // 명령 속도의 35% 도 못 냈으면 밀고만 있는 것이다
            if (Vector2.Distance(Position, _stuckAnchor) < commanded * elapsed * 0.35f) BeginUnstick();

            _stuckSince = -1f;
            _stuckAnchor = Position;
        }

        private void BeginUnstick()
        {
            Vector2 dir = Body.linearVelocity.sqrMagnitude > 0.0001f ? Body.linearVelocity.normalized : Vector2.right;
            Vector2 left = Vector2.Perpendicular(dir);
            Vector2 right = -left;

            bool leftOpen = HasClearPath(Position, Position + left * 1.2f);
            bool rightOpen = HasClearPath(Position, Position + right * 1.2f);

            if (leftOpen && rightOpen) _unstickDir = Random.value < 0.5f ? left : right;
            else if (leftOpen) _unstickDir = left;
            else if (rightOpen) _unstickDir = right;
            else _unstickDir = -dir;                     // 양옆이 다 막혔으면 뒤로 뺀다

            _unstickUntil = Time.time + unstickDuration;
            _path.Clear();                               // 빠져나온 자리에서 길을 새로 잡는다
            _nextRepathAt = 0f;
        }

        /// <summary>
        /// 벽을 피해 목표까지 간다. 추격 · 접근은 전부 이걸 쓴다.
        ///
        /// 예전에는 어디로 가든 MoveToward 하나였다. 그래서 사이에 벽이 있으면
        /// 벽을 향해 계속 속도를 꽂은 채 비비고만 있었다 - 길을 못 찾는 게 아니라 아예 안 찾았다.
        /// 순서는 셋이다. (1) 곧장 갈 수 있으면 그냥 간다 (대부분 여기서 끝난다)
        /// (2) 막혔으면 격자에서 길을 찾아 따라간다 (3) 그마저 없으면 벽을 따라 미끄러진다.
        /// </summary>
        protected void MoveAlongPathTo(Vector2 target, float speedMultiplier = 1f)
        {
            Map.NavGrid.EnsureBuilt(_obstacle, agentRadius);

            if (HasClearPath(Position, target))
            {
                _path.Clear();
                MoveToward(target, speedMultiplier);
                return;
            }

            bool goalMoved = Vector2.Distance(_pathGoal, target) > Map.NavGrid.CellSize;
            if (_path.Count == 0 || goalMoved || Time.time >= _nextRepathAt)
            {
                _nextRepathAt = Time.time + repathInterval;
                _pathGoal = target;
                _pathIndex = 0;
                if (!Map.NavGrid.FindPath(Position, target, _path)) _path.Clear();
            }

            if (_path.Count == 0) { SlideToward(target, speedMultiplier); return; }
            FollowPath(speedMultiplier);
        }

        private void FollowPath(float speedMultiplier)
        {
            // 갈 수 있는 가장 먼 지점까지 건너뛴다.
            // 격자 칸을 그대로 밟으면 톱니처럼 꺾이면서 걷는다
            int skipped = 0;
            while (_pathIndex + 1 < _path.Count && skipped < 4 && HasClearPath(Position, _path[_pathIndex + 1]))
            {
                _pathIndex++;
                skipped++;
            }

            if (_pathIndex >= _path.Count) { _path.Clear(); Stop(); return; }

            Vector2 wp = _path[_pathIndex];
            if (Vector2.Distance(Position, wp) <= waypointReach)
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count) { _path.Clear(); Stop(); return; }
                wp = _path[_pathIndex];
            }
            MoveToward(wp, speedMultiplier);
        }

        /// <summary>
        /// 길이 아예 없을 때의 마지막 수단. 벽을 정면으로 미는 대신 벽면을 따라 흐른다.
        /// 목표가 벽 속에 있거나(배회 지점) 격자 밖에 있을 때 여기로 온다.
        /// </summary>
        private void SlideToward(Vector2 target, float speedMultiplier)
        {
            Vector2 delta = target - Position;
            if (delta.sqrMagnitude < 0.0001f) { Stop(); return; }

            Vector2 dir = delta.normalized;
            var hit = Physics2D.CircleCast(Position, agentRadius, dir, agentRadius * 2.5f, _obstacle);
            if (hit.collider == null) { MoveToward(target, speedMultiplier); return; }

            Vector2 along = Vector2.Perpendicular(hit.normal);
            if (Vector2.Dot(along, dir) < 0f) along = -along;
            if (Time.time < _unstickUntil) along = _unstickDir;      // 끼임 탈출이 우선이다
            Body.linearVelocity = along * moveSpeed * speedMultiplier;
        }

        /// <summary>몸 굵기를 감안해 두 점 사이가 뚫려 있는지. 벽을 스치는 대각선도 걸러진다.</summary>
        protected bool HasClearPath(Vector2 from, Vector2 to)
        {
            if (_obstacle.value == 0) return true;

            Vector2 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.01f) return true;
            return Physics2D.CircleCast(from, agentRadius, delta / dist, dist, _obstacle).collider == null;
        }

        /// <summary>그 자리에 서 있을 수 있는가 (배회 지점 고를 때).</summary>
        protected bool IsStandable(Vector2 point)
            => Physics2D.OverlapCircle(point, agentRadius, _obstacle) == null;

        protected void Stop()
        {
            Body.linearVelocity = Vector2.zero;
            _path.Clear();
        }

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
