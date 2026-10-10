using System;
using UnityEngine;

namespace Capstone.Core
{
    /// <summary>
    /// 소리 전파. 발소리처럼 "어디서 얼마나 크게"만 알리면 듣는 쪽이 알아서 판단한다.
    ///
    /// 감염체는 빛이 아니라 소리로 플레이어를 찾는다 (피드백 - 레이드 맵).
    /// 소리를 내는 쪽은 누가 듣는지 몰라도 되고, 듣는 쪽은 누가 냈는지 몰라도 된다.
    /// GameSession · NavGrid 와 같은 이유로 static 이다 - 씬에 얹을 것이 없다.
    /// </summary>
    public static class NoiseSystem
    {
        /// <summary>(소리 난 자리, 들리는 반경 m, 낸 쪽)</summary>
        public static event Action<Vector2, float, GameObject> OnNoise;

        public static void Emit(Vector2 position, float radius, GameObject source)
        {
            if (radius <= 0f) return;
            OnNoise?.Invoke(position, radius, source);
        }
    }
}
