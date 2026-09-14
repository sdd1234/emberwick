using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 석궁 볼트. 사거리만큼 날아가고, 적에게 맞으면 피해를 주고 사라진다.
    /// 벽에 맞으면 박힌 채 남아 50% 확률로 회수할 수 있다 (기획서 7.4).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Bolt : MonoBehaviour
    {
        [SerializeField] private float speed = 26f;
        [SerializeField] private LayerMask hitMask;
        [Tooltip("벽에 박힌 볼트를 회수할 수 있을 확률 (기획서 7.4 - 50%)")]
        [SerializeField, Range(0f, 1f)] private float recoverChance = 0.5f;
        [Tooltip("박힌 볼트가 씬에 남아 있는 시간(초). 0 이면 계속 남는다")]
        [SerializeField] private float stuckLifetime = 20f;

        private float _damage;
        private float _range;
        private float _traveled;
        private bool  _isCrit;
        private bool  _stuck;
        private Vector2 _direction;
        private Rigidbody2D _rb;
        private GameObject _owner;

        public bool IsRecoverable { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        /// <summary>발사 직후 호출. 방향은 정규화되어 들어온다고 가정한다.</summary>
        public void Launch(Vector2 direction, float damage, float range, bool isCrit, GameObject owner)
        {
            _direction = direction.normalized;
            _damage = damage;
            _range = range;
            _isCrit = isCrit;
            _owner = owner;

            transform.right = _direction;                    // 볼트 스프라이트는 +X 를 향한다고 가정
            _rb.linearVelocity = _direction * speed;
        }

        private void Update()
        {
            if (_stuck) return;

            _traveled += speed * Time.deltaTime;
            if (_traveled >= _range) Destroy(gameObject);    // 사거리 끝나면 소멸
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_stuck) return;
            // 허트박스는 자식 오브젝트라서 == 비교로는 자기 자신을 못 거른다
            if (_owner != null && other.transform.IsChildOf(_owner.transform)) return;
            if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;

            var health = other.GetComponentInParent<Health>();
            if (health != null && !health.IsDead)
            {
                health.TakeDamage(_damage, _direction);
                Destroy(gameObject);
                return;
            }

            Stick();
        }

        /// <summary>벽/엄폐물에 박힌다.</summary>
        private void Stick()
        {
            _stuck = true;
            _rb.linearVelocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Static;
            IsRecoverable = Random.value < recoverChance;

            // 회수 불가 볼트는 어둡게 표시해 구분한다
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr && !IsRecoverable) sr.color = new Color(0.45f, 0.42f, 0.38f, 0.85f);

            if (stuckLifetime > 0f) Destroy(gameObject, stuckLifetime);
        }

        public bool IsCritical => _isCrit;
    }
}
