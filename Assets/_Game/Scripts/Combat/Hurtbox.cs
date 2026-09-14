using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 피격 판정 전용 콜라이더.
    ///
    /// 탑다운 2D 에서는 콜라이더 하나로 이동과 피격을 같이 처리할 수 없다.
    /// 이동용 콜라이더는 캐릭터가 바닥에 닿는 '발자국'이라 아주 납작한데,
    /// 투사체는 총구 높이(어깨쯤)로 날아가기 때문에 그 위를 그냥 지나가 버린다.
    /// 그래서 몸 전체를 덮는 트리거를 따로 두고, 맞는 판정은 이쪽으로만 받는다.
    ///
    /// 투사체는 GetComponentInParent 로 Health 를 찾으므로 부모에 Health 가 있으면 된다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Hurtbox : MonoBehaviour
    {
        [SerializeField] private Health owner;

        /// <summary>이 허트박스가 대신 맞아주는 체력.</summary>
        public Health Owner => owner;

        private void Awake()
        {
            if (!owner) owner = GetComponentInParent<Health>();

            foreach (var c in GetComponents<Collider2D>()) c.isTrigger = true;

            if (owner == null)
                Debug.LogWarning($"[Hurtbox] {name} 에 연결된 Health 가 없습니다. 피격이 무시됩니다.", this);
        }
    }
}
