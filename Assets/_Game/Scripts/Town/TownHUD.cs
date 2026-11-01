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
        [Tooltip("소지금 뒤 판자 - 글자 폭에 맞춰 늘인다 (맨 글씨는 뒤 상자 그림에 묻혔다)")]
        [SerializeField] private RectTransform goldPlate;
        [SerializeField] private float goldPad = 24f;

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
            if (goldText == null) return;
            goldText.text = $"{GameSession.Gold:N0} G";
            if (goldPlate)
            {
                float w = goldText.GetPreferredValues(goldText.text).x + goldPad * 2f;
                goldPlate.sizeDelta = new Vector2(Mathf.Ceil(w / 4f) * 4f, goldPlate.sizeDelta.y);   // 4 단위 - 판자 테두리 도트가 반 칸에 걸리지 않게
            }
        }
    }
}
