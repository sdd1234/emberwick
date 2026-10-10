using UnityEngine;
using Capstone.Core;

namespace Capstone.Items
{
    /// <summary>아이템 원본 데이터. Create > Capstone > Item Definition 으로 만든다.</summary>
    [CreateAssetMenu(menuName = "Capstone/Item Definition", fileName = "Item_")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("표시")]
        public string displayName = "이름 없는 물건";
        [TextArea] public string description;
        public Sprite icon;

        [Header("등급")]
        public ItemGrade grade = ItemGrade.Common;

        [Header("그리드 인벤토리 (기획서 6.1)")]
        [Tooltip("차지하는 칸 너비")]
        public int gridWidth = 1;
        [Tooltip("차지하는 칸 높이")]
        public int gridHeight = 1;

        [Header("값")]
        public int value = 10;
        public float weight = 0.5f;
        [Tooltip("여러 개가 한 칸에 쌓이는 아이템인지")]
        public bool stackable;
        public int maxStack = 1;

        [Header("사용 효과 (소모품)")]
        [Tooltip("쓰면 회복하는 체력. 0 이면 회복 효과 없음")]
        public float healAmount = 0f;
        [Tooltip("쓰면 출혈이 멎는다 (붕대)")]
        public bool stopsBleeding = false;

        public Color GradeColor => grade.ToColor();
    }
}
