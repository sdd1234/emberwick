using UnityEngine;

namespace Capstone.Player
{
    /// <summary>
    /// 발소리와 소음 (피드백 - 걸으면 3m, 뛰면 10m).
    /// 한 걸음마다 소리를 내고 같은 반경을 NoiseSystem 에 알린다.
    /// 걷는 동안에는 그 반경에 반투명 원을 고정으로 띄워 둔다 (퍼지는 연출 없이 최대 거리만).
    /// 원이 곧 "이 안의 감염체는 들었다"의 경계다 - 보이는 것과 판정이 같아야 소리를 관리할 수 있다.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class Footsteps : MonoBehaviour
    {
        [Header("소음 반경 (m)")]
        [SerializeField] private float walkNoiseRadius = 3f;
        [SerializeField] private float runNoiseRadius = 10f;
        [Tooltip("구르기는 뛰는 것과 같은 소리로 친다")]
        [SerializeField] private bool rollCountsAsRun = true;

        [Header("걸음 간격 (초)")]
        [SerializeField] private float walkInterval = 0.5f;
        [SerializeField] private float runInterval = 0.32f;
        [Tooltip("이 속도(m/s)보다 느리면 걷는 걸로 치지 않는다")]
        [SerializeField] private float minSpeed = 0.4f;

        [Header("소리")]
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip[] walkClips;
        [SerializeField] private AudioClip[] runClips;
        [SerializeField, Range(0f, 1f)] private float walkVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] private float runVolume = 0.55f;
        [SerializeField] private Vector2 pitchJitter = new(0.92f, 1.08f);

        [Header("시각화")]
        [Tooltip("소리가 닿는 최대 거리를 보여주는 원. 플레이어 발밑에 하나만 만들어 계속 쓴다. 비우면 소리만 난다")]
        [SerializeField] private NoiseRing ringPrefab;
        [SerializeField] private bool showRings = true;

        private PlayerController _player;
        private Rigidbody2D _body;
        private float _nextStepAt;
        private int _lastClip = -1;
        private NoiseRing _ring;

        /// <summary>지금 내고 있는 소음 반경. 가만히 있으면 0.</summary>
        public float CurrentNoiseRadius { get; private set; }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _body = GetComponent<Rigidbody2D>();
            if (!source) source = GetComponent<AudioSource>();
        }

        private void OnDestroy()
        {
            if (_ring != null) Destroy(_ring.gameObject);
        }

        private void Update()
        {
            float speed = _body ? _body.linearVelocity.magnitude : 0f;
            bool moving = _player.enabled && speed >= minSpeed;
            bool running = moving && (_player.IsSprinting || (rollCountsAsRun && _player.IsRolling));
            CurrentNoiseRadius = !moving ? 0f : running ? runNoiseRadius : walkNoiseRadius;

            // 걷는 동안 최대 거리에 원을 고정해 둔다. 멈추면 사라진다
            var ring = Ring();
            if (ring != null) ring.Show(CurrentNoiseRadius, running, moving);

            if (!moving) return;

            if (Time.time < _nextStepAt) return;
            _nextStepAt = Time.time + (running ? runInterval : walkInterval);
            Step(running);
        }

        /// <summary>
        /// 발소리 말고도 소리를 내는 행동(사격 등)이 쓴다. 같은 원으로 보여줘야 "이만큼 들렸다"를 읽을 수 있다.
        /// </summary>
        public void EmitNoise(float radius, bool loud)
        {
            Core.NoiseSystem.Emit(transform.position, radius, gameObject);
            var ring = Ring();
            if (ring != null) ring.Flash(radius, loud);
        }

        /// <summary>원은 하나만 찍어 두고 계속 쓴다. 발을 따라다닌다.</summary>
        private NoiseRing Ring()
        {
            if (!showRings || ringPrefab == null) { if (_ring) _ring.Show(0f, false, false); return null; }
            if (_ring == null)
            {
                _ring = Instantiate(ringPrefab, transform.position, Quaternion.identity);
                _ring.name = "NoiseRing";
                _ring.Follow(transform);
            }
            return _ring;
        }

        private void Step(bool running)
        {
            float radius = running ? runNoiseRadius : walkNoiseRadius;
            Vector2 feet = transform.position;          // 피벗이 발밑이다

            Core.NoiseSystem.Emit(feet, radius, gameObject);

            var clips = running && runClips != null && runClips.Length > 0 ? runClips : walkClips;
            if (source && clips != null && clips.Length > 0)
            {
                // 같은 발소리가 연달아 나면 기계음처럼 들린다
                int i = Random.Range(0, clips.Length);
                if (clips.Length > 1 && i == _lastClip) i = (i + 1) % clips.Length;
                _lastClip = i;
                source.pitch = Random.Range(pitchJitter.x, pitchJitter.y);
                source.PlayOneShot(clips[i], running ? runVolume : walkVolume);
            }

        }
    }
}
