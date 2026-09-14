using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Capstone.Core;
using Capstone.Items;

namespace Capstone.UI
{
    /// <summary>
    /// 격자 위 물건에 커서를 올리면 뜨는 설명창.
    /// 칸이 작아서 블록 안에 다 담을 수 없는 정보(등급 · 무게 · 가치 · 설명)를 여기서 보여준다.
    /// 커서를 따라다니되 화면 밖으로 나가지 않게 붙잡는다.
    /// </summary>
    public class ItemTooltip : MonoBehaviour
    {
        public static ItemTooltip Instance { get; private set; }

        [SerializeField] private Vector2 cursorOffset = new(18f, -18f);

        [Header("프리팹 조각")]
        [Tooltip("설명창 본체. 커서를 따라다니는 판")]
        [SerializeField] private RectTransform root;
        [Tooltip("맨 위 등급 색 띠")]
        [SerializeField] private Image gradeBar;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text gradeText;
        [SerializeField] private TMP_Text statText;
        [SerializeField] private TMP_Text descText;

        private RectTransform _canvasRect;

        private void Awake()
        {
            Instance = this;
            _canvasRect = GetComponentInParent<Canvas>().rootCanvas.GetComponent<RectTransform>();

            // 인스펙터에서 안 꽂았으면 이름으로 찾아 잇는다
            if (root == null)
            {
                var t = transform.Find("ItemTooltip");
                if (t != null) root = t as RectTransform;
            }
            if (root != null)
            {
                if (gradeBar == null)  { var t = root.Find("GradeBar"); if (t) gradeBar = t.GetComponent<Image>(); }
                if (nameText == null)  { var t = root.Find("Name");  if (t) nameText  = t.GetComponent<TMP_Text>(); }
                if (gradeText == null) { var t = root.Find("Grade"); if (t) gradeText = t.GetComponent<TMP_Text>(); }
                if (statText == null)  { var t = root.Find("Stat");  if (t) statText  = t.GetComponent<TMP_Text>(); }
                if (descText == null)  { var t = root.Find("Desc");  if (t) descText  = t.GetComponent<TMP_Text>(); }
                root.gameObject.SetActive(false);
            }
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void LateUpdate()
        {
            if (root == null || !root.gameObject.activeSelf) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, mouse.position.ReadValue(), null, out local);

            // 화면 밖으로 새지 않게 가장자리에서 붙잡는다
            Vector2 size = root.sizeDelta;
            Vector2 half = _canvasRect.sizeDelta * 0.5f;
            Vector2 pos = local + cursorOffset;
            pos.x = Mathf.Clamp(pos.x, -half.x, half.x - size.x);
            pos.y = Mathf.Clamp(pos.y, -half.y + size.y, half.y);
            root.anchoredPosition = pos;
        }

        public static void Show(GridItem item)
        {
            if (Instance == null || item == null || item.Def == null) return;
            if (!item.Identified) { Hide(); return; }     // 감정 전에는 알려주지 않는다
            Instance.Populate(item);
        }

        public static void Hide()
        {
            if (Instance != null && Instance.root != null)
                Instance.root.gameObject.SetActive(false);
        }

        private void Populate(GridItem item)
        {
            if (root == null) return;
            var def = item.Def;
            var c = def.grade.ToColor();

            nameText.text = item.Count > 1 ? $"{def.displayName}  x{item.Count}" : def.displayName;
            nameText.color = c;
            gradeBar.color = c;

            gradeText.text = def.grade.ToKorean();
            gradeText.color = c;

            statText.text = $"{item.W}x{item.H}칸   ·   {def.weight * item.Count:0.0}kg   ·   {def.value * item.Count}G";
            descText.text = def.description;

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

    }
}
