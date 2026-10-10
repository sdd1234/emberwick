using UnityEngine;
using Capstone.Items;

namespace Capstone.Town
{
    /// <summary>
    /// 마을 건물 하나 (피드백 - 대장간 · 잡화점 · 의료소). 하는 일은 거래뿐이다.
    /// 문 앞에 서서 F 를 누르면 거래창이 열린다. 파는 건 어느 가게든 받고, 사는 건 가게마다 다르다.
    /// </summary>
    public class TownShop : MonoBehaviour, IInteractable
    {
        [SerializeField] private string shopName = "가게";
        [TextArea, SerializeField] private string greeting = "";
        [Tooltip("이 가게가 파는 물건")]
        [SerializeField] private ItemDefinition[] stock;
        [Tooltip("사는 값 = 물건 값어치 x 이 배율")]
        [SerializeField] private float buyMarkup = 2f;

        public string ShopName => shopName;
        public string Greeting => greeting;
        public ItemDefinition[] Stock => stock;
        public int PriceOf(ItemDefinition def) => def == null ? 0 : Mathf.CeilToInt(Mathf.Max(1, def.value) * buyMarkup);

        public string GetPrompt(GameObject interactor)
            => UI.ShopUI.Current == this ? "[F] 거래 마치기" : $"[F] {shopName} 들어가기";

        public bool CanInteract(GameObject interactor) => true;

        public void Interact(GameObject interactor)
        {
            var ui = FindFirstObjectByType<UI.ShopUI>(FindObjectsInactive.Include);
            if (ui == null) return;
            if (UI.ShopUI.Current == this) ui.Close();
            else ui.Open(this);
        }
    }
}
