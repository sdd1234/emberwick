#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Capstone.Dev
{
    /// <summary>
    /// 조명만 보는 촬영용 산책 (2026-11-01). 적을 모두 끄고, 레이드 큰길을 따라 가로등을 지나며 걷는다.
    /// 중간에 등불을 끄고(가로등 빛 웅덩이만), 약하게, 다시 세게 - 불빛과 어둠의 대비를 차례로 보여 준다.
    /// 이동은 실제 입력(WASD)을 넣어서 걷기 흔들림 · 불빛 일렁임이 플레이 그대로 나온다.
    /// </summary>
    public class LightTour : MonoBehaviour
    {
        private struct Step { public Vector2 to; public string act; public float wait; }

        private readonly List<Step> _steps = new();
        private Player.PlayerController _pc;
        private Player.TorchFuel _torch;
        private readonly HashSet<Key> _held = new();
        private Vector2 _aim;

        public bool Done { get; private set; }

        private string _route = "road";

        /// <summary>route: "road" = 큰길 가로등 산책, "house" = 집 A 안을 돌며 가구 · 벽 그림자가 등불 따라 도는 것</summary>
        public static LightTour Begin(string route = "road")
        {
            var t = new GameObject("LightTour").AddComponent<LightTour>();
            t._route = route;
            return t;
        }

        private void Go(float x, float y, string act = null, float wait = 0f) => _steps.Add(new Step { to = new Vector2(x, y), act = act, wait = wait });

        private void Start()
        {
            _pc = FindFirstObjectByType<Player.PlayerController>();
            _torch = _pc ? _pc.GetComponentInChildren<Player.TorchFuel>() : null;
            if (_torch == null) _torch = FindFirstObjectByType<Player.TorchFuel>();
            foreach (var e in FindObjectsByType<Enemy.EnemyAI>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            // 가로등은 시작할 때 무작위로 켜진다 - 촬영 길목 것만 고정 (3번은 일부러 꺼 두어 어두운 구간을 만든다)
            foreach (var l in FindObjectsByType<Map.LampPost>(FindObjectsSortMode.None))
            {
                if (l.name == "LampPost_2" || l.name == "LampPost_4" || l.name == "LampPost_5" || l.name == "LampPost_10") l.SetLitImmediate(true);
                else if (l.name == "LampPost_3") l.SetLitImmediate(false);
            }

            if (_route == "house")
            {
                // 집 A (9..21, -22..-12), 문 북쪽 x=15. 문밖 가로등(11 · 12)만 켜 둔다 - 벽 그림자가 문으로 들어온다
                foreach (var l in FindObjectsByType<Map.LampPost>(FindObjectsSortMode.None))
                    l.SetLitImmediate(l.name == "LampPost_11" || l.name == "LampPost_12");
                var rb = _pc.GetComponent<Rigidbody2D>();
                Vector2 st = new Vector2(15f, -8.5f);
                if (rb) rb.position = st; _pc.transform.position = st;
                Go(15f, -11f);
                Go(15f, -14.6f);
                Go(11f, -14.2f);
                Go(10.9f, -19.2f);
                Go(15.2f, -20.2f);
                Go(19.6f, -18f);
                Go(19.4f, -14.6f);
                Go(15f, -16f, "off", 1.6f);
                Go(15.05f, -16f, "strong", 1.2f);
                Go(15f, -10.5f);
            }
            else
            {
                // 시작 → 첫 가로등(3.4,-29) 앞에서 등불 끄기 → 다시 켜고 북쪽 → 약하게 → 세게 → 네거리 → 서쪽 가로등
                Go(0f, -31f);
                Go(0.6f, -29.5f, "off", 2.6f);
                Go(0.6f, -29.4f, "strong", 1.2f);
                Go(0f, -20f, "weak", 0.4f);
                Go(0f, -12f, "strong", 0.6f);
                Go(0f, 1.5f);
                Go(-9f, 2f, "off", 2.4f);
                Go(-9.2f, 2f, "strong", 1.5f);
            }
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Lamp(Player.LanternLevel.Strong);
            yield return new WaitForSeconds(1.2f);
            var path = new List<Vector2>();
            foreach (var s in _steps)
            {
                float giveUp = Time.time + 20f;
                if (!Map.NavGrid.FindPath(_pc.transform.position, s.to, path, 40000)) { path.Clear(); path.Add(s.to); }
                int k = 0;
                while (_pc && k < path.Count && Time.time < giveUp)
                {
                    if (Vector2.Distance(_pc.transform.position, path[k]) < (k == path.Count - 1 ? 0.35f : 0.6f)) { k++; continue; }
                    Vector2 d = path[k] - (Vector2)_pc.transform.position;
                    _held.Clear();
                    Vector2 n = d.normalized;
                    if (n.x > 0.38f) _held.Add(Key.D); else if (n.x < -0.38f) _held.Add(Key.A);
                    if (n.y > 0.38f) _held.Add(Key.W); else if (n.y < -0.38f) _held.Add(Key.S);
                    _aim = (Vector2)_pc.transform.position + n * 3f + Vector2.up * 0.8f;
                    yield return null;
                }
                _held.Clear();
                if (Time.time >= giveUp) Debug.LogWarning($"[LightTour] {s.to} 에 못 갔다 - 지금 {_pc.transform.position}");
                if (s.act != null) Lamp(s.act == "off" ? Player.LanternLevel.Off : s.act == "weak" ? Player.LanternLevel.Weak : Player.LanternLevel.Strong);
                if (s.wait > 0f) yield return new WaitForSeconds(s.wait);
            }
            yield return new WaitForSeconds(1f);
            Done = true;
        }

        private void Lamp(Player.LanternLevel level)
        {
            if (_torch == null) return;
            if (level == Player.LanternLevel.Off) { _torch.SetLit(false); return; }
            _torch.SetLevel(level);
            _torch.SetLit(true);
        }

        private void Update()
        {
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(new List<Key>(_held).ToArray()));
            var cam = Camera.main;
            if (Mouse.current != null && cam)
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = cam.WorldToScreenPoint(new Vector3(_aim.x, _aim.y, 0f)) });
        }
    }
}
#endif
