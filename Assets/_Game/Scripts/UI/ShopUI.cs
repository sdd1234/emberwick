using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Capstone.Core;

namespace Capstone.UI
{
    /// <summary>
    /// 마을 상점 (기획서 3 - 전리품 정산).
    /// 왼쪽에 창고 격자, 오른쪽에 판매대. 레이드에서 쓰던 드래그를 그대로 쓴다.
    /// 급할 땐 [전부 팔기] 한 방, 남겨 둘 게 있으면 하나씩 끌어다 판다.
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        [Header("조각")]
        [SerializeField] private GridPanelUI stashPanel;
        [SerializeField] private SellDropZone sellZone;
        [SerializeField] private TMP_Text goldText;
        [Tooltip("방금 판 것을 알려주는 줄")]
        [SerializeField] private TMP_Text receiptText;
        [Tooltip("창고에 남은 것의 값어치")]
        [SerializeField] private TMP_Text stashValueText;
        [SerializeField] private Button sellAllButton;

        [Header("연출")]
        [Tooltip("방금 판 것이 화면에 남아 있는 시간(초)")]
        [SerializeField] private float receiptSeconds = 2.5f;

        private float _receiptTimer;

        private void Awake()
        {
            // 인스펙터에서 안 꽂았으면 찾아 잇는다 (씬을 다시 구워도 돌아가야 한다)
            if (stashPanel == null) stashPanel = GetComponentInChildren<GridPanelUI>(true);
            if (sellZone == null) sellZone = GetComponentInChildren<SellDropZone>(true);
            if (receiptText != null) receiptText.text = "";
        }

        private void OnEnable()
        {
            // 소지금은 세션이, 창고 내용은 격자가 알린다.
            // 세션 쪽만 듣고 있으면 격자에서 직접 물건이 빠졌을 때 요약이 옛날 값으로 남는다.
            GameSession.OnChanged += Refresh;
            GameSession.Stash.OnChanged += Refresh;
            if (sellZone != null) sellZone.OnSold += HandleSold;
            if (sellAllButton != null) sellAllButton.onClick.AddListener(SellEverything);
        }

        private void OnDisable()
        {
            GameSession.OnChanged -= Refresh;
            GameSession.Stash.OnChanged -= Refresh;
            if (sellZone != null) sellZone.OnSold -= HandleSold;
            if (sellAllButton != null) sellAllButton.onClick.RemoveListener(SellEverything);
        }

        private void Start()
        {
            if (stashPanel != null) stashPanel.Bind(GameSession.Stash, "창고");
            Refresh();
        }

        private void Update()
        {
            if (_receiptTimer <= 0f) return;
            _receiptTimer -= Time.deltaTime;
            if (_receiptTimer <= 0f && receiptText != null) receiptText.text = "";
        }

        private void HandleSold(Items.GridItem item, int gained)
        {
            string name = item != null && item.Def != null ? item.Def.displayName : "물건";
            Say($"{Object(name)} 넘겼다.   <color=#E8C46A>+{gained} G</color>");
        }

        private void SellEverything()
        {
            int count = GameSession.Stash.Items.Count;
            if (count == 0) { Say("팔 것이 없다."); return; }

            int total = GameSession.SellAll(GameSession.Stash);
            Say($"{count}종을 전부 넘겼다.   <color=#E8C46A>+{total} G</color>");
        }

        /// <summary>
        /// 이름 끝 받침에 따라 목적격 조사를 고른다 ("붕대를" / "가면을").
        /// "을(를)" 로 도망가면 대사가 안내문처럼 읽힌다.
        /// </summary>
        private static string Object(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            char last = word[word.Length - 1];
            if (last < 0xAC00 || last > 0xD7A3) return word + "을(를)";   // 한글이 아니면 안전하게
            return word + (((last - 0xAC00) % 28) == 0 ? "를" : "을");
        }

        private void Say(string line)
        {
            if (receiptText == null) return;
            receiptText.text = line;
            _receiptTimer = receiptSeconds;
        }

        private void Refresh()
        {
            if (goldText != null) goldText.text = $"{GameSession.Gold:N0} G";
            if (stashValueText != null)
            {
                int count = GameSession.Stash.Items.Count;
                stashValueText.text = count == 0
                    ? "창고가 비었다"
                    : $"창고 {count}종 · 값어치 {GameSession.ValueOf(GameSession.Stash):N0} G";
            }
            if (sellAllButton != null) sellAllButton.interactable = GameSession.Stash.Items.Count > 0;
        }
    }
}
