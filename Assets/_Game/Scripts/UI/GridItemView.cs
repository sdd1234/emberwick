using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using Capstone.Core;
using Capstone.Items;

namespace Capstone.UI
{
    /// <summary>
    /// 격자 위 물건 하나의 블록. 끌어서 다른 격자로 옮길 수 있다.
    /// 감정이 안 끝난 물건은 물음표로만 보이고 집을 수도 없다 (기획서 6.3).
    /// </summary>
    public class GridItemView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [Header("프리팹 조각")]
        [Tooltip("블록 바탕. 등급 색이 옅게 깔린다")]
        [SerializeField] private Image fill;
        [SerializeField] private Image icon;
        [Tooltip("속이 빈 테두리. 채워진 스프라이트를 쓰면 아이콘이 가려진다")]
        [SerializeField] private Image border;
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text countLabel;
        [SerializeField] private CanvasGroup group;

        private GridPanelUI _panel;
        private GridItem _item;
        private Color _unknownColor;
        private RectTransform _rt;

        private static DragVisual _dragVisual;      // 커서를 따라다니는 블록 (한 개만 만들어 돌려 쓴다)
        private Camera _dragCamera;                 // 드래그를 시작한 캔버스의 카메라 (오버레이면 null)

        public GridItem Item => _item;

        public void Setup(GridPanelUI panel, GridItem item, Color unknownColor)
        {
            _panel = panel; _item = item; _unknownColor = unknownColor;
            _rt = GetComponent<RectTransform>();
            Refresh();
        }

        public void Refresh()
        {
            if (_item == null || _panel == null) return;

            _rt.sizeDelta = _panel.SizeOf(_item.W, _item.H);
            _rt.anchoredPosition = _panel.CellToLocal(_item.X, _item.Y);

            if (_item.Identified)
            {
                var c = _item.Def.grade.ToColor();
                fill.color = new Color(c.r * 0.30f, c.g * 0.30f, c.b * 0.30f, 0.96f);
                border.color = c;

                icon.sprite = _item.Def.icon;
                icon.enabled = icon.sprite != null;

                // 아이콘이 있으면 이름은 아래에 작게, 없으면 칸 전체에 크게
                bool hasIcon = icon.enabled;
                label.text = _item.Def.displayName;
                label.alignment = hasIcon ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Center;
                label.fontSizeMax = hasIcon ? 12f : 15f;
                label.fontSizeMin = 7f;
                label.color = hasIcon ? new Color(0.93f, 0.92f, 0.89f, 0.95f) : c;

                countLabel.text = _item.Count > 1 ? $"x{_item.Count}" : "";
            }
            else
            {
                // 감정 전에는 크기로만 짐작할 수 있다 (기획서 6.3)
                fill.color = _unknownColor;
                border.color = new Color(0.42f, 0.40f, 0.42f, 0.8f);
                icon.enabled = false;                    // 정체가 드러나기 전엔 아이콘도 감춘다
                label.text = "?";
                label.alignment = TextAlignmentOptions.Center;
                label.fontSizeMax = 34f;
                label.fontSizeMin = 20f;
                label.color = new Color(0.62f, 0.60f, 0.58f, 1f);
                countLabel.text = "";
            }
        }

        // ---------- 드래그 ----------
        public void OnBeginDrag(PointerEventData e)
        {
            // 감정 전에는 집을 수 없다
            if (_item == null || !_item.Identified) return;

            int cx, cy;
            _dragCamera = e.pressEventCamera;
            _panel.ScreenToCell(e.position, _dragCamera, out cx, out cy);
            int offX = Mathf.Clamp(cx - _item.X, 0, _item.W - 1);
            int offY = Mathf.Clamp(cy - _item.Y, 0, _item.H - 1);

            ItemTooltip.Hide();
            ItemDrag.Begin(_item, _panel.Grid, _panel, offX, offY);
            group.alpha = 0.35f;
            group.blocksRaycasts = false;          // 아래 격자가 드롭을 받아야 한다

            EnsureDragVisual();
            UpdateDragVisual(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!ItemDrag.IsDragging || ItemDrag.Item != _item) return;

            UpdateDragVisual(e.position);
            UpdateGhost(e.position);
        }

        /// <summary>
        /// 들고 있는 동안 R 로 90도 회전.
        /// 이 검사를 OnDrag 에 두면 안 된다 - OnDrag 는 **커서가 움직인 프레임에만** 불린다.
        /// 아이템을 든 채로 손을 멈추고 R 을 누르면 그 프레임에 OnDrag 가 오지 않아 입력을 통째로 놓친다.
        /// ("가끔만 먹는다"의 정체가 이것이었다 - 마우스를 움직이던 중에 눌러야만 들었다)
        /// </summary>
        private void Update()
        {
            if (!ItemDrag.IsDragging || ItemDrag.Item != _item) return;

            var kb = Keyboard.current;
            if (kb == null || !kb.rKey.wasPressedThisFrame || !CanRotate()) return;

            ItemDrag.Rotated = !ItemDrag.Rotated;

            // 손이 멈춰 있어도 돌린 결과가 바로 보여야 한다
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 p = mouse.position.ReadValue();
            UpdateDragVisual(p);
            UpdateGhost(p);
        }

        /// <summary>
        /// 가로세로가 다를 때만 회전이 의미가 있다.
        /// 1x1 은 물론이고 2x2 도 돌려봐야 차지하는 자리가 똑같다.
        /// </summary>
        private bool CanRotate()
            => _item != null && _item.Def != null && _item.Def.gridWidth != _item.Def.gridHeight;

        public void OnPointerEnter(PointerEventData e)
        {
            if (ItemDrag.IsDragging) return;      // 끌고 다니는 중엔 설명창이 방해만 된다
            ItemTooltip.Show(_item);
        }

        public void OnPointerExit(PointerEventData e) => ItemTooltip.Hide();

        public void OnEndDrag(PointerEventData e)
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;

            EndDragVisual();
            Refresh();
        }

        /// <summary>
        /// 끌던 도중에 이 블록이 사라질 수 있다.
        /// 마지막 물건을 꺼내면 상자가 비어서 Interactor 가 루팅 화면을 닫아버리는 경우다.
        /// 그러면 OnEndDrag 가 영영 안 불리고, 커서를 따라다니던 블록이 화면에 남는다.
        /// </summary>
        private void OnDisable()
        {
            if (ItemDrag.Item == _item) EndDragVisual();
        }

        private static void EndDragVisual()
        {
            if (_dragVisual) _dragVisual.gameObject.SetActive(false);
            ClearAllGhosts();
            ItemDrag.Clear();
        }


        /// <summary>커서 아래 패널에 놓을 자리를 미리 보여준다.</summary>
        private void UpdateGhost(Vector2 screenPos)
        {
            ClearAllGhosts();

            var panel = PanelUnder(screenPos);
            if (panel == null || panel.Grid == null) return;

            int cx, cy;
            if (!panel.ScreenToCell(screenPos, _dragCamera, out cx, out cy)) return;

            int w, h, gx, gy;
            ItemDrag.GrabbedCell(out w, out h, out gx, out gy);

            int tx = cx - gx;
            int ty = cy - gy;

            bool sameGrid = ReferenceEquals(panel.Grid, ItemDrag.Source);
            bool ok = panel.Grid.CanPlace(_item, tx, ty, ItemDrag.Rotated, sameGrid ? _item : null)
                      && (sameGrid || !panel.Grid.WouldExceedWeight(_item));

            panel.ShowGhost(tx, ty, w, h, ok);
        }

        private static void ClearAllGhosts()
        {
            foreach (var p in FindObjectsByType<GridPanelUI>(FindObjectsSortMode.None)) p.HideGhost();
        }

        private static GridPanelUI PanelUnder(Vector2 screenPos)
        {
            if (EventSystem.current == null) return null;

            // 레이캐스트는 위치만 보면 된다. 손이 멈춰 있을 때도 불러야 해서 이벤트 데이터를 직접 만든다
            var probe = new PointerEventData(EventSystem.current) { position = screenPos };
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(probe, results);
            foreach (var r in results)
            {
                var p = r.gameObject.GetComponentInParent<GridPanelUI>();
                if (p != null) return p;
            }
            return null;
        }

        // ---------- 커서를 따라다니는 블록 ----------
        /// <summary>
        /// 블록은 화면 전체에서 하나만 있으면 된다. 처음 끌 때 만들어 두고 계속 돌려 쓴다.
        /// 루트 캔버스에 붙여야 어느 패널 위로 가져가든 맨 위에 그려진다.
        /// </summary>
        private void EnsureDragVisual()
        {
            if (_dragVisual != null) { _dragVisual.gameObject.SetActive(true); return; }
            if (_panel == null || _panel.DragVisualPrefab == null) return;

            var canvas = GetComponentInParent<Canvas>();
            _dragVisual = Instantiate(_panel.DragVisualPrefab, canvas.rootCanvas.transform);
            _dragVisual.name = "DragVisual";
        }

        private void UpdateDragVisual(Vector2 screenPos)
        {
            if (_dragVisual == null) return;
            _dragVisual.transform.SetAsLastSibling();

            int w, h, gx, gy;
            ItemDrag.GrabbedCell(out w, out h, out gx, out gy);

            var rect = _dragVisual.Rect;
            Vector2 size = _panel.SizeOf(w, h);
            rect.sizeDelta = size;

            // 블록을 커서 한가운데 매달면 안 된다.
            // 놓이는 자리는 '붙잡은 칸'이 커서 밑에 오도록 계산되므로,
            // 피벗을 그 칸의 중심에 맞춰야 미리보기와 실제 결과가 같은 자리를 가리킨다.
            float step = _panel.CellSize + _panel.CellGap;
            rect.pivot = new Vector2(
                (gx * step + _panel.CellSize * 0.5f) / Mathf.Max(1f, size.x),
                1f - (gy * step + _panel.CellSize * 0.5f) / Mathf.Max(1f, size.y));

            var c = _item.Def.grade.ToColor();
            if (_dragVisual.Fill) _dragVisual.Fill.color = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.8f);
            if (_dragVisual.Label)
            {
                _dragVisual.Label.text = _item.Def.displayName;
                _dragVisual.Label.color = new Color(0.93f, 0.92f, 0.89f, 0.95f);
            }
            if (_dragVisual.Icon)
            {
                _dragVisual.Icon.sprite = _item.Def.icon;
                _dragVisual.Icon.enabled = _dragVisual.Icon.sprite != null;
            }

            var canvas = _dragVisual.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                rect.position = screenPos;
            }
            else
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.transform as RectTransform, screenPos, _dragCamera, out local);
                rect.localPosition = local;
            }
        }
    }
}
