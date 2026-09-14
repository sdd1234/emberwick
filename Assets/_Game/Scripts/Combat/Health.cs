using System;
using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>플레이어와 적이 공유하는 체력 컴포넌트.</summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private bool destroyOnDeath = true;
        [SerializeField] private float deathDelay = 0f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Normalized => maxHealth <= 0f ? 0f : Current / maxHealth;
        public bool IsDead { get; private set; }

        /// <summary>구르기 무적 등에서 true 로 올리면 피해를 무시한다.</summary>
        public bool Invulnerable { get; set; }

        public event Action<float, Vector2> OnDamaged;   // (피해량, 피격 방향)
        public event Action OnDeath;
        public event Action OnChanged;

        private void Awake() => Current = maxHealth;

        public void TakeDamage(float amount, Vector2 hitDirection = default)
        {
            if (IsDead || Invulnerable || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            OnDamaged?.Invoke(amount, hitDirection);
            OnChanged?.Invoke();

            if (Current <= 0f) Die();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(maxHealth, Current + amount);
            OnChanged?.Invoke();
        }

        /// <summary>리스폰용. 체력을 되돌리고 사망 상태를 푼다.</summary>
        public void Revive(float amount = -1f)
        {
            IsDead = false;
            Invulnerable = false;
            Current = amount > 0f ? Mathf.Min(amount, maxHealth) : maxHealth;
            OnChanged?.Invoke();
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;
            OnDeath?.Invoke();
            if (destroyOnDeath) Destroy(gameObject, deathDelay);
        }
    }
}
