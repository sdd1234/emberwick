using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Player
{
    /// <summary>
    /// 탑다운 이동. 세로로 긴 원화를 쓰므로 스프라이트는 회전시키지 않고
    /// 마우스가 있는 쪽으로 좌우 플립만 한다. 조준 방향은 시야 원뿔이 담당한다.
    /// 기획서 8.1 - WASD 이동 / Shift 달리기 / Space 구르기
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("이동 (m/s)")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float sprintSpeed = 5.6f;
        [Tooltip("입력 방향이 바뀔 때 얼마나 빨리 따라붙는지. 값이 클수록 즉각적")]
        [SerializeField] private float acceleration = 40f;

        [Header("구르기 (기획서 8.1 - Space)")]
        [SerializeField] private float rollSpeed = 9f;
        [SerializeField] private float rollDuration = 0.35f;
        [SerializeField] private float rollCooldown = 0.6f;
        [Tooltip("구르는 동안 피격 무효")]
        [SerializeField] private bool invulnerableWhileRolling = true;

        [Header("참조")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private PlayerStamina stamina;
        [SerializeField] private Combat.Health health;

        /// <summary>석궁 차징처럼 외부에서 거는 이동속도 배율. 1 = 정상, 0.4 = -60%</summary>
        public float ExternalSpeedMultiplier { get; set; } = 1f;

        public Vector2 MoveInput { get; private set; }
        public Vector2 AimDirection { get; private set; } = Vector2.right;
        public Vector2 AimWorldPoint { get; private set; }
        public bool IsSprinting { get; private set; }
        /// <summary>우클릭 홀드. 시야각이 90도에서 70도로 좁아진다 (기획서 6.6)</summary>
        public bool IsAiming { get; private set; }
        public bool IsRolling { get; private set; }
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;

        private Rigidbody2D _rb;
        private Camera _cam;
        private Vector2 _velocity;
        private Vector2 _rollDirection;
        private float _rollEndTime;
        private float _rollReadyTime;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;                          // 탑다운 - 중력 없음
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (!stamina) stamina = GetComponent<PlayerStamina>();
            if (!health) health = GetComponent<Combat.Health>();
        }

        private void Start() => _cam = Camera.main;

        private void Update()
        {
            ReadAim();
            ReadMove();
            IsAiming = Mouse.current != null && Mouse.current.rightButton.isPressed;
            HandleRollInput();
            FaceAim();
        }

        private void FixedUpdate()
        {
            if (IsRolling)
            {
                _rb.linearVelocity = _rollDirection * rollSpeed;
                return;
            }

            float target = IsSprinting ? sprintSpeed : walkSpeed;
            target *= Mathf.Clamp01(ExternalSpeedMultiplier);

            Vector2 desired = MoveInput * target;
            _velocity = Vector2.MoveTowards(_velocity, desired, acceleration * Time.fixedDeltaTime);
            _rb.linearVelocity = _velocity;
        }

        private void ReadMove()
        {
            var kb = Keyboard.current;
            if (kb == null) { MoveInput = Vector2.zero; IsSprinting = false; return; }

            Vector2 raw = Vector2.zero;
            if (kb.wKey.isPressed) raw.y += 1f;
            if (kb.sKey.isPressed) raw.y -= 1f;
            if (kb.dKey.isPressed) raw.x += 1f;
            if (kb.aKey.isPressed) raw.x -= 1f;
            MoveInput = raw.sqrMagnitude > 1f ? raw.normalized : raw;

            bool wantsSprint = kb.leftShiftKey.isPressed && IsMoving && !IsRolling;
            IsSprinting = wantsSprint && stamina != null && stamina.DrainSprint(Time.deltaTime);
        }

        private void ReadAim()
        {
            // 가방·상자 격자를 다루는 동안엔 마우스가 UI 를 향한다.
            // 그대로 조준에 쓰면 아이템을 옮길 때마다 시야 원뿔이 따라 돌아서
            // 창을 닫았을 때 내가 어디를 보고 있었는지를 잃어버린다. 들어갈 때의 방향을 그대로 둔다.
            if (UI.LootScreenUI.AnyScreenOpen) return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

            Vector3 screen = mouse.position.ReadValue();
            screen.z = Mathf.Abs(_cam.transform.position.z - transform.position.z);
            Vector3 world = _cam.ScreenToWorldPoint(screen);
            AimWorldPoint = world;

            Vector2 delta = (Vector2)world - (Vector2)transform.position;
            if (delta.sqrMagnitude > 0.0001f) AimDirection = delta.normalized;
        }

        private void HandleRollInput()
        {
            if (IsRolling)
            {
                if (Time.time >= _rollEndTime) EndRoll();
                return;
            }

            var kb = Keyboard.current;
            if (kb == null || !kb.spaceKey.wasPressedThisFrame) return;
            if (Time.time < _rollReadyTime) return;
            if (stamina != null && !stamina.TrySpend(stamina.RollCost)) return;

            // 입력이 없으면 조준 방향으로 구른다
            _rollDirection = IsMoving ? MoveInput : AimDirection;
            IsRolling = true;
            _rollEndTime = Time.time + rollDuration;
            _rollReadyTime = _rollEndTime + rollCooldown;
            if (invulnerableWhileRolling && health != null) health.Invulnerable = true;
        }

        private void EndRoll()
        {
            IsRolling = false;
            _velocity = _rollDirection * walkSpeed;   // 구르기 끝나고 급정지하지 않도록
            if (health != null) health.Invulnerable = false;
        }

        /// <summary>스프라이트를 회전시키지 않고 좌우만 뒤집는다.</summary>
        private void FaceAim()
        {
            if (!spriteRenderer) return;
            if (Mathf.Abs(AimDirection.x) < 0.05f) return;   // 정면/후면일 땐 유지
            spriteRenderer.flipX = AimDirection.x < 0f;
        }
    }
}
