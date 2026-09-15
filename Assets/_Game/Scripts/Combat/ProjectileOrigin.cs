using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 발사 원점을 벽 밖으로 붙잡아 둔다.
    ///
    /// 탑다운이라 화면의 '위'는 하늘이 아니라 <b>북쪽</b>이다. 총구를 어깨 높이(=+Y)에 두면
    /// 그림으로는 어깨에서 나가는 것처럼 보이지만 실제로는 몸에서 북쪽으로 한참 떨어진 자리에서
    /// 화살이 태어난다. 북쪽에 벽이 있으면 벽 <b>속에서</b> 생겨 그대로 박힌다.
    /// 쏜 사람 눈에는 "아무것도 없는데 화살이 사라진" 것으로 보인다.
    ///
    /// 그래서 원점은 광원과 같은 평면(발밑에서 조금 위)에 두고, 그래도 벽에 걸리면 몸으로 당긴다.
    /// "빛이 닿는 데까지 화살도 간다"가 되어야 눈과 손이 어긋나지 않는다.
    /// </summary>
    public static class ProjectileOrigin
    {
        /// <summary>몸에서 총구까지 벽을 지나면 몸에서 쏜다.</summary>
        public static Vector2 Resolve(Vector2 body, Vector2 desired, LayerMask wallMask)
        {
            if (wallMask.value == 0) return desired;
            if (Physics2D.OverlapPoint(body, wallMask) != null) return body;      // 몸부터 벽 속이면 더 손쓸 게 없다
            if (Physics2D.OverlapPoint(desired, wallMask) != null) return body;
            return Physics2D.Linecast(body, desired, wallMask).collider != null ? body : desired;
        }

        /// <summary>인스펙터에서 안 채웠을 때 쓸 기본 벽 레이어.</summary>
        public static LayerMask DefaultWallMask(LayerMask configured)
        {
            if (configured.value != 0) return configured;
            int wall = LayerMask.NameToLayer(Core.GameLayers.Wall);
            return wall >= 0 ? (LayerMask)(1 << wall) : (LayerMask)0;
        }
    }
}
