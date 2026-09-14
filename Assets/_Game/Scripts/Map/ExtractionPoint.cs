using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Map
{
    /// <summary>
    /// 탈출 지점 (기획서 5.2 - "5초의 대기 시간을 보내야 탈출이 완료된다").
    /// 서 있는 동안만 차오르고 벗어나면 되돌아간다.
    /// 챙긴 걸 들고 여기까지 살아 오는 것이 레이드의 목적이다.
    ///
    /// 겉모습은 지하로 내려가는 참나무 뚜껑이다.
    /// 뚜껑 자체는 색을 건드리지 않고, 틈에서 새어 나오는 빛만 따로 얹어서 흔든다.
    /// 나무를 통째로 초록으로 물들이면 물건이 아니라 표식으로 읽힌다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ExtractionPoint : MonoBehaviour
    {
        [Tooltip("탈출 완료까지 서 있어야 하는 시간 (기획서 5.2 - 5초)")]
        [SerializeField] private float holdSeconds = 5f;
        [Tooltip("벗어났을 때 진행도가 되돌아가는 속도 배율")]
        [SerializeField] private float decayMultiplier = 2f;

        [Header("새어 나오는 빛")]
        [Tooltip("프리팹의 Glow 자식. 뚜껑 위에 겹쳐 그린다")]
        [SerializeField] private SpriteRenderer glow;
        [Tooltip("프리팹의 ExitLight 자식. 멀리서도 보이게 하는 실제 광원")]
        [SerializeField] private Light2D exitLight;
        [SerializeField] private Color idleColor   = new(1f, 0.62f, 0.26f, 0.32f);
        [SerializeField] private Color activeColor = new(1f, 0.86f, 0.52f, 1f);
        [SerializeField] private float pulsePeriod = 1.6f;
        [Tooltip("빛이 숨 쉬듯 커졌다 작아지는 폭")]
        [SerializeField] private float pulseScale = 0.06f;

        [Header("등불")]
        [Tooltip("기본 세기. 플레이어가 올라서면 이 값의 1.9 배까지 밝아진다")]
        [SerializeField] private float lightIntensity = 1.1f;

        public float Progress { get; private set; }
        public bool PlayerInside { get; private set; }
        public bool Completed { get; private set; }

        public event Action<float> OnProgress;
        public event Action OnExtracted;

        private GameObject _player;
        private Vector3 _glowBaseScale = Vector3.one;

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;

            // 빛은 프리팹에 자식으로 들어 있다. 인스펙터에서 안 꽂았을 때만 이름으로 찾는다
            if (glow == null)
            {
                var t = transform.Find("Glow");
                if (t != null) glow = t.GetComponent<SpriteRenderer>();
            }
            if (exitLight == null)
            {
                var t = transform.Find("ExitLight");
                if (t != null) exitLight = t.GetComponent<Light2D>();
            }

            if (glow != null)
            {
                _glowBaseScale = glow.transform.localScale;
                glow.color = idleColor;
            }
            if (exitLight != null) exitLight.intensity = lightIntensity;
        }

        private void Update()
        {
            if (Completed) return;

            float before = Progress;
            if (PlayerInside) Progress += Time.deltaTime / Mathf.Max(0.1f, holdSeconds);
            else               Progress -= Time.deltaTime / Mathf.Max(0.1f, holdSeconds) * decayMultiplier;

            Progress = Mathf.Clamp01(Progress);
            if (!Mathf.Approximately(before, Progress)) OnProgress?.Invoke(Progress);

            if (Progress >= 1f)
            {
                Completed = true;
                OnExtracted?.Invoke();
            }

            UpdateGlow();
        }

        private void UpdateGlow()
        {
            float pulse = (Mathf.Sin(Time.time * Mathf.PI * 2f / pulsePeriod) + 1f) * 0.5f;
            // 서 있으면 활짝, 비어 있으면 은은하게 숨만 쉰다
            float k = Mathf.Max(Progress, PlayerInside ? pulse : pulse * 0.35f);

            if (glow != null)
            {
                glow.color = Color.Lerp(idleColor, activeColor, k);
                float s = 1f + pulseScale * (k - 0.5f) * 2f;
                glow.transform.localScale = _glowBaseScale * s;
            }

            if (exitLight != null)
                exitLight.intensity = lightIntensity * Mathf.Lerp(0.75f, 1.9f, k);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<Capstone.Player.PlayerController>() == null) return;
            _player = other.gameObject;
            PlayerInside = true;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (_player == null || other.gameObject != _player) return;
            PlayerInside = false;
            _player = null;
        }
    }
}
