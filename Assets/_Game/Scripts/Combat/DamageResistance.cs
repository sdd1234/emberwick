using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 피해 내성 (피드백 - 감염자).
    /// 물리 피해는 거의 먹지 않는다. 다만 한 방이 문턱(pierceThreshold)을 넘으면 방어를 뚫고 그대로 들어간다 -
    /// 석궁 풀차징 · 기습 사격이 여기에 걸린다. 빛 · 불은 오히려 더 아프다.
    /// Health 가 피해를 받기 전에 이 값을 묻는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class DamageResistance : MonoBehaviour
    {
        [Header("물리")]
        [Tooltip("보통 물리 피해에 곱하는 배율. 0.2 = 80% 막는다")]
        [SerializeField, Range(0f, 1f)] private float physicalMultiplier = 0.2f;
        [Tooltip("한 방 피해가 이 값 이상이면 방어를 뚫고 전부 들어간다")]
        [SerializeField] private float pierceThreshold = 50f;

        [Header("속성")]
        [SerializeField] private float lightMultiplier = 1.8f;
        [SerializeField] private float fireMultiplier = 1.5f;

        public float Apply(float amount, DamageType type, out HitResult result)
        {
            switch (type)
            {
                case DamageType.Light: result = HitResult.Weakness; return amount * lightMultiplier;
                case DamageType.Fire:  result = HitResult.Weakness; return amount * fireMultiplier;
            }
            if (amount >= pierceThreshold) { result = HitResult.Pierced; return amount; }
            result = HitResult.Resisted;
            return amount * physicalMultiplier;
        }
    }
}
