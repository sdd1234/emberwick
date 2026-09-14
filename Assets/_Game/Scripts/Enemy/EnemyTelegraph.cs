using UnityEngine;
using UnityEngine.UI;

namespace Capstone.Enemy
{
    /// <summary>
    /// 공격 예고.
    /// 근접 적은 예비 동작 동안 앞쪽으로 붉은 부채꼴이 뻗어나오고, 다 뻗으면 휘두른다.
    /// 부채꼴이라 옆이나 뒤로 돌아 들어가면 피할 수 있다 - 원형이면 피할 방법이 없다.
    /// 원거리 적은 주위로 붉은 원이 조여들다가 다 조이면 쏜다.
    /// 둘 다 월드 스페이스 UI 라서 Light2D 어둠에 먹히지 않는다 - 시야 밖에서도 위험이 보인다.
    /// </summary>
    public class EnemyTelegraph : MonoBehaviour
    {
        [Header("근접 - 앞쪽 공격 범위")]
        [SerializeField] private Color meleeZoneColor   = new(0.85f, 0.22f, 0.20f, 0.16f);
        [SerializeField] private Color meleeStrikeColor = new(1f, 0.30f, 0.24f, 0.55f);
        [Tooltip("예비 동작 시작 시 부채꼴이 뻗어나온 비율")]
        [SerializeField, Range(0f, 1f)] private float strikeStartScale = 0.25f;

        [Header("원거리 - 조여드는 원")]
        [SerializeField] private Color rangedRingColor = new(0.95f, 0.25f, 0.22f, 0.85f);
        [Tooltip("조준 시작 시 원의 크기 배율")]
        [SerializeField] private float ringStartScale = 3.4f;
        [Tooltip("발사 직전 원의 반경 (m)")]
        [SerializeField] private float ringEndRadius = 0.75f;

        [SerializeField] private float worldScale = 0.01f;

        [Header("프리팹 조각")]
        [Tooltip("월드 스페이스 Canvas 의 RectTransform")]
        [SerializeField] private RectTransform canvasRect;
        [Tooltip("근접 - 공격이 닿는 범위 전체 (옅게)")]
        [SerializeField] private Image zone;
        [Tooltip("근접 - 실제로 뻗어나오는 부채꼴 (진하게)")]
        [SerializeField] private Image strike;
        [Tooltip("원거리 - 조여드는 원")]
        [SerializeField] private Image ring;

        private MeleeEnemy _melee;
        private RangedEnemy _ranged;

        private void Awake()
        {
            _melee  = GetComponentInParent<MeleeEnemy>();
            _ranged = GetComponentInParent<RangedEnemy>();

            if (canvasRect == null)
            {
                var t = transform.Find("TelegraphCanvas");
                if (t != null) canvasRect = t as RectTransform;
            }
            if (zone != null)   zone.gameObject.SetActive(false);
            if (strike != null) strike.gameObject.SetActive(false);
            if (ring != null)   ring.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (canvasRect == null) return;

            // 발밑 살짝 위 - 바닥에 깔린 것처럼 보이게
            canvasRect.position = transform.position + Vector3.up * 0.3f;
            canvasRect.rotation = Quaternion.identity;

            if (_melee  != null) UpdateMelee();
            if (_ranged != null) UpdateRanged();
        }

        // ---------- 근접 ----------
        private void UpdateMelee()
        {
            float p = _melee.WindupProgress;
            bool show = p >= 0f;

            if (zone.gameObject.activeSelf != show)
            {
                zone.gameObject.SetActive(show);
                strike.gameObject.SetActive(show);
            }
            if (!show) return;

            float arc = _melee.AttackArcDegrees;
            float fill = Mathf.Clamp01(arc / 360f);

            // Radial360 은 로컬 위쪽에서 시계방향으로 채워진다.
            // 부채꼴의 한가운데가 공격 방향을 보게 하려면 아래만큼 돌려주면 된다.
            Vector2 facing = _melee.AttackFacing;
            float facingDeg = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            float z = facingDeg - 90f + arc * 0.5f;

            float fullDiameterPx = (_melee.AttackRange * 2f) / worldScale;

            // 닿는 범위 전체 - 옅게 깔아둔다
            zone.fillAmount = fill;
            zone.rectTransform.sizeDelta = Vector2.one * fullDiameterPx;
            zone.rectTransform.localRotation = Quaternion.Euler(0f, 0f, z);

            // 실제 타격 - 예비 동작이 진행될수록 앞으로 뻗어나온다
            float reach = Mathf.Lerp(strikeStartScale, 1f, p);
            strike.fillAmount = fill;
            strike.rectTransform.sizeDelta = Vector2.one * (fullDiameterPx * reach);
            strike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, z);

            var c = meleeStrikeColor;
            c.a = Mathf.Lerp(meleeStrikeColor.a * 0.5f, meleeStrikeColor.a, p);
            strike.color = c;
        }

        // ---------- 원거리 ----------
        private void UpdateRanged()
        {
            float p = _ranged.AimProgress;
            bool show = p >= 0f;

            if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
            if (!show) return;

            // 크게 벌어졌다가 발사 직전에 몸에 딱 붙는다
            float radius = Mathf.Lerp(ringEndRadius * ringStartScale, ringEndRadius, p);
            ring.rectTransform.sizeDelta = Vector2.one * ((radius * 2f) / worldScale);

            var c = rangedRingColor;
            c.a = Mathf.Lerp(0.35f, 1f, p);
            ring.color = c;
        }
    }
}
