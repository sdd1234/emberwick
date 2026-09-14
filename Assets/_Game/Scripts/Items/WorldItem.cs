using UnityEngine;
using Capstone.Core;

namespace Capstone.Items
{
    /// <summary>바닥에 떨어진 아이템. F 로 줍는다.</summary>
    public class WorldItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int count = 1;
        [SerializeField] private SpriteRenderer spriteRenderer;

        public ItemDefinition Item => item;

        private void Awake()
        {
            if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            EnsureTrigger();
            Refresh();
        }

        /// <summary>프리팹을 깔아둔 뒤 어떤 물건인지 정해줄 때 쓴다.</summary>
        public void Setup(ItemDefinition definition, int amount)
        {
            item = definition;
            count = amount;
            if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            EnsureTrigger();
            Refresh();
        }

        /// <summary>프리팹에 들어 있는 콜라이더가 트리거인지만 확인한다.</summary>
        private void EnsureTrigger()
        {
            var col = GetComponent<CircleCollider2D>();
            if (col) col.isTrigger = true;
        }

        private void Refresh()
        {
            if (!spriteRenderer || !item) return;
            if (item.icon) spriteRenderer.sprite = item.icon;
            spriteRenderer.color = item.GradeColor;          // 등급을 색으로 구분
            gameObject.name = $"Item_{item.displayName}";
        }

        public string GetPrompt(GameObject interactor)
            => item ? $"[F] {item.displayName} 줍기 ({item.grade.ToKorean()})" : "[F] 줍기";

        public bool CanInteract(GameObject interactor)
        {
            if (!item) return false;
            var inv = interactor.GetComponent<PlayerInventory>();
            return inv != null && inv.CanFit(item, count);
        }

        public void Interact(GameObject interactor)
        {
            var inv = interactor.GetComponent<PlayerInventory>();
            if (inv == null || !inv.TryAdd(item, count)) return;
            Destroy(gameObject);
        }
    }
}
