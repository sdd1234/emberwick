using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Capstone.UI
{
    /// <summary>
    /// 드래그 중 커서를 따라다니는 반투명 블록.
    /// 격자 위 아이템 블록과 모양은 같지만 쓰임이 달라서 따로 둔다 - 이건 판정에 관여하지 않는다.
    /// </summary>
    public class DragVisual : MonoBehaviour
    {
        [SerializeField] private Image fill;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image icon;

        public RectTransform Rect => (RectTransform)transform;
        public Image Fill => fill;
        public TMP_Text Label => label;
        public Image Icon => icon;
    }
}
