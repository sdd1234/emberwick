using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 총구 위치를 조준 방향에 맞춰 돌려준다.
    /// 고정해 두면 왼쪽을 봐도 오른쪽에서 볼트가 나가 어색하고, 조준도 틀어진다.
    ///
    /// 높이를 어깨(0.7m)에 두면 안 된다. 탑다운에서 +Y 는 하늘이 아니라 <b>북쪽</b>이라,
    /// 그만큼 몸에서 북쪽으로 떨어진 자리에서 볼트가 태어난다. 북쪽에 벽이 있으면
    /// 벽 속에서 생겨 그대로 박힌다. 광원과 같은 평면에 두는 게 맞다.
    /// </summary>
    public class WeaponMuzzle : MonoBehaviour
    {
        [SerializeField] private Player.PlayerController player;
        [Tooltip("발밑 피벗 기준 높이 (m). 광원(ConeLight)과 같은 평면으로 맞춘다")]
        [SerializeField] private float shoulderHeight = 0.19f;
        [Tooltip("몸 중심에서 총구까지의 거리 (m)")]
        [SerializeField] private float reach = 0.42f;
        [Tooltip("위아래로 겨눌 때 총구가 따라 올라가는 비율")]
        [SerializeField, Range(0f, 1f)] private float verticalFollow = 0.45f;

        private void Awake()
        {
            if (!player) player = GetComponentInParent<Player.PlayerController>();
        }

        private void LateUpdate()
        {
            if (player == null) return;
            Vector2 d = player.AimDirection;
            transform.localPosition = new Vector3(
                d.x * reach,
                shoulderHeight + d.y * reach * verticalFollow,
                0f);
        }
    }
}
