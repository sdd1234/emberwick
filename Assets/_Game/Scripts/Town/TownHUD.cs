using UnityEngine;
using TMPro;
using Capstone.Core;

namespace Capstone.Town
{
    /// <summary>마을 화면 위 글자 - 상호작용 안내와 소지금.</summary>
    public class TownHUD : MonoBehaviour
    {
        [SerializeField] private Items.Interactor interactor;
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private TMP_Text goldText;

        private void Awake()
        {
            if (!interactor) interactor = FindFirstObjectByType<Items.Interactor>();
        }

        private void OnEnable() => GameSession.OnChanged += RefreshGold;
        private void OnDisable() => GameSession.OnChanged -= RefreshGold;
        private void Start() => RefreshGold();

        private void Update()
        {
            if (promptText == null) return;
            string p = interactor != null ? interactor.CurrentPrompt : "";
            if (promptText.text != p) promptText.text = p;
        }

        private void RefreshGold()
        {
            if (goldText) goldText.text = $"{GameSession.Gold:N0} G";
        }
    }
}
