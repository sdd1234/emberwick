using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Player
{
    /// <summary>
    /// 광원 기반 시야 (기획서 6.4 / 6.6).
    /// 광원 두 개로 구성한다.
    ///   1) 근접 원형 - 캐릭터 주변 2m, 항상 켜짐. 발밑은 늘 보인다.
    ///   2) 시야 원뿔 - 마우스 방향, 90도(조준 시 70도). 불을 켜면 2m -> 5m 로 늘어난다.
    /// 두 광원 바깥은 전역 광원 강도를 0 에 가깝게 눌러 '검정'으로 만든다.
    /// </summary>
    public class VisionController : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private PlayerController player;
        [SerializeField] private TorchFuel torch;
        [Tooltip("캐릭터 주변 근접 시야. Light2D Point, Outer Angle 360")]
        [SerializeField] private Light2D proximityLight;
        [Tooltip("마우스 방향 시야 원뿔. Light2D Point")]
        [SerializeField] private Light2D coneLight;
        [Tooltip("씬 전체 어둠. Light2D Global")]
        [SerializeField] private Light2D globalLight;

        [Header("사거리 (m) - 기획서 6.4")]
        [Tooltip("근접 시야 반경. 불과 무관하게 항상 유지")]
        [SerializeField] private float proximityRadius = 2f;
        [Tooltip("불을 끈 상태의 원뿔 사거리")]
        [SerializeField] private float coneRangeUnlit = 2f;
        [Tooltip("횃불/등불 점등 시 원뿔 사거리")]
        [SerializeField] private float coneRangeLit = 5f;

        [Header("시야각 (도) - 기획서 6.6")]
        [SerializeField] private float fovNormal = 90f;
        [SerializeField] private float fovAiming = 70f;
        [Tooltip("원뿔 가장자리가 부드럽게 풀리는 정도")]
        [SerializeField, Range(0f, 1f)] private float coneFalloff = 0.5f;

        [Header("밝기")]
        [SerializeField] private float proximityIntensity = 0.55f;
        [SerializeField] private float coneIntensityUnlit = 0.5f;
        [SerializeField] private float coneIntensityLit = 1.1f;
        [Tooltip("시야 밖 어둠. 0 이면 완전한 검정 (기획서 6.4)")]
        [SerializeField, Range(0f, 0.3f)] private float ambientDarkness = 0.02f;

        [Header("반응 속도")]
        [Tooltip("원뿔이 마우스를 따라 도는 속도. 클수록 즉각적")]
        [SerializeField] private float rotateLerp = 18f;
        [Tooltip("조준/점등으로 각도와 사거리가 바뀔 때의 전환 속도")]
        [SerializeField] private float transitionLerp = 8f;

        [Header("보정")]
        [Tooltip("Light2D 원뿔이 바라보는 기준축 보정값. 원뿔이 엉뚱한 쪽을 보면 -90 또는 90 을 넣는다")]
        [SerializeField] private float coneAngleOffset = -90f;

        /// <summary>현재 시야 원뿔의 반각(도). 적 탐지 판정 등에서 참조한다.</summary>
        /// <summary>불을 켰을 때의 원뿔 길이. 적이 "이 반경 안이면 무조건 들킨다"의 기준으로 쓴다.</summary>
        public float LitConeRange => coneRangeLit;
        public float CurrentHalfAngle => _currentFov * 0.5f;
        public float CurrentConeRange => _currentRange;
        public Vector2 ConeDirection => player ? player.AimDirection : Vector2.right;

        private float _currentFov;
        private float _currentRange;
        private float _currentIntensity;
        private float _lightAngle;

        private void Awake()
        {
            if (!player) player = GetComponentInParent<PlayerController>();
            if (!torch)  torch  = GetComponentInParent<TorchFuel>();

            _currentFov = fovNormal;
            _currentRange = coneRangeUnlit;
            _currentIntensity = coneIntensityUnlit;
        }

        private void Start()
        {
            ApplyStaticSettings();
            if (globalLight) globalLight.intensity = ambientDarkness;
        }

        private void LateUpdate()
        {
            if (player == null) return;

            bool lit = torch != null && torch.IsLit;

            // --- 목표값 계산
            float targetFov       = player.IsAiming ? fovAiming : fovNormal;
            float targetRange     = lit ? coneRangeLit : coneRangeUnlit;
            float targetIntensity = lit ? coneIntensityLit : coneIntensityUnlit;

            float t = 1f - Mathf.Exp(-transitionLerp * Time.deltaTime);   // 프레임레이트 독립 보간
            _currentFov       = Mathf.Lerp(_currentFov, targetFov, t);
            _currentRange     = Mathf.Lerp(_currentRange, targetRange, t);
            _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, t);

            // --- 원뿔을 마우스 방향으로 회전
            Vector2 dir = player.AimDirection;
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + coneAngleOffset;
            float r = 1f - Mathf.Exp(-rotateLerp * Time.deltaTime);
            _lightAngle = Mathf.LerpAngle(_lightAngle, targetAngle, r);

            // --- 적용
            if (coneLight)
            {
                coneLight.transform.rotation = Quaternion.Euler(0f, 0f, _lightAngle);
                coneLight.pointLightOuterAngle = _currentFov;
                coneLight.pointLightInnerAngle = _currentFov * (1f - coneFalloff);
                coneLight.pointLightOuterRadius = _currentRange;
                coneLight.pointLightInnerRadius = _currentRange * 0.15f;
                coneLight.intensity = _currentIntensity;
            }

            if (proximityLight)
                proximityLight.intensity = lit ? proximityIntensity * 1.15f : proximityIntensity;
        }

        private void ApplyStaticSettings()
        {
            if (proximityLight)
            {
                proximityLight.pointLightOuterAngle = 360f;
                proximityLight.pointLightInnerAngle = 360f;
                proximityLight.pointLightOuterRadius = proximityRadius;
                proximityLight.pointLightInnerRadius = 0f;
                proximityLight.intensity = proximityIntensity;
            }
        }

        /// <summary>월드 좌표가 지금 플레이어 시야 안에 들어오는지. 적 AI / 아이템 감정 등에서 쓴다.</summary>
        public bool IsInVision(Vector2 worldPoint)
        {
            Vector2 origin = transform.position;
            Vector2 delta = worldPoint - origin;
            float dist = delta.magnitude;

            if (dist <= proximityRadius) return true;                 // 근접 시야
            if (dist > _currentRange) return false;                   // 원뿔 사거리 밖

            float angle = Vector2.Angle(player.AimDirection, delta);
            return angle <= CurrentHalfAngle;
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || player == null) return;
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, proximityRadius);

            Vector3 o = transform.position;
            Quaternion l = Quaternion.Euler(0f, 0f,  CurrentHalfAngle);
            Quaternion r = Quaternion.Euler(0f, 0f, -CurrentHalfAngle);
            Vector3 d = player.AimDirection * _currentRange;
            Gizmos.DrawLine(o, o + l * d);
            Gizmos.DrawLine(o, o + r * d);
        }
    }
}
