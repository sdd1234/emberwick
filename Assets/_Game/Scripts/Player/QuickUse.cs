using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Player
{
    /// <summary>
    /// 단축키 사용 - H 로 붕대를 감는다 (가방 → 중요 보관함 순서로 찾는다).
    /// 가방 화면을 열 필요 없이 출혈을 바로 멎게 하려고 둔다. 가방 화면에서는 우클릭으로도 쓸 수 있다.
    /// </summary>
    public class QuickUse : MonoBehaviour
    {
        [SerializeField] private Items.ItemDefinition bandage;
        [SerializeField] private Items.PlayerInventory inventory;

        public Items.ItemDefinition Bandage => bandage;

        private void Awake()
        {
            if (!inventory) inventory = GetComponent<Items.PlayerInventory>();
        }

        private void Update()
        {
            if (bandage == null || !Core.KeyBindings.Pressed(Core.GameAction.Bandage) || UI.KeyBindPanel.BlocksInput) return;
            var pc = GetComponent<PlayerController>();
            if (pc != null && !pc.enabled) return;

            if (!Items.ItemUse.Find(bandage, inventory, out var item, out var grid))
            {
                UI.HUDController.Toast("붕대가 없다");
                return;
            }
            Items.ItemUse.TryUse(item, grid, gameObject, out var msg);
            UI.HUDController.Toast(msg);
        }
    }
}
