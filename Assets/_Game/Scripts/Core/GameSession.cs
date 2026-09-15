using System;
using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Core
{
    /// <summary>
    /// 한 번의 실행 동안만 살아 있는 세션 상태 - 창고와 소지금 (기획서 3 - 코어 루프의 마을 구간).
    ///
    /// 레이드는 씬을 통째로 다시 열어 초기화하므로(RaidManager 참고), 판이 끝나도 남아야 하는 것은
    /// 씬 바깥에 둬야 한다. MonoBehaviour 가 아니라 static 인 이유가 둘 있다.
    /// 하나는 이 프로젝트가 "코드로 GameObject 를 만들지 않는다"를 지키고 있다는 것(전부 프리팹),
    /// 다른 하나는 씬에 얹을 것이 애초에 없다는 것 - 그릴 것도 움직일 것도 없는 순수 데이터다.
    /// 저장은 하지 않는다. 플레이를 멈추면 도메인 리로드와 함께 사라진다.
    /// </summary>
    public static class GameSession
    {
        /// <summary>창고 격자. 가방(8x6)보다 넉넉해야 여러 판을 털어 와도 자리가 남는다.</summary>
        public const int StashColumns = 12;
        public const int StashRows = 8;

        private static Items.ItemGrid _stash;

        public static Items.ItemGrid Stash
        {
            get
            {
                if (_stash == null) _stash = new Items.ItemGrid(StashColumns, StashRows);
                return _stash;
            }
        }

        public static int Gold { get; private set; }

        // ---------- 마지막 레이드 결과 (마을 머리말에 쓴다) ----------
        public static bool HasRaided { get; private set; }
        public static bool LastRaidSurvived { get; private set; }
        /// <summary>창고에 들어간 종수.</summary>
        public static int LastRaidHaul { get; private set; }
        /// <summary>자리가 없거나 죽어서 잃은 종수.</summary>
        public static int LastRaidLost { get; private set; }
        /// <summary>들여온 것의 값어치 합 (판 것이 아니라 가져온 것).</summary>
        public static int LastRaidValue { get; private set; }

        public static event Action OnChanged;

        /// <summary>
        /// 레이드 한 판을 끝낸다.
        /// 살아 나왔으면 가방에 든 것이 창고로 옮겨지고, 죽었으면 전부 사라진다.
        /// 어느 쪽이든 가방은 비워진다 - 다음 판은 맨몸으로 시작한다.
        /// </summary>
        public static void FinishRaid(bool survived, Items.ItemGrid bag)
        {
            HasRaided = true;
            LastRaidSurvived = survived;
            LastRaidHaul = 0;
            LastRaidLost = 0;
            LastRaidValue = 0;

            if (bag == null) { OnChanged?.Invoke(); return; }

            if (!survived)
            {
                LastRaidLost = bag.Items.Count;
                bag.Clear();
                OnChanged?.Invoke();
                return;
            }

            // Remove 가 목록을 건드리므로 복사본을 돌아야 한다
            var carried = new List<Items.GridItem>(bag.Items);
            for (int i = 0; i < carried.Count; i++)
            {
                var it = carried[i];
                bag.Remove(it);
                if (it.Def == null) continue;

                it.Identified = true;               // 감정 안 된 채로 들고 나왔어도 창고에서는 보인다
                it.Rotated = false;
                if (Stash.TryAutoPlace(it))
                {
                    LastRaidHaul++;
                    LastRaidValue += Mathf.Max(0, it.Def.value) * Mathf.Max(1, it.Count);
                }
                else
                {
                    LastRaidLost++;                 // 창고가 꽉 찼다
                }
            }
            OnChanged?.Invoke();
        }

        /// <summary>물건 하나를 판다. 판 값을 돌려준다 (못 팔면 0).</summary>
        public static int Sell(Items.GridItem item, Items.ItemGrid from)
        {
            if (item == null || item.Def == null || from == null) return 0;
            if (!from.Remove(item)) return 0;

            int gained = Mathf.Max(0, item.Def.value) * Mathf.Max(1, item.Count);
            Gold += gained;
            OnChanged?.Invoke();
            return gained;
        }

        /// <summary>격자에 있는 것을 전부 판다. 총액을 돌려준다.</summary>
        public static int SellAll(Items.ItemGrid grid)
        {
            if (grid == null) return 0;

            var all = new List<Items.GridItem>(grid.Items);
            int total = 0;
            for (int i = 0; i < all.Count; i++) total += Sell(all[i], grid);
            return total;
        }

        public static int ValueOf(Items.ItemGrid grid)
        {
            if (grid == null) return 0;
            int total = 0;
            foreach (var it in grid.Items)
                if (it.Def != null) total += Mathf.Max(0, it.Def.value) * Mathf.Max(1, it.Count);
            return total;
        }

        /// <summary>처음부터 다시. 에디터 메뉴에서 부른다.</summary>
        public static void ResetAll()
        {
            // 새 격자로 갈아끼우지 않고 비우기만 한다.
            // 갈아끼우면 이미 이 격자의 OnChanged 를 듣고 있는 화면이 옛 격자에 묶인 채 남는다.
            if (_stash != null) _stash.Clear();
            Gold = 0;
            HasRaided = false;
            LastRaidSurvived = false;
            LastRaidHaul = LastRaidLost = LastRaidValue = 0;
            OnChanged?.Invoke();
        }
    }
}
