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

        private void LateUpdate()
        {
            if (holder != null)                                   // 손에 든 횃불만 손을 따라간다 (모닥불은 제자리)
            {
                bool flip = holder.flipX;                         // 원화는 오른쪽을 본다 - 뒤집히면 왼쪽
                transform.localPosition = new Vector3(flip ? -handRight.x : handRight.x, handRight.y, 0f);
                if (_sr) _sr.flipX = flip;
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
