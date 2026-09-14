using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Capstone.Map;

namespace Capstone.UI
{
    /// <summary>
    /// M 으로 여닫는 실시간 미니맵 (기획서 5.4).
    ///
    /// 탐험 단계별로 세 가지로 나눈다.
    ///   미탐색 - 존재 자체를 모른다. 아무것도 그리지 않는다.
    ///   발견   - 지나가며 존재만 확인한 상태. 윤곽만 흐리게.
    ///   방문   - 직접 가본 곳. 벽까지 온전히 보인다.
    /// 가운데 '발견' 단계가 핵심이다. 저쪽에 길이 있다는 것만 알려주고 내용은 감춰,
    /// 다음 목적지는 고를 수 있게 하되 가볼 이유는 남긴다.
    ///
    /// 기획서 구현 메모대로 Texture2D 한 장에 굽고 filterMode = Point 로 둔다.
    /// 플레이어와 탈출구처럼 움직이거나 강조할 표시는 텍스처에 굽지 않고 UI 로 얹는다.
    /// </summary>
    public class MinimapUI : MonoBehaviour
    {
        private const byte Unexplored = 0, Discovered = 1, Visited = 2;

        [Header("대상")]
        [SerializeField] private MazeData maze;
        [SerializeField] private Transform player;
        [SerializeField] private ExtractionPoint exit;

        [Header("조작")]
        [SerializeField] private Key toggleKey = Key.M;
        [Tooltip("켠 채로 시작할지")]
        [SerializeField] private bool openByDefault;

        [Header("해상도")]
        [Tooltip("타일 한 칸을 몇 픽셀로 그릴지")]
        [SerializeField] private int pixelsPerTile = 4;
        [Tooltip("화면에 표시할 때의 확대 배율")]
        [SerializeField] private float displayScale = 4.5f;

        [Header("탐험")]
        [Tooltip("플레이어 주변 몇 타일까지 '방문'으로 칠할지")]
        [SerializeField] private int visitRadius = 3;
        [Tooltip("방문한 칸에서 몇 타일까지 '발견'으로 드러낼지")]
        [SerializeField] private int discoverRadius = 6;
        [Tooltip("탐험 상태를 갱신하는 주기(초). 매 프레임 돌릴 필요는 없다")]
        [SerializeField] private float updateInterval = 0.12f;

        [Header("색")]
        [SerializeField] private Color visitedFloor    = new(0.30f, 0.29f, 0.31f, 1f);
        [SerializeField] private Color visitedWall     = new(0.62f, 0.60f, 0.64f, 1f);
        [SerializeField] private Color discoveredFloor = new(0.13f, 0.13f, 0.15f, 1f);
        [SerializeField] private Color discoveredWall  = new(0.24f, 0.23f, 0.26f, 1f);
        [SerializeField] private Color unexplored      = new(0.04f, 0.04f, 0.05f, 0.92f);
        [SerializeField] private Color playerColor     = new(0.98f, 0.88f, 0.55f, 1f);
        [SerializeField] private Color exitColor       = new(0.40f, 0.95f, 0.55f, 1f);

        public bool IsOpen { get; private set; }

        [Header("프리팹 조각")]
        [Tooltip("미니맵 전체를 감싸는 판. M 으로 여닫는다")]
        [SerializeField] private RectTransform root;
        [Tooltip("구운 지도 텍스처를 띄우는 RawImage")]
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform mapRect;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private RectTransform exitMarker;
        [SerializeField] private TMP_Text titleText;

        private Texture2D _tex;
        private Color32[] _pixels;
        private byte[,] _state;              // 안개 상태 - 원본(벽 배열)과 분리해 둔다
        private bool _dirty;
        private float _nextUpdate;

        private void Awake()
        {
            if (!maze) maze = FindFirstObjectByType<MazeData>();
            if (!exit) exit = FindFirstObjectByType<ExtractionPoint>();
            if (!player)
            {
                var pc = FindFirstObjectByType<Capstone.Player.PlayerController>();
                if (pc) player = pc.transform;
            }

            BindPieces();
            if (maze != null && maze.HasData) InitTexture();

            IsOpen = openByDefault;
            if (root) root.gameObject.SetActive(IsOpen);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame) Toggle();

            if (maze == null || !maze.HasData || player == null) return;

            // 안개는 지도를 닫아둔 동안에도 갱신해야 다시 열었을 때 최신이다
            if (Time.time >= _nextUpdate)
            {
                _nextUpdate = Time.time + updateInterval;
                UpdateExploration();
            }

            if (!IsOpen) return;
            if (_dirty) { Repaint(); _dirty = false; }
            UpdateMarkers();
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool on)
        {
            IsOpen = on;
            if (root) root.gameObject.SetActive(on);
            if (on) { Repaint(); _dirty = false; }
        }

        // ---------- 탐험 상태 ----------
        private void UpdateExploration()
        {
            int px, py;
            if (!maze.WorldToTile(player.position, out px, out py)) return;

            Mark(px, py, visitRadius, Visited);
            Mark(px, py, discoverRadius, Discovered);
        }

        /// <summary>이미 더 높은 단계면 낮추지 않는다.</summary>
        private void Mark(int cx, int cy, int radius, byte level)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= maze.TileWidth || y >= maze.TileHeight) continue;
                if (dx * dx + dy * dy > radius * radius) continue;      // 원형으로
                if (_state[x, y] >= level) continue;
                _state[x, y] = level;
                _dirty = true;
            }
        }

        // ---------- 텍스처 ----------
        private void InitTexture()
        {
            int w = maze.TileWidth * pixelsPerTile;
            int h = maze.TileHeight * pixelsPerTile;

            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;      // 기획서 5.4 구현 메모
            _tex.wrapMode = TextureWrapMode.Clamp;
            _pixels = new Color32[w * h];
            _state = new byte[maze.TileWidth, maze.TileHeight];

            mapImage.texture = _tex;
            mapRect.sizeDelta = new Vector2(w * displayScale, h * displayScale);
            Repaint();
        }

        private void Repaint()
        {
            if (_tex == null) return;
            int w = _tex.width;

            for (int ty = 0; ty < maze.TileHeight; ty++)
            for (int tx = 0; tx < maze.TileWidth; tx++)
            {
                byte s = _state[tx, ty];
                bool wall = maze.IsWall(tx, ty);

                Color32 c = s == Visited    ? (wall ? visitedWall : visitedFloor)
                          : s == Discovered ? (wall ? discoveredWall : discoveredFloor)
                          : (Color32)unexplored;

                // 텍스처는 아래에서 위로 채워지므로 세로를 뒤집는다
                int baseY = (maze.TileHeight - 1 - ty) * pixelsPerTile;
                int baseX = tx * pixelsPerTile;
                for (int py = 0; py < pixelsPerTile; py++)
                for (int px = 0; px < pixelsPerTile; px++)
                    _pixels[(baseY + py) * w + baseX + px] = c;
            }

            _tex.SetPixels32(_pixels);
            _tex.Apply(false);
        }

        // ---------- 마커 ----------
        private void UpdateMarkers()
        {
            // 플레이어
            int px, py;
            if (maze.WorldToTile(player.position, out px, out py))
            {
                playerMarker.gameObject.SetActive(true);
                playerMarker.anchoredPosition = TileToMapLocal(px, py);

                var pc = player.GetComponent<Capstone.Player.PlayerController>();
                if (pc != null)
                {
                    float deg = Mathf.Atan2(pc.AimDirection.y, pc.AimDirection.x) * Mathf.Rad2Deg - 90f;
                    playerMarker.localRotation = Quaternion.Euler(0f, 0f, deg);
                }
            }
            else playerMarker.gameObject.SetActive(false);

            // 탈출구 - 발견하기 전에는 숨긴다
            if (exit == null) { exitMarker.gameObject.SetActive(false); return; }

            int ex, ey;
            bool known = maze.WorldToTile(exit.transform.position, out ex, out ey)
                         && _state[ex, ey] != Unexplored;
            exitMarker.gameObject.SetActive(known);
            if (known)
            {
                exitMarker.anchoredPosition = TileToMapLocal(ex, ey);
                float pulse = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
                exitMarker.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.25f, pulse);
            }
        }

        /// <summary>타일 좌표 -> 지도 이미지 안의 로컬 좌표 (중앙 피벗 기준).</summary>
        private Vector2 TileToMapLocal(int tx, int ty)
        {
            float w = mapRect.sizeDelta.x, h = mapRect.sizeDelta.y;
            float cell = pixelsPerTile * displayScale;
            return new Vector2(
                -w * 0.5f + (tx + 0.5f) * cell,
                 h * 0.5f - (ty + 0.5f) * cell);
        }

        // ---------- 프리팹 조각 잇기 ----------
        /// <summary>
        /// 인스펙터에서 안 꽂았을 때만 이름으로 찾는다.
        /// 계층은 Minimap / Frame / Map / PlayerMarker · ExitMarker / Title · Hint 순이다.
        /// </summary>
        private void BindPieces()
        {
            if (root == null)
            {
                var t = transform.Find("Minimap");
                if (t != null) root = t as RectTransform;
            }
            if (root == null) return;

            var frame = root.Find("Frame");
            if (mapRect == null && frame != null) mapRect = frame.Find("Map") as RectTransform;
            if (mapImage == null && mapRect != null) mapImage = mapRect.GetComponent<RawImage>();
            if (mapRect != null)
            {
                if (playerMarker == null) playerMarker = mapRect.Find("PlayerMarker") as RectTransform;
                if (exitMarker == null)   exitMarker   = mapRect.Find("ExitMarker") as RectTransform;
            }
            if (titleText == null)
            {
                var t = root.Find("Title");
                if (t != null) titleText = t.GetComponent<TMP_Text>();
            }

            if (playerMarker != null) playerMarker.gameObject.SetActive(false);
            if (exitMarker != null)   exitMarker.gameObject.SetActive(false);
        }
    }
}
