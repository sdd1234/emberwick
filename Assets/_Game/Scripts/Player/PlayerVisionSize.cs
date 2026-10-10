using UnityEngine;

namespace Capstone.Player
{
    /// <summary>
    /// 캐릭터 주변 시야 크기 (피드백 - 에디터에서 바로 고칠 수 있도록).
    /// 시야의 "크기와 모양"만 여기 모았다. 빛을 실제로 움직이는 건 VisionController 이고,
    /// 이 값을 바꾸면 에디트 모드에서도 플레이 중에도 그 프레임에 바로 빛에 반영된다.
    ///
    /// 경계 흐림(edgeBlur) 하나로 근접 원과 원뿔이 만나는 곳, 원뿔 양옆, 빛의 끝을 함께 흐린다.
    /// 높일수록 "손전등으로 비춘" 또렷한 부채꼴 대신 주변이 서서히 밝아지는 느낌이 된다.
    /// 판정(적 탐지 · IsInVision)은 흐림과 상관없이 시야각 · 사거리 숫자 그대로 쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVisionSize : MonoBehaviour
    {
        [Header("근접 시야 (m)")]
        [Tooltip("캐릭터 주변 원형 시야 반경. 불과 상관없이 항상 보이는 범위")]
        [Min(0.5f)] public float proximityRadius = 4f;

        [Header("시야 원뿔 (m)")]
        [Tooltip("불을 끈 상태의 원뿔 길이")]
        [Min(0.5f)] public float coneRangeUnlit = 4f;
        [Tooltip("불을 켰을 때의 원뿔 길이. 적이 무조건 알아채는 반경이기도 하다")]
        [Min(0.5f)] public float coneRangeLit = 9f;

        [Header("시야각 (도)")]
        [Range(10f, 180f)] public float fovNormal = 90f;
        [Tooltip("우클릭 조준 중")]
        [Range(10f, 180f)] public float fovAiming = 70f;

        [Header("광원 높이 (m, 발밑 기준)")]
        [Tooltip("시야 원뿔이 나오는 높이. 2.0 = 머리. 북쪽 벽에 붙어 서면 벽 앞까지만 올라간다")]
        [Range(0f, 3f)] public float coneHeight = 2.0f;
        [Tooltip("근접 원의 중심 높이. 1.25 = 몸 가운데")]
        [Range(0f, 3f)] public float proximityHeight = 1.25f;

        [Header("경계 흐림")]
        [Tooltip("0 = 또렷한 손전등 부채꼴, 1 = 근접 원과 원뿔이 경계 없이 녹아든다")]
        [Range(0f, 1f)] public float edgeBlur = 0.85f;

        private void OnValidate()
        {
            if (coneRangeLit < coneRangeUnlit) coneRangeLit = coneRangeUnlit;
            // 에디트 모드에서 값을 바꾸면 빛을 바로 다시 건다
            var vc = GetComponent<VisionController>();
            if (vc != null) vc.ApplyNow();
        }
    }
}
