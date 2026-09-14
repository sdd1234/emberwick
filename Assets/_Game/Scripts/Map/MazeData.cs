using UnityEngine;

namespace Capstone.Map
{
    /// <summary>
    /// 미로의 타일 격자를 씬에 남겨 둔다.
    /// 미니맵은 벽 오브젝트를 훑는 대신 이 데이터를 읽는다 (기획서 5.4 -
    /// "미니맵 전용 데이터를 따로 만들지 않고 격자 배열과 문 정보를 그대로 원본으로 삼는다").
    /// bool[,] 는 직렬화가 안 되므로 1차원으로 펴서 저장한다.
    /// </summary>
    public class MazeData : MonoBehaviour
    {
        [SerializeField] private int tileWidth;
        [SerializeField] private int tileHeight;
        [SerializeField] private float tileSize = 2.5f;
        [SerializeField] private bool[] walls;          // 행 우선, true = 벽

        [SerializeField] private Vector2 spawnPosition;
        [SerializeField] private Vector2 exitPosition;

        public int TileWidth => tileWidth;
        public int TileHeight => tileHeight;
        public float TileSize => tileSize;
        public Vector2 SpawnPosition => spawnPosition;
        public Vector2 ExitPosition => exitPosition;
        public bool HasData => walls != null && walls.Length == tileWidth * tileHeight && tileWidth > 0;

        public void Store(bool[,] grid, int w, int h, float size, Vector2 spawn, Vector2 exit)
        {
            tileWidth = w; tileHeight = h; tileSize = size;
            spawnPosition = spawn; exitPosition = exit;

            walls = new bool[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                walls[y * w + x] = grid[x, y];
        }

        public bool IsWall(int tx, int ty)
        {
            if (!HasData) return true;
            if (tx < 0 || ty < 0 || tx >= tileWidth || ty >= tileHeight) return true;
            return walls[ty * tileWidth + tx];
        }

        /// <summary>타일 중심의 월드 좌표. MazeBuilder 의 배치 규칙과 반드시 같아야 한다.</summary>
        public Vector2 TileToWorld(int tx, int ty)
            => new((tx - (tileWidth - 1) * 0.5f) * tileSize,
                   ((tileHeight - 1) * 0.5f - ty) * tileSize);

        public bool WorldToTile(Vector2 world, out int tx, out int ty)
        {
            tx = Mathf.RoundToInt(world.x / tileSize + (tileWidth - 1) * 0.5f);
            ty = Mathf.RoundToInt((tileHeight - 1) * 0.5f - world.y / tileSize);
            return tx >= 0 && ty >= 0 && tx < tileWidth && ty < tileHeight;
        }
    }
}
