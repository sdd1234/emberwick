using UnityEngine;
using UnityEngine.UI;

namespace Capstone.UI
{
    /// <summary>
    /// 캐릭터 7시 방향의 도넛형 스태미나 (기획서 6.6).
    /// 시계 방향으로 감소한다. 가득 찬 동안은 숨겨서 화면을 어지럽히지 않는다.
    /// </summary>
    public class StaminaDial : MonoBehaviour
    {
        [SerializeField] private Player.PlayerStamina stamina;
        [Tooltip("Image Type = Filled, Fill Method = Radial 360 으로 설정한 링 이미지")]
        [SerializeField] private Image ring;
        [SerializeField] private CanvasGroup group;

        [Header("표시")]
        [Tooltip("가득 찼을 때 숨긴다")]
        [SerializeField] private bool hideWhenFull = true;
        [SerializeField] private float fadeSpeed = 6f;
        [SerializeField] private Color normalColor = new(0.85f, 0.80f, 0.55f, 0.9f);
        [Tooltip("이 비율 아래로 떨어지면 경고색")]
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.25f;
        [SerializeField] private Color lowColor = new(0.85f, 0.35f, 0.25f, 0.95f);

        private void Awake()
        {
            if (!stamina) stamina = GetComponentInParent<Player.PlayerStamina>();
            if (!group) group = GetComponent<CanvasGroup>();

            if (ring)
            {
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Radial360;
                ring.fillOrigin = (int)Image.Origin360.Top;
                ring.fillClockwise = true;          // 기획서 6.6 - 시계 방향으로 감소
            }
        }

        private void LateUpdate()
        {
            if (stamina == null) return;

            float n = stamina.Normalized;
            if (ring)
            {
                ring.fillAmount = n;
                ring.color = n <= lowThreshold ? lowColor : normalColor;
            }

            if (!group) return;
            float target = (hideWhenFull && n >= 0.999f) ? 0f : 1f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.deltaTime);
        }
    }
}
