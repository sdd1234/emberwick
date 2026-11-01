using System;
using UnityEngine;

namespace Capstone.Core
{
    /// <summary>
    /// 빛 신호 - NoiseSystem 의 빛 버전 (피드백 - 도적은 빛이나 소리에 반응해 그 자리로 온다).
    /// 등불을 켜고 끄거나 빛 플라스크가 터지는 것처럼 "갑자기 밝아지는 일"을 알린다.
    /// 계속 켜져 있는 빛(플레이어 등불 · 적 횃불)은 보는 쪽이 직접 찾는다.
    /// </summary>
    public static class LightSignal
    {
        /// <summary>(빛난 자리, 보이는 반경 m, 낸 쪽)</summary>
        public static event Action<Vector2, float, GameObject> OnFlash;

        public static void Emit(Vector2 position, float radius, GameObject source)
        {
            if (radius <= 0f) return;
            OnFlash?.Invoke(position, radius, source);
        }
    }
}
