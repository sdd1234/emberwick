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
    ///
    /// 피드백 반영:
    ///   - 시야 크기 · 각도 · 경계 흐림은 PlayerVisionSize 컴포넌트에 따로 모았다 (즉시 반영)
    ///   - 경계 흐림: 원뿔을 보이는 시야각보다 넓게 펴고 안쪽 각을 좁혀 양옆이 서서히 사라지게 하고,
    ///     근접 원은 가운데부터 옅어지게 해서 원뿔과 만나는 곳에 선이 안 생기게 한다
    ///   - 전체 밝기 배율 (기본 1.3 = 30% 상향)
    /// </summary>
    [ExecuteAlways]
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

        [Tooltip("시야 크기 · 각도 · 경계 흐림. 비우면 같은 오브젝트에서 찾고, 없으면 기본값을 쓴다")]
        [SerializeField] private PlayerVisionSize size;

        [Header("밝기")]
        [Tooltip("모든 시야 빛과 어둠에 곱하는 전체 밝기 배율. 1.3 = 30% 밝게")]
        [SerializeField, Range(0.5f, 3f)] private float brightness = 1.3f;
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
        public float LitConeRange => ConeRangeLit;
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

            _wallMask = Combat.ProjectileOrigin.DefaultWallMask(0);
            _currentFov = FovNormal;
            _currentRange = ConeRangeUnlit;
            _currentIntensity = coneIntensityUnlit;
        }

        private void Start()
        {
            ApplyStaticSettings();
        }

        /// <summary>인스펙터에서 값을 바꾸면 에디트 모드에서도 바로 빛에 반영한다.</summary>
        private void OnValidate() => ApplyNow();

        private void LateUpdate()
        {
            // 근접 원과 어둠은 매 프레임 다시 건다 - 플레이 중에 인스펙터에서 고쳐도 바로 보이게
            ApplyStaticSettings();
            if (!Application.isPlaying) return;
            if (player == null) return;

            bool lit = torch != null && torch.IsLit;

            // --- 목표값 계산
            float targetFov       = player.IsAiming ? FovAiming : FovNormal;
            float targetRange     = lit ? ConeRangeLit : ConeRangeUnlit;
            float targetIntensity = lit ? coneIntensityLit : coneIntensityUnlit;

            float t = 1f - Mathf.Exp(-transitionLerp * Time.deltaTime);   // 프레임레이트 독립 보간
            _currentFov       = Mathf.Lerp(_currentFov, targetFov, t);
            _currentRange     = Mathf.Lerp(_currentRange, targetRange, t);
            _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, t);

            PlaceLight(coneLight, ConeHeight);
            PlaceLight(proximityLight, ProximityHeight);

            // --- 원뿔을 마우스 방향으로 회전. 빛이 머리에서 나오므로 머리에서 커서를 향하게 잰다
            Vector2 dir = player.AimDirection;
            if (coneLight)
            {
                Vector2 fromHead = player.AimWorldPoint - (Vector2)coneLight.transform.position;
                if (fromHead.sqrMagnitude > 0.25f) dir = fromHead.normalized;
            }
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + coneAngleOffset;
            float r = 1f - Mathf.Exp(-rotateLerp * Time.deltaTime);
            _lightAngle = Mathf.LerpAngle(_lightAngle, targetAngle, r);

            // --- 적용
            if (coneLight)
            {
                coneLight.transform.rotation = Quaternion.Euler(0f, 0f, _lightAngle);
                ApplyCone(_currentFov, _currentRange, _currentIntensity);
            }

            if (proximityLight)
                proximityLight.intensity = (lit ? proximityIntensity * 1.15f : proximityIntensity) * brightness;
        }

        /// <summary>
        /// 원뿔 모양을 건다. 안쪽 반경을 0 으로 둬야 근접 원과 원뿔이 만나는 곳에 선이 안 생긴다.
        /// 흐림을 올리면 빛의 부채꼴을 실제 시야각보다 넓게 펴고(최대 1.6배) 안쪽 각을 좁힌다.
        /// 그러면 시야각 경계에서 밝기가 뚝 끊기지 않고 바깥으로 서서히 사라진다 - 손전등 테두리가 없어진다.
        /// </summary>
        private void ApplyCone(float fov, float range, float intensity)
        {
            float blur = EdgeBlur;
            float outer = Mathf.Min(359f, fov * (1f + blur * 0.6f));
            coneLight.pointLightOuterAngle = outer;
            coneLight.pointLightInnerAngle = fov * (1f - blur);
            coneLight.pointLightOuterRadius = range * (1f + blur * 0.15f);
            coneLight.pointLightInnerRadius = 0f;
            coneLight.falloffIntensity = Mathf.Lerp(0.5f, 0.62f, blur);
            coneLight.intensity = intensity * brightness;
        }

        /// <summary>PlayerVisionSize 를 고쳤을 때 바로 빛에 반영한다 (에디트 모드 미리보기 포함).</summary>
        public void ApplyNow()
        {
            if (!isActiveAndEnabled) return;
            ApplyStaticSettings();
            if (!Application.isPlaying) PreviewCone();
        }

        private void PreviewCone()
        {
            if (!coneLight) return;
            coneLight.transform.localPosition = new Vector3(0f, ConeHeight, 0f);
            if (proximityLight) proximityLight.transform.localPosition = new Vector3(0f, ProximityHeight, 0f);
            ApplyCone(FovNormal, ConeRangeUnlit, coneIntensityUnlit);
        }

        private PlayerVisionSize Sz
        {
            get { if (size == null) size = GetComponent<PlayerVisionSize>(); return size; }
        }

        // 컴포넌트가 빠져 있어도 돌아가도록 기본값을 둔다
        private float ProximityRadius => Sz ? Sz.proximityRadius : 4f;
        private float ConeRangeUnlit  => Sz ? Sz.coneRangeUnlit : 4f;
        private float ConeRangeLit    => Sz ? Sz.coneRangeLit : 9f;
        private float FovNormal       => Sz ? Sz.fovNormal : 90f;
        private float FovAiming       => Sz ? Sz.fovAiming : 70f;
        private float EdgeBlur        => Sz ? Sz.edgeBlur : 0.85f;
        private float ConeHeight      => Sz ? Sz.coneHeight : 2f;
        private float ProximityHeight => Sz ? Sz.proximityHeight : 1.25f;

        /// <summary>
        /// 빛을 머리 · 몸 높이로 올린다. 탑다운에서 +Y 는 하늘이 아니라 북쪽이라(ProjectileOrigin 참고),
        /// 북쪽 벽에 붙어 서면 머리 높이가 벽 속이나 벽 너머가 된다 - 그러면 빛이 벽 뒤를 비춘다.
        /// 발에서 위로 훑어 벽에 닿으면 벽 바로 앞까지만 올린다. 몸으로 통째로 끌어내리면 벽에 다가갈 때 빛이 툭 떨어진다.
        /// </summary>
        private void PlaceLight(Light2D light, float height)
        {
            if (!light || light.transform.parent == null) return;
            Vector2 feet = light.transform.parent.position;
            float h = height;
            if (_wallMask.value != 0 && h > 0f)
            {
                var hit = Physics2D.Raycast(feet, Vector2.up, h + 0.2f, _wallMask);
                if (hit.collider != null) h = Mathf.Max(0f, hit.distance - 0.2f);
            }
            light.transform.localPosition = new Vector3(0f, h, 0f);
        }

        private LayerMask _wallMask;

        private void ApplyStaticSettings()
        {
            if (proximityLight)
            {
                proximityLight.pointLightOuterAngle = 360f;
                proximityLight.pointLightInnerAngle = 360f;
                // 흐림이 클수록 꽉 찬 밝기 구간을 줄여 가운데부터 옅어지게 한다
                float blur = EdgeBlur;
                proximityLight.pointLightOuterRadius = ProximityRadius;
                proximityLight.pointLightInnerRadius = ProximityRadius * Mathf.Lerp(0.35f, 0f, blur);
                proximityLight.falloffIntensity = Mathf.Lerp(0.5f, 0.62f, blur);
                if (!Application.isPlaying) proximityLight.intensity = proximityIntensity * brightness;
            }
            if (globalLight) globalLight.intensity = ambientDarkness * brightness;
        }

        /// <summary>월드 좌표가 지금 플레이어 시야 안에 들어오는지. 적 AI / 아이템 감정 등에서 쓴다.</summary>
        public bool IsInVision(Vector2 worldPoint)
        {
            Vector2 origin = transform.position;
            Vector2 delta = worldPoint - origin;
            float dist = delta.magnitude;

            if (dist <= ProximityRadius) return true;                 // 근접 시야
            if (dist > _currentRange) return false;                   // 원뿔 사거리 밖

            float angle = Vector2.Angle(player.AimDirection, delta);
            return angle <= CurrentHalfAngle;
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || player == null) return;
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, ProximityRadius);

            Vector3 o = transform.position;
            Quaternion l = Quaternion.Euler(0f, 0f,  CurrentHalfAngle);
            Quaternion r = Quaternion.Euler(0f, 0f, -CurrentHalfAngle);
            Vector3 d = player.AimDirection * _currentRange;
            Gizmos.DrawLine(o, o + l * d);
            Gizmos.DrawLine(o, o + r * d);
        }
    }
}
