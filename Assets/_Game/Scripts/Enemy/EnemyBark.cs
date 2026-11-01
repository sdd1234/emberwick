using UnityEngine;
using TMPro;

namespace Capstone.Enemy
{
    /// <summary>
    /// 적의 짧은 외침 ("교대한다", "후퇴!", "같이 쳐!").
    /// 이교도가 교대하고 후퇴해서 동료를 데려오는 것, 도적이 서로 부르는 것 - 행동만으로는 잘 안 읽히는
    /// 의도를 머리 위 한 줄로 보여 준다. TextMeshPro 는 조명을 안 받아 어둠 속에서도 읽힌다.
    /// </summary>
    public class EnemyBark : MonoBehaviour
    {
        [SerializeField] private float height = 3.1f;
        [SerializeField] private float seconds = 1.8f;

        private TextMeshPro _text;
        private float _until;
        private float _cooldownUntil;

        public static void Say(Component who, string line, Color color, float cooldown = 3f)
        {
            if (who == null) return;
            var b = who.GetComponent<EnemyBark>();
            if (b == null) b = who.gameObject.AddComponent<EnemyBark>();
            b.Show(line, color, cooldown);
        }

        private void Show(string line, Color color, float cooldown)
        {
            if (Time.time < _cooldownUntil) return;
            _cooldownUntil = Time.time + cooldown;
            if (_text == null)
            {
                var go = new GameObject("Bark");
                go.transform.SetParent(transform, false);
                _text = go.AddComponent<TextMeshPro>();
                if (UI.PixelHUD.MainFont != null) _text.font = UI.PixelHUD.MainFont;
                else foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t != _text && t.font != null) { _text.font = t.font; break; }
                _text.fontSize = 2.4f;
                _text.alignment = TextAlignmentOptions.Center;
                _text.outlineWidth = 0.28f;
                _text.outlineColor = new Color32(8, 6, 10, 255);
                _text.sortingOrder = 58;
                _text.rectTransform.sizeDelta = new Vector2(8f, 1.2f);
            }
            _text.text = line;
            _text.color = color;
            _text.gameObject.SetActive(true);
            _until = Time.time + seconds;
        }

        private void LateUpdate()
        {
            if (_text == null || !_text.gameObject.activeSelf) return;
            // 부모 스케일(2배)과 상관없이 같은 크기 · 높이
            float s = Mathf.Abs(transform.lossyScale.x);
            _text.transform.localScale = Vector3.one / Mathf.Max(0.01f, s);
            _text.transform.localPosition = new Vector3(0f, height / Mathf.Max(0.01f, s), 0f);
            float left = _until - Time.time;
            var c = _text.color; c.a = Mathf.Clamp01(left / 0.4f); _text.color = c;
            if (left <= 0f) _text.gameObject.SetActive(false);
        }
    }
}
