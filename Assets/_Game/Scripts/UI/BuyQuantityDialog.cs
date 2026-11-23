using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Capstone.Core;

namespace Capstone.UI
{
    /// <summary>
    /// 상점 수량 구매창 (2026-11-04). 물건 줄을 누르면 뜬다.
    /// 살 수 있는 최대치는 소지금과 창고 자리로 정해지고, 뒤의 거래창은 눌리지 않는다.
    /// </summary>
    public class BuyQuantityDialog : MonoBehaviour
    {
        private const float Width = 600f, Height = 330f;
        private static readonly Color Ivory = new(0.93f, 0.90f, 0.82f), Gold = new(0.93f, 0.76f, 0.38f);

        private Image _icon;
        private TMP_Text _name, _unit, _quantity, _total;
        private int _count, _max, _price;
        private Action<int> _confirm;

        public bool IsOpen => gameObject.activeSelf;

        public void Build(TMP_FontAsset font)
        {
            // 화면 전체를 덮어 뒤의 거래창을 막는다
            var blocker = (RectTransform)transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = blocker.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var panel = QuickBarUI.NewRect("Panel", blocker);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(Width, Height);
            PixelSkin.Apply(panel.gameObject.AddComponent<Image>(), PixelSkin.Window, Color.white);

            var iconRect = QuickBarUI.NewRect("Icon", panel);
            Place(iconRect, new Vector2(32f, 226f), new Vector2(72f, 72f));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            _name = Label(panel, "Name", new Vector2(120f, 262f), new Vector2(Width - 152f, 36f), font, 24f, TextAlignmentOptions.Left, Ivory);
            _unit = Label(panel, "Unit", new Vector2(120f, 226f), new Vector2(Width - 152f, 28f), font, 24f, TextAlignmentOptions.Left, Gold);

            float y = 152f;
            QuickBarUI.MakeButton(panel, "-10", font, new Vector2(32f, y), new Vector2(72f, 52f), () => Change(-10));
            QuickBarUI.MakeButton(panel, "-1", font, new Vector2(112f, y), new Vector2(64f, 52f), () => Change(-1));
            _quantity = Label(panel, "Quantity", new Vector2(184f, y), new Vector2(104f, 52f), font, 36f, TextAlignmentOptions.Center, Ivory);
            QuickBarUI.MakeButton(panel, "+1", font, new Vector2(296f, y), new Vector2(64f, 52f), () => Change(1));
            QuickBarUI.MakeButton(panel, "+10", font, new Vector2(368f, y), new Vector2(72f, 52f), () => Change(10));
            QuickBarUI.MakeButton(panel, "최대", font, new Vector2(448f, y), new Vector2(120f, 52f), () => { _count = _max; Refresh(); });

            _total = Label(panel, "Total", new Vector2(32f, 92f), new Vector2(Width - 64f, 40f), font, 24f, TextAlignmentOptions.Center, Gold);

            QuickBarUI.MakeButton(panel, "구매", font, new Vector2(32f, 24f), new Vector2(256f, 52f), Confirm);
            QuickBarUI.MakeButton(panel, "취소", font, new Vector2(Width - 288f, 24f), new Vector2(256f, 52f), Hide);

            gameObject.SetActive(false);
        }

        public void Show(Items.ItemDefinition def, int price, int max, Action<int> confirm)
        {
            _price = price; _max = max; _count = 1; _confirm = confirm;
            _icon.sprite = def.icon;
            _icon.enabled = def.icon != null;
            _name.text = def.displayName;
            _name.color = def.grade.ToColor();
            _unit.text = $"개당 {price:N0} G";
            Refresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            _confirm = null;
            gameObject.SetActive(false);
        }

        private void Change(int delta)
        {
            _count = Mathf.Clamp(_count + delta, 1, _max);
            Refresh();
        }

        private void Refresh()
        {
            _quantity.text = _count.ToString();
            _total.text = $"합계 {_count * _price:N0} G   (최대 {_max}개)";
        }

        private void Confirm()
        {
            var confirm = _confirm;
            int count = _count;
            Hide();
            confirm?.Invoke(count);
        }

        private static void Place(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        private static TMP_Text Label(RectTransform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, float fontSize, TextAlignmentOptions align, Color color)
        {
            var r = QuickBarUI.NewRect(name, parent);
            Place(r, pos, size);
            return QuickBarUI.MakeText(r, "", font, fontSize, align, color);
        }
    }
}
