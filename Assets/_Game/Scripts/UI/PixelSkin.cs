using UnityEngine;
using UnityEngine.UI;

namespace Capstone.UI
{
    /// <summary>
    /// 도트 UI 틀 (2026-10-29 - 격자 · 창을 AI 도트로). Resources/PixelUI 에서 읽어 어느 씬에서든 같은 틀을 쓴다.
    /// 틀은 9-슬라이스 - 모서리 리벳은 2배 도트 그대로, 가운데만 늘린다.
    /// </summary>
    public static class PixelSkin
    {
        private static Sprite _slot, _slotHi, _window;
        private static bool _loaded;

        public static Sprite Slot { get { Load(); return _slot; } }
        public static Sprite SlotHighlight { get { Load(); return _slotHi; } }
        public static Sprite Window { get { Load(); return _window; } }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _slot = Resources.Load<Sprite>("PixelUI/ui_slot");
            _slotHi = Resources.Load<Sprite>("PixelUI/ui_slot_hi");
            _window = Resources.Load<Sprite>("PixelUI/ui_window");
        }

        /// <summary>이미지를 도트 틀로 바꾼다 (2배 도트, 9-슬라이스).</summary>
        public static void Apply(Image img, Sprite s, Color tint, float pixelScale = 2f)
        {
            if (img == null || s == null) return;
            img.sprite = s;
            img.type = s.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f / pixelScale;
            img.color = tint;
        }
    }
}
