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

        /// <summary>날아가는 중인 적 투사체. 도적이 피하는 데 쓴다.</summary>
        public static readonly System.Collections.Generic.List<EnemyProjectile> Flying = new();
        private void OnEnable() => Flying.Add(this);
        private void OnDisable() => Flying.Remove(this);
        public Vector2 Direction => _direction;
        public float Speed => speed;
        public GameObject Owner => _owner;

        private SpriteRenderer _sprite;
        private Vector3 _baseScale;

        private float _damage;
        private Vector2 _direction;
        private GameObject _owner;
        private Core.FactionMember _ownerFaction;

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

        private float _trailAt;

        private void Update()
        {
            // 짧고 어두운 꼬리 불티 - 불빛 밖에서 '뭔가 날아온다' 정도만 읽힌다 (공포 쪽)
            if (_direction != Vector2.zero && Time.time >= _trailAt)
            {
                _trailAt = Time.time + 0.04f;
                FX.PixelFX.Burst((Vector2)transform.position - _direction * 0.15f, -_direction, glowColor * 0.6f, new Color(0.25f, 0.03f, 0.03f), 1, 0.5f, 20f, false, 0.14f);
            }
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
            _ownerFaction = owner != null ? owner.GetComponent<Core.FactionMember>() : null;
            GetComponent<Rigidbody2D>().linearVelocity = _direction * speed;
            Destroy(gameObject, lifetime);
            // 쏘는 순간 총구에서 불티가 튄다 - 화면 밖 사수도 번쩍임으로 위치가 드러난다
            FX.PixelFX.Burst(transform.position, _direction, glowColor, Color.white, 6, 4f, 25f, false, 0.2f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 허트박스는 자식 오브젝트라서 == 비교로는 자기 자신을 못 거른다
            if (_owner != null && other.transform.IsChildOf(_owner.transform)) return;
            if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;
            // 같은 편은 지나간다 - 조원 등 뒤에서 쏴도 조원이 맞지 않는다
            var them = other.GetComponentInParent<Core.FactionMember>();
            if (them != null && _ownerFaction != null && them.Faction == _ownerFaction.Faction) return;

            var health = other.GetComponentInParent<Health>();
            if (health != null && !health.IsDead) health.TakeDamage(_damage, _direction, DamageType.Physical, _owner);
            else FX.PixelFX.Burst(transform.position, -_direction, glowColor, Color.white, 10, 4f, 70f, true, 0.35f);   // 벽에 터진다

            Destroy(gameObject);
        }
    }
}
