using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Capstone.Core;
using Capstone.Items;

namespace Capstone.UI
{
    /// <summary>
    /// 격자 하나를 그리는 패널. 가방이든 상자든 시체든 같은 걸 쓴다.
    /// 껍데기(제목 · 칸 자리 · 블록 자리 · 미리보기)는 프리팹에 들어 있고,
    /// 칸과 아이템 블록만 격자 크기에 맞춰 찍어낸다 - 개수가 그때그때 달라지기 때문이다.
    /// </summary>
    public class GridPanelUI : MonoBehaviour, IDropHandler
    {
        [Header("모양")]
        [SerializeField] private float cellSize = 56f;
        [SerializeField] private float cellGap = 3f;

        [Header("색")]
        [SerializeField] private Color cellColor    = new(0.15f, 0.145f, 0.155f, 0.95f);
        [SerializeField] private Color unknownColor = new(0.27f, 0.26f, 0.28f, 0.96f);
        [SerializeField] private Color validColor   = new(0.30f, 0.75f, 0.40f, 0.35f);
        [SerializeField] private Color invalidColor = new(0.85f, 0.25f, 0.22f, 0.35f);

        [Header("프리팹 조각")]
        [SerializeField] private TMP_Text titleText;
        [Tooltip("칸이 들어갈 자리. 좌상단 기준 (0,1) 앵커라야 한다")]
        [SerializeField] private RectTransform cellRoot;
        [Tooltip("아이템 블록이 들어갈 자리. 칸보다 위에 그려진다")]
        [SerializeField] private RectTransform itemRoot;
        [Tooltip("놓을 자리 미리보기")]
        [SerializeField] private Image ghost;

        [Header("찍어낼 프리팹")]
        [Tooltip("빈 칸 하나. 격자 크기만큼 찍어낸다")]
        [SerializeField] private Image cellPrefab;
        [Tooltip("격자 위 물건 블록")]
        [SerializeField] private GridItemView itemViewPrefab;
        [Tooltip("드래그 중 커서를 따라다니는 블록")]
        [SerializeField] private DragVisual dragVisualPrefab;

        public ItemGrid Grid { get; private set; }
        /// <summary>상자/시체면 설정된다. 식별 안 된 물건은 못 꺼내게 막는 데 쓴다.</summary>
        public LootContainer Container { get; private set; }
        public float CellSize => cellSize;
        public float CellGap => cellGap;
        public DragVisual DragVisualPrefab => dragVisualPrefab;
        public RectTransform ItemRoot => itemRoot;

        private RectTransform _rt;
        private readonly List<GameObject> _cells = new();
        private readonly Dictionary<GridItem, GridItemView> _views = new();
        private bool _bound;

        private void Awake() => BindPieces();

        /// <summary>
        /// 프리팹에 들어 있는 조각을 잇는다. 인스펙터에서 안 꽂았으면 이름으로 찾는다.
        /// </summary>
        private void BindPieces()
        {
            if (_bound) return;
            _bound = true;
            _rt = GetComponent<RectTransform>();

            if (titleText == null) { var t = transform.Find("Title"); if (t) titleText = t.GetComponent<TMP_Text>(); }
            if (cellRoot == null) cellRoot = transform.Find("Cells") as RectTransform;
            if (itemRoot == null) itemRoot = transform.Find("Items") as RectTransform;
            if (ghost == null && itemRoot != null) { var t = itemRoot.Find("Ghost"); if (t) ghost = t.GetComponent<Image>(); }

            if (ghost != null) ghost.gameObject.SetActive(false);
        }

        // ---------- 연결 ----------
        public void Bind(ItemGrid grid, string title, LootContainer container = null)
        {
            BindPieces();
            Unbind();

            Grid = grid;
            Container = container;
            if (titleText) titleText.text = title;

            BuildCells();
            Rebuild();
            if (Grid != null) Grid.OnChanged += Rebuild;
        }

        public void Unbind()
        {
            if (Grid != null) Grid.OnChanged -= Rebuild;
            Grid = null; Container = null;

            foreach (var c in _cells) if (c) Destroy(c);
            _cells.Clear();
            foreach (var kv in _views) if (kv.Value) Destroy(kv.Value.gameObject);
            _views.Clear();
        }

        private void OnDestroy() => Unbind();

        // ---------- 그리기 ----------
        private void BuildCells()
        {
            if (Grid == null || cellPrefab == null || cellRoot == null) return;

            float w = Grid.Columns * cellSize + (Grid.Columns - 1) * cellGap;
            float h = Grid.Rows * cellSize + (Grid.Rows - 1) * cellGap;
            _rt.sizeDelta = new Vector2(w, h);

            for (int y = 0; y < Grid.Rows; y++)
            for (int x = 0; x < Grid.Columns; x++)
            {
                var img = Instantiate(cellPrefab, cellRoot);
                img.name = $"C{x}_{y}";
                img.gameObject.SetActive(true);

                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = Vector2.one * cellSize;
                rt.anchoredPosition = CellToLocal(x, y);

                img.color = cellColor;
                img.raycastTarget = true;        // 빈 칸에도 드롭할 수 있어야 한다
                _cells.Add(img.gameObject);
            }
        }

        /// <summary>격자 좌표 -> 패널 안 픽셀 좌표 (좌상단 기준).</summary>
        public Vector2 CellToLocal(int x, int y)
            => new(x * (cellSize + cellGap), -y * (cellSize + cellGap));

        public Vector2 SizeOf(int w, int h)
            => new(w * cellSize + (w - 1) * cellGap, h * cellSize + (h - 1) * cellGap);

        public void Rebuild()
        {
            if (Grid == null) return;

            // 사라진 물건의 뷰를 정리한다
            var stale = new List<GridItem>();
            foreach (var kv in _views) if (!Contains(Grid, kv.Key)) stale.Add(kv.Key);
            foreach (var s in stale) { if (_views[s]) Destroy(_views[s].gameObject); _views.Remove(s); }

            foreach (var item in Grid.Items)
            {
                GridItemView view;
                if (!_views.TryGetValue(item, out view) || view == null)
                {
                    view = CreateView(item);
                    if (view == null) continue;
                    _views[item] = view;
                }
                view.Refresh();
            }
            if (ghost) ghost.rectTransform.SetAsLastSibling();
        }

        private static bool Contains(ItemGrid grid, GridItem item)
        {
            foreach (var i in grid.Items) if (ReferenceEquals(i, item)) return true;
            return false;
        }

        private GridItemView CreateView(GridItem item)
        {
            if (itemViewPrefab == null || itemRoot == null) return null;

            var view = Instantiate(itemViewPrefab, itemRoot);
            view.name = "Item";
            view.gameObject.SetActive(true);
            view.Setup(this, item, unknownColor);
            return view;
        }

        // ---------- 드래그 미리보기 ----------
        public void ShowGhost(int x, int y, int w, int h, bool valid)
        {
            if (ghost == null) return;
            ghost.gameObject.SetActive(true);
            ghost.rectTransform.sizeDelta = SizeOf(w, h);
            ghost.rectTransform.anchoredPosition = CellToLocal(x, y);
            ghost.color = valid ? validColor : invalidColor;
            ghost.rectTransform.SetAsLastSibling();
        }

        public void HideGhost()
        {
            if (ghost) ghost.gameObject.SetActive(false);
        }

        /// <summary>화면 좌표 아래의 격자 칸. 범위를 벗어나도 값은 돌려주므로 CanPlace 로 걸러야 한다.</summary>
        public bool ScreenToCell(Vector2 screenPos, Camera cam, out int cx, out int cy)
        {
            cx = cy = 0;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screenPos, cam, out local))
                return false;

            // _rt 의 피벗이 중앙일 수 있으므로 좌상단 기준으로 옮긴다
            Vector2 topLeft = new(-_rt.rect.width * _rt.pivot.x, _rt.rect.height * (1f - _rt.pivot.y));
            Vector2 rel = local - topLeft;

            cx = Mathf.FloorToInt(rel.x / (cellSize + cellGap));
            cy = Mathf.FloorToInt(-rel.y / (cellSize + cellGap));
            return true;
        }

        // ---------- 드롭 ----------
        public void OnDrop(PointerEventData eventData)
        {
            if (!ItemDrag.IsDragging || Grid == null) { HideGhost(); return; }

            var item = ItemDrag.Item;
            var source = ItemDrag.Source;

            // 상자에서 꺼낼 때는 감정이 끝난 물건만 허용한다
            if (ItemDrag.SourcePanel != null && ItemDrag.SourcePanel.Container != null && !item.Identified)
            { HideGhost(); return; }

            int cx, cy;
            if (!ScreenToCell(eventData.position, eventData.pressEventCamera, out cx, out cy)) { HideGhost(); return; }

            int w, h, gx, gy;
            ItemDrag.GrabbedCell(out w, out h, out gx, out gy);

            Grid.MoveFrom(source, item, cx - gx, cy - gy, ItemDrag.Rotated);
            HideGhost();
        }
    }
}
