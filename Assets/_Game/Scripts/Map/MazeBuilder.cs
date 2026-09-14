using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Map
{
    /// <summary>
    /// 미로 맵을 만들어 씬에 굽는다.
    /// 런타임 절차 생성이 아니라 에디터에서 한 번 돌려 결과를 씬에 남기는 방식이다
    /// (기획서 5 의 절차 생성은 나중에 이 알고리즘을 그대로 옮기면 된다).
    ///
    /// 1) 재귀적 백트래킹으로 완전 미로를 판다
    /// 2) 브레이딩 - 막다른 길 일부를 뚫어 우회로를 만든다 (기획서 5.1)
    ///    이게 없으면 들어간 길로만 되돌아 나와야 해서 추격을 따돌릴 수 없다
    /// 3) 남은 막다른 길에 상자를 놓는다 - 위험을 무릅쓰고 들어갈 이유를 만든다
    /// </summary>
    public class MazeBuilder : MonoBehaviour
    {
        [Header("크기")]
        [Tooltip("미로 칸 수 (가로)")]
        [SerializeField] private int cols = 11;
        [Tooltip("미로 칸 수 (세로)")]
        [SerializeField] private int rows = 8;
        [Tooltip("타일 한 칸의 한 변 길이 (m). 통로 폭이자 벽 두께가 된다")]
        [SerializeField] private float tileSize = 2.5f;

        [Header("브레이딩 (기획서 5.1)")]
        [Tooltip("막다른 길을 뚫어 우회로로 만들 확률")]
        [SerializeField, Range(0f, 1f)] private float braidChance = 0.35f;

        [Header("배치")]
        [SerializeField] private int lootBoxCount = 14;
        [SerializeField] private int meleeEnemyCount = 7;
        [SerializeField] private int rangedEnemyCount = 5;
        [Tooltip("스폰 지점에서 이 거리 안에는 적을 두지 않는다 (m)")]
        [SerializeField] private float safeRadius = 10f;
        [Tooltip("시작 지점 근처에 따로 깔아둘 원거리 적 수 (교전 테스트용)")]
        [SerializeField] private int nearSpawnRangedCount = 3;
        [Tooltip("그 적들을 둘 거리 구간 (m)")]
        [SerializeField] private Vector2 nearSpawnBand = new Vector2(5f, 12f);

        [System.Serializable]
        public struct ContainerSpawn
        {
            public GameObject prefab;
            [Tooltip("뽑힐 가중치. 클수록 자주 나온다")]
            public float weight;
            [Tooltip("클수록 미로 깊숙한 막다른 길에 놓인다. 좋은 상자에 높게 준다")]
            public int depthTier;
        }

        [Header("상자 종류")]
        [Tooltip("비워두면 아래 lootBoxPrefab 하나만 쓴다")]
        [SerializeField] private ContainerSpawn[] containerTable;

        [Header("프리팹 / 스프라이트")]
        [Tooltip("바닥 한 장. 맵 크기만큼 늘려서 쓴다")]
        [SerializeField] private GameObject floorPrefab;
        [Tooltip("벽 한 칸. 가로로 이어진 만큼 늘려서 쓴다")]
        [SerializeField] private GameObject wallPrefab;
        [SerializeField] private GameObject lootBoxPrefab;
        [SerializeField] private GameObject meleeEnemyPrefab;
        [SerializeField] private GameObject rangedEnemyPrefab;
        [Tooltip("지하 탈출 해치. 빛도 프리팹 안에 들어 있다")]
        [SerializeField] private GameObject exitPrefab;

        [Header("씬 루트")]
        [SerializeField] private Transform mapRoot;
        [SerializeField] private Transform enemyRoot;
        [SerializeField] private Transform lootRoot;

        [Header("결과")]
        [SerializeField] private Vector2 spawnPosition;
        [SerializeField] private Vector2 exitPosition;
        public Vector2 SpawnPosition => spawnPosition;
        public Vector2 ExitPosition => exitPosition;

        // 타일 격자 - true 면 벽
        private bool[,] _wall;
        private int _tw, _th;

        /// <summary>미로를 새로 만들어 씬에 배치한다. 기존 결과는 지운다.</summary>
        public void Build(int seed)
        {
            Random.InitState(seed);

            _tw = cols * 2 + 1;
            _th = rows * 2 + 1;

            CarveMaze();
            Braid();
            ClearExisting();
            SpawnFloor();
            SpawnWalls();

            var open = CollectOpenCells();
            var deadEnds = CollectDeadEnds();

            // 스폰은 좌하단 구석 칸 - 기획서 5.2 의 "가장자리 구역"
            spawnPosition = CellToWorld(0, rows - 1);

            // 탈출 지점은 스폰에서 가장 먼 칸 (기획서 5.2 - 최소 4칸 거리)
            var exitCell = FarthestCellFrom(new Vector2Int(0, rows - 1), open);
            exitPosition = CellToWorld(exitCell.x, exitCell.y);
            SpawnExit();

            PlaceLoot(deadEnds, open);
            PlaceEnemies(open);
            PlaceNearSpawnRanged(open);
            StoreData();
        }

        private Vector2Int FarthestCellFrom(Vector2Int from, List<Vector2Int> open)
        {
            var best = from;
            int bestDist = -1;
            foreach (var c in open)
            {
                int d = Mathf.Abs(c.x - from.x) + Mathf.Abs(c.y - from.y);   // 맨해튼 거리면 충분하다
                if (d <= bestDist) continue;
                bestDist = d; best = c;
            }
            return best;
        }

        private void SpawnExit()
        {
            if (exitPrefab == null) { Debug.LogError("[MazeBuilder] exitPrefab 이 비어 있다"); return; }

            var go = Spawn(exitPrefab, exitPosition, mapRoot);
            go.name = "ExtractionPoint";
            // 스프라이트가 1x1 유닛이라 타일 크기를 곱하면 통로에 꼭 맞는다
            go.transform.localScale = Vector3.one * (tileSize * 0.9f);
        }

        private void PlaceNearSpawnRanged(List<Vector2Int> open)
        {
            if (rangedEnemyPrefab == null || nearSpawnRangedCount <= 0) return;

            var band = new List<Vector2Int>();
            foreach (var c in open)
            {
                float d = Vector2.Distance(CellToWorld(c.x, c.y), spawnPosition);
                if (d >= nearSpawnBand.x && d <= nearSpawnBand.y) band.Add(c);
            }
            Shuffle(band);

            int n = Mathf.Min(nearSpawnRangedCount, band.Count);
            for (int i = 0; i < n; i++)
                Spawn(rangedEnemyPrefab, CellToWorld(band[i].x, band[i].y), enemyRoot)
                    .name = $"Enemy_Ranged_Near_{i + 1}";
        }

        private void StoreData()
        {
            var data = GetComponent<MazeData>();
            if (data == null) data = gameObject.AddComponent<MazeData>();
            data.Store(_wall, _tw, _th, tileSize, spawnPosition, exitPosition);
        }

        // ---------- 1) 재귀적 백트래킹 ----------
        private void CarveMaze()
        {
            _wall = new bool[_tw, _th];
            for (int x = 0; x < _tw; x++)
            for (int y = 0; y < _th; y++)
                _wall[x, y] = true;

            var visited = new bool[cols, rows];
            var stack = new Stack<Vector2Int>();
            var start = new Vector2Int(0, 0);
            visited[0, 0] = true;
            _wall[1, 1] = false;
            stack.Push(start);

            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

            while (stack.Count > 0)
            {
                var cur = stack.Peek();
                var candidates = new List<Vector2Int>();

                foreach (var d in dirs)
                {
                    var n = cur + d;
                    if (n.x < 0 || n.y < 0 || n.x >= cols || n.y >= rows) continue;
                    if (visited[n.x, n.y]) continue;
                    candidates.Add(n);
                }

                if (candidates.Count == 0) { stack.Pop(); continue; }

                var next = candidates[Random.Range(0, candidates.Count)];
                visited[next.x, next.y] = true;

                // 칸과 칸 사이의 벽을 뚫는다
                int wx = cur.x * 2 + 1 + (next.x - cur.x);
                int wy = cur.y * 2 + 1 + (next.y - cur.y);
                _wall[wx, wy] = false;
                _wall[next.x * 2 + 1, next.y * 2 + 1] = false;

                stack.Push(next);
            }
        }

        // ---------- 2) 브레이딩 ----------
        private void Braid()
        {
            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                if (CountOpenNeighbours(cx, cy) != 1) continue;       // 막다른 길만
                if (Random.value > braidChance) continue;

                // 아직 막혀 있는 방향 중 하나를 뚫어 우회로를 만든다
                var blocked = new List<Vector2Int>();
                foreach (var d in dirs)
                {
                    var n = new Vector2Int(cx + d.x, cy + d.y);
                    if (n.x < 0 || n.y < 0 || n.x >= cols || n.y >= rows) continue;
                    int wx = cx * 2 + 1 + d.x, wy = cy * 2 + 1 + d.y;
                    if (_wall[wx, wy]) blocked.Add(d);
                }
                if (blocked.Count == 0) continue;

                var pick = blocked[Random.Range(0, blocked.Count)];
                _wall[cx * 2 + 1 + pick.x, cy * 2 + 1 + pick.y] = false;
            }
        }

        private int CountOpenNeighbours(int cx, int cy)
        {
            int n = 0;
            if (!_wall[cx * 2 + 2, cy * 2 + 1]) n++;
            if (!_wall[cx * 2,     cy * 2 + 1]) n++;
            if (!_wall[cx * 2 + 1, cy * 2 + 2]) n++;
            if (!_wall[cx * 2 + 1, cy * 2    ]) n++;
            return n;
        }

        // ---------- 좌표 ----------
        private Vector2 TileToWorld(int tx, int ty)
            => new((tx - (_tw - 1) * 0.5f) * tileSize, ((_th - 1) * 0.5f - ty) * tileSize);

        private Vector2 CellToWorld(int cx, int cy) => TileToWorld(cx * 2 + 1, cy * 2 + 1);

        // ---------- 3) 씬에 굽기 ----------
        private void ClearExisting()
        {
            ClearChildren(mapRoot);
            ClearChildren(enemyRoot);
            ClearChildren(lootRoot);
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyImmediate(root.GetChild(i).gameObject);
        }

        private void SpawnFloor()
        {
            if (floorPrefab == null) { Debug.LogError("[MazeBuilder] floorPrefab 이 비어 있다"); return; }

            var go = Spawn(floorPrefab, Vector2.zero, mapRoot);
            go.name = "Floor";
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(_tw * tileSize, _th * tileSize, 1f);
        }

        /// <summary>
        /// 벽 타일을 가로로 이어 붙여 하나의 오브젝트로 만든다.
        /// 타일마다 오브젝트를 만들면 400개 가까이 나와서 씬이 무거워진다.
        /// </summary>
        private void SpawnWalls()
        {
            if (wallPrefab == null) { Debug.LogError("[MazeBuilder] wallPrefab 이 비어 있다"); return; }

            var used = new bool[_tw, _th];
            int count = 0;

            for (int y = 0; y < _th; y++)
            for (int x = 0; x < _tw; x++)
            {
                if (!_wall[x, y] || used[x, y]) continue;

                int run = 0;
                while (x + run < _tw && _wall[x + run, y] && !used[x + run, y]) run++;
                for (int i = 0; i < run; i++) used[x + i, y] = true;

                Vector2 a = TileToWorld(x, y);
                Vector2 b = TileToWorld(x + run - 1, y);

                var go = Spawn(wallPrefab, (a + b) * 0.5f, mapRoot);
                go.name = $"Wall_{x}_{y}";
                go.transform.localScale = new Vector3(run * tileSize, tileSize, 1f);
                count++;
            }
            Debug.Log($"[MazeBuilder] 벽 오브젝트 {count}개 (타일 {_tw}x{_th})");
        }

        private List<Vector2Int> CollectOpenCells()
        {
            var list = new List<Vector2Int>();
            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
                list.Add(new Vector2Int(cx, cy));
            return list;
        }

        private List<Vector2Int> CollectDeadEnds()
        {
            var list = new List<Vector2Int>();
            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
                if (CountOpenNeighbours(cx, cy) == 1) list.Add(new Vector2Int(cx, cy));
            return list;
        }

        private void PlaceLoot(List<Vector2Int> deadEnds, List<Vector2Int> open)
        {
            bool useTable = containerTable != null && containerTable.Length > 0;
            if (!useTable && lootBoxPrefab == null) return;

            // 막다른 길을 스폰에서 먼 순서로 정렬한다.
            // 좋은 상자를 깊은 곳에 두면 "더 들어갈 것인가"라는 선택이 생긴다.
            var ends = new List<Vector2Int>(deadEnds);
            ends.Sort((a, b) =>
                Vector2.Distance(CellToWorld(b.x, b.y), spawnPosition)
                .CompareTo(Vector2.Distance(CellToWorld(a.x, a.y), spawnPosition)));

            var filler = new List<Vector2Int>(open);
            Shuffle(filler);

            // 배치 후보: 깊은 막다른 길 우선, 모자라면 아무 칸
            var spots = new List<Vector2Int>(ends);
            foreach (var f in filler) if (!spots.Contains(f)) spots.Add(f);

            // 등급이 높은(depthTier 큰) 상자부터 깊은 자리에 넣는다
            var order = new List<ContainerSpawn>();
            if (useTable)
            {
                var byTier = new List<ContainerSpawn>(containerTable);
                byTier.Sort((a, b) => b.depthTier.CompareTo(a.depthTier));

                // 상위 티어는 개수를 적게, 하위 티어는 나머지를 가중치로 채운다
                int highCount = Mathf.Min(byTier.Count, Mathf.Max(1, lootBoxCount / 4));
                for (int i = 0; i < highCount && i < byTier.Count; i++)
                    if (byTier[i].prefab != null) order.Add(byTier[i]);

                while (order.Count < lootBoxCount)
                {
                    var pick = PickContainer();
                    if (pick.prefab == null) break;
                    order.Add(pick);
                }
            }

            int placed = 0;
            for (int i = 0; i < spots.Count && placed < lootBoxCount; i++)
            {
                var p = CellToWorld(spots[i].x, spots[i].y);
                if (Vector2.Distance(p, spawnPosition) < 3f) continue;   // 스폰 바로 앞은 비운다

                GameObject prefab = useTable && placed < order.Count ? order[placed].prefab : lootBoxPrefab;
                if (prefab == null) continue;

                Spawn(prefab, p, lootRoot).name = $"{prefab.name}_{placed + 1}";
                placed++;
            }
            Debug.Log($"[MazeBuilder] 상자 {placed}개 배치 (막다른 길 {ends.Count}곳)");
        }

        /// <summary>가중치로 상자 종류 하나를 뽑는다.</summary>
        private ContainerSpawn PickContainer()
        {
            float total = 0f;
            foreach (var c in containerTable) if (c.prefab != null) total += Mathf.Max(0f, c.weight);
            if (total <= 0f) return default;

            float roll = Random.Range(0f, total);
            foreach (var c in containerTable)
            {
                if (c.prefab == null) continue;
                roll -= Mathf.Max(0f, c.weight);
                if (roll <= 0f) return c;
            }
            return containerTable[0];
        }

        private void PlaceEnemies(List<Vector2Int> open)
        {
            var spots = new List<Vector2Int>(open);
            Shuffle(spots);

            int placed = 0, wantMelee = meleeEnemyCount, wantRanged = rangedEnemyCount;
            foreach (var c in spots)
            {
                if (wantMelee <= 0 && wantRanged <= 0) break;

                var p = CellToWorld(c.x, c.y);
                if (Vector2.Distance(p, spawnPosition) < safeRadius) continue;   // 시작하자마자 죽지 않게

                GameObject prefab;
                string label;
                if (wantMelee >= wantRanged && meleeEnemyPrefab != null)
                { prefab = meleeEnemyPrefab; label = "Melee"; wantMelee--; }
                else if (rangedEnemyPrefab != null)
                { prefab = rangedEnemyPrefab; label = "Ranged"; wantRanged--; }
                else break;

                Spawn(prefab, p, enemyRoot).name = $"Enemy_{label}_{++placed}";
            }
        }

        /// <summary>
        /// 에디터에서는 프리팹 링크를 유지한 채 배치한다.
        /// 그냥 Instantiate 하면 사본이 되어, 나중에 프리팹을 고쳐도 맵의 적들이 안 따라온다.
        /// </summary>
        private GameObject Spawn(GameObject prefab, Vector2 pos, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = pos;
                return go;
            }
#endif
            return Instantiate(prefab, pos, Quaternion.identity, parent);
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
