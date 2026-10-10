using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Map
{
    /// <summary>
    /// 맵에 박혀 있는 고정 광원 (피드백 - 레이드 맵).
    /// 이 빛 안에 서 있으면 어둠 디버프를 받지 않고, 쌓여 있던 디버프도 전부 풀린다.
    /// 반경은 붙어 있는 Light2D 의 바깥 반경을 따라간다 - 보이는 빛과 판정이 어긋나면 안 된다.
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    public class FixedLightZone : MonoBehaviour
    {
        [Tooltip("빛 반경 대비 판정 반경. 가장자리는 어둑하니 조금 안쪽에서 끊는다")]
        [SerializeField, Range(0.3f, 1f)] private float coverage = 0.8f;

        private static readonly List<FixedLightZone> Active = new();
        private Light2D _light;

        public float Radius => (_light ? _light.pointLightOuterRadius : 3f) * coverage;

        private void Awake() => _light = GetComponent<Light2D>();
        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        /// <summary>그 자리가 어느 고정 광원 안에라도 들어가는가.</summary>
        public static bool IsLit(Vector2 point)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var z = Active[i];
                if (z == null || !z._light || !z._light.enabled) continue;
                float r = z.Radius;
                if (((Vector2)z.transform.position - point).sqrMagnitude <= r * r) return true;
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_light) _light = GetComponent<Light2D>();
            Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
