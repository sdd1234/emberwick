using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 도적 (피드백 2026-10-29).
    ///   - 혼자 움직이지만 자기 구역이 있다. 구역 안에서 따로 순찰한다
    ///   - 빛이나 소리가 나면 그 자리로 간다. 플레이어든 이교도든 보이면 덮친다 (감염자는 건드리지 않는다)
    ///   - 붙어서 단도로 빠르게 여러 번 찌른다 (한 방은 약하고 초당 피해가 높다)
    ///   - 플레이 기록이 쌓일수록 석궁 · 이교도 화살을 옆으로 굴러 피한다 (RogueLearning)
    ///   - 싸우는 중에 근처에 다른 도적이 있으면 불러서 둘러싸고 번갈아 찌른다
    /// </summary>
    public class RogueEnemy : EnemyAI
    {
        [Header("단도")]
        [SerializeField] private float attackRange = 1.25f;
        [SerializeField] private float attackDamage = 5f;
        [SerializeField] private float windUp = 0.16f;
        [SerializeField] private float attackCooldown = 0.42f;
        [SerializeField, Range(0f, 1f)] private float bleedChance = 0.5f;
        [Tooltip("한 번 붙으면 몇 번 찌르고 빠지는가")]
        [SerializeField] private int comboHits = 3;

        [Header("구역")]
        [SerializeField] private float territoryRadius = 9f;
        [SerializeField] private float patrolPause = 2.2f;
        [SerializeField, Range(0.1f, 1f)] private float patrolSpeed = 0.42f;

        [Header("빛 반응")]
        [Tooltip("켜진 등불 · 횃불이 이 거리 안에 보이면 보러 간다 (m)")]
        [SerializeField] private float lightNoticeRange = 18f;

        [Header("협공")]
        [Tooltip("싸움이 나면 이 거리 안의 도적을 부른다 (m)")]
        [SerializeField] private float callRadius = 18f;
        [Tooltip("둘러쌀 때 표적에서 떨어져 기다리는 거리 (m)")]
        [SerializeField] private float circleDistance = 2.6f;
        [Tooltip("동시에 찌르러 들어가는 도적 수")]
        [SerializeField] private int maxStrikers = 2;

        [Header("회피")]
        [SerializeField] private float dodgeDistance = 2.4f;
        [SerializeField] private float dodgeTime = 0.22f;
        [SerializeField] private float dodgeCooldown = 1.1f;

        private static readonly List<RogueEnemy> _all = new();
        private static readonly Color Call = new(0.95f, 0.75f, 0.45f);

        public Vector2 Home { get; private set; }
        public float WindupProgress => _swingAt < 0f ? -1f : Mathf.Clamp01(1f - (_swingAt - Time.time) / Mathf.Max(0.01f, windUp));
        public bool IsDodging => Time.time < _dodgeUntil;
        public Vector2 AttackFacing => _attackFacing;
        public float AttackRange => attackRange;
        public float AttackArcDegrees => 100f;

        private Vector2 _patrolGoal;
        private float _patrolWaitUntil;
        private float _nextAttackAt;
        private float _swingAt = -1f;
        private Vector2 _attackFacing = Vector2.right;
        private Transform _swingTarget;
        private int _hitsLeft;
        private float _backOffUntil;
        private float _slotAngle;
        private float _dodgeUntil, _nextDodgeAt;
        private Vector2 _dodgeDir;
        private float _nextLightLook;
        private readonly HashSet<Object> _seenProjectiles = new();

        public void SetHome(Vector2 home, float radius) { Home = home; territoryRadius = radius; }

        protected override void Awake()
        {
            base.Awake();
            _slotAngle = Random.Range(0f, 360f);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _all.Add(this);
            Core.LightSignal.OnFlash += HandleFlash;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _all.Remove(this);
            Core.LightSignal.OnFlash -= HandleFlash;
        }

        protected override void Start()
        {
            base.Start();
            if (Home == Vector2.zero) Home = transform.position;
            _patrolGoal = Home;
            _hitsLeft = comboHits;
        }

        // ---------- 빛 ----------
        private void HandleFlash(Vector2 at, float radius, GameObject source)
        {
            if (!enabled || State == AlertState.Alerted) return;
            if (Vector2.Distance(Position, at) > Mathf.Max(radius, lightNoticeRange)) return;
            Investigate(at, 9f);
        }

        /// <summary>켜진 빛을 둘러본다 - 플레이어 등불, 이교도 횃불.</summary>
        private void LookForLights()
        {
            if (Time.time < _nextLightLook || State == AlertState.Alerted) return;
            _nextLightLook = Time.time + 0.4f;

            if (PlayerTorch != null && PlayerTorch.IsLit)
            {
                float range = lightNoticeRange * (PlayerTorch.Level == Capstone.Player.LanternLevel.Weak ? 0.55f : 1f);
                if (DistanceToPlayer <= range && HasLineOfSightTo(Player.position)) { Investigate(Player.position, 7f); return; }
            }
            var torches = EnemyTorch.All;
            for (int i = 0; i < torches.Count; i++)
            {
                var t = torches[i];
                if (t == null || !t.DrawsRogues) continue;
                if (Vector2.Distance(Position, t.Position) <= lightNoticeRange && HasLineOfSightTo(t.Position))
                { Investigate(t.Position, 8f); return; }
            }
        }

        // ---------- 회피 ----------
        private void WatchProjectiles()
        {
            if (Time.time < _nextDodgeAt || IsDodging) return;
            foreach (var b in Combat.Bolt.Flying)
                if (b != null && b.InFlight && Threatens(b.transform.position, b.Direction, b.Speed, b, b.Owner)) return;
            foreach (var p in Combat.EnemyProjectile.Flying)
                if (p != null && Threatens(p.transform.position, p.Direction, p.Speed, p, p.Owner)) return;
        }

        /// <summary>날아오는 것이 나를 맞힐 궤적이면, 배운 만큼의 확률로 옆으로 구른다.</summary>
        private bool Threatens(Vector2 pos, Vector2 dir, float speed, Object id, GameObject owner)
        {
            if (owner == gameObject) return false;
            Vector2 chest = Position + Vector2.up * 0.9f;
            Vector2 rel = chest - pos;
            float along = Vector2.Dot(rel, dir);
            if (along <= 0f || along > 9f) return false;
            float miss = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
            if (miss > 0.9f) return false;
            if (along / Mathf.Max(1f, speed) > 0.45f) return false;
            if (_seenProjectiles.Contains(id)) return false;
            _seenProjectiles.Add(id);
            if (_seenProjectiles.Count > 64) _seenProjectiles.Clear();

            bool fromPlayer = owner != null && Player != null && owner.transform == Player;
            if (fromPlayer) RogueLearning.SawBolt();
            float chance = fromPlayer ? RogueLearning.DodgeChance : RogueLearning.DodgeChance * 0.8f + 0.1f;
            if (Random.value > chance) return false;

            Vector2 side = Vector2.Perpendicular(dir);
            if (Vector2.Dot(side, rel) < 0f) side = -side;               // 이미 비껴 있는 쪽으로
            if (!HasClearPath(Position, Position + side * dodgeDistance)) side = -side;
            if (!HasClearPath(Position, Position + side * dodgeDistance)) return false;
            _dodgeDir = side;
            _dodgeUntil = Time.time + dodgeTime;
            _nextDodgeAt = Time.time + dodgeCooldown;
            FX.SfxBank.Play("dodge", Position, 0.8f);
            FX.Vfx.Play("vfx_dust", Position + Vector2.up * 0.25f, _dodgeDir.x > 0f, 20);
            _swingAt = -1f;
            FX.PixelFX.Burst(Position + Vector2.up * 0.2f, -side, new Color(0.45f, 0.4f, 0.35f), new Color(0.6f, 0.55f, 0.45f), 6, 2.5f, 40f, true, 0.35f);
            return true;
        }

        // ---------- 협공 ----------
        protected override void OnEngaged(Core.FactionMember foe) => CallFriends(foe.transform);

        protected override void OnStateChanged(AlertState next)
        {
            if (next == AlertState.Alerted && TargetIsPlayer && Player != null) CallFriends(Player);
        }

        private void CallFriends(Transform target)
        {
            int called = 0;
            foreach (var r in _all)
            {
                if (r == this || r == null || !r.enabled) continue;
                if (Vector2.Distance(r.Position, Position) > callRadius) continue;
                if (r.State == AlertState.Alerted) continue;
                r.JoinFight(target);
                called++;
            }
            if (called > 0) EnemyBark.Say(this, "여기다! 같이 쳐!", Call, 6f);
        }

        private void JoinFight(Transform target)
        {
            var fm = target.GetComponent<Core.FactionMember>();
            if (fm != null && fm.Faction != Core.Faction.Player) Engage(fm, true);
            else Investigate(target.position, 8f);
            EnemyBark.Say(this, "간다!", Call, 4f);
        }

        /// <summary>같은 표적을 노리는 도적 중 몇 번째인가 - 둘러쌀 자리를 나눈다.</summary>
        private void PackInfo(out int index, out int count, out int strikersAhead)
        {
            index = 0; count = 0; strikersAhead = 0;
            var me = Target;
            foreach (var r in _all)
            {
                if (r == null || r.State != AlertState.Alerted || r.Target != me) continue;
                if (r == this) index = count;
                else if (r.DistanceToTarget < DistanceToTarget && r._backOffUntil < Time.time) strikersAhead++;
                count++;
            }
        }

        // ---------- 생각 ----------
        protected override void Think()
        {
            WatchProjectiles();
            if (IsDodging)
            {
                Body.linearVelocity = _dodgeDir * (dodgeDistance / dodgeTime);
                return;
            }
            LookForLights();

            if (_swingAt > 0f)
            {
                Stop();
                if (Time.time >= _swingAt) Strike();
                return;
            }

            switch (State)
            {
                case AlertState.Alerted: Fight(); break;
                case AlertState.Suspicious:
                    if (Vector2.Distance(Position, ChaseTarget) < 1.2f) { Stop(); if (!Investigating) EndInvestigation(); }
                    else MoveAlongPathTo(ChaseTarget, 0.75f);
                    break;
                default: Patrol(); break;
            }
        }

        private void Fight()
        {
            // 감염자에게 쫓기면 맞서지 않고 빠진다
            if (!TargetIsPlayer && Foe != null && Foe.Faction == Core.Faction.Infected)
            {
                Vector2 away = (Position - Foe.Position).normalized;
                MoveToward(Position + away * 3f, 1.1f);
                return;
            }

            if (!CanSeeTarget) { MoveAlongPathTo(ChaseTarget, 1.1f); return; }

            PackInfo(out int index, out int count, out int ahead);
            bool myTurn = ahead < maxStrikers && Time.time >= _backOffUntil;

            if (!myTurn)
            {
                // 둘러싼다 - 표적 둘레의 내 자리로 돌아 들어간다
                float ang = _slotAngle + index * (360f / Mathf.Max(1, count)) + Time.time * 25f;
                Vector2 slot = TargetPosition + new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * circleDistance;
                if (Vector2.Distance(Position, slot) > 0.4f) MoveAlongPathTo(slot, 0.85f);
                else Stop();
                return;
            }

            if (DistanceToTarget > attackRange) { MoveAlongPathTo(TargetPosition, 1.15f); return; }
            Stop();
            if (Time.time >= _nextAttackAt) BeginStrike();
        }

        private void BeginStrike()
        {
            _swingTarget = Target;
            Vector2 to = TargetPosition - Position;
            if (to.sqrMagnitude > 0.0001f) _attackFacing = to.normalized;
            _swingAt = Time.time + windUp;
            _nextAttackAt = Time.time + windUp + attackCooldown;
        }

        private void Strike()
        {
            _swingAt = -1f;
            var victim = _swingTarget;
            _swingTarget = null;
            if (victim == null) return;
            FX.SfxBank.Play("dagger", Position, 0.7f, 0.15f);
            FX.Vfx.Play("vfx_slash", Position + Vector2.up * 1.0f + _attackFacing * 0.7f, _attackFacing.x < 0f);
            Vector2 to = (Vector2)victim.position - Position;
            if (to.magnitude <= attackRange * 1.3f && HasLineOfSightTo(victim.position) && Vector2.Angle(_attackFacing, to) <= 60f)
            {
                var h = victim.GetComponent<Combat.Health>();
                if (h != null && !h.IsDead)
                {
                    h.TakeDamage(attackDamage, to.normalized, Combat.DamageType.Physical, gameObject);
                    var ail = victim.GetComponent<Combat.PlayerAilments>();
                    if (ail != null && !h.IsDead) ail.Wound(0f, bleedChance);
                }
            }
            // 몇 번 찌르면 한 발 빠졌다가 다시 들어온다 - 둘러싼 동료와 번갈아
            if (--_hitsLeft <= 0)
            {
                _hitsLeft = comboHits;
                _backOffUntil = Time.time + 1.1f;
            }
        }

        private void Patrol()
        {
            if (Time.time < _patrolWaitUntil) { Stop(); return; }
            if (Vector2.Distance(Position, _patrolGoal) < 0.6f)
            {
                _patrolWaitUntil = Time.time + patrolPause * Random.Range(0.6f, 1.4f);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 c = Home + Random.insideUnitCircle * territoryRadius;
                    if (IsStandable(c)) { _patrolGoal = c; break; }
                }
                Stop();
                return;
            }
            // 구역 밖으로 끌려 나갔으면 돌아온다
            MoveAlongPathTo(_patrolGoal, Vector2.Distance(Position, Home) > territoryRadius * 1.5f ? 0.6f : patrolSpeed);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.6f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(Application.isPlaying ? (Vector3)Home : transform.position, territoryRadius);
        }
    }

    /// <summary>
    /// 도적이 석궁에 익숙해지는 정도 (피드백 - 플레이 기록이 쌓이면 나중에는 무빙으로 피한다).
    /// 도적이 플레이어의 볼트를 "본" 횟수가 쌓일수록 피할 확률이 오른다. 판이 바뀌어도 세션 동안 남는다.
    /// </summary>
    public static class RogueLearning
    {
        public static int BoltsSeen { get; private set; }
        /// <summary>볼트 하나마다 3%p, 최대 60%.</summary>
        public static float DodgeChance => Mathf.Min(0.6f, BoltsSeen * 0.03f);

        private static int _lastToastStep;

        public static void SawBolt()
        {
            BoltsSeen++;
            int step = Mathf.FloorToInt(DodgeChance * 10f);       // 10%p 마다 한 번 알린다
            if (step > _lastToastStep)
            {
                _lastToastStep = step;
                UI.HUDController.Toast($"도적들이 석궁 궤적에 익숙해지고 있다 (회피 {DodgeChance * 100f:0}%)");
            }
        }
    }
}
