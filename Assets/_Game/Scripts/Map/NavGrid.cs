using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.Map
{
    /// <summary>
    /// 벽을 실제로 훑어 만든 격자 위에서 A* 로 길을 찾는다.
    ///
    /// 미로는 에디터에서 구워 씬에 남기므로(MazeBuilder), 런타임에 지형을 알려면 물리로 재는 수밖에 없다.
    /// 씬을 한 번 훑어 "이 칸에 서면 벽에 끼는가"를 미리 계산해 두고, 그 위에서만 길을 찾는다.
    /// 손으로 놓은 벽이든 구운 벽이든 콜라이더만 있으면 똑같이 잡힌다.
    ///
    /// MonoBehaviour 가 아니라 static 인 이유는 GameSession 과 같다 - 그릴 것도 움직일 것도 없다.
    /// 씬이 바뀌면 스스로 다시 굽는다(씬 핸들을 기억해 둔다).
    /// </summary>
    public static class NavGrid
    {
        /// <summary>격자 한 칸. 미로 타일(2.5m)의 절반이라 통로 한 칸에 두 줄이 들어간다.</summary>
        public const float CellSize = 1.25f;
        /// <summary>벽 범위 바깥으로 더 잡아두는 여유. 맵 밖에서 들어오는 경우가 있다.</summary>
        private const float BoundsMargin = 3f;

        private static bool[] _blocked;
        private static int _w, _h;
        private static Vector2 _min;          // (0,0) 칸의 중심
        private static int _builtScene = -1;
        private static int _builtMask;
        private static float _builtRadius = -1f;

        // A* 작업 공간. 매번 새로 잡으면 쓰레기가 쌓이므로 재사용하고,
        // 세대 도장(_stamp)으로 초기화를 대신한다 - 배열을 매번 지울 필요가 없다
        private static float[] _g;
        private static int[] _came;
        private static int[] _stamp;
        private static int[] _heap;
        private static float[] _heapKey;
        private static int _heapCount;
        private static int _gen;

        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public static bool Ready => _blocked != null;
        public static int Columns => _w;
        public static int Rows => _h;

        /// <summary>아직 안 구웠거나 씬이 바뀌었으면 굽는다. 매 프레임 불러도 된다.</summary>
        public static void EnsureBuilt(LayerMask obstacleMask, float agentRadius)
        {
            int scene = SceneManager.GetActiveScene().handle;
            if (_blocked != null && scene == _builtScene && obstacleMask.value == _builtMask
                && Mathf.Approximately(agentRadius, _builtRadius)) return;

            Build(obstacleMask, agentRadius);
            _builtScene = scene;
            _builtMask = obstacleMask.value;
            _builtRadius = agentRadius;
        }

        private static void Build(LayerMask obstacleMask, float agentRadius)
        {
            var walls = new List<Collider2D>();
            foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            {
                if (c.isTrigger) continue;
                if ((obstacleMask.value & (1 << c.gameObject.layer)) == 0) continue;
                walls.Add(c);
            }

            if (walls.Count == 0)
            {
                // 벽이 없으면 격자도 필요 없다. 부르는 쪽은 직선 이동으로 떨어진다
                _blocked = null;
                return;
            }

            Bounds b = walls[0].bounds;
            for (int i = 1; i < walls.Count; i++) b.Encapsulate(walls[i].bounds);
            b.Expand(BoundsMargin * 2f);

            _w = Mathf.Max(1, Mathf.CeilToInt(b.size.x / CellSize));
            _h = Mathf.Max(1, Mathf.CeilToInt(b.size.y / CellSize));
            _min = new Vector2(b.min.x + CellSize * 0.5f, b.min.y + CellSize * 0.5f);

            _blocked = new bool[_w * _h];

            // 칸 한가운데에 몸 크기의 원을 놓아 보고 벽에 닿으면 못 서는 칸으로 친다.
            // 벽 면에서 몸 반지름만큼은 떨어져 있어야 하므로 이것만으로 벽 비비기가 사라진다
            float probe = agentRadius;
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
                _blocked[y * _w + x] = Physics2D.OverlapCircle(CellCenter(x, y), probe, obstacleMask) != null;

            int n = _w * _h;
            if (_g == null || _g.Length < n)
            {
                _g = new float[n];
                _came = new int[n];
                _stamp = new int[n];
                _heap = new int[n + 1];
                _heapKey = new float[n + 1];
            }
            _gen = 0;
            System.Array.Clear(_stamp, 0, _stamp.Length);
        }

        // ---------- 좌표 ----------
        public static Vector2 CellCenter(int x, int y) => new(_min.x + x * CellSize, _min.y + y * CellSize);
        private static Vector2 CellCenter(int i) => CellCenter(i % _w, i / _w);

        private static bool TryCell(Vector2 world, out int x, out int y)
        {
            x = Mathf.RoundToInt((world.x - _min.x) / CellSize);
            y = Mathf.RoundToInt((world.y - _min.y) / CellSize);
            return x >= 0 && y >= 0 && x < _w && y < _h;
        }

        public static bool IsWalkable(Vector2 world)
        {
            if (!Ready) return true;
            int x, y;
            if (!TryCell(world, out x, out y)) return false;
            return !_blocked[y * _w + x];
        }

        /// <summary>그 자리가 벽이면 가까운 빈 칸을 찾아 준다. 못 찾으면 -1.</summary>
        private static int NearestFree(Vector2 world, int maxRing = 4)
        {
            int cx, cy;
            if (!TryCell(world, out cx, out cy))
            {
                cx = Mathf.Clamp(cx, 0, _w - 1);
                cy = Mathf.Clamp(cy, 0, _h - 1);
            }
            if (!_blocked[cy * _w + cx]) return cy * _w + cx;

            for (int r = 1; r <= maxRing; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;   // 껍질만
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
                if (!_blocked[y * _w + x]) return y * _w + x;
            }
            return -1;
        }

        // ---------- 길찾기 ----------
        /// <summary>from 에서 to 까지의 길을 world 좌표 목록으로 채운다. 못 찾으면 false.</summary>
        public static bool FindPath(Vector2 from, Vector2 to, List<Vector2> result, int nodeBudget = 3000)
        {
            result.Clear();
            if (!Ready) return false;

            int start = NearestFree(from);
            int goal = NearestFree(to);
            if (start < 0 || goal < 0) return false;
            if (start == goal) { result.Add(CellCenter(goal)); return true; }

            _gen++;
            _heapCount = 0;
            _g[start] = 0f;
            _came[start] = -1;
            _stamp[start] = _gen;
            HeapPush(start, Heuristic(start, goal));

            int expanded = 0;
            while (_heapCount > 0)
            {
                int cur = HeapPop();
                if (cur == goal) return Reconstruct(goal, result);
                if (++expanded > nodeBudget) return false;

                int cx = cur % _w, cy = cur / _w;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;

                    int ni = ny * _w + nx;
                    if (_blocked[ni]) continue;

                    // 대각선으로 벽 모서리를 뚫고 지나가지 않게 한다.
                    // 이걸 빼면 길은 나오는데 실제로는 벽에 걸려 못 간다
                    if (d >= 4 && (_blocked[cy * _w + nx] || _blocked[ny * _w + cx])) continue;

                    float step = d >= 4 ? 1.41421356f : 1f;
                    float g = _g[cur] + step;

                    if (_stamp[ni] == _gen && g >= _g[ni]) continue;
                    _stamp[ni] = _gen;
                    _g[ni] = g;
                    _came[ni] = cur;
                    HeapPush(ni, g + Heuristic(ni, goal));
                }
            }
            return false;
        }

        private static bool Reconstruct(int goal, List<Vector2> result)
        {
            int guard = 0;
            for (int i = goal; i >= 0; i = _came[i])
            {
                result.Add(CellCenter(i));
                if (++guard > _w * _h) break;
            }
            result.Reverse();
            if (result.Count > 0) result.RemoveAt(0);      // 서 있는 칸은 목표가 아니다
            return result.Count > 0;
        }

        /// <summary>옥타일 거리. 대각선을 아는 휴리스틱이라야 격자에서 헤매지 않는다.</summary>
        private static float Heuristic(int a, int b)
        {
            int dx = Mathf.Abs(a % _w - b % _w);
            int dy = Mathf.Abs(a / _w - b / _w);
            return (dx + dy) + (1.41421356f - 2f) * Mathf.Min(dx, dy);
        }

        // ---------- 이진 힙 ----------
        private static void HeapPush(int cell, float key)
        {
            int i = ++_heapCount;
            if (i >= _heap.Length) { _heapCount--; return; }      // 넘칠 일은 없지만 안전장치
            _heap[i] = cell; _heapKey[i] = key;
            while (i > 1 && _heapKey[i >> 1] > _heapKey[i])
            {
                Swap(i, i >> 1);
                i >>= 1;
            }
        }

        private static int HeapPop()
        {
            int top = _heap[1];
            _heap[1] = _heap[_heapCount];
            _heapKey[1] = _heapKey[_heapCount];
            _heapCount--;

            int i = 1;
            while (true)
            {
                int l = i << 1, r = l + 1, best = i;
                if (l <= _heapCount && _heapKey[l] < _heapKey[best]) best = l;
                if (r <= _heapCount && _heapKey[r] < _heapKey[best]) best = r;
                if (best == i) break;
                Swap(i, best);
                i = best;
            }
            return top;
        }

        private static void Swap(int a, int b)
        {
            int c = _heap[a]; _heap[a] = _heap[b]; _heap[b] = c;
            float k = _heapKey[a]; _heapKey[a] = _heapKey[b]; _heapKey[b] = k;
        }

        /// <summary>씬을 다시 구웠을 때처럼 강제로 다시 훑게 한다.</summary>
        public static void Invalidate() { _blocked = null; _builtScene = -1; }
    }
}
