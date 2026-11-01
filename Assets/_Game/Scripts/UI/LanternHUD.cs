using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Capstone.UI
{
    /// <summary>
    /// 화면 오른쪽 위 랜턴 (피드백 2026-10-29).
    /// 등불을 강하게 켰는지 · 약하게 켰는지 · 끄고 움직이는지에 따라 랜턴 유리의 빛이 바뀌고,
    /// 유리 안 빛이 차 있는 높이가 남은 불씨를 보여 준다. 숫자를 읽지 않아도 한눈에 들어오게.
    ///   꺼짐 - 유리가 어둡고 불씨 잔량만 희미하게
    ///   약하게 - 호박색, 작은 불꽃
    ///   강하게 - 밝은 노란빛, 큰 불꽃과 둘레의 빛무리
    /// 조각(빛무리 · 빈 유리 · 차오른 빛 · 불꽃 · 틀)은 처음 켜질 때 코드로 만든다.
    /// </summary>
    public class LanternHUD : MonoBehaviour
    {
        [SerializeField] private Player.TorchFuel torch;

        [Header("그림")]
        [Tooltip("랜턴 틀 - 유리 자리는 투명")]
        [SerializeField] private Sprite frameSprite;
        [Tooltip("유리 자리만 흰색인 마스크")]
        [SerializeField] private Sprite glassSprite;
        [SerializeField] private Sprite flameSprite;
        [SerializeField] private Sprite haloSprite;
        [Tooltip("랜턴 크기 (UI px). 도트가 정수배로 보이게 원화 크기의 배수로")]
        [SerializeField] private float pixelScale = 4f;

        [Header("색")]
        [SerializeField] private Color glassEmpty = new(0.07f, 0.07f, 0.09f, 1f);
        [SerializeField] private Color offFill = new(0.32f, 0.26f, 0.18f, 1f);
        [SerializeField] private Color weakFill = new(0.95f, 0.62f, 0.25f, 1f);
        [SerializeField] private Color strongFill = new(1f, 0.9f, 0.55f, 1f);

        private RectTransform _root;
        private Image _halo, _glassBack, _glassFill, _flame, _frame;
        private TMP_Text _label;
        private bool _ready;
        private float _shown;           // 0 꺼짐, 0.5 약, 1 강 - 서서히 따라간다
        private float _fillShown = 1f;

        private void Awake() => Ensure();
        private void OnEnable() => Ensure();

        private void Ensure()
        {
            if (_ready || frameSprite == null) return;
            _ready = true;
            if (!torch) torch = FindFirstObjectByType<Player.TorchFuel>();

            Vector2 size = frameSprite.rect.size * pixelScale;
            _root = new GameObject("Lantern", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-28f, -24f);
            _root.sizeDelta = size;

            _halo = Layer("Halo", haloSprite, size * 2.6f);
            _glassBack = Layer("GlassEmpty", glassSprite, size);
            _glassFill = Layer("GlassFill", glassSprite, size);
            _glassFill.type = Image.Type.Filled;
            _glassFill.fillMethod = Image.FillMethod.Vertical;
            _glassFill.fillOrigin = (int)Image.OriginVertical.Bottom;
            _flame = Layer("Flame", flameSprite, flameSprite ? flameSprite.rect.size * pixelScale : Vector2.one * 24f);
            _flame.rectTransform.anchoredPosition = new Vector2(0f, -size.y * 0.06f);
            _frame = Layer("Frame", frameSprite, size);

            var labelGo = new GameObject("LanternLabel", typeof(RectTransform));
            labelGo.transform.SetParent(_root, false);
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            if (PixelHUD.MainFont != null) _label.font = PixelHUD.MainFont;
            else foreach (var t in GetComponentsInChildren<TMP_Text>(true)) if (t != _label && t.font) { _label.font = t.font; break; }
            _label.fontSize = 24;
            _label.alignment = TextAlignmentOptions.Center;
            _label.outlineWidth = 0.25f;
            _label.outlineColor = new Color32(0, 0, 0, 255);
            var lr = _label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.anchoredPosition = new Vector2(0f, -6f);
            lr.sizeDelta = new Vector2(220f, 52f);
        }

        private Image Layer(string name, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            if (sprite == null) img.enabled = false;
            return img;
        }

        private void LateUpdate()
        {
            if (!_ready) { Ensure(); if (!_ready) return; }
            if (torch == null) { torch = FindFirstObjectByType<Player.TorchFuel>(); if (torch == null) return; }

            var level = torch.Level;
            float goal = level == Player.LanternLevel.Strong ? 1f : level == Player.LanternLevel.Weak ? 0.5f : 0f;
            _shown = Mathf.MoveTowards(_shown, goal, Time.unscaledDeltaTime * 3f);
            _fillShown = Mathf.Lerp(_fillShown, torch.Normalized, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

            float t = Time.unscaledTime;
            float flick = 1f + (Mathf.PerlinNoise(t * 6f, 0.3f) - 0.5f) * 0.22f + (Mathf.PerlinNoise(t * 17f, 2.1f) - 0.5f) * 0.1f;

            Color fill = _shown <= 0.5f ? Color.Lerp(offFill, weakFill, _shown * 2f) : Color.Lerp(weakFill, strongFill, (_shown - 0.5f) * 2f);
            float bright = Mathf.Lerp(0.55f, 1f, _shown) * (_shown > 0.01f ? flick : 1f);
            _glassBack.color = glassEmpty;
            _glassFill.color = new Color(Mathf.Min(1f, fill.r * bright), Mathf.Min(1f, fill.g * bright), Mathf.Min(1f, fill.b * bright), 1f);
            _glassFill.fillAmount = _fillShown;

            if (_flame)
            {
                _flame.enabled = _shown > 0.02f && flameSprite != null;
                float s = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((_shown - 0.3f) / 0.7f)) * (0.92f + (flick - 1f) * 0.8f);
                // 도트가 뭉개지지 않게 스케일은 계단으로
                s = Mathf.Max(0.5f, Mathf.Round(s * 4f) / 4f);
                _flame.rectTransform.localScale = new Vector3(1f, s, 1f);
                _flame.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_shown * 2.2f));
            }
            if (_halo)
            {
                float a = _shown * _shown * 0.55f * flick;
                _halo.color = new Color(fill.r, fill.g * 0.95f, fill.b * 0.7f, a);
                _halo.enabled = haloSprite != null && a > 0.005f;
            }

            string lv = level == Player.LanternLevel.Strong ? "<color=#FFE08A>강</color>"
                      : level == Player.LanternLevel.Weak ? "<color=#F2A65A>약</color>" : "<color=#8A8478>꺼짐</color>";
            _label.text = $"{lv}  <color=#E6E0D2>{torch.Fuel:0}%</color>";
        }
    }
}
