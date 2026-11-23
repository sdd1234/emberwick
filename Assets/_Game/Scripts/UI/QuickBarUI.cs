using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Capstone.Core;
using Capstone.Items;

namespace Capstone.UI
{
    /// <summary>
    /// 퀵슬롯 5칸 (2026-11-04). 레이드 중 키 한 번으로 소모품을 쓴다.
    /// 가방 화면에서 물건에 커서를 올리고 슬롯 키를 누르거나, 끌어다 놓으면 등록된다. 슬롯 우클릭 = 비우기.
    /// 슬롯은 물건 종류를 기억한다 - 쓸 때 가방 → 보관함 순으로 찾는다 (ItemUse.Find).
    /// </summary>
    public class QuickBarUI : MonoBehaviour
    {
        private const float SlotSize = 76f, SlotGap = 4f;
        private static readonly Color Ivory = new(0.93f, 0.90f, 0.82f), Ink = new(0.05f, 0.04f, 0.04f);

        private readonly QuickSlotView[] _slots = new QuickSlotView[KeyBindings.SlotCount];
        private RectTransform _bar;
        private GameObject _settingsButton;
        private KeyBindPanel _panel;
        private TMP_FontAsset _font;
        private Player.PlayerController _player;
        private PlayerInventory _inventory;
        private Core.RaidManager _raid;
        private bool _wasOpen;

        public void Build(TMP_FontAsset font)
        {
            _font = font;
            _bar = NewRect("QuickBar", transform);
            _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f);
            _bar.pivot = new Vector2(0.5f, 0f);
            _bar.sizeDelta = new Vector2(SlotSize * _slots.Length + SlotGap * (_slots.Length - 1), SlotSize);
            _bar.anchoredPosition = new Vector2(0f, 20f);
            _bar.SetSiblingIndex(1);

            for (int i = 0; i < _slots.Length; i++)
            {
                var r = NewRect("Slot" + (i + 1), _bar);
                r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
                r.pivot = Vector2.zero;
                r.sizeDelta = Vector2.one * SlotSize;
                r.anchoredPosition = new Vector2(i * (SlotSize + SlotGap), 0f);
                _slots[i] = r.gameObject.AddComponent<QuickSlotView>();
                _slots[i].Build(this, i, font);
            }

            _panel = NewRect("KeyBindPanel", _bar).gameObject.AddComponent<KeyBindPanel>();
            _panel.Build(font);
            _settingsButton = MakeButton(_bar, "키 설정", font, new Vector2(_bar.sizeDelta.x * 0.5f - 72f, SlotSize + 8f), new Vector2(144f, 40f), () => _panel.Show());
            _settingsButton.SetActive(false);
        }

        private void Start()
        {
            _player = FindFirstObjectByType<Player.PlayerController>();
            if (_player != null) _inventory = _player.GetComponent<PlayerInventory>();
            _raid = FindFirstObjectByType<Core.RaidManager>();
            // 처음 들어가면 붕대를 1번 칸에 넣어 둔다
            var quick = _player != null ? _player.GetComponent<Player.QuickUse>() : null;
            if (!GameSession.QuickSlotsReady && quick != null && quick.Bandage != null)
            {
                GameSession.QuickSlots[0] = quick.Bandage;
                GameSession.QuickSlotsReady = true;
            }
        }

        private void Update()
        {
            bool open = LootScreenUI.AnyScreenOpen;
            if (open != _wasOpen)
            {
                _wasOpen = open;
                if (open) _bar.SetAsLastSibling(); else _bar.SetSiblingIndex(1);    // 가방 화면이 덮지 않게 위로
                _bar.anchoredPosition = new Vector2(0f, open ? 64f : 20f);          // 맨 아래 조작 안내를 가리지 않게
                _settingsButton.SetActive(open);
                if (!open) _panel.Hide();
            }

            bool ended = _raid != null && (_raid.IsDead || _raid.IsExtracted);
            if (_bar.gameObject.activeSelf == ended) _bar.gameObject.SetActive(!ended);
            if (ended) return;

            foreach (var s in _slots) s.Refresh(_inventory);
            if (KeyBindPanel.BlocksInput) return;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (!KeyBindings.Pressed(KeyBindings.Slot(i))) continue;
                if (open) AssignHovered(i); else Use(i);
            }
        }

        private void AssignHovered(int index)
        {
            var hovered = GridItemView.Hovered;
            if (hovered != null) Assign(index, hovered.Def);
        }

        /// <summary>슬롯에 등록한다. 쓸 수 있는 물건만 받고, 같은 물건이 다른 칸에 있으면 옮긴다.</summary>
        public void Assign(int index, ItemDefinition def)
        {
            if (def == null) return;
            if (!ItemUse.IsUsable(def)) { HUDController.Toast($"{def.displayName}은(는) 쓸 수 없다"); return; }
            for (int i = 0; i < GameSession.QuickSlots.Length; i++)
                if (GameSession.QuickSlots[i] == def) GameSession.QuickSlots[i] = null;
            GameSession.QuickSlots[index] = def;
            FX.SfxBank.Play2D("button_press");
        }

        public void Clear(int index) => GameSession.QuickSlots[index] = null;

        private void Use(int index)
        {
            if (_player == null || !_player.enabled) return;
            var def = GameSession.QuickSlots[index];
            if (def == null) { HUDController.Toast($"{index + 1}번 칸이 비어 있다"); return; }
            if (!ItemUse.Find(def, _inventory, out var item, out var grid)) { HUDController.Toast($"{def.displayName}이(가) 없다"); return; }
            ItemUse.TryUse(item, grid, _player.gameObject, out var msg);
            HUDController.Toast(msg);
        }

        // ---------- 도우미 ----------
        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            return r;
        }

        public static TMP_Text MakeText(RectTransform parent, string text, TMP_FontAsset font, float size, TextAlignmentOptions align, Color color)
        {
            var r = NewRect("Text", parent);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = text; t.fontSize = size; t.alignment = align; t.color = color;
            t.outlineWidth = 0f; t.outlineColor = Ink;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static GameObject MakeButton(RectTransform parent, string label, TMP_FontAsset font, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var r = NewRect("Button_" + label, parent);
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            var img = r.gameObject.AddComponent<Image>();
            PixelSkin.Apply(img, PixelSkin.Slot, Color.white);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            MakeText(r, label, font, 24f, TextAlignmentOptions.Center, Ivory);
            return r.gameObject;
        }
    }

    /// <summary>퀵슬롯 한 칸. 물건을 끌어다 놓으면 등록되고, 우클릭하면 비운다.</summary>
    public class QuickSlotView : MonoBehaviour, IDropHandler, IPointerClickHandler
    {
        private QuickBarUI _bar;
        private int _index;
        private Image _icon;
        private TMP_Text _key, _count;

        public void Build(QuickBarUI bar, int index, TMP_FontAsset font)
        {
            _bar = bar; _index = index;
            var r = (RectTransform)transform;
            PixelSkin.Apply(gameObject.AddComponent<Image>(), PixelSkin.Slot, Color.white);

            var iconRect = QuickBarUI.NewRect("Icon", r);
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = Vector2.one * 48f;
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;

            _key = QuickBarUI.MakeText(r, "", font, 12f, TextAlignmentOptions.TopLeft, new Color(0.93f, 0.90f, 0.82f));
            _key.margin = new Vector4(6f, 4f, 0f, 0f);
            _count = QuickBarUI.MakeText(r, "", font, 12f, TextAlignmentOptions.BottomRight, new Color(0.93f, 0.90f, 0.82f));
            _count.margin = new Vector4(0f, 0f, 6f, 4f);
        }

        public void Refresh(PlayerInventory inventory)
        {
            _key.text = KeyBindings.Label(KeyBindings.Slot(_index));
            var def = GameSession.QuickSlots[_index];
            _icon.enabled = def != null && def.icon != null;
            if (def == null) { _count.text = ""; return; }
            _icon.sprite = def.icon;
            int n = ItemUse.Count(def, inventory);
            _count.text = n.ToString();
            _icon.color = n > 0 ? Color.white : new Color(1f, 1f, 1f, 0.3f);
        }

        public void OnDrop(PointerEventData e)
        {
            if (ItemDrag.IsDragging) _bar.Assign(_index, ItemDrag.Item.Def);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) _bar.Clear(_index);
        }
    }
}
