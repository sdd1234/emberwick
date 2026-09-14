using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Combat
{
    /// <summary>
    /// 석궁 - 저소음 저화력, 은신 플레이의 주력 (기획서 7.3 / 7.4).
    /// 좌클릭을 홀드해 3단계로 차징하고, 놓으면 발사한다.
    /// 차징이 깊을수록 세지지만 그만큼 느려져 도망칠 수 없게 된다.
    /// </summary>
    public class Crossbow : MonoBehaviour
    {
        [Serializable]
        public struct ChargeLevel
        {
            [Tooltip("이 단계에 도달하는 데 필요한 홀드 시간(초)")]
            public float chargeTime;
            [Tooltip("기본 데미지 대비 배율")]
            public float damageMultiplier;
            [Tooltip("사거리 (m)")]
            public float range;
            [Tooltip("이동속도 배율. 0.7 = -30%")]
            public float moveSpeedMultiplier;
            [Tooltip("사거리 끝에서의 좌우 탄착 폭 (m)")]
            public float spreadWidth;
        }

        [Header("기본치")]
        [SerializeField] private float baseDamage = 25f;
        [Tooltip("기획서 7.4 - 재장전 1.5초")]
        [SerializeField] private float reloadTime = 1.5f;
        [Tooltip("기습 사격 최종 배율 (기획서 7.4 - x2.5)")]
        [SerializeField] private float ambushMultiplier = 2.5f;

        [Header("차징 단계 (기획서 7.4)")]
        [SerializeField]
        private ChargeLevel[] levels =
        {
            new() { chargeTime = 0f,    damageMultiplier = 1.0f, range = 15f, moveSpeedMultiplier = 1.0f, spreadWidth = 1.5f },
            new() { chargeTime = 0.5f,  damageMultiplier = 1.5f, range = 18f, moveSpeedMultiplier = 0.7f, spreadWidth = 1.2f },
            new() { chargeTime = 1.2f,  damageMultiplier = 2.2f, range = 22f, moveSpeedMultiplier = 0.4f, spreadWidth = 1.0f },
        };

        [Header("참조")]
        [SerializeField] private Bolt boltPrefab;
        [Tooltip("볼트가 나가는 지점. 비워두면 플레이어 위치에서 0.6m 앞")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private Player.PlayerController player;
        [SerializeField] private PlayerStealth stealth;

        public int CurrentLevel { get; private set; }
        public bool IsCharging { get; private set; }
        public bool IsReloading { get; private set; }
        /// <summary>다음 단계까지의 진행도 0~1. 최고 단계면 1.</summary>
        public float ChargeProgress { get; private set; }
        public float ReloadProgress { get; private set; }

        public event Action<int> OnFired;          // 발사한 차징 단계
        public event Action OnFullCharge;          // 풀차징 도달 (시각/청각 피드백용)

        private float _chargeStart;
        private float _reloadEnd;
        private bool  _loaded = true;
        private bool  _fullChargeNotified;

        private void Awake()
        {
            if (!player)  player  = GetComponentInParent<Player.PlayerController>();
            if (!stealth) stealth = GetComponentInParent<PlayerStealth>();
        }

        private void Update()
        {
            TickReload();

            var mouse = Mouse.current;
            if (mouse == null) return;

            if (IsReloading) { CancelCharge(); return; }

            // wasPressedThisFrame 로 받으면, 재장전 중에 누른 클릭이 그 프레임에 버려진다.
            // 장전이 끝났을 때 버튼을 계속 누르고 있으면 떼었다 다시 눌러야 하는 문제가 생긴다.
            // isPressed 로 보면 장전이 끝나는 즉시 이어서 차징에 들어간다.
            if (!IsCharging && _loaded && mouse.leftButton.isPressed) BeginCharge();
            if (IsCharging) TickCharge();
            if (mouse.leftButton.wasReleasedThisFrame && IsCharging) Release();
        }

        private void BeginCharge()
        {
            IsCharging = true;
            _chargeStart = Time.time;
            CurrentLevel = 0;
            _fullChargeNotified = false;
        }

        private void TickCharge()
        {
            float held = Time.time - _chargeStart;

            // 도달한 최고 단계를 찾는다
            int level = 0;
            for (int i = 0; i < levels.Length; i++)
                if (held >= levels[i].chargeTime) level = i;
            CurrentLevel = level;

            // 다음 단계까지의 진행도
            if (level + 1 < levels.Length)
            {
                float from = levels[level].chargeTime;
                float to   = levels[level + 1].chargeTime;
                ChargeProgress = Mathf.InverseLerp(from, to, held);
            }
            else
            {
                ChargeProgress = 1f;
                if (!_fullChargeNotified)
                {
                    _fullChargeNotified = true;
                    OnFullCharge?.Invoke();          // 기획서 7.4 - 풀차징 시 시청각 피드백
                }
            }

            // 차징 중 이동속도 페널티
            if (player) player.ExternalSpeedMultiplier = levels[level].moveSpeedMultiplier;
        }

        private void Release()
        {
            var lv = levels[CurrentLevel];
            bool isFullCharge = CurrentLevel == levels.Length - 1;

            // 기습 사격 - 은신 상태에서 풀차징으로 쏘면 치명타 보장
            bool ambush = isFullCharge && stealth != null && stealth.IsStealthed;
            float damage = baseDamage * lv.damageMultiplier * (ambush ? ambushMultiplier : 1f);

            Fire(lv, damage, ambush);

            if (ambush) stealth.BreakStealth();       // 기획서 7.4 - 기습 후 은신 즉시 해제

            CancelCharge();
            _loaded = false;
            IsReloading = true;
            _reloadEnd = Time.time + reloadTime;
            OnFired?.Invoke(CurrentLevel);
        }

        private void Fire(ChargeLevel lv, float damage, bool isCrit)
        {
            if (!boltPrefab || !player) return;

            Vector2 origin = muzzle ? (Vector2)muzzle.position
                                    : (Vector2)player.transform.position + player.AimDirection * 0.6f + Vector2.up * 1.0f;

            // 발사 방향은 반드시 '총구에서 커서로' 계산한다.
            // 플레이어 원점 기준으로 쏘면 총구가 원점에서 떨어진 만큼 시차가 생겨 커서에 안 맞는다.
            Vector2 toCursor = player.AimWorldPoint - origin;
            Vector2 baseDir = toCursor.sqrMagnitude > 0.0001f ? toCursor.normalized : player.AimDirection;

            // 탄착 폭을 각도로 환산한다 (사거리 끝에서 spreadWidth 만큼 벌어지도록)
            float halfSpreadDeg = Mathf.Atan2(lv.spreadWidth * 0.5f, lv.range) * Mathf.Rad2Deg;
            float offset = UnityEngine.Random.Range(-halfSpreadDeg, halfSpreadDeg);
            Vector2 dir = (Quaternion.Euler(0f, 0f, offset) * baseDir).normalized;

            var bolt = Instantiate(boltPrefab, origin, Quaternion.identity);
            bolt.Launch(dir, damage, lv.range, isCrit, player.gameObject);
        }

        private void CancelCharge()
        {
            IsCharging = false;
            ChargeProgress = 0f;
            if (player) player.ExternalSpeedMultiplier = 1f;   // 속도 페널티 해제
        }

        private void TickReload()
        {
            if (!IsReloading) return;

            ReloadProgress = 1f - Mathf.Clamp01((_reloadEnd - Time.time) / reloadTime);
            if (Time.time < _reloadEnd) return;

            IsReloading = false;
            ReloadProgress = 1f;
            _loaded = true;
        }

        /// <summary>현재 차징 단계의 설정값 조회 (HUD 표시용).</summary>
        public ChargeLevel GetLevel(int index) => levels[Mathf.Clamp(index, 0, levels.Length - 1)];
        public int LevelCount => levels.Length;

        /// <summary>지금 쏘면 벌어질 탄착 반각(도). 조준점 크기에 쓴다.</summary>
        public float CurrentHalfSpreadDegrees
        {
            get
            {
                var lv = levels[Mathf.Clamp(CurrentLevel, 0, levels.Length - 1)];
                return Mathf.Atan2(lv.spreadWidth * 0.5f, lv.range) * Mathf.Rad2Deg;
            }
        }

        /// <summary>장전되어 있는지. 조준점 색으로 표시한다.</summary>
        public bool IsLoaded => _loaded;
    }
}
