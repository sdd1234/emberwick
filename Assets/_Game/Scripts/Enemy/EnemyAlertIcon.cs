using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Capstone.Enemy
{
    /// <summary>
    /// 적 머리 위의 경계 표시 - 의심이면 ?, 발각이면 !.
    /// 월드 스페이스 Canvas 를 쓰는 이유가 있다. UI 는 Light2D 의 영향을 받지 않아서
    /// 어둠 속에서도 그대로 보인다. 시야 밖 적의 상태를 알 수 있어야 도망칠지 말지 판단할 수 있다.
    /// </summary>
    public class EnemyAlertIcon : MonoBehaviour
    {
        [SerializeField] private EnemyAI enemy;
        [Tooltip("적 발밑 기준 표시 높이 (m)")]
        [SerializeField] private float height = 1.75f;
        [SerializeField] private float worldScale = 0.006f;

        [Header("색")]
        [SerializeField] private Color suspiciousColor = new(0.98f, 0.82f, 0.30f, 1f);
        [SerializeField] private Color alertedColor    = new(0.95f, 0.26f, 0.22f, 1f);

        [Header("연출")]
        [Tooltip("상태가 바뀔 때 튀어오르는 크기 배율")]
        [SerializeField] private float popScale = 1.7f;
        [SerializeField] private float popDecay = 6f;
        [Tooltip("발각 상태일 때 깜빡이는 주기(초). 0 이면 안 깜빡인다")]
        [SerializeField] private float alertBlinkPeriod = 0.7f;

        [Header("도트 아이콘 (2026-10-29) - 있으면 글자 대신 그림을 쓴다")]
        [SerializeField] private Sprite alertSprite;
        [SerializeField] private Sprite suspectSprite;

        [Header("프리팹 조각")]
        [Tooltip("월드 스페이스 Canvas 의 RectTransform")]
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text label;

        private AlertState _shown = AlertState.Idle;
        private float _pop;

        private void Awake()
        {
            if (!enemy) enemy = GetComponentInParent<EnemyAI>();

            // 인스펙터에서 안 꽂혔으면 이름으로 한 번 찾아본다
            if (canvasRect == null)
            {
                var t = transform.Find("AlertCanvas");
                if (t != null) canvasRect = t as RectTransform;
            }
            if (canvasRect != null)
            {
                if (canvasGroup == null) canvasGroup = canvasRect.GetComponent<CanvasGroup>();
                if (label == null) label = canvasRect.GetComponentInChildren<TMP_Text>(true);
                canvasRect.gameObject.SetActive(false);
            }
        }

        private static float _exposureReadyAt;
        private SpriteRenderer _icon;
        private float _shownAt;

        /// <summary>
        /// 도트 아이콘 연출.
        ///   등장 - 도트 2배로 튀어나왔다가 1배로 내려앉는다 (어중간한 배율 없이 계단으로)
        ///   대기 - 1도트씩 위아래로 떠 있다
        ///   발각(!) - 짧게 하얗게 번쩍이고, 이후엔 천천히 깜빡인다 / 의심(?) - 좌우로 1도트씩 갸웃
        /// </summary>
        private void PixelIcon(AlertState state)
        {
            if (_icon == null)
            {
                var go = new GameObject("PixelAlert");
                go.transform.SetParent(transform, false);
                _icon = go.AddComponent<SpriteRenderer>();
                _icon.sharedMaterial = FX.PixelFX.UnlitMaterial;
                _icon.sortingOrder = 57;
                if (canvasRect) canvasRect.gameObject.SetActive(false);
            }
            bool show = state != AlertState.Idle;
            if (state != _shown)
            {
                // 발각음은 나를 봤을 때만 - 적끼리 싸우기 시작한 것까지 울리면 신호가 무뎌진다. 조원이 줄줄이 봐도 한 번만
                if (state == AlertState.Alerted && !enemy.FightingFoe && Time.time >= _exposureReadyAt)
                {
                    _exposureReadyAt = Time.time + 2f;
                    FX.SfxBank.Play2D("exposure", 1f);
                }
                else if (state == AlertState.Suspicious) FX.SfxBank.Play("suspect", transform.position, 0.45f, 0.04f);
                _shown = state; _shownAt = Time.time;
            }
            _icon.enabled = show;
            if (!show) return;

            _icon.sprite = state == AlertState.Alerted ? alertSprite : suspectSprite;
            float age = Time.time - _shownAt;
            float px = 1f / 38f;
            float ps = transform.lossyScale.x != 0f ? Mathf.Abs(transform.lossyScale.x) : 1f;
            int scale = age < 0.08f ? 2 : 1;
            float bob = Mathf.Round(Mathf.Sin(Time.time * 4f) * 1.2f) * px;
            float jump = age < 0.18f ? Mathf.Round((1f - age / 0.18f) * 6f) * px : 0f;
            float sway = state == AlertState.Suspicious ? Mathf.Round(Mathf.Sin(Time.time * 2.2f)) * px : 0f;
            _icon.transform.localScale = Vector3.one * (scale / ps);
            _icon.transform.rotation = Quaternion.identity;
            Vector3 p = transform.position + new Vector3(sway, height + 0.25f + bob + jump, 0f);
            _icon.transform.position = new Vector3(Mathf.Round(p.x / px) * px, Mathf.Round(p.y / px) * px, 0f);
            if (state == AlertState.Alerted)
            {
                bool flash = age < 0.12f || (age < 0.36f && age > 0.24f);
                float blink = alertBlinkPeriod > 0f && age > 0.5f ? Mathf.PingPong(Time.time / alertBlinkPeriod, 1f) : 1f;
                _icon.color = flash ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, Mathf.Lerp(0.6f, 1f, blink));   // 처음 두 번 빠르게 깜빡여 눈길을 끈다
            }
            else _icon.color = Color.white;
        }

        private void LateUpdate()
        {
            if (enemy == null) return;
            if (alertSprite != null && suspectSprite != null) { PixelIcon(enemy.State); return; }
            if (canvasRect == null) return;

            var state = enemy.State;
            bool show = state != AlertState.Idle;
            var canvasGo = canvasRect.gameObject;

            if (state != _shown)
            {
                _shown = state;
                _pop = 1f;                               // 상태가 바뀌면 한 번 튀어오른다
                if (show)
                {
                    label.text = state == AlertState.Alerted ? "!" : "?";
                    label.color = state == AlertState.Alerted ? alertedColor : suspiciousColor;
                }
            }

            if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
            if (!show) return;

            _pop = Mathf.MoveTowards(_pop, 0f, popDecay * Time.deltaTime);
            float scale = worldScale * (1f + (popScale - 1f) * _pop);
            canvasRect.localScale = Vector3.one * scale;

            // 표시는 항상 화면을 똑바로 보게 (부모가 뒤집혀도 글자가 안 뒤집히도록)
            canvasRect.rotation = Quaternion.identity;
            canvasRect.position = transform.position + Vector3.up * height;

            if (state == AlertState.Alerted && alertBlinkPeriod > 0f)
            {
                float t = Mathf.PingPong(Time.time / alertBlinkPeriod, 1f);
                canvasGroup.alpha = Mathf.Lerp(0.55f, 1f, t);
            }
            else canvasGroup.alpha = 1f;
        }
    }
}
