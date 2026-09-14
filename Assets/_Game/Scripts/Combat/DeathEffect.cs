using System.Collections;
using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 죽는 순간의 연출. 피가 튀고, 흰 실루엣이 한 번 번쩍이고, 바닥에 핏자국이 남는다.
    /// 사망 처리(EnemyCorpse)와 분리해 둔 이유는 두 가지다.
    /// - 연출은 대상이 파괴되든 시체로 남든 상관없이 끝까지 재생돼야 한다 → 떼어낸 오브젝트가 돌린다
    /// - EnemyCorpse 가 스프라이트를 시체 색으로 덮어쓰므로, 번쩍임은 본체가 아니라 복제한 실루엣에 준다
    /// 조각은 전부 프리팹이다. 어둠 속에서 죽여도 보이도록 언릿 머티리얼을 물려둔다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class DeathEffect : MonoBehaviour
    {
        [Header("프리팹")]
        [Tooltip("연출을 끝까지 돌리는 빈 오브젝트 (DeathEffectRunner)")]
        [SerializeField] private DeathEffectRunner runnerPrefab;
        [Tooltip("바닥에 번지는 핏자국. 여러 개 넣으면 골라 쓴다")]
        [SerializeField] private SpriteRenderer[] stainPrefabs;
        [SerializeField] private SpriteRenderer dropletPrefab;
        [SerializeField] private SpriteRenderer speckPrefab;
        [Tooltip("죽은 모습을 복제해 한 번 번쩍이는 실루엣")]
        [SerializeField] private SpriteRenderer flashPrefab;

        [Header("튀는 피")]
        [SerializeField] private int dropletCount = 18;
        [SerializeField] private Vector2 dropletSpeed = new(3.5f, 8f);
        [SerializeField] private float dropletLife = 0.5f;
        [Tooltip("피격 방향을 중심으로 퍼지는 각도(도)")]
        [SerializeField] private float sprayArc = 70f;
        [SerializeField] private float dropletDrag = 5.5f;
        [SerializeField] private Vector2 dropletScale = new(0.06f, 0.17f);

        [Header("바닥 핏자국")]
        [SerializeField] private float stainRadius = 0.62f;
        [SerializeField] private float stainGrow = 0.22f;
        [Tooltip("튄 피가 바닥에 남기는 작은 점의 개수 상한")]
        [SerializeField] private int maxSpecks = 10;

        [Header("번쩍임")]
        [SerializeField] private Color flashColor = new(1f, 0.94f, 0.88f, 1f);
        [SerializeField] private float flashDuration = 0.12f;
        [SerializeField] private float flashGrow = 1.12f;

        [Header("카메라")]
        [Tooltip("처치 순간의 짧은 흔들림. 0 이면 안 흔든다")]
        [SerializeField] private float killShake = 0.22f;

        private Health _health;
        private Vector2 _lastHitDir = Vector2.up;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.OnDamaged += RememberHit;
            _health.OnDeath += Play;
        }

        private void OnDestroy()
        {
            if (_health == null) return;
            _health.OnDamaged -= RememberHit;
            _health.OnDeath -= Play;
        }

        private void RememberHit(float amount, Vector2 dir)
        {
            if (dir.sqrMagnitude > 0.0001f) _lastHitDir = dir.normalized;
        }

        private void Play()
        {
            if (runnerPrefab == null) return;

            // 연출은 이 오브젝트가 파괴돼도 끝까지 돌아야 하므로 떼어낸 오브젝트에 맡긴다
            var runner = Instantiate(runnerPrefab, transform.position, Quaternion.identity);
            runner.name = $"DeathFX_{name}";
            runner.Run(this, GetComponentInChildren<SpriteRenderer>(), _lastHitDir);

            if (killShake > 0f)
            {
                var cam = FindFirstObjectByType<Player.CameraFollow>();
                if (cam) cam.AddShake(killShake);
            }
        }

        // ---------- Runner 가 읽어가는 설정 ----------
        internal SpriteRenderer PickStain()
        {
            if (stainPrefabs == null || stainPrefabs.Length == 0) return null;
            return stainPrefabs[Random.Range(0, stainPrefabs.Length)];
        }

        internal SpriteRenderer DropletPrefab => dropletPrefab;
        internal SpriteRenderer SpeckPrefab => speckPrefab;
        internal SpriteRenderer FlashPrefab => flashPrefab;

        internal int DropletCount => dropletCount;
        internal Vector2 DropletSpeed => dropletSpeed;
        internal float DropletLife => dropletLife;
        internal float SprayArc => sprayArc;
        internal float DropletDrag => dropletDrag;
        internal Vector2 DropletScale => dropletScale;
        internal float StainRadius => stainRadius;
        internal float StainGrow => stainGrow;
        internal int MaxSpecks => maxSpecks;
        internal Color FlashColor => flashColor;
        internal float FlashDuration => flashDuration;
        internal float FlashGrow => flashGrow;
    }
}
