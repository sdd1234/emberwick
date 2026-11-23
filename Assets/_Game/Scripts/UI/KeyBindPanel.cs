using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Capstone.Core;

namespace Capstone.UI
{
    /// <summary>
    /// 키 설정 창 (2026-11-04). 가방 화면의 "키 설정" 버튼으로 연다.
    /// 칸을 누르고 원하는 키를 누르면 바뀐다. ESC 는 취소, 창이 떠 있는 동안 다른 입력은 막는다.
    /// </summary>
    public class KeyBindPanel : MonoBehaviour
    {
        private static readonly (GameAction action, string name)[] Rows =
        {
            (GameAction.Slot1, "퀵슬롯 1"), (GameAction.Slot2, "퀵슬롯 2"), (GameAction.Slot3, "퀵슬롯 3"),
            (GameAction.Slot4, "퀵슬롯 4"), (GameAction.Slot5, "퀵슬롯 5"),
            (GameAction.Bandage, "붕대"), (GameAction.Flask, "빛 플라스크"),
        };

        private const float RowHeight = 48f, Width = 480f;
        private static readonly Color Ivory = new(0.93f, 0.90f, 0.82f), Gold = new(0.93f, 0.76f, 0.38f);

        private static int _closedFrame = -1;
        private static bool _shown;

        /// <summary>이 창이 입력을 쓰는 중. 닫는 ESC 가 뒤의 가방까지 닫지 않게 닫은 프레임도 막는다.</summary>
        public static bool BlocksInput => _shown || _closedFrame == Time.frameCount;

        private readonly List<TMP_Text> _keys = new();
        private TMP_Text _message;
        private int _capturing = -1;

        public void Build(TMP_FontAsset font)
        {
            float height = RowHeight * (Rows.Length + 2) + 40f;
            var r = (RectTransform)transform;
            // 화면 가운데 - 부모는 아래쪽 퀵바라서 캔버스 기준으로 옮긴다
            r.SetParent(r.parent.parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(Width, height);
            var bg = gameObject.AddComponent<Image>();
            PixelSkin.Apply(bg, PixelSkin.Window, Color.white);

            for (int i = 0; i < Rows.Length; i++)
            {
                int row = i;
                float y = height - 20f - RowHeight * (i + 1);
                var label = QuickBarUI.NewRect("Label" + i, r);
                Place(label, new Vector2(32f, y), new Vector2(240f, RowHeight - 6f));
                QuickBarUI.MakeText(label, Rows[i].name, font, 24f, TextAlignmentOptions.Left, Ivory);
                var button = QuickBarUI.MakeButton(r, "", font, new Vector2(Width - 192f, y), new Vector2(160f, RowHeight - 6f), () => StartCapture(row));
                _keys.Add(button.GetComponentInChildren<TMP_Text>());
            }

            var messageRect = QuickBarUI.NewRect("Message", r);
            Place(messageRect, new Vector2(32f, 20f + RowHeight), new Vector2(Width - 64f, RowHeight - 12f));
            _message = QuickBarUI.MakeText(messageRect, "", font, 12f, TextAlignmentOptions.Center, Gold);

            QuickBarUI.MakeButton(r, "기본값", font, new Vector2(32f, 16f), new Vector2(160f, RowHeight - 6f), () => { KeyBindings.ResetAll(); _capturing = -1; Refresh(); _message.text = ""; });
            QuickBarUI.MakeButton(r, "닫기", font, new Vector2(Width - 192f, 16f), new Vector2(160f, RowHeight - 6f), Hide);

            gameObject.SetActive(false);
        }

        private static void Place(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        public void Show()
        {
            _capturing = -1;
            _message.text = "바꿀 칸을 누른 뒤 원하는 키를 누른다";
            Refresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _shown = true;
        }

        public void Hide()
        {
            if (_shown) _closedFrame = Time.frameCount;
            _shown = false;
            _capturing = -1;
            gameObject.SetActive(false);
        }

        private void OnDestroy() => _shown = false;

        private void StartCapture(int row)
        {
            _capturing = row;
            _message.text = $"{Rows[row].name}: 새 키를 누른다 (ESC 취소)";
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _keys.Count; i++)
            {
                _keys[i].text = i == _capturing ? "..." : KeyBindings.Label(Rows[i].action);
                _keys[i].color = i == _capturing ? Gold : Ivory;
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_capturing < 0)
            {
                if (kb.escapeKey.wasPressedThisFrame) Hide();
                return;
            }

            foreach (var control in kb.allKeys)
            {
                if (control == null || !control.wasPressedThisFrame) continue;
                var key = control.keyCode;
                if (key == Key.Escape) { _capturing = -1; _message.text = ""; Refresh(); return; }
                if (KeyBindings.IsReserved(key)) { _message.text = $"{KeyBindings.KeyName(key)} 는 다른 조작에 고정돼 있다"; return; }
                KeyBindings.Set(Rows[_capturing].action, key);
                _message.text = $"{Rows[_capturing].name} = {KeyBindings.KeyName(key)}";
                _capturing = -1;
                Refresh();
                return;
            }
        }
    }
}
