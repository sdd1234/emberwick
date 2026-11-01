using UnityEngine;

namespace Capstone.Map
{
    /// <summary>
    /// 최종 스테이지로 가는 길을 막은 가시덤불 (피드백 2026-10-29).
    /// 감염자들이 모여 있는 곳 너머에 있다. 지금은 못 지나간다 - 3번째 지역에서 얻는 연금술 레시피가 있어야 걷힌다.
    /// 첫 맵에서 최종 지역 입구를 보여 주는 오마주 (타르코프의 첫 맵 → 최종 맵).
    /// </summary>
    public class ThornGate : MonoBehaviour, Items.IInteractable
    {
        [SerializeField] private string message = "가시덤불이 길을 막고 있다 - 특별한 연금술이 있어야 걷어낼 수 있다";

        public string GetPrompt(GameObject who) => "[F] 가시덤불 살펴보기";
        public bool CanInteract(GameObject who) => true;
        public void Interact(GameObject who) => UI.HUDController.Toast(message);
    }
}
