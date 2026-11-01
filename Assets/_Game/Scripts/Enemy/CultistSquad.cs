using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Enemy
{
    /// <summary>
    /// 이교도 무리 지휘 (피드백 2026-10-29).
    ///   - 2인 1조로 순찰로를 돈다
    ///   - 정기적으로 순찰조가 교대된다: 숙소에서 쉬던 두 명이 나와 순찰로를 넘겨받고, 돌던 조는 숙소로 들어간다
    ///   - 한 명이 죽으면 남은 한 명은 숙소로 후퇴하려 한다
    ///   - 후퇴해서 살아남으면 숙소의 동료를 데리고 그 자리로 돌아와, 그 구역을 중심으로 순찰을 돈다
    /// 순찰 · 따라가기 · 이동은 RangedEnemy 가 하고, 여기서는 누가 어디로 갈지만 정한다.
    /// </summary>
    public class CultistSquad : MonoBehaviour
    {
        [System.Serializable]
        public class Route
        {
            public string name;
            public Vector2[] points;
            public RangedEnemy leader;
            public RangedEnemy follower;
        }

        [SerializeField] private List<Route> routes = new();
        [Tooltip("숙소에서 쉬는 이교도들")]
        [SerializeField] private List<RangedEnemy> reserves = new();
        [Tooltip("숙소 안 쉬는 자리들. 후퇴 · 교대 후 여기로 들어간다")]
        [SerializeField] private Vector2[] barracksSpots;

        [Header("교대")]
        [SerializeField] private float shiftInterval = 70f;
        [Tooltip("첫 교대까지 (초) - 판이 시작하고 금방 한 번 보이게")]
        [SerializeField] private float firstShiftAt = 35f;

        [Header("후퇴 · 증원")]
        [Tooltip("조원이 죽었을 때 남은 쪽이 후퇴할 확률 (피드백 - 후퇴하려는 경향)")]
        [SerializeField, Range(0f, 1f)] private float retreatChance = 0.85f;
        [Tooltip("후퇴해서 숙소에 머무는 시간 (초). 이 동안 살아 있으면 동료를 데려 나온다")]
        [SerializeField] private float regroupDelay = 3f;
        [SerializeField] private float newAreaRadius = 5.5f;

        private static readonly Color Talk = new(0.86f, 0.84f, 0.92f);
        private static readonly Color Alarm = new(1f, 0.45f, 0.35f);

        private float _nextShiftAt;
        private int _shiftCursor;
        private readonly HashSet<RangedEnemy> _busy = new();

        public void Setup(List<Route> r, List<RangedEnemy> res, Vector2[] spots)
        {
            routes = r; reserves = res; barracksSpots = spots;
        }

        private void Start()
        {
            _nextShiftAt = Time.time + firstShiftAt;
            foreach (var r in routes) { Watch(r.leader); Watch(r.follower); }
            foreach (var c in reserves) Watch(c);
        }

        private void Watch(RangedEnemy e)
        {
            if (e == null) return;
            var h = e.GetComponent<Combat.Health>();
            if (h != null) h.OnDeath += () => OnDied(e);
        }

        private static bool Alive(RangedEnemy e) => e != null && e.isActiveAndEnabled && !e.GetComponent<Combat.Health>().IsDead;

        private void Update()
        {
            reserves.RemoveAll(e => !Alive(e));
            if (Time.time < _nextShiftAt) return;
            _nextShiftAt = Time.time + shiftInterval;
            TryShiftChange();
        }

        // ---------- 교대 ----------
        private List<RangedEnemy> RestingReserves()
        {
            var list = new List<RangedEnemy>();
            foreach (var e in reserves) if (Alive(e) && !_busy.Contains(e) && !e.HasOrder && !e.InCombat) list.Add(e);
            return list;
        }

        private void TryShiftChange()
        {
            var rest = RestingReserves();
            if (rest.Count < 2 || routes.Count == 0) return;
            for (int k = 0; k < routes.Count; k++)
            {
                var r = routes[(_shiftCursor + k) % routes.Count];
                if (!Alive(r.leader) || !Alive(r.follower)) continue;
                if (r.leader.InCombat || r.follower.InCombat || _busy.Contains(r.leader)) continue;
                _shiftCursor = (_shiftCursor + k + 1) % routes.Count;
                StartShift(r, rest[0], rest[1]);
                return;
            }
        }

        private void StartShift(Route r, RangedEnemy a, RangedEnemy b)
        {
            reserves.Remove(a); reserves.Remove(b);
            _busy.Add(a); _busy.Add(b);
            EnemyBark.Say(a, "교대 나간다.", Talk);
            // 순찰조가 지금 있는 자리로 마중 나간다 - 거기서 넘겨받는다
            Vector2 meet = r.leader.Position;
            b.SetPatrol(null, a, -1f);
            a.SetPatrol(null, null);
            a.Order(meet, 0.75f, false, _ => Handover(r, a, b));
        }

        private void Handover(Route r, RangedEnemy a, RangedEnemy b)
        {
            _busy.Remove(a); _busy.Remove(b);
            var oldL = r.leader; var oldF = r.follower;
            if (!Alive(a)) return;
            EnemyBark.Say(a, "교대한다. 들어가 쉬어라.", Talk);
            a.SetPatrol(r.points, null);
            if (Alive(b)) b.SetPatrol(r.points, a, 1f);
            r.leader = a; r.follower = Alive(b) ? b : null;

            if (Alive(oldL))
            {
                EnemyBark.Say(oldL, "이상 없다.", Talk);
                oldL.SetPatrol(null, null);
                oldL.Order(Spot(0), 0.6f, false, e => { e.SetPatrol(null, null); if (!reserves.Contains(e)) reserves.Add(e); });
            }
            if (Alive(oldF))
            {
                oldF.SetPatrol(null, null);
                oldF.Order(Spot(1), 0.6f, false, e => { e.SetPatrol(null, null); if (!reserves.Contains(e)) reserves.Add(e); });
            }
        }

        private Vector2 Spot(int i)
        {
            if (barracksSpots == null || barracksSpots.Length == 0) return transform.position;
            return barracksSpots[Mathf.Abs(i) % barracksSpots.Length];
        }

        // ---------- 후퇴 · 증원 ----------
        private void OnDied(RangedEnemy dead)
        {
            reserves.Remove(dead);
            foreach (var r in routes)
            {
                RangedEnemy survivor = null;
                if (r.leader == dead) { survivor = r.follower; }
                else if (r.follower == dead) { survivor = r.leader; }
                else continue;

                Vector2 where = dead.Position;
                r.leader = survivor; r.follower = null;
                if (!Alive(survivor)) return;

                if (Random.value < retreatChance)
                {
                    EnemyBark.Say(survivor, "당했다! 후퇴한다!", Alarm, 0f);
                    survivor.SetPatrol(null, null);
                    _busy.Add(survivor);
                    survivor.Order(Spot(2), 1.35f, true, e => StartCoroutine(Regroup(e, where, r)));
                }
                else
                {
                    EnemyBark.Say(survivor, "이 놈이...!", Alarm, 0f);
                    survivor.SetPatrol(r.points, null);
                }
                return;
            }
        }

        private IEnumerator Regroup(RangedEnemy survivor, Vector2 where, Route r)
        {
            yield return new WaitForSeconds(regroupDelay);
            _busy.Remove(survivor);
            if (!Alive(survivor)) yield break;           // 숙소까지 쫓아와 끝냈으면 거기서 끝이다

            var rest = RestingReserves();
            var squad = new List<RangedEnemy>();
            for (int i = 0; i < rest.Count && i < 2; i++) squad.Add(rest[i]);
            foreach (var e in squad) reserves.Remove(e);

            EnemyBark.Say(survivor, squad.Count > 0 ? "일어나라! 저쪽에서 당했다!" : "...혼자라도 간다.", Alarm, 0f);
            foreach (var e in squad) EnemyBark.Say(e, "간다!", Talk, 0f);

            // 죽은 자리를 중심으로 도는 새 순찰로 - 이제 이 구역이 이 무리의 담당이다
            var loop = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                loop[i] = where + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * newAreaRadius;
            }
            survivor.SetPatrol(loop, null);
            for (int i = 0; i < squad.Count; i++) squad[i].SetPatrol(null, survivor, i == 0 ? 1f : -1f);
            survivor.Order(where, 0.85f, false, null);

            r.points = loop;
            r.leader = survivor;
            r.follower = squad.Count > 0 ? squad[0] : null;
            // 셋째는 같은 순찰로의 후미로 남는다
            if (squad.Count > 1)
            {
                var extra = new Route { name = r.name + "_증원", points = loop, leader = survivor, follower = squad[1] };
                routes.Add(extra);
            }
        }

        private void OnDrawGizmos()
        {
            if (routes == null) return;
            Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.6f);
            foreach (var r in routes)
            {
                if (r.points == null) continue;
                for (int i = 0; i < r.points.Length; i++) Gizmos.DrawLine(r.points[i], r.points[(i + 1) % r.points.Length]);
            }
            Gizmos.color = new Color(0.9f, 0.8f, 0.3f, 0.8f);
            if (barracksSpots != null) foreach (var p in barracksSpots) Gizmos.DrawWireSphere(p, 0.5f);
        }
    }
}
