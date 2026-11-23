using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.Map
{
    /// <summary>
    /// 발밑이 흙인지 풀인지 (2026-11-03 - 흙길 · 풀밭 발소리가 따로 왔다).
    /// 흙길(Roads 아래 그림) · 마을 땅(GroundPaint)은 구불구불하게 구운 한 장 그림이라 그 픽셀이 칠해져 있으면 흙이다.
    /// 집 바닥(Floor)도 흙으로 친다 - 나무 발소리가 없다. 그려진 땅이 없는 씬은 전부 흙이다.
    /// </summary>
    public static class GroundSurface
    {
        private const float PaintMargin = 1.6f;     // GroundPainter 가 길 둘레에 둔 여유 - 읽을 수 없는 그림은 이만큼 줄여 상자로 본다

        private static int _scene = -1;
        private static readonly List<SpriteRenderer> _paint = new();
        private static readonly List<Bounds> _floors = new();

        public static bool IsGrass(Vector2 at)
        {
            int id = SceneManager.GetActiveScene().handle;
            if (id != _scene) { _scene = id; Collect(); }
            if (_paint.Count == 0) return false;

            foreach (var f in _floors) if (f.Contains(new Vector3(at.x, at.y, f.center.z))) return false;
            foreach (var sr in _paint) if (sr != null && Painted(sr, at)) return false;
            return true;
        }

        private static void Collect()
        {
            _paint.Clear(); _floors.Clear();
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                var parent = sr.transform.parent;
                if (sr.name == "GroundPaint" || (parent != null && parent.name == "Roads")) _paint.Add(sr);
                else if (sr.name == "Floor") _floors.Add(sr.bounds);
            }
        }

        private static bool Painted(SpriteRenderer sr, Vector2 at)
        {
            var b = sr.bounds;
            if (at.x < b.min.x || at.x > b.max.x || at.y < b.min.y || at.y > b.max.y) return false;
            var sprite = sr.sprite;
            if (sprite == null) return false;
            var tex = sprite.texture;
            if (!tex.isReadable)
                return at.x > b.min.x + PaintMargin && at.x < b.max.x - PaintMargin && at.y > b.min.y + PaintMargin && at.y < b.max.y - PaintMargin;

            Vector2 local = sr.transform.InverseTransformPoint(at);
            int x = Mathf.FloorToInt(sprite.rect.x + sprite.pivot.x + local.x * sprite.pixelsPerUnit);
            int y = Mathf.FloorToInt(sprite.rect.y + sprite.pivot.y + local.y * sprite.pixelsPerUnit);
            return tex.GetPixel(x, y).a > 0.5f;
        }
    }
}
