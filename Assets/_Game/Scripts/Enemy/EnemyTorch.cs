using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Enemy
{
    /// <summary>
    /// 적이 든 횃불 (피드백 - 적 횃불은 붉은빛이 강하고, 빛을 거둘 수 없다. 주변을 밝히는 데만 쓴다).
    /// 주인공 등불(노란빛)과 색으로 구분된다. 도적은 이 빛을 보고 이교도를 찾아간다.
    /// 들고 있는 쪽이 오른쪽/왼쪽을 볼 때 손 위치를 따라 옮긴다.
    /// </summary>
    public class EnemyTorch : MonoBehaviour
    {
        [SerializeField] private Light2D torchLight;
        [SerializeField] private SpriteRenderer holder;
        [Tooltip("오른쪽을 볼 때 손 위치 (로컬, 부모 스케일 전)")]
        [SerializeField] private Vector2 handRight = new(0.32f, 0.62f);
        [SerializeField] private float baseIntensity = 1.25f;
        [SerializeField] private float flicker = 0.18f;
        [Tooltip("도적이 이 불을 보고 찾아오는가. 도적 야영지 모닥불은 끈다 - 자기 불이다")]
        [SerializeField] private bool drawsRogues = true;
        public bool DrawsRogues => drawsRogues;
        [Tooltip("불티가 튀는 빈도 (초당). 모닥불은 높게")]
        [SerializeField] private float embersPerSecond = 3f;
        [Tooltip("불티가 나오는 곳 (불꽃 위치, 이 오브젝트 기준)")]
        [SerializeField] private Vector2 emberOffset = new(0f, 0.3f);

        private static readonly List<EnemyTorch> _all = new();
        public static IReadOnlyList<EnemyTorch> All => _all;

        private SpriteRenderer _sr;
        private float _seed;

        public Vector2 Position => transform.position;
        public float Radius => torchLight ? torchLight.pointLightOuterRadius : 5f;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (!torchLight) torchLight = GetComponentInChildren<Light2D>();
            if (!holder && transform.parent) holder = transform.parent.GetComponent<SpriteRenderer>();
            _seed = Random.value * 30f;
        }

        private void OnEnable() => _all.Add(this);
        private void OnDisable() => _all.Remove(this);

        /// <summary>도적 허리 등불처럼 코드로 붙일 때 쓴다.</summary>
        public void Configure(Light2D light, SpriteRenderer body, Vector2 right, float intensity, float flickerAmount, bool attractsRogues, float embers)
        {
            torchLight = light; holder = body; handRight = right; baseIntensity = intensity;
            flicker = flickerAmount; drawsRogues = attractsRogues; embersPerSecond = embers;
            if (holder) _anim = holder.GetComponentInParent<EnemySpriteAnimator>();
        }

        [Tooltip("위 · 아래를 볼 때 몸 가운데에서 앞뒤로 내미는 거리 (로컬)")]
        [SerializeField] private float frontBackReach = 0.12f;
        [Tooltip("그림 없는 불빛일 때 시선 방향으로 빛을 내미는 거리 (로컬, 부모 스케일 전)")]
        [SerializeField] private float lightReach = 0.45f;
        private Vector2 _lightOffset;
        private EnemySpriteAnimator _anim;
        private int _baseOrder;

        private void Start()
        {
            if (holder) _anim = holder.GetComponentInParent<EnemySpriteAnimator>();
            if (_sr) _baseOrder = _sr.sortingOrder;
        }

        private void LateUpdate()
        {
            if (holder != null)                                   // 손에 든 횃불만 손을 따라간다 (모닥불은 제자리)
            {
                // 수정사항 2026-11-10 - 불빛이 시선 방향(상하좌우)을 알려 준다. 오른쪽을 보면 오른쪽, 위를 보면 몸 위(뒤), 아래를 보면 몸 앞
                Vector2 face = _anim != null ? _anim.Face : (holder.flipX ? Vector2.left : Vector2.right);
                Vector3 local = Vector3.zero;
                int order = _baseOrder;
                if (_sr == null)
                {
                    // 그림 없는 불빛 (도적 허리 등불) - 빛 웅덩이가 시선 쪽 앞에 깔려야 어디를 보는지 읽힌다
                    Vector2 d4 = Mathf.Abs(face.y) > Mathf.Abs(face.x) * 1.2f ? new Vector2(0f, Mathf.Sign(face.y)) : new Vector2(Mathf.Sign(face.x), 0f);
                    _lightOffset = Vector2.Lerp(_lightOffset, d4 * lightReach, 1f - Mathf.Exp(-10f * Time.deltaTime));   // 돌아설 때 툭 튀지 않게
                    transform.localPosition = new Vector3(_lightOffset.x, handRight.y + _lightOffset.y, 0f);
                }
                else if (Mathf.Abs(face.y) > Mathf.Abs(face.x) * 1.2f)
                {
                    bool up = face.y > 0f;
                    // 원화는 옆모습뿐이라 몸 한가운데에 두면 위를 볼 땐 몸에 통째로 가려 사라지고, 아래를 볼 땐 가랑이에 매달린다.
                    // 그림이 보는 쪽 허리 가까이에 두고 - 위를 보면 몸 뒤에서 반쯤 보이고, 아래를 보면 허리띠 앞에 온다
                    float side = (_anim != null ? _anim.LooksRight : !holder.flipX) ? 1f : -1f;
                    local = up
                        ? new Vector3(side * handRight.x * 1.45f, handRight.y + frontBackReach, 0f)
                        : new Vector3(side * handRight.x * 0.35f, handRight.y, 0f);
                    if (up) order = _baseOrder - 2;               // 위를 보면 불은 몸 뒤로 넘어간다
                    if (_sr) _sr.flipX = side < 0f;
                }
                else
                {
                    bool right = _anim != null ? face.x > 0f : !holder.flipX;
                    local = new Vector3(right ? handRight.x : -handRight.x, handRight.y, 0f);
                    if (_sr) _sr.flipX = !right;
                }
                if (_sr != null)
                {
                    transform.localPosition = local;
                    _sr.sortingOrder = order;
                }
            }
            if (embersPerSecond > 0f && Random.value < embersPerSecond * Time.deltaTime)
                FX.PixelFX.Burst((Vector2)transform.position + emberOffset + Random.insideUnitCircle * 0.12f, Vector2.up,
                    new Color(1f, 0.42f, 0.12f), new Color(1f, 0.78f, 0.3f), 1, 1.4f, 30f, false, 0.6f);
            if (torchLight)
            {
                float n = Mathf.PerlinNoise(_seed, Time.time * 7f) - 0.5f;
                float n2 = Mathf.PerlinNoise(_seed + 11f, Time.time * 2.3f) - 0.5f;
                torchLight.intensity = baseIntensity * (1f + (n + n2 * 0.6f) * 2f * flicker);
            }
        }
    }
}
