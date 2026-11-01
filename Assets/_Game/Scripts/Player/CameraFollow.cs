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

        [Header("킥 (방향 있는 밀림)")]
        [Tooltip("킥 최대 거리 (m). 도트 몇 칸 수준이라 시야가 흔들리지 않는다")]
        [SerializeField] private float maxKick = 0.35f;
        [SerializeField] private float kickReturn = 14f;
        private Vector2 _kick;
        private Vector3 _applied;     // 지난 프레임에 얹은 킥 - 추적이 킥을 따라가지 않게 먼저 걷어낸다

        /// <summary>
        /// 방향 있는 짧은 밀림 (게임 손맛 - 무작위 흔들림보다 "어디서 맞았는지"가 읽힌다).
        /// 맞으면 맞은 방향으로, 쏘면 쏜 반대쪽으로 민다. 바로 제자리로 돌아온다.
        /// </summary>
        public void Kick(Vector2 direction, float meters)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            _kick = Vector2.ClampMagnitude(_kick + direction.normalized * meters, maxKick);
        }

        public static void KickMain(Vector2 direction, float meters)
        {
            var cam = Camera.main;
            var f = cam ? cam.GetComponent<CameraFollow>() : null;
            if (f) f.Kick(direction, meters);
        }

        /// <summary>피격 등에서 호출. 0~1 세기로 화면을 흔든다. 누적되되 1을 넘지 않는다.</summary>
        public void AddShake(float strength)
        {
            _shake = Mathf.Clamp01(_shake + Mathf.Clamp01(strength));
        }

        [Header("도트 배율")]
        [Tooltip("도트 1점이 화면 픽셀 몇 개인지를 늘 정수로 맞춘다. 1.66배 같은 배율이면 움직일 때 점 폭이 1·2 픽셀로 번갈아 캐릭터가 들쭉날쭉해 보인다")]
        [SerializeField] private bool integerPixelScale = true;
        [SerializeField] private float artPixelsPerUnit = 38f;
        private Camera _cam;
        private float _baseOrtho = -1f;

        /// <summary>1080p 에서 2배(설계 크기)가 되는 배율을 기준으로, 화면 높이에 가장 가까운 정수 배율을 고른다.</summary>
        private void FitPixelScale()
        {
            if (!integerPixelScale) return;
            if (_cam == null) _cam = GetComponent<Camera>();
            int h = _cam != null ? _cam.pixelHeight : 0;      // Screen.height 는 에디터에서 다른 창 크기를 줄 때가 있다
            if (_cam == null || !_cam.orthographic || h <= 0) return;
            if (_baseOrtho < 0f) _baseOrtho = _cam.orthographicSize;
            float scale = Mathf.Max(1f, Mathf.Round(h / (2f * _baseOrtho * artPixelsPerUnit)));
            _cam.orthographicSize = h / (2f * artPixelsPerUnit * scale);
        }

        private void LateUpdate()
        {
            FitPixelScale();
            if (!target) return;

            transform.position -= _applied;
            _applied = Vector3.zero;

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

            // 킥 - 도트 격자에 맞춰 얹고 빠르게 0 으로
            if (_kick.sqrMagnitude > 0.000001f)
            {
                Vector2 k = new(Mathf.Round(_kick.x * 76f) / 76f, Mathf.Round(_kick.y * 76f) / 76f);
                transform.position += (Vector3)k;
                _applied = k;
                _kick = Vector2.MoveTowards(_kick, Vector2.zero, kickReturn * _kick.magnitude * Time.unscaledDeltaTime + 0.2f * Time.unscaledDeltaTime);
            }

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
