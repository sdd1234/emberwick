using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Capstone.Dev
{
    /// <summary>
    /// 에디터 자동 테스트용 입력 꼭두각시. 에디터 쪽(MCP · 메뉴)에서 Held · Mouse 를 정해 두면
    /// 게임 프레임마다 그 상태를 입력 시스템에 다시 넣는다. 창 포커스가 없어도 게임이 그 입력을 받는다
    /// (에디터 업데이트에서 넣은 입력은 플레이어 업데이트에 안 닿을 때가 있다 - PlaytestBot 과 같은 방식).
    /// 빌드에는 쓰이지 않는다. 씬에 아무것도 없으면 아무 일도 안 한다.
    /// </summary>
    public class InputPuppet : MonoBehaviour
    {
        public static readonly HashSet<Key> Held = new();
        public static readonly List<Key> Tap = new();
        public static Vector2 MouseScreen = new(960f, 540f);
        public static bool Left, Right;
        public static bool RightClickOnce, LeftClickOnce;

        private static InputPuppet _inst;
        public static void Ensure()
        {
            if (_inst != null) return;
            var go = new GameObject("InputPuppet");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<InputPuppet>();
        }

        public static void Clear()
        {
            Held.Clear(); Tap.Clear(); Left = Right = RightClickOnce = LeftClickOnce = false;
        }

        private bool _clickPhase;

        private void Update()
        {
            if (Keyboard.current != null)
            {
                var keys = new List<Key>(Held); keys.AddRange(Tap);
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys.ToArray()));
                Tap.Clear();
            }
            if (Mouse.current != null)
            {
                var ms = new MouseState { position = MouseScreen };
                // 한 번 클릭 - 이번 프레임 누르고 다음 프레임 뗀다
                bool l = Left, r = Right;
                if (!_clickPhase && (LeftClickOnce || RightClickOnce)) { l |= LeftClickOnce; r |= RightClickOnce; _clickPhase = true; }
                else if (_clickPhase) { LeftClickOnce = RightClickOnce = false; _clickPhase = false; }
                if (l) ms = ms.WithButton(MouseButton.Left);
                if (r) ms = ms.WithButton(MouseButton.Right);
                InputSystem.QueueStateEvent(Mouse.current, ms);
            }
        }

        private void OnDestroy() { if (_inst == this) _inst = null; }
    }
}
