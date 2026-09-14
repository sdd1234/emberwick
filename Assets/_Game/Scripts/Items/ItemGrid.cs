using System;
using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Items
{
    /// <summary>
    /// 테트리스형 격자 보관함 (기획서 6.1).
    /// 가방 · 상자 · 시체가 전부 이 모델을 공유하므로 드래그 한 번으로 서로 옮길 수 있다.
    /// MonoBehaviour 가 아니라 순수 데이터라서 테스트하기도 쉽다.
    /// </summary>
    [Serializable]
    public class ItemGrid
    {
        public int Columns { get; private set; }
        public int Rows { get; private set; }

        /// <summary>0 이하면 무게 제한 없음.</summary>
        public float MaxWeight { get; set; }

        private readonly List<GridItem> _items = new();
        private GridItem[,] _cells;

        public IReadOnlyList<GridItem> Items => _items;

        public float CurrentWeight
        {
            get { float w = 0f; foreach (var i in _items) w += i.Weight; return w; }
        }

        public event Action OnChanged;

        public ItemGrid(int columns, int rows, float maxWeight = 0f)
        {
            Resize(columns, rows);
            MaxWeight = maxWeight;
        }

        public void Resize(int columns, int rows)
        {
            Columns = Mathf.Max(1, columns);
            Rows = Mathf.Max(1, rows);
            _cells = new GridItem[Columns, Rows];
            _items.Clear();
        }

        // ---------- 조회 ----------
        public GridItem At(int x, int y)
            => (x < 0 || y < 0 || x >= Columns || y >= Rows) ? null : _cells[x, y];

        /// <summary>ignore 를 제외하고 그 자리에 놓을 수 있는지. 드래그 중 미리보기에 쓴다.</summary>
        public bool CanPlace(GridItem item, int x, int y, bool rotated, GridItem ignore = null)
        {
            if (item == null) return false;

            int w = rotated ? Mathf.Max(1, item.Def.gridHeight) : Mathf.Max(1, item.Def.gridWidth);
            int h = rotated ? Mathf.Max(1, item.Def.gridWidth)  : Mathf.Max(1, item.Def.gridHeight);

            if (x < 0 || y < 0 || x + w > Columns || y + h > Rows) return false;

            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                var occupant = _cells[x + dx, y + dy];
                if (occupant != null && occupant != ignore) return false;
            }
            return true;
        }

        public bool WouldExceedWeight(GridItem item, GridItem ignore = null)
        {
            if (MaxWeight <= 0f) return false;
            float w = CurrentWeight - (ignore != null ? ignore.Weight : 0f);
            return w + item.Weight > MaxWeight;
        }

        // ---------- 배치 ----------
        public bool Place(GridItem item, int x, int y, bool rotated)
        {
            if (!CanPlace(item, x, y, rotated)) return false;
            if (WouldExceedWeight(item)) return false;

            item.X = x; item.Y = y; item.Rotated = rotated;
            Stamp(item, item);
            _items.Add(item);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>빈 자리를 알아서 찾아 넣는다. 안 들어가면 90도 돌려서 한 번 더 시도한다.</summary>
        public bool TryAutoPlace(GridItem item)
        {
            if (item == null || WouldExceedWeight(item)) return false;

            for (int pass = 0; pass < 2; pass++)
            {
                bool rot = pass == 1;
                for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                    if (CanPlace(item, x, y, rot)) return Place(item, x, y, rot);
            }
            return false;
        }

        public bool TryAutoPlace(ItemDefinition def, int count = 1)
            => TryAutoPlace(new GridItem { Def = def, Count = count });

        /// <summary>다른 격자에서 이 격자로 옮긴다. 실패하면 원래 자리에 그대로 남는다.</summary>
        public bool MoveFrom(ItemGrid source, GridItem item, int x, int y, bool rotated)
        {
            if (source == null || item == null) return false;

            bool sameGrid = ReferenceEquals(source, this);
            var ignore = sameGrid ? item : null;

            if (!CanPlace(item, x, y, rotated, ignore)) return false;
            if (!sameGrid && WouldExceedWeight(item)) return false;

            source.Remove(item);
            return Place(item, x, y, rotated);
        }

        public bool Remove(GridItem item)
        {
            if (item == null || !_items.Remove(item)) return false;
            Stamp(item, null);
            OnChanged?.Invoke();
            return true;
        }

        public void Clear()
        {
            _items.Clear();
            _cells = new GridItem[Columns, Rows];
            OnChanged?.Invoke();
        }

        /// <summary>기획서 6.2 - 등급 순으로 좌측 상단부터 다시 채운다.</summary>
        public void SortByGrade()
        {
            var all = new List<GridItem>(_items);
            all.Sort((a, b) =>
            {
                int g = b.Def.grade.CompareTo(a.Def.grade);
                if (g != 0) return g;
                int size = (b.W * b.H).CompareTo(a.W * a.H);          // 큰 것부터 채워야 빈틈이 준다
                return size != 0 ? size
                     : string.Compare(a.Def.displayName, b.Def.displayName, StringComparison.Ordinal);
            });

            Clear();
            foreach (var it in all) { it.Rotated = false; TryAutoPlace(it); }
        }

        /// <summary>셀 점유표를 갱신한다. value 가 null 이면 비운다.</summary>
        private void Stamp(GridItem item, GridItem value)
        {
            for (int dy = 0; dy < item.H; dy++)
            for (int dx = 0; dx < item.W; dx++)
            {
                int cx = item.X + dx, cy = item.Y + dy;
                if (cx >= 0 && cy >= 0 && cx < Columns && cy < Rows) _cells[cx, cy] = value;
            }
        }

        public void RaiseChanged() => OnChanged?.Invoke();
    }
}
