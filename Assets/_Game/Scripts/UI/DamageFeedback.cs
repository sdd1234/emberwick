using UnityEngine;
using UnityEngine.UI;

namespace Capstone.UI
{
    /// <summary>
    /// 피격 피드백 - 화면 가장자리가 붉어지고 카메라가 흔들린다.
    /// 체력바 숫자만으로는 맞았다는 게 잘 안 읽힌다. 특히 시야가 좁은 이 게임에서는
    /// "어디서 맞았는지 모르겠지만 맞았다"는 감각이 바로 와야 물러설지 반격할지 판단할 수 있다.
    /// 비네트는 화면 가장자리만 덮어 시야를 가리지 않는다.
    /// </summary>
    public class DamageFeedback : MonoBehaviour
    {
        [Header("대상")]
        [SerializeField] private Combat.Health health;
        [SerializeField] private Player.CameraFollow cameraFollow;

        [Header("붉은 비네트")]
        [Tooltip("프리팹의 DamageVignette. 화면 가장자리를 덮는 Image")]
        [SerializeField] private Image vignette;
        [SerializeField] private Color damageColor = new(0.72f, 0.06f, 0.05f, 1f);
        [Tooltip("피해 1회로 도달하는 최대 진하기")]
        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.72f;
        [Tooltip("이만큼의 피해를 받으면 최대 진하기가 된다")]
        [SerializeField] private float damageForFullFlash = 30f;
        [Tooltip("한 번 맞았을 때 붉은 기운이 뜨고 사라지기까지의 총 시간 (초)")]
        [SerializeField] private float flashDuration = 1f;
        [Tooltip("이 비율만큼은 순식간에 차오른다. 나머지 시간 동안 빠진다")]
        [SerializeField, Range(0.02f, 0.5f)] private float attackPortion = 0.12f;

        [Header("카메라 흔들림")]
        [Tooltip("피해 1회당 흔들림 세기 배율")]
        [SerializeField] private float shakePerDamage = 0.035f;
        [SerializeField] private float maxShake = 0.85f;

        private float _timer;       // 남은 표시 시간 (초)
        private float _peak;        // 이번 피격의 최대 진하기 0~1

        private bool _ready;

        private void Awake() => EnsureReady();
        private void OnEnable() => EnsureReady();

        /// <summary>
        /// 참조 연결과 UI 생성을 한 번만 한다.
        /// Awake 가 호출되지 않는 경로(에디터에서 컴포넌트를 붙인 직후 등)가 있어서
        /// LateUpdate 에서도 한 번 더 확인한다 - 없으면 조용히 아무것도 안 뜬다.
        /// </summary>
        private void EnsureReady()
        {
            if (_ready) return;
            _ready = true;

            if (!health)
            {
                var pc = FindFirstObjectByType<Player.PlayerController>();
                if (pc) health = pc.GetComponent<Combat.Health>();
            }
            if (!cameraFollow) cameraFollow = FindFirstObjectByType<Player.CameraFollow>();

            if (vignette == null)
            {
                var t = transform.Find("DamageVignette");
                if (t != null) vignette = t.GetComponent<Image>();
            }
            if (vignette != null)
            {
                vignette.color = new Color(damageColor.r, damageColor.g, damageColor.b, 0f);
                vignette.enabled = false;
            }

            if (health != null)
            {
                health.OnDamaged += HandleDamaged;
                health.OnDeath += HandleDeath;
            }
        }

        private void OnDestroy()
        {
            if (health == null) return;
            health.OnDamaged -= HandleDamaged;
            health.OnDeath -= HandleDeath;
        }

        private void HandleDamaged(float amount, Vector2 direction)
        {
            float t = Mathf.Clamp01(amount / Mathf.Max(1f, damageForFullFlash));
            // 연속으로 맞으면 더 진해지되, 시간은 항상 처음부터 다시 센다
            _peak = Mathf.Clamp01(Mathf.Max(_peak, Mathf.Lerp(0.45f, 1f, t)));
            _timer = flashDuration;

            if (cameraFollow != null)
                cameraFollow.AddShake(Mathf.Min(maxShake, amount * shakePerDamage));
        }

        private void HandleDeath()
        {
            _peak = 1f;
            _timer = flashDuration;
            if (cameraFollow != null) cameraFollow.AddShake(maxShake);
        }

        private void LateUpdate()
        {
            if (vignette == null) { _ready = false; EnsureReady(); }
            if (vignette == null) return;

            if (_timer <= 0f)
            {
                if (vignette.enabled) vignette.enabled = false;
                _peak = 0f;
                return;
            }

            _timer = Mathf.Max(0f, _timer - Time.deltaTime);

            // 0 -> 1 로 흐르는 진행도
            float p = 1f - _timer / Mathf.Max(0.01f, flashDuration);

            // 앞부분은 순식간에 차오르고, 나머지 시간 동안 부드럽게 빠진다
            float k = p < attackPortion
                ? p / attackPortion
                : 1f - Mathf.SmoothStep(0f, 1f, (p - attackPortion) / (1f - attackPortion));

            var c = damageColor;
            c.a = _peak * maxAlpha * k;
            vignette.color = c;
            vignette.enabled = c.a > 0.002f;
        }

    }
}
