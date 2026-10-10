using UnityEngine;
using UnityEngine.UI;

namespace Capstone.Player
{
    /// <summary>
    /// 은신 패널티 (피드백). 불을 끄고 어둠 속에 오래 있으면 마녀가 다가온다.
    ///   15초 - 속삭임이 들리고 화면 가장자리가 어두워진다
    ///   30초 - 초당 1~4 의 피해를 무작위로 입는다
    /// 맵의 고정 광원 안에 들어가면 쌓인 시간이 전부 풀린다.
    /// 불을 켜면 그동안은 시간이 멈춘다 - 풀리지는 않으니, 불씨로 버티는 것과
    /// 고정 광원까지 가는 것이 다른 선택이 된다.
    /// </summary>
    public class DarknessPenalty : MonoBehaviour
    {
        [Header("단계 (초)")]
        [SerializeField] private float whisperAfter = 15f;
        [SerializeField] private float damageAfter = 30f;

        [Header("피해")]
        [Tooltip("초당 피해의 최소 · 최대 (정수로 굴린다)")]
        [SerializeField] private Vector2Int damagePerSecond = new(1, 4);
        [SerializeField] private float damageTick = 1f;

        [Header("빛")]
        [Tooltip("불을 켜면 시간이 멈춘다. 끄면 불을 켜는 즉시 0 으로 풀린다")]
        [SerializeField] private bool torchOnlyPauses = true;

        [Header("속삭임")]
        [SerializeField] private AudioSource whisperSource;
        [SerializeField, Range(0f, 1f)] private float whisperMaxVolume = 0.75f;
        [Tooltip("15초에서 이 시간에 걸쳐 최대 음량까지 차오른다")]
        [SerializeField] private float whisperRampSeconds = 10f;
        [SerializeField] private float whisperFadeOut = 1.2f;

        [Header("가장자리 어둠")]
        [Tooltip("HUD 의 DarknessVignette. 가운데가 뚫린 비네트 이미지")]
        [SerializeField] private Image vignette;
        [SerializeField, Range(0f, 1f)] private float vignetteAtWhisper = 0.55f;
        [SerializeField, Range(0f, 1f)] private float vignetteAtDamage = 0.92f;
        [Tooltip("피해 단계에서 숨 쉬듯 일렁이는 폭")]
        [SerializeField, Range(0f, 0.3f)] private float vignettePulse = 0.08f;

        [Header("참조")]
        [SerializeField] private TorchFuel torch;
        [SerializeField] private Combat.Health health;
        [SerializeField] private PlayerController controller;

        /// <summary>어둠 속에서 쌓인 시간 (초).</summary>
        public float DarkTime { get; private set; }
        public bool InFixedLight { get; private set; }
        public bool IsWhispering => DarkTime >= whisperAfter;
        public bool IsDamaging => DarkTime >= damageAfter;

        private float _nextDamageAt;
        private float _vignetteAlpha;

        private void Awake()
        {
            if (!torch) torch = GetComponent<TorchFuel>();
            if (!health) health = GetComponent<Combat.Health>();
            if (!controller) controller = GetComponent<PlayerController>();
            FindVignette();
        }

        private void FindVignette()
        {
            if (vignette) return;
            foreach (var img in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (img.name == "DarknessVignette") { vignette = img; break; }
        }

        private void OnDisable()
        {
            if (whisperSource) whisperSource.Stop();
            if (vignette) vignette.enabled = false;
        }

        private void Update()
        {
            bool alive = health == null || !health.IsDead;
            bool playing = alive && (controller == null || controller.enabled);

            InFixedLight = Map.FixedLightZone.IsLit(transform.position);
            bool lit = torch != null && torch.IsLit;

            if (!playing || InFixedLight || (lit && !torchOnlyPauses))
            {
                DarkTime = 0f;                          // 고정 광원 - 쌓인 디버프가 전부 풀린다
            }
            else if (!lit)
            {
                DarkTime += Time.deltaTime;
            }
            // 불을 켠 동안은 그대로 멈춰 있는다

            bool suspended = lit || !playing;
            UpdateWhisper(suspended);
            UpdateVignette(suspended);
            UpdateDamage(suspended);
        }

        private void UpdateDamage(bool suspended)
        {
            if (suspended || !IsDamaging || health == null)
            {
                _nextDamageAt = Time.time + damageTick;  // 다시 들어오면 1초 뒤부터 깎는다
                return;
            }
            if (Time.time < _nextDamageAt) return;
            _nextDamageAt = Time.time + damageTick;

            int dmg = Random.Range(damagePerSecond.x, damagePerSecond.y + 1);
            health.TakeDamage(dmg, Vector2.zero, true);
        }

        private void UpdateWhisper(bool suspended)
        {
            if (!whisperSource) return;

            float target = 0f;
            if (!suspended && IsWhispering)
                target = whisperMaxVolume * Mathf.Clamp01((DarkTime - whisperAfter) / Mathf.Max(0.1f, whisperRampSeconds) + 0.25f);

            if (target > 0f && !whisperSource.isPlaying) { whisperSource.volume = 0f; whisperSource.Play(); }

            float rate = target > whisperSource.volume ? 0.6f : whisperMaxVolume / Mathf.Max(0.05f, whisperFadeOut);
            whisperSource.volume = Mathf.MoveTowards(whisperSource.volume, target, rate * Time.deltaTime);

            if (target <= 0f && whisperSource.isPlaying && whisperSource.volume <= 0.001f) whisperSource.Stop();
        }

        private void UpdateVignette(bool suspended)
        {
            if (!vignette) { FindVignette(); if (!vignette) return; }

            float target = 0f;
            if (!suspended && IsWhispering)
            {
                float k = Mathf.Clamp01((DarkTime - whisperAfter) / Mathf.Max(0.1f, damageAfter - whisperAfter));
                target = Mathf.Lerp(vignetteAtWhisper * 0.5f, vignetteAtWhisper, k);
                if (IsDamaging) target = vignetteAtDamage + Mathf.Sin(Time.time * 2.4f) * vignettePulse;
            }

            _vignetteAlpha = Mathf.MoveTowards(_vignetteAlpha, Mathf.Clamp01(target), 0.8f * Time.deltaTime);
            var c = vignette.color; c.a = _vignetteAlpha;
            vignette.color = c;
            vignette.enabled = _vignetteAlpha > 0.003f;
        }
    }
}
