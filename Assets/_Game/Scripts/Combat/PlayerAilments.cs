using System;
using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 상처와 병 (피드백 - 감염체에게 맞으면 감염 · 출혈).
    ///   출혈 - 초당 피해가 붕대를 감을 때까지 계속된다
    ///   감염 - 맞을수록 쌓인다(0~100). 쌓인 만큼 걸음과 숨이 무거워지고, 꽉 차면 체력이 서서히 썩는다
    /// 판이 끝나면(씬이 바뀌면) 둘 다 사라진다 - 마을에서 회복한 것으로 친다.
    /// </summary>
    public class PlayerAilments : MonoBehaviour
    {
        [Header("출혈")]
        [SerializeField] private float bleedDamagePerSecond = 1.5f;
        [SerializeField] private float bleedTick = 1f;

        [Header("감염")]
        [Tooltip("감염 100% 일 때의 이동속도 배율")]
        [SerializeField, Range(0.3f, 1f)] private float slowAtFull = 0.75f;
        [Tooltip("감염 100% 일 때의 스태미나 회복 배율")]
        [SerializeField, Range(0f, 1f)] private float staminaRegenAtFull = 0.4f;
        [Tooltip("감염이 꽉 찼을 때 이 간격(초)마다 체력 1 이 깎인다")]
        [SerializeField] private float rotInterval = 2f;

        [Header("참조")]
        [SerializeField] private Health health;

        public bool IsBleeding { get; private set; }
        /// <summary>감염 수치 0~100.</summary>
        public float Infection { get; private set; }
        public bool IsInfected => Infection > 0.5f;

        /// <summary>PlayerController · PlayerStamina 가 곱해 쓰는 배율.</summary>
        public float MoveSpeedMultiplier => Mathf.Lerp(1f, slowAtFull, Infection / 100f);
        public float StaminaRegenMultiplier => Mathf.Lerp(1f, staminaRegenAtFull, Infection / 100f);

        public event Action OnChanged;

        private float _nextBleedAt;
        private float _nextRotAt;

        private void Awake()
        {
            if (!health) health = GetComponent<Health>();
        }

        private void Update()
        {
            if (health == null || health.IsDead) return;

            if (IsBleeding && Time.time >= _nextBleedAt)
            {
                _nextBleedAt = Time.time + bleedTick;
                health.TakeDamage(bleedDamagePerSecond * bleedTick, Vector2.zero, true);
            }

            if (Infection >= 100f && Time.time >= _nextRotAt)
            {
                _nextRotAt = Time.time + rotInterval;
                health.TakeDamage(1f, Vector2.zero, true);
            }
        }

        /// <summary>맞았다. infection 만큼 감염이 쌓이고, bleedChance 확률로 피가 나기 시작한다.</summary>
        public void Wound(float infection, float bleedChance)
        {
            bool changed = false;
            if (infection > 0f)
            {
                Infection = Mathf.Min(100f, Infection + infection);
                changed = true;
            }
            if (!IsBleeding && bleedChance > 0f && UnityEngine.Random.value < bleedChance)
            {
                IsBleeding = true;
                _nextBleedAt = Time.time + bleedTick;
                changed = true;
            }
            if (changed) OnChanged?.Invoke();
        }

        /// <summary>붕대 - 피를 멎게 한다. 멎게 했으면 true.</summary>
        public bool StopBleeding()
        {
            if (!IsBleeding) return false;
            IsBleeding = false;
            OnChanged?.Invoke();
            return true;
        }
    }
}
