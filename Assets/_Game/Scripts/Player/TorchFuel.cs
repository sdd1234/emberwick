using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Player
{
    /// <summary>등불 밝기 단계 (피드백 2026-10-29 - 강하게 / 약하게 / 끄고 움직인다).</summary>
    public enum LanternLevel { Off, Weak, Strong }

    /// <summary>
    /// 불씨 - 조명과 화승총 격발이 함께 쓰는 자원 (기획서 6.5).
    /// 시야를 넓히면 불씨가 닳고, 불씨가 닳으면 화승총을 못 쏜다. 이 게임의 핵심 긴장.
    ///
    /// 피드백 반영: 밝기가 두 단계다. E 로 켜고 끄고, 켠 채로 마우스 휠을 굴려 약/강을 고른다.
    /// 약하게 켜면 덜 보이는 대신 덜 들키고 덜 닳는다. 길가 등불에서 빛을 거둬 채울 수 있다.
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

        [Header("밝기 단계")]
        [Tooltip("약하게 켰을 때 닳는 속도 배율")]
        [SerializeField] private float weakDrain = 0.5f;
        [Tooltip("강하게 켰을 때 닳는 속도 배율")]
        [SerializeField] private float strongDrain = 1.25f;
        [SerializeField] private LanternLevel litLevel = LanternLevel.Strong;

        public float Fuel => fuel;
        public float Normalized => fuel / 100f;
        public bool IsLit { get; private set; }
        public bool IsEmpty => fuel <= 0f;
        /// <summary>지금 밝기. 꺼져 있으면 Off.</summary>
        public LanternLevel Level => IsLit ? litLevel : LanternLevel.Off;
        /// <summary>켰을 때 쓸 밝기 (꺼져 있어도 기억한다).</summary>
        public LanternLevel PreferredLevel => litLevel;

        public event Action OnChanged;
        public event Action<bool> OnLitChanged;
        public event Action<LanternLevel> OnLevelChanged;

        /// <summary>초당 소모량. 100% 를 (레이드시간 * 비율) 동안 태우도록 역산한다.</summary>
        private float DrainPerSecond => 100f / (maxRaidMinutes * 60f * burnTimeRatio)
                                        * (litLevel == LanternLevel.Weak ? weakDrain : strongDrain);

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame) Toggle();

            // 휠 - 위로 굴리면 강하게, 아래로 굴리면 약하게. 꺼져 있을 때 굴리면 다음에 켤 밝기만 바꾼다
            var mouse = Mouse.current;
            if (mouse != null && !UI.LootScreenUI.AnyScreenOpen)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.1f) SetLevel(LanternLevel.Strong);
                else if (wheel < -0.1f) SetLevel(LanternLevel.Weak);
            }

            if (!IsLit) return;

            fuel = Mathf.Max(0f, fuel - DrainPerSecond * Time.deltaTime);
            OnChanged?.Invoke();
            if (fuel <= 0f) SetLit(false);      // 다 타면 강제 소등
        }

        public void Toggle() => SetLit(!IsLit);

        public void SetLevel(LanternLevel level)
        {
            if (level == LanternLevel.Off) { SetLit(false); return; }
            if (litLevel == level) return;
            litLevel = level;
            OnLevelChanged?.Invoke(Level);
        }

        public void SetLit(bool lit)
        {
            if (lit && IsEmpty) return;         // 불씨 없이는 켜지지 않는다
            if (IsLit == lit) return;
            IsLit = lit;
            OnLitChanged?.Invoke(IsLit);
            OnLevelChanged?.Invoke(Level);
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

        /// <summary>정해진 양을 쓴다 (빛 플라스크 · 등불에 빛 걸기). 모자라면 false.</summary>
        public bool TrySpend(float amount)
        {
            if (fuel < amount) return false;
            fuel -= amount;
            OnChanged?.Invoke();
            if (fuel <= 0f) SetLit(false);
            return true;
        }

        /// <summary>기름으로 충전 · 등불에서 빛을 거둔다. 상한 100%.</summary>
        public void Refill(float amount)
        {
            fuel = Mathf.Clamp(fuel + amount, 0f, 100f);
            OnChanged?.Invoke();
        }

        /// <summary>HUD 아이콘 단계 (기획서 6.5 - 100 / 50 / 30).</summary>
        public int IconStage => fuel > 50f ? 0 : fuel > 30f ? 1 : 2;
    }
}
