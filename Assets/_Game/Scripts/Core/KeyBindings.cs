using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Core
{
    public enum GameAction { Slot1, Slot2, Slot3, Slot4, Slot5, Bandage, Flask }

    /// <summary>
    /// 바꿀 수 있는 키 (2026-11-04). PlayerPrefs 에 저장해 다음 실행에도 남는다.
    /// 다른 동작이 쓰는 키를 고르면 서로 맞바꾼다. 이동 · 상호작용처럼 고정된 키는 고를 수 없다.
    /// </summary>
    public static class KeyBindings
    {
        public const int SlotCount = 5;

        private static readonly Dictionary<GameAction, Key> Defaults = new()
        {
            { GameAction.Slot1, Key.Digit1 }, { GameAction.Slot2, Key.Digit2 }, { GameAction.Slot3, Key.Digit3 },
            { GameAction.Slot4, Key.Digit4 }, { GameAction.Slot5, Key.Digit5 },
            { GameAction.Bandage, Key.H }, { GameAction.Flask, Key.G },
        };

        private static readonly HashSet<Key> Reserved = new()
        {
            Key.W, Key.A, Key.S, Key.D, Key.E, Key.F, Key.Q, Key.R, Key.Tab, Key.Space, Key.Escape, Key.Enter,
            Key.LeftShift, Key.RightShift,
        };

        private static Dictionary<GameAction, Key> _keys;

        private static Dictionary<GameAction, Key> Keys
        {
            get
            {
                if (_keys != null) return _keys;
                _keys = new Dictionary<GameAction, Key>();
                foreach (var kv in Defaults) _keys[kv.Key] = (Key)PlayerPrefs.GetInt(PrefName(kv.Key), (int)kv.Value);
                return _keys;
            }
        }

        public static GameAction Slot(int index) => GameAction.Slot1 + index;

        public static Key Get(GameAction action) => Keys[action];

        public static bool Pressed(GameAction action)
        {
            var kb = Keyboard.current;
            var key = Get(action);
            return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
        }

        public static bool IsReserved(Key key) => Reserved.Contains(key);

        /// <summary>키를 바꾼다. 다른 동작이 쓰던 키면 그 동작이 내 옛 키를 받는다.</summary>
        public static void Set(GameAction action, Key key)
        {
            var old = Keys[action];
            foreach (var other in new List<GameAction>(Keys.Keys))
                if (other != action && Keys[other] == key) { Keys[other] = old; Save(other); }
            Keys[action] = key;
            Save(action);
        }

        public static void ResetAll()
        {
            foreach (var kv in Defaults) { Keys[kv.Key] = kv.Value; Save(kv.Key); }
        }

        public static string Label(GameAction action) => KeyName(Get(action));

        public static string KeyName(Key key)
        {
            string s = key.ToString();
            if (s.StartsWith("Digit", StringComparison.Ordinal)) return s.Substring(5);
            if (s.StartsWith("Numpad", StringComparison.Ordinal)) return "N" + s.Substring(6);
            if (s.StartsWith("Left", StringComparison.Ordinal)) return "L" + s.Substring(4);
            if (s.StartsWith("Right", StringComparison.Ordinal)) return "R" + s.Substring(5);
            return s;
        }

        private static string PrefName(GameAction action) => "key." + action;

        private static void Save(GameAction action)
        {
            PlayerPrefs.SetInt(PrefName(action), (int)Keys[action]);
            PlayerPrefs.Save();
        }
    }
}
