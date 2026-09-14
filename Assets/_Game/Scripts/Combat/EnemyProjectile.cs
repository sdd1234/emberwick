using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 적이 쏘는 빛나는 구체. 벽에 맞으면 사라진다.
    /// 스프라이트를 언릿 머티리얼로 그리기 때문에 Light2D 어둠에 먹히지 않는다 -
    /// 시야 밖에서 날아오는 것도 보여야 피할 수 있다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyProjectile : MonoBehaviour
    {
        [SerializeField] private float speed = 14f;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private LayerMask hitMask;

        [Header("발광")]
        [SerializeField] private Color glowColor = new(1f, 0.28f, 0.22f, 1f);
        [Tooltip("날아가는 동안 크기가 맥동하는 폭")]
        [SerializeField] private float pulseAmount = 0.14f;
        [SerializeField] private float pulseSpeed = 14f;

        private SpriteRenderer _sprite;
        private Vector3 _baseScale;

        private float _damage;
        private Vector2 _direction;
        private GameObject _owner;

        private void Awake()
        {
            var rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            _sprite = GetComponentInChildren<SpriteRenderer>();
            if (_sprite)
            {
                _sprite.color = glowColor;
                _baseScale = _sprite.transform.localScale;
            }
        }

        private void Update()
        {
            // 살아있는 것처럼 맥동시켜 눈에 띄게 한다
            if (_sprite == null || pulseAmount <= 0f) return;
            float k = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            _sprite.transform.localScale = _baseScale * k;
        }

        public void Launch(Vector2 direction, float damage, GameObject owner)
        {
            _direction = direction.normalized;
            _damage = damage;
            _owner = owner;
            GetComponent<Rigidbody2D>().linearVelocity = _direction * speed;
            Destroy(gameObject, lifetime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 허트박스는 자식 오브젝트라서 == 비교로는 자기 자신을 못 거른다
            if (_owner != null && other.transform.IsChildOf(_owner.transform)) return;
            if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;

            var health = other.GetComponentInParent<Health>();
            if (health != null && !health.IsDead) health.TakeDamage(_damage, _direction);

            Destroy(gameObject);
        }
    }
}
