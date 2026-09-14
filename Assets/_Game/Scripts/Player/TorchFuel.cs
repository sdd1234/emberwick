using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Player
{
    /// <summary>
    /// 불씨 - 조명과 화승총 격발이 함께 쓰는 자원 (기획서 6.5).
    /// 시야를 넓히면 불씨가 닳고, 불씨가 닳으면 화승총을 못 쏜다. 이 게임의 핵심 긴장.
    /// </summary>
    public class TorchFuel : MonoBehaviour
    {
        [Header("잔량 (%)")]
        [SerializeField, Range(0f, 100f)] private float fuel = 100f;

        [Header("소모")]
        [Tooltip("최대 레이드 시간(분). 기획서 6.5 - 20분을 넘지 않는다")]
        [SerializeField] private float maxRaidMinutes = 20f;
        [Tooltip("점등 가능 시간의 상한 비율. 기획서 6.5 - 레이드 시간의 80%")]
        [SerializeField, Range(0.1f, 1f)] private float burnTimeRatio = 0.8f;
        [Tooltip("화승총 1발당 소모량 (기획서 7.7 - 2%)")]
        [SerializeField] private float matchlockShotCost = 2f;

        public float Fuel => fuel;
        public float Normalized => fuel / 100f;
        public bool IsLit { get; private set; }
        public bool IsEmpty => fuel <= 0f;

        public event Action OnChanged;
        public event Action<bool> OnLitChanged;

        /// <summary>초당 소모량. 100% 를 (레이드시간 * 비율) 동안 태우도록 역산한다.</summary>
        private float DrainPerSecond => 100f / (maxRaidMinutes * 60f * burnTimeRatio);

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame) Toggle();

            if (!IsLit) return;

            fuel = Mathf.Max(0f, fuel - DrainPerSecond * Time.deltaTime);
            OnChanged?.Invoke();
            if (fuel <= 0f) SetLit(false);      // 다 타면 강제 소등
        }

        public void Toggle() => SetLit(!IsLit);

        public void SetLit(bool lit)
        {
            if (lit && IsEmpty) return;         // 불씨 없이는 켜지지 않는다
            if (IsLit == lit) return;
            IsLit = lit;
            OnLitChanged?.Invoke(IsLit);
        }

        /// <summary>화승총 격발. 불씨가 모자라면 false (격발 실패).</summary>
        public bool TryConsumeForShot()
        {
            if (fuel < matchlockShotCost) return false;
            fuel -= matchlockShotCost;
            OnChanged?.Invoke();
            if (fuel <= 0f) SetLit(false);
            return true;
        }

        /// <summary>기름으로 충전. 상한 100%.</summary>
        public void Refill(float amount)
        {
            fuel = Mathf.Clamp(fuel + amount, 0f, 100f);
            OnChanged?.Invoke();
        }

        /// <summary>HUD 아이콘 단계 (기획서 6.5 - 100 / 50 / 30).</summary>
        public int IconStage => fuel > 50f ? 0 : fuel > 30f ? 1 : 2;
    }
}
