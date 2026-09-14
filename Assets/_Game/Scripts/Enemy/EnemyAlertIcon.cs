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

        private void LateUpdate()
        {
            if (enemy == null || canvasRect == null) return;

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
