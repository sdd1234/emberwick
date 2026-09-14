using UnityEngine;

namespace Capstone.Player
{
    /// <summary>플레이어를 부드럽게 따라가는 카메라. 조준 방향으로 약간 앞서 본다.</summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [Tooltip("따라붙는 데 걸리는 대략적인 시간(초). 작을수록 즉각적")]
        [SerializeField] private float smoothTime = 0.15f;

        [Header("조준 방향 리드")]
        [Tooltip("마우스 쪽으로 카메라가 얼마나 끌려갈지 (m)")]
        [SerializeField] private float aimLeadDistance = 1.8f;
        [SerializeField] private float aimLeadSmoothTime = 0.25f;

        [Header("흔들림")]
        [Tooltip("흔들림이 잦아드는 속도. 클수록 빨리 멎는다")]
        [SerializeField] private float shakeDecay = 3.2f;
        [Tooltip("흔들림 진동수 (Hz). 높을수록 잘게 떨린다")]
        [SerializeField] private float shakeFrequency = 26f;
        [Tooltip("흔들림 최대 진폭 (m)")]
        [SerializeField] private float maxShakeAmplitude = 0.7f;

        [Header("세로 보정")]
        [Tooltip("발밑 피벗 기준이라 살짝 위를 봐야 화면 중앙에 캐릭터가 온다 (m)")]
        [SerializeField] private float verticalOffset = 1.0f;

        private PlayerController _player;
        private Vector3 _velocity;
        private float _shake;          // 0~1
        private float _shakeSeed;
        private Vector2 _lead;
        private Vector2 _leadVelocity;

        private void Awake()
        {
            if (!target)
            {
                var found = FindFirstObjectByType<PlayerController>();
                if (found) target = found.transform;
            }
            if (target) _player = target.GetComponent<PlayerController>();
            _shakeSeed = Random.value * 100f;
        }

        /// <summary>피격 등에서 호출. 0~1 세기로 화면을 흔든다. 누적되되 1을 넘지 않는다.</summary>
        public void AddShake(float strength)
        {
            _shake = Mathf.Clamp01(_shake + Mathf.Clamp01(strength));
        }

        private void LateUpdate()
        {
            if (!target) return;

            Vector2 desiredLead = Vector2.zero;
            if (_player != null)
            {
                // 마우스가 멀수록 리드가 커지되 aimLeadDistance 로 상한
                Vector2 toMouse = _player.AimWorldPoint - (Vector2)target.position;
                desiredLead = Vector2.ClampMagnitude(toMouse * 0.35f, aimLeadDistance);
            }
            _lead = Vector2.SmoothDamp(_lead, desiredLead, ref _leadVelocity, aimLeadSmoothTime);

            Vector3 goal = target.position;
            goal.x += _lead.x;
            goal.y += _lead.y + verticalOffset;
            goal.z = transform.position.z;                  // z 는 건드리지 않는다

            transform.position = Vector3.SmoothDamp(transform.position, goal, ref _velocity, smoothTime);

            // 흔들림은 추적이 끝난 뒤에 얹는다. 안 그러면 SmoothDamp 가 흔들림을 따라가서 멀미가 난다.
            if (_shake > 0.0001f)
            {
                float t = Time.unscaledTime * shakeFrequency;
                // 펄린 노이즈라 무작위보다 부드럽게 흔들린다
                float ox = (Mathf.PerlinNoise(_shakeSeed, t) - 0.5f) * 2f;
                float oy = (Mathf.PerlinNoise(_shakeSeed + 37f, t) - 0.5f) * 2f;

                float amp = _shake * _shake * maxShakeAmplitude;   // 제곱이라 끝맺음이 깔끔하다
                transform.position += new Vector3(ox * amp, oy * amp, 0f);

                _shake = Mathf.MoveTowards(_shake, 0f, shakeDecay * Time.deltaTime);
            }
        }

        public void SetTarget(Transform t)
        {
            target = t;
            _player = t ? t.GetComponent<PlayerController>() : null;
        }
    }
}
