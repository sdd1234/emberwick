using System;
using System.Collections.Generic;
using UnityEngine;
using Capstone.Core;

namespace Capstone.Items
{
    /// <summary>
    /// 루팅 대상 - 상자와 시체가 같이 쓴다 (기획서 6.3).
    /// 내용물이 격자 위에 놓여 있고 처음엔 전부 물음표다. 크기로만 짐작할 수 있고,
    /// 뒤지는 시간이 쌓이면 하나씩 정체가 드러난다. 좋은 물건일수록 늦게 나온다.
    /// 감정은 불을 켠 상태에서만 진행된다 - 빛을 켜면 적에게 들키므로 매번 선택을 강요한다.
    /// </summary>
    public class LootContainer : MonoBehaviour, IInteractable
    {
        [Serializable]
        public struct GradeRoll
        {
            public ItemGrade grade;
            [Tooltip("가중치. 등급 추첨의 상대 확률")]
            public float weight;
        }

        [Header("격자 (기획서 6.1)")]
        [SerializeField] private int gridColumns = 6;
        [SerializeField] private int gridRows = 4;

        [Header("감정 (기획서 6.3)")]
        [Tooltip("마지막 물건까지 드러나는 데 걸리는 시간(초)")]
        [SerializeField] private float fullSearchTime = 6f;
        [Tooltip("불을 켜야만 감정이 진행된다")]
        [SerializeField] private bool requiresLight = true;

        [Header("내용물")]
        [SerializeField] private int minItems = 2;
        [SerializeField] private int maxItems = 5;
        [SerializeField] private List<ItemDefinition> pool = new();
        [SerializeField]
        private GradeRoll[] gradeTable =
        {
            new() { grade = ItemGrade.Common,    weight = 60f },
            new() { grade = ItemGrade.Rare,      weight = 25f },
            new() { grade = ItemGrade.Unique,    weight = 12f },
            new() { grade = ItemGrade.Legendary, weight = 3f  },
        };

        [Header("표시")]
        [SerializeField] private string displayLabel = "상자";
        [SerializeField] private SpriteRenderer highlight;

        public ItemGrid Grid { get; private set; }
        public string DisplayLabel => displayLabel;
        public bool IsBeingSearched { get; private set; }
        public bool IsGenerated { get; private set; }
        public float SearchTime => _searchTime;
        public float SearchProgress => Mathf.Clamp01(_searchTime / fullSearchTime);
        public bool HasRemaining => Grid != null && Grid.Items.Count > 0;

        /// <summary>돋보기가 올라가 있는 슬롯 - 다음에 드러날 물건.</summary>
        public GridItem Revealing
        {
            get
            {
                if (Grid == null) return null;
                GridItem next = null;
                foreach (var s in Grid.Items)
                    if (!s.Identified && (next == null || s.RevealAt < next.RevealAt)) next = s;
                return next;
            }
        }

        public event Action OnContentsGenerated;
        public event Action<GridItem> OnSlotIdentified;

        private float _searchTime;

        private void Awake()
        {
            Grid = new ItemGrid(gridColumns, gridRows);
        }

        /// <summary>시체 등 외부에서 내용물을 직접 채울 때 쓴다.</summary>
        public void SetupAsCorpse(string label, IEnumerable<ItemDefinition> contents, float searchTime)
        {
            displayLabel = label;
            fullSearchTime = Mathf.Max(0.5f, searchTime);
            if (Grid == null) Grid = new ItemGrid(gridColumns, gridRows);
            Grid.Clear();

            var list = new List<ItemDefinition>(contents);
            list.Sort((a, b) => a.grade.CompareTo(b.grade));
            PlaceAll(list);
            IsGenerated = true;
        }

        // ---------- IInteractable ----------
        public string GetPrompt(GameObject interactor)
        {
            if (IsGenerated && !HasRemaining) return "비어 있음";

            if (requiresLight && !HasLight(interactor))
            {
                string tail = _searchTime > 0f ? $"   (감정 {SearchProgress * 100f:0}%)" : "";
                return $"어두워서 뒤질 수 없다 — [E] 로 불을 켜라{tail}";
            }

            if (IsBeingSearched) return "[F] 닫기";
            return _searchTime > 0f
                ? $"[F] 이어서 {displayLabel} 뒤지기 ({SearchProgress * 100f:0}%)"
                : $"[F] {displayLabel} 뒤지기";
        }

        public bool CanInteract(GameObject interactor) => !IsGenerated || HasRemaining;

        public void Interact(GameObject interactor)
        {
            if (IsGenerated && !HasRemaining) return;

            if (requiresLight && !HasLight(interactor))
            {
                IsBeingSearched = false;
                return;
            }

            if (!IsGenerated) Generate();

            IsBeingSearched = true;
            _searchTime += Time.deltaTime;

            bool revealed = false;
            foreach (var s in Grid.Items)
            {
                if (s.Identified || _searchTime < s.RevealAt) continue;
                s.Identified = true;
                revealed = true;
                OnSlotIdentified?.Invoke(s);
            }

            // 격자 자체는 안 바뀌었지만 표시는 바뀌었다.
            // 이걸 알리지 않으면 UI 가 물음표를 계속 들고 있는다.
            if (revealed) Grid.RaiseChanged();
        }

        public void StopSearching() => IsBeingSearched = false;

        /// <summary>드래그로 가져갈 때 호출. 식별 안 된 물건은 못 가져간다.</summary>
        public bool CanTake(GridItem item) => item != null && item.Identified;

        // ---------- 생성 ----------
        private void Generate()
        {
            IsGenerated = true;
            Grid.Clear();

            int count = UnityEngine.Random.Range(minItems, maxItems + 1);
            var picked = new List<ItemDefinition>();
            for (int i = 0; i < count; i++)
            {
                var item = PickWeighted();
                if (item != null) picked.Add(item);
            }

            // 낮은 등급이 먼저, 좋은 건 마지막에 드러나도록 (기획서 6.3)
            picked.Sort((a, b) => a.grade.CompareTo(b.grade));
            PlaceAll(picked);
            OnContentsGenerated?.Invoke();
        }

        private void PlaceAll(List<ItemDefinition> picked)
        {
            int placed = 0;
            for (int i = 0; i < picked.Count; i++)
            {
                var gi = new GridItem { Def = picked[i], Count = 1, Identified = false };
                if (!Grid.TryAutoPlace(gi)) continue;          // 자리가 없으면 버린다
                placed++;

                float t = picked.Count == 1 ? 0.55f : (i + 1f) / picked.Count;
                gi.RevealAt = fullSearchTime * t * 0.95f;
            }
            if (placed == 0) IsBeingSearched = false;
        }

        private ItemDefinition PickWeighted()
        {
            if (pool.Count == 0) return null;

            float total = 0f;
            foreach (var r in gradeTable) total += r.weight;
            float roll = UnityEngine.Random.Range(0f, total);

            ItemGrade grade = ItemGrade.Common;
            foreach (var r in gradeTable)
            {
                roll -= r.weight;
                if (roll <= 0f) { grade = r.grade; break; }
            }

            var matches = new List<ItemDefinition>();
            foreach (var it in pool) if (it && it.grade == grade) matches.Add(it);
            if (matches.Count == 0) matches.AddRange(pool);
            return matches.Count == 0 ? null : matches[UnityEngine.Random.Range(0, matches.Count)];
        }

        private static bool HasLight(GameObject interactor)
        {
            if (interactor == null) return false;
            var torch = interactor.GetComponent<Player.TorchFuel>();
            return torch != null && torch.IsLit;
        }
    }
}
