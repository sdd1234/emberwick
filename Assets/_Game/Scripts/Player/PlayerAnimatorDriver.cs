using UnityEngine;

namespace Capstone.Player
{
    /// <summary>
    /// 플레이어 상태를 애니메이터 파라미터로 옮긴다.
    /// 상태 판단은 전부 PlayerController · Crossbow 가 이미 하고 있으므로 여기서는 옮기기만 한다.
    ///
    /// Speed: 0 대기 / 1 걷기 / 2 달리기
    /// Aiming: 석궁을 당기고 있는 동안
    /// Fire: 발사한 순간 한 번
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        private static readonly int PSpeed  = Animator.StringToHash("Speed");
        private static readonly int PAiming = Animator.StringToHash("Aiming");
        private static readonly int PFire   = Animator.StringToHash("Fire");

        private PlayerController _controller;
        private Combat.Crossbow _crossbow;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _crossbow = GetComponent<Combat.Crossbow>();
            if (!animator) animator = GetComponentInChildren<Animator>();

            if (_crossbow != null) _crossbow.OnFired += HandleFired;
        }

        private void OnDestroy()
        {
            if (_crossbow != null) _crossbow.OnFired -= HandleFired;
        }

        private void HandleFired(int level)
        {
            if (animator != null) animator.SetTrigger(PFire);
        }

        private void Update()
        {
            if (animator == null || _controller == null) return;

            float speed = !_controller.IsMoving ? 0f : (_controller.IsSprinting ? 2f : 1f);
            // 구르는 동안은 달리는 그림이 제일 가깝다. 따로 굽지 않았다
            if (_controller.IsRolling) speed = 2f;

            animator.SetFloat(PSpeed, speed);
            animator.SetBool(PAiming, _crossbow != null && _crossbow.IsCharging);
        }
    }
}
