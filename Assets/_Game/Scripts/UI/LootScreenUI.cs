using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Capstone.Items;

namespace Capstone.UI
{
    /// <summary>
    /// 루팅 화면 (기획서 6.1 / 6.2 / 6.3).
    /// 왼쪽에 털 대상(상자 · 시체), 오른쪽에 내 가방을 나란히 놓고 드래그로 주고받는다.
    /// Tab 을 누르면 가방만 단독으로 열린다 (기획서 8.2 - 필드에서는 인벤토리만).
    /// </summary>
    public class LootScreenUI : MonoBehaviour
    {
        [Header("대상")]
        [SerializeField] private Interactor interactor;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private Combat.Crossbow crossbow;

        [Header("돋보기 (기획서 6.3)")]
        [Tooltip("물건 위를 훑고 도는 속도 (초당 각도)")]
        [SerializeField] private float magnifierOrbitSpeed = 200f;
        [Tooltip("체크 해제하면 반시계 방향 (기획서 원문은 반시계)")]
        [SerializeField] private bool orbitClockwise = true;
        [SerializeField] private float magnifierSize = 52f;
        [Tooltip("물건 중심에서 돋보기가 도는 반경 (px). 물건보다 크면 자동으로 줄어든다")]
        [SerializeField] private float orbitRadius = 16f;
        [Tooltip("훑으면서 손목이 까딱이는 정도 (도). 0 이면 완전히 고정")]
        [SerializeField] private float wobbleDegrees = 7f;

        [Header("프리팹 조각")]
        [Tooltip("루팅 화면 전체를 덮는 판")]
        [SerializeField] private RectTransform root;
        [Tooltip("왼쪽 - 털 대상")]
        [SerializeField] private RectTransform lootSide;
        [SerializeField] private GridPanelUI lootPanel;
        [Tooltip("오른쪽 - 내 가방")]
        [SerializeField] private GridPanelUI bagPanel;
        [SerializeField] private RectTransform progressBar;
        [SerializeField] private Image progressFill;
        [Tooltip("물건 위를 훑는 돋보기")]
        [SerializeField] private RectTransform magnifier;
        [SerializeField] private TMP_Text weightText;
        [SerializeField] private TMP_Text hintText;

        /// <summary>어떤 격자 화면이든 열려 있으면 true. 조준점과 사격을 끄는 데 쓴다.</summary>
        public static bool AnyScreenOpen { get; private set; }

        private LootContainer _open;
        private bool _bagOnly;
        private float _spin;

        private void Awake()
        {
            if (!interactor) interactor = FindFirstObjectByType<Interactor>();
            if (!inventory && interactor) inventory = interactor.GetComponent<PlayerInventory>();
            if (!crossbow && interactor) crossbow = interactor.GetComponent<Combat.Crossbow>();
            BindPieces();
            SetOpen(false);
        }

        private void OnDisable() => AnyScreenOpen = false;

        private void Update()
        {
            var searching = interactor != null ? interactor.Searching : null;
            var kb = Keyboard.current;

            // ESC - 열려 있는 격자 화면은 뭐든 닫는다.
            // 뒤지던 중이라면 Interactor 쪽 상태까지 풀어야 다음 프레임에 다시 열리지 않는다.
            if (kb != null && kb.escapeKey.wasPressedThisFrame && AnyScreenOpen)
            {
                if (interactor != null && interactor.Searching != null) interactor.StopSearching();
                _open = null;
                CloseAll();
                return;
            }

            // Tab - 가방만 열고 닫기 (기획서 8.2)
            if (kb != null && kb.tabKey.wasPressedThisFrame && searching == null)
            {
                if (_bagOnly) CloseAll();
                else OpenBagOnly();
            }

            // 상자/시체를 뒤지기 시작하면 가방 단독 화면은 밀어낸다
            if (searching != null && searching != _open) OpenWithContainer(searching);
            else if (searching == null && _open != null) { _open = null; CloseAll(); }

            if (!AnyScreenOpen) return;

            if (_open != null)
            {
                if (progressFill) progressFill.fillAmount = _open.SearchProgress;
                UpdateMagnifier();
            }

            if (weightText && inventory != null)
                weightText.text = $"{inventory.CurrentWeight:0.0} / {inventory.MaxWeight:0} kg";
        }

        // ---------- 열고 닫기 ----------
        private void OpenWithContainer(LootContainer box)
        {
            _open = box; _bagOnly = false;
            SetOpen(true);
            if (lootSide) lootSide.gameObject.SetActive(true);
            if (progressBar) progressBar.gameObject.SetActive(true);

            lootPanel.Bind(box.Grid, box.DisplayLabel, box);
            bagPanel.Bind(inventory.Grid, "가방");
            if (hintText) hintText.text = "드래그로 옮기기   ·   들고 [R] 90도 회전   ·   [F] / [ESC] 닫기";
        }

        private void OpenBagOnly()
        {
            _open = null; _bagOnly = true;
            SetOpen(true);
            if (lootSide) lootSide.gameObject.SetActive(false);
            if (progressBar) progressBar.gameObject.SetActive(false);
            if (magnifier) magnifier.gameObject.SetActive(false);

            bagPanel.Bind(inventory.Grid, "가방");
            if (hintText) hintText.text = "드래그로 옮기기   ·   들고 [R] 90도 회전   ·   [Tab] / [ESC] 닫기";
        }

        private void CloseAll()
        {
            _bagOnly = false;
            lootPanel.Unbind();
            bagPanel.Unbind();
            SetOpen(false);
        }

        private void SetOpen(bool on)
        {
            AnyScreenOpen = on;
            if (root) root.gameObject.SetActive(on);

            // 격자를 다루는 동안엔 시스템 커서가 필요하고, 오발을 막아야 한다
            Cursor.visible = on;
            if (crossbow) crossbow.enabled = !on;
        }

        // ---------- 돋보기 ----------
        private void UpdateMagnifier()
        {
            if (magnifier == null || _open == null) return;

            var next = _open.Revealing;
            if (next == null || !_open.IsBeingSearched) { magnifier.gameObject.SetActive(false); return; }

            if (!magnifier.gameObject.activeSelf) magnifier.gameObject.SetActive(true);
            magnifier.SetAsLastSibling();      // 아이템 블록보다 위에 있어야 보인다

            Vector2 center = lootPanel.CellToLocal(next.X, next.Y);
            Vector2 size = lootPanel.SizeOf(next.W, next.H);
            center.x += size.x * 0.5f;
            center.y -= size.y * 0.5f;

            // 돋보기는 제자리에서 빙글 돌지 않는다. 물건 위를 훑고 지나간다.
            // 손잡이가 회전축이 되어 도는 건 실제로 물건을 살펴보는 동작이 아니다.
            _spin += magnifierOrbitSpeed * Time.deltaTime * (orbitClockwise ? -1f : 1f);
            float rad = _spin * Mathf.Deg2Rad;

            // 작은 물건 위에서 궤도가 삐져나가지 않도록 크기에 맞춰 줄인다
            float r = Mathf.Min(orbitRadius, Mathf.Min(size.x, size.y) * 0.28f);
            magnifier.anchoredPosition = center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;

            // 유리는 똑바로 유지하고 손목만 살짝 까딱인다
            magnifier.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(rad) * wobbleDegrees);
        }

        // ---------- 프리팹 조각 잇기 ----------
        /// <summary>
        /// 인스펙터에서 안 꽂았을 때만 이름으로 찾는다.
        /// 계층은 LootScreen / LootSide(Grid, ProgressBG/Fill) · BagSide(Grid) / Weight · Hint 이고,
        /// 돋보기는 왼쪽 패널의 Items 아래에 있다 - 아이템 블록보다 위에 그려져야 하기 때문이다.
        /// </summary>
        private void BindPieces()
        {
            if (root == null)
            {
                var t = transform.Find("LootScreen");
                if (t != null) root = t as RectTransform;
            }
            if (root == null) { Debug.LogError("[LootScreenUI] LootScreen 자식을 못 찾았다"); return; }

            if (lootSide == null) lootSide = root.Find("LootSide") as RectTransform;
            if (lootPanel == null && lootSide != null) lootPanel = lootSide.GetComponentInChildren<GridPanelUI>(true);

            var bagSide = root.Find("BagSide");
            if (bagPanel == null && bagSide != null) bagPanel = bagSide.GetComponentInChildren<GridPanelUI>(true);

            if (progressBar == null && lootSide != null) progressBar = lootSide.Find("ProgressBG") as RectTransform;
            if (progressFill == null && progressBar != null)
            {
                var f = progressBar.Find("Fill");
                if (f != null) progressFill = f.GetComponent<Image>();
            }
            if (magnifier == null && lootPanel != null && lootPanel.ItemRoot != null)
                magnifier = lootPanel.ItemRoot.Find("Magnifier") as RectTransform;

            if (weightText == null) { var t = root.Find("Weight"); if (t) weightText = t.GetComponent<TMP_Text>(); }
            if (hintText == null)   { var t = root.Find("Hint");   if (t) hintText   = t.GetComponent<TMP_Text>(); }

            if (magnifier != null) magnifier.gameObject.SetActive(false);
        }
    }
}
