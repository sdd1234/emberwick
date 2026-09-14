using UnityEngine;

namespace Capstone.Items
{
    /// <summary>
    /// 격자 위에 놓인 물건 하나. 가방이든 상자든 시체든 전부 이걸 쓴다.
    /// 같은 타입을 공유해야 드래그로 서로 주고받을 수 있다.
    /// </summary>
    public class GridItem
    {
        public ItemDefinition Def;
        public int X, Y;                 // 좌상단 셀 (기획서 6.1 - 회전 기준점은 좌측 상단)
        public bool Rotated;             // T 키로 90도 회전
        public int Count = 1;

        /// <summary>감정 전에는 물음표로 뜬다 (상자/시체에서만 의미 있음).</summary>
        public bool Identified = true;
        /// <summary>감정이 끝나 정체가 드러나는 시점 (초).</summary>
        public float RevealAt;

        public int W => Rotated ? Mathf.Max(1, Def.gridHeight) : Mathf.Max(1, Def.gridWidth);
        public int H => Rotated ? Mathf.Max(1, Def.gridWidth)  : Mathf.Max(1, Def.gridHeight);

        public float Weight => Def != null ? Def.weight * Count : 0f;
    }
}
