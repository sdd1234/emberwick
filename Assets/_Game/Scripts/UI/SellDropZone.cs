using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Capstone.Core;

namespace Capstone.UI
{
    /// <summary>
    /// 상점 판매대. 창고에서 물건을 끌어다 여기에 놓으면 팔린다.
    ///
    /// GridPanelUI 를 쓰지 않는 이유: 판 물건은 어디에도 놓이지 않고 사라지므로
    /// 자리 계산도, 회전도, 겹침 검사도 필요 없다. 드롭만 받으면 된다.
    /// 끌던 블록을 지우는 건 GridItemView 쪽에서 알아서 한다 - 물건이 격자에서 빠지면
    /// 그 블록이 파괴되고, OnDisable 이 드래그 상태를 정리한다.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SellDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("판매대 바닥. 비우면 같은 오브젝트의 Image 를 쓴다")]
        [SerializeField] private Image plate;
        [SerializeField] private Color idleColor  = new(0.20f, 0.17f, 0.12f, 0.92f);
        [Tooltip("물건을 들고 올라왔을 때")]
        [SerializeField] private Color hoverColor = new(0.42f, 0.34f, 0.16f, 0.98f);

        /// <summary>(판 물건, 받은 돈)</summary>
        public event Action<Items.GridItem, int> OnSold;

        private void Awake()
        {
            if (plate == null) plate = GetComponent<Image>();
            Paint(false);
        }

        private void OnDisable() => Paint(false);

        // 끌고 있을 때만 달아오른다. 빈손으로 지나가는 건 아무 의미가 없다
        public void OnPointerEnter(PointerEventData e) => Paint(ItemDrag.IsDragging);
        public void OnPointerExit(PointerEventData e) => Paint(false);

        public void OnDrop(PointerEventData e)
        {
            Paint(false);
            if (!ItemDrag.IsDragging) return;

            var item = ItemDrag.Item;
            int gained = GameSession.Sell(item, ItemDrag.Source);
            if (gained > 0 && OnSold != null) OnSold(item, gained);
        }

        private void Paint(bool hot)
        {
            if (plate != null) plate.color = hot ? hoverColor : idleColor;
        }
    }
}
