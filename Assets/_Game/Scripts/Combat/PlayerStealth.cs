using System;
using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 은신 상태 (기획서 7.4).
    /// 불을 끄고 적에게 발각되지 않은 동안 은신으로 친다.
    /// 기습 사격을 하면 즉시 풀리고 쿨다운 후에야 다시 은신할 수 있다.
    /// </summary>
    public class PlayerStealth : MonoBehaviour
    {
        [Tooltip("기습 발사 후 재은신까지의 쿨다운 (기획서 7.4 - 7초)")]
        [SerializeField] private float restealthCooldown = 7f;
        [SerializeField] private Player.TorchFuel torch;

        public bool IsStealthed { get; private set; } = true;
        public bool IsDetected { get; private set; }
        public float CooldownRemaining => Mathf.Max(0f, _restealthAt - Time.time);

        public event Action<bool> OnStealthChanged;

        private float _restealthAt;
        private int _detectorCount;

        private void Awake()
        {
            if (!torch) torch = GetComponent<Player.TorchFuel>();
        }

        private void Update()
        {
            bool lit = torch != null && torch.IsLit;
            bool canStealth = !lit && !IsDetected && Time.time >= _restealthAt;

            if (canStealth != IsStealthed)
            {
                IsStealthed = canStealth;
                OnStealthChanged?.Invoke(IsStealthed);
            }
        }

        /// <summary>기습 사격 후 호출. 은신을 깨고 쿨다운을 건다.</summary>
        public void BreakStealth()
        {
            _restealthAt = Time.time + restealthCooldown;
            if (!IsStealthed) return;
            IsStealthed = false;
            OnStealthChanged?.Invoke(false);
        }

        /// <summary>적이 플레이어를 발각/상실할 때 호출한다. 발각 중인 적이 하나라도 있으면 은신 불가.</summary>
        public void SetDetectedBy(bool detected)
        {
            _detectorCount = Mathf.Max(0, _detectorCount + (detected ? 1 : -1));
            IsDetected = _detectorCount > 0;
        }
    }
}
