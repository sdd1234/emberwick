using UnityEngine;

namespace Capstone.Items
{
    /// <summary>
    /// 소모품 사용 (피드백 - 붕대를 쓸 수 있게). 가방 화면에서 우클릭하거나 H 키(붕대)로 쓴다.
    /// 지금 쓸 수 있는 건 회복 · 지혈 효과가 있는 물건(붕대)뿐이다. 나머지 물건은 효과가 정해지면 붙인다.
    /// </summary>
    public static class ItemUse
    {
        public static bool IsUsable(ItemDefinition def) => def != null && (def.healAmount > 0f || def.stopsBleeding);

        /// <summary>격자의 물건 하나를 쓴다. 쓸 데가 없으면(멀쩡한데 붕대) 안 쓰고 false.</summary>
        public static bool TryUse(GridItem item, ItemGrid from, GameObject user, out string message)
        {
            message = "";
            if (item == null || item.Def == null || from == null || user == null) return false;
            var def = item.Def;
            if (!IsUsable(def)) { message = $"{def.displayName}은(는) 쓸 수 없다"; return false; }

            var health = user.GetComponent<Combat.Health>();
            var ailments = user.GetComponent<Combat.PlayerAilments>();
            bool bleeding = ailments != null && ailments.IsBleeding;
            bool hurt = health != null && health.Current < health.Max - 0.5f;
            if (!(def.stopsBleeding && bleeding) && !(def.healAmount > 0f && hurt))
            {
                message = "지금은 쓸 필요가 없다";
                return false;
            }

            if (def.stopsBleeding && ailments != null) ailments.StopBleeding();
            if (def.healAmount > 0f && health != null) health.Heal(def.healAmount);

            // 하나 덜어낸다. 더미가 비면 격자에서 뺀다
            item.Count -= 1;
            if (item.Count <= 0) from.Remove(item);
            else from.RaiseChanged();

            message = bleeding && def.stopsBleeding ? $"{def.displayName}로 피를 멎게 했다" : $"{def.displayName}을(를) 썼다";
            return true;
        }

        /// <summary>가방 → 중요 보관함 순서로 그 물건을 찾는다.</summary>
        public static bool Find(ItemDefinition def, PlayerInventory bag, out GridItem item, out ItemGrid grid)
        {
            item = null; grid = null;
            foreach (var g in new[] { bag != null ? bag.Grid : null, Core.GameSession.Secure })
            {
                if (g == null) continue;
                foreach (var it in g.Items)
                    if (it.Def == def) { item = it; grid = g; return true; }
            }
            return false;
        }

        /// <summary>그 물건이 가방 · 보관함에 몇 개 있는지.</summary>
        public static int Count(ItemDefinition def, PlayerInventory bag)
        {
            int n = 0;
            foreach (var g in new[] { bag != null ? bag.Grid : null, Core.GameSession.Secure })
            {
                if (g == null) continue;
                foreach (var it in g.Items) if (it.Def == def) n += Mathf.Max(1, it.Count);
            }
            return n;
        }
    }
}
