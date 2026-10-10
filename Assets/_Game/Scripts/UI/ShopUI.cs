using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Capstone.Core;

namespace Capstone.UI
{
    /// <summary>
    /// 마을 거래창 (피드백 - 건물별로 거래하는 상호작용만).
    /// 대장간 · 잡화점 · 의료소 문 앞에서 F 를 누르면 그 가게 이름으로 열린다.
    ///   왼쪽  - 창고 격자와 중요 보관함(3x3). 보관함에 넣어 둔 것만 레이드에 들고 들어간다
    ///   오른쪽 - 판매대(끌어다 놓으면 팔린다) · 전부 팔기 · 그 가게가 파는 물건 목록
    /// 창이 열려 있는 동안은 캐릭터가 움직이지 않는다.
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        /// <summary>지금 거래창이 열려 있는 가게. 닫혀 있으면 null.</summary>
        public static Town.TownShop Current { get; private set; }

        [Header("창")]
        [SerializeField] private GameObject window;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text greetingText;
        [SerializeField] private Button closeButton;

        [Header("격자")]
        [SerializeField] private GridPanelUI stashPanel;
        [SerializeField] private GridPanelUI securePanel;

        [Header("판매")]
        [SerializeField] private SellDropZone sellZone;
        [Tooltip("방금 거래한 것을 알려주는 줄")]
        [SerializeField] private TMP_Text receiptText;
        [Tooltip("창고에 남은 것의 값어치")]
        [SerializeField] private TMP_Text stashValueText;
        [SerializeField] private Button sellAllButton;

        [Header("구매")]
        [Tooltip("파는 물건 줄이 쌓이는 자리 (VerticalLayoutGroup)")]
        [SerializeField] private RectTransform buyList;
        [Tooltip("물건 한 줄. 자식에 Icon(Image) · Name · Price(TMP) 가 있는 버튼")]
        [SerializeField] private Button buyRowPrefab;

        [Header("연출")]
        [Tooltip("방금 거래한 것이 화면에 남아 있는 시간(초)")]
        [SerializeField] private float receiptSeconds = 2.5f;

        private float _receiptTimer;
        private readonly List<Button> _rows = new();
        private Player.PlayerController _player;

        private void Awake()
        {
            if (receiptText != null) receiptText.text = "";
            if (window != null) window.SetActive(false);
            Current = null;
        }

        private void OnEnable()
        {
            // 소지금은 세션이, 창고 내용은 격자가 알린다.
            // 세션 쪽만 듣고 있으면 격자에서 직접 물건이 빠졌을 때 요약이 옛날 값으로 남는다.
            GameSession.OnChanged += Refresh;
            GameSession.Stash.OnChanged += Refresh;
            if (sellZone != null) sellZone.OnSold += HandleSold;
            if (sellAllButton != null) sellAllButton.onClick.AddListener(SellEverything);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            GameSession.OnChanged -= Refresh;
            GameSession.Stash.OnChanged -= Refresh;
            if (sellZone != null) sellZone.OnSold -= HandleSold;
            if (sellAllButton != null) sellAllButton.onClick.RemoveListener(SellEverything);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }

        private void Update()
        {
            if (Current != null)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            }

            if (_receiptTimer <= 0f) return;
            _receiptTimer -= Time.deltaTime;
            if (_receiptTimer <= 0f && receiptText != null) receiptText.text = "";
        }

        // ---------- 열고 닫기 ----------
        public void Open(Town.TownShop shop)
        {
            if (shop == null) return;
            Current = shop;
            if (window != null) window.SetActive(true);
            if (titleText != null) titleText.text = shop.ShopName;
            if (greetingText != null) greetingText.text = shop.Greeting;

            if (stashPanel != null) stashPanel.Bind(GameSession.Stash, "창고");
            if (securePanel != null) securePanel.Bind(GameSession.Secure, "중요 보관함");
            BuildBuyList(shop);
            Refresh();

            LockPlayer(true);
            Cursor.visible = true;
        }

        public void Close()
        {
            if (Current == null) return;
            if (ItemDrag.IsDragging) return;          // 끌던 블록이 화면에 남지 않도록 손을 뗄 때까지 기다린다
            Current = null;
            if (stashPanel != null) stashPanel.Unbind();
            if (securePanel != null) securePanel.Unbind();
            ItemTooltip.Hide();
            if (window != null) window.SetActive(false);
            LockPlayer(false);
        }

        private void LockPlayer(bool locked)
        {
            if (_player == null) _player = FindFirstObjectByType<Player.PlayerController>();
            if (_player == null) return;
            _player.enabled = !locked;
            var body = _player.GetComponent<Rigidbody2D>();
            if (body != null && locked) body.linearVelocity = Vector2.zero;
        }

        // ---------- 구매 ----------
        private void BuildBuyList(Town.TownShop shop)
        {
            foreach (var b in _rows) if (b) Destroy(b.gameObject);
            _rows.Clear();
            if (buyList == null || buyRowPrefab == null || shop.Stock == null) return;

            foreach (var def in shop.Stock)
            {
                if (def == null) continue;
                var row = Instantiate(buyRowPrefab, buyList);
                row.gameObject.SetActive(true);
                row.name = $"Buy_{def.name}";
                int price = shop.PriceOf(def);

                var icon = row.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null) { icon.sprite = def.icon; icon.enabled = def.icon != null; icon.color = Color.white; }
                var label = row.transform.Find("Name")?.GetComponent<TMP_Text>();
                if (label != null) { label.text = def.displayName; label.color = def.grade.ToColor(); }
                var priceText = row.transform.Find("Price")?.GetComponent<TMP_Text>();
                if (priceText != null) priceText.text = $"{price:N0} G";

                var captured = def;
                row.onClick.AddListener(() => TryBuy(captured, price));
                _rows.Add(row);
            }
        }

        private void TryBuy(Items.ItemDefinition def, int price)
        {
            if (GameSession.Gold < price) { Say($"돈이 모자란다.  ({price:N0} G)"); return; }
            if (!GameSession.Buy(def, price)) { Say("창고에 자리가 없다."); return; }
            Say($"{Object(def.displayName)} 샀다.   <color=#E8C46A>-{price:N0} G</color>");
        }

        // ---------- 판매 ----------
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
            if (stashValueText != null)
            {
                int count = GameSession.Stash.Items.Count;
                stashValueText.text = count == 0
                    ? "창고가 비었다"
                    : $"창고 {count}종 · 값어치 {GameSession.ValueOf(GameSession.Stash):N0} G";
            }
            if (sellAllButton != null) sellAllButton.interactable = GameSession.Stash.Items.Count > 0;
            if (Current != null)
                foreach (var row in _rows)
                    if (row) row.interactable = true;
        }
    }
}
