using System;
using UnityEngine;

namespace Capstone.Player
{
    /// <summary>스태미나. 달리기(지속), 구르기(즉시), 근접공격(즉시 10%)에 쓰인다.</summary>
    public class PlayerStamina : MonoBehaviour
    {
        [Header("용량")]
        [SerializeField] private float maxStamina = 100f;

        [Header("소모")]
        [Tooltip("달리기 중 초당 소모량")]
        [SerializeField] private float sprintDrainPerSecond = 18f;
        [Tooltip("구르기 1회 소모량")]
        [SerializeField] private float rollCost = 25f;
        [Tooltip("근접 공격 1회 소모량 (기획서 7.3 - 10%)")]
        [SerializeField] private float meleeCost = 10f;

        [Header("회복")]
        [SerializeField] private float regenPerSecond = 14f;
        [Tooltip("소모 후 회복이 시작되기까지의 지연")]
        [SerializeField] private float regenDelay = 1.0f;

        public float Max => maxStamina;
        public float Current { get; private set; }
        public float Normalized => maxStamina <= 0f ? 0f : Current / maxStamina;
        public float RollCost => rollCost;
        public float MeleeCost => meleeCost;

        public event Action OnChanged;

        private float _regenBlockedUntil;

        private void Awake() => Current = maxStamina;

        private void Update()
        {
            if (Time.time >= _regenBlockedUntil && Current < maxStamina)
            {
                Current = Mathf.Min(maxStamina, Current + regenPerSecond * Time.deltaTime);
                OnChanged?.Invoke();
            }
        }

        public bool Has(float amount) => Current >= amount;

        /// <summary>즉시 소모. 잔량이 부족하면 아무것도 하지 않고 false.</summary>
        public bool TrySpend(float amount)
        {
            if (Current < amount) return false;
            Current -= amount;
            _regenBlockedUntil = Time.time + regenDelay;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>달리기처럼 매 프레임 갉아먹는 소모. 바닥나면 false 를 돌려 달리기를 끊는다.</summary>
        public bool DrainSprint(float deltaTime)
        {
            if (Current <= 0f) return false;
            Current = Mathf.Max(0f, Current - sprintDrainPerSecond * deltaTime);
            _regenBlockedUntil = Time.time + regenDelay;
            OnChanged?.Invoke();
            return Current > 0f;
        }
    }
}
