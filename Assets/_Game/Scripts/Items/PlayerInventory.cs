using System;
using UnityEngine;

namespace Capstone.Items
{
    /// <summary>
    /// 플레이어 가방 (기획서 6.1).
    /// 테트리스형 격자 위에 물건을 놓고, 무게와 공간 두 축으로 제약한다.
    /// 실제 배치 로직은 ItemGrid 가 맡는다 - 상자·시체와 같은 모델을 써야 드래그로 오갈 수 있다.
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        [Header("격자")]
        [SerializeField] private int columns = 8;
        [SerializeField] private int rows = 6;

        [Header("무게")]
        [SerializeField] private float maxWeight = 30f;

        public ItemGrid Grid { get; private set; }

        public float CurrentWeight => Grid.CurrentWeight;
        public float MaxWeight => maxWeight;
        public int Columns => columns;
        public int Rows => rows;

        public event Action OnChanged;

        private void Awake()
        {
            Grid = new ItemGrid(columns, rows, maxWeight);
            Grid.OnChanged += () => OnChanged?.Invoke();
        }

        public bool CanFit(ItemDefinition item, int count = 1)
        {
            if (item == null) return false;
            var probe = new GridItem { Def = item, Count = count };
            if (Grid.WouldExceedWeight(probe)) return false;

            for (int pass = 0; pass < 2; pass++)
            {
                bool rot = pass == 1;
                for (int y = 0; y < Grid.Rows; y++)
                for (int x = 0; x < Grid.Columns; x++)
                    if (Grid.CanPlace(probe, x, y, rot)) return true;
            }
            return false;
        }

        /// <summary>줍기 등에서 쓰는 간편 추가. 자리를 알아서 찾는다.</summary>
        public bool TryAdd(ItemDefinition item, int count = 1)
        {
            if (item == null) return false;

            // 쌓이는 물건이면 기존 더미에 먼저 얹는다
            if (item.stackable)
            {
                foreach (var g in Grid.Items)
                {
                    if (g.Def != item || g.Count >= item.maxStack) continue;
                    int room = item.maxStack - g.Count;
                    int put = Mathf.Min(room, count);
                    g.Count += put; count -= put;
                    if (count <= 0) { Grid.RaiseChanged(); return true; }
                }
            }

            return Grid.TryAutoPlace(new GridItem { Def = item, Count = count });
        }

        public bool Remove(GridItem item) => Grid.Remove(item);

        /// <summary>기획서 6.2 - 등급 순 정렬 (전설 > 유니크 > 레어 > 일반).</summary>
        public void SortByGrade() => Grid.SortByGrade();
    }
}
