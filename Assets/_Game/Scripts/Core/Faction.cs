using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Core
{
    /// <summary>누구 편인가 (피드백 2026-10-29 - 적들끼리도 싸운다).</summary>
    public enum Faction { Player, Cultist, Rogue, Infected }

    /// <summary>
    /// 진영 표시. 적 AI 는 플레이어만 보는 게 아니라 적대 진영도 찾아서 싸운다.
    ///   이교도 - 플레이어 · 도적 · 감염자와 싸운다 (감염자에게 대응수단이 있지만 모두가 가진 건 아니다)
    ///   도적   - 플레이어 · 이교도를 노린다. 감염자는 기본적으로 건드리지 않는다
    ///   감염자 - 들리는 것은 다 덮친다
    /// 플레이어가 어그로를 끌고 두 구역을 겹치게 하면 셋이 한데 엉킨다 - 어부지리도, 다굴도 여기서 나온다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FactionMember : MonoBehaviour
    {
        [SerializeField] private Faction faction = Faction.Cultist;

        private static readonly List<FactionMember> _all = new();
        public static IReadOnlyList<FactionMember> All => _all;

        public Faction Faction => faction;
        public Combat.Health Health { get; private set; }
        public bool Alive => Health == null || !Health.IsDead;
        public Vector2 Position => _body != null ? _body.position : (Vector2)transform.position;

        private Rigidbody2D _body;

        public void SetFaction(Faction f) => faction = f;

        private void Awake()
        {
            Health = GetComponent<Combat.Health>();
            _body = GetComponent<Rigidbody2D>();
        }

        private void OnEnable() => _all.Add(this);
        private void OnDisable() => _all.Remove(this);

        public static bool Hostile(Faction a, Faction b)
        {
            if (a == b) return false;
            switch (a)
            {
                case Faction.Cultist:  return true;                               // 모두와 싸운다
                case Faction.Rogue:    return b == Faction.Player || b == Faction.Cultist;
                case Faction.Infected: return true;                               // 들리면 덮친다
                case Faction.Player:   return true;
            }
            return false;
        }

        public bool IsHostileTo(FactionMember other) => other != null && Hostile(faction, other.faction);
    }
}
