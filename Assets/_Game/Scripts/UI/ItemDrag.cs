using UnityEngine;

namespace Capstone.UI
{
    /// <summary>
    /// 지금 끌고 있는 물건에 대한 전역 상태.
    /// 드롭을 받는 쪽(격자 패널)이 "무엇이 어디서 왔는지" 알아야 하는데,
    /// 이벤트 인자로는 그걸 넘길 수 없어서 한 곳에 모아 둔다.
    /// </summary>
    public static class ItemDrag
    {
        public static Items.GridItem Item;
        public static Items.ItemGrid Source;
        public static GridPanelUI SourcePanel;
        /// <summary>물건의 어느 칸을 잡았는지. 놓을 때 이만큼 되돌려 맞춘다.</summary>
        public static int GrabOffsetX, GrabOffsetY;
        /// <summary>드래그 중 T 로 뒤집은 상태.</summary>
        public static bool Rotated;

        public static bool IsDragging => Item != null;

        public static void Begin(Items.GridItem item, Items.ItemGrid source, GridPanelUI panel, int offX, int offY)
        {
            Item = item; Source = source; SourcePanel = panel;
            GrabOffsetX = offX; GrabOffsetY = offY;
            Rotated = item.Rotated;
        }

        /// <summary>
        /// 회전을 반영한 현재 크기(w,h)와, 그 안에서 실제로 붙잡고 있는 칸(gx,gy).
        /// 돌리면 가로세로가 뒤바뀌어 잡은 칸이 범위 밖으로 나갈 수 있으므로 여기서 한 번 조인다.
        /// 미리보기 · 커서 블록 · 실제 드롭이 전부 이 값을 써야 세 자리가 어긋나지 않는다.
        /// </summary>
        public static void GrabbedCell(out int w, out int h, out int gx, out int gy)
        {
            w = h = 1; gx = gy = 0;
            if (Item == null || Item.Def == null) return;

            w = Mathf.Max(1, Rotated ? Item.Def.gridHeight : Item.Def.gridWidth);
            h = Mathf.Max(1, Rotated ? Item.Def.gridWidth  : Item.Def.gridHeight);
            gx = Mathf.Clamp(GrabOffsetX, 0, w - 1);
            gy = Mathf.Clamp(GrabOffsetY, 0, h - 1);
        }

        public static void Clear()
        {
            Item = null; Source = null; SourcePanel = null;
            GrabOffsetX = GrabOffsetY = 0; Rotated = false;
        }
    }
}
