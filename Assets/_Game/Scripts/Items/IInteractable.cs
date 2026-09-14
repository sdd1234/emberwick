using UnityEngine;

namespace Capstone.Items
{
    /// <summary>F 키로 상호작용 가능한 대상 (기획서 8.2).</summary>
    public interface IInteractable
    {
        /// <summary>
        /// HUD 에 뜨는 안내 문구. 예) "[F] 상자 뒤지기"
        /// 상호작용자를 받는 이유는, 조건이 안 맞을 때 왜 안 되는지 알려주기 위해서다.
        /// (예: 불이 꺼져 있으면 "[E] 불을 켜야 뒤질 수 있다")
        /// </summary>
        string GetPrompt(GameObject interactor);
        bool CanInteract(GameObject interactor);
        void Interact(GameObject interactor);
    }
}
