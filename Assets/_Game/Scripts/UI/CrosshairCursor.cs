using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Capstone.UI
{
    /// <summary>
    /// 마우스 커서를 십자 조준점으로 대체한다 (기획서 6.6).
    /// 조준점은 장식이 아니라 정보다 - 벌어진 간격이 지금 쏘면 생길 탄착 범위이고,
    /// 차징이 깊어질수록 좁아진다. 재장전 중에는 붉게 죽는다.
    /// 눈금 네 개와 가운데 점은 프리팹에 들어 있다 (Tick0~3, CenterDot).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CrosshairCursor : MonoBehaviour
    {
        [Header("대상")]
        [SerializeField] private Combat.Crossbow crossbow;
        [SerializeField] private Player.PlayerController player;
        [SerializeField] private Camera worldCamera;

        [Header("모양")]
        [SerializeField] private float tickLength = 11f;
        [SerializeField] private float tickThickness = 2f;
        [Tooltip("가장 좁혀졌을 때의 중심-눈금 간격 (px)")]
        [SerializeField] private float minGap = 5f;
        [Tooltip("가장 벌어졌을 때의 간격 (px)")]
        [SerializeField] private float maxGap = 34f;
        [SerializeField] private float centerDotSize = 2.5f;
        [SerializeField] private float gapLerpSpeed = 14f;

        [Header("색")]
        [SerializeField] private Color idleColor     = new(0.92f, 0.90f, 0.84f, 0.85f);
        [SerializeField] private Color chargingColor = new(0.98f, 0.82f, 0.36f, 0.95f);
        [SerializeField] private Color fullColor     = new(0.98f, 0.45f, 0.28f, 1f);
        [SerializeField] private Color reloadColor   = new(0.55f, 0.58f, 0.66f, 0.55f);

        [Header("동작")]
        [Tooltip("OS 커서를 숨긴다")]
        [SerializeField] private bool hideSystemCursor = true;

        [Header("프리팹 조각")]
        [Tooltip("위 · 아래 · 왼쪽 · 오른쪽 순서")]
        [SerializeField] private RectTransform[] ticks = new RectTransform[4];
        [SerializeField] private Image[] tickImages = new Image[4];
        [SerializeField] private Image centerDot;

        private RectTransform _rt;
        private Canvas _canvas;
        private float _gap;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            if (!player) player = FindFirstObjectByType<Player.PlayerController>();
            if (!crossbow && player) crossbow = player.GetComponent<Combat.Crossbow>();
            if (!worldCamera) worldCamera = Camera.main;

            BindPieces();
            _gap = maxGap;
        }

        /// <summary>인스펙터에서 안 꽂았으면 이름으로 찾아 잇는다.</summary>
        private void BindPieces()
        {
            for (int i = 0; i < 4; i++)
            {
                if (ticks[i] == null)
                {
                    var t = transform.Find("Tick" + i);
                    if (t != null) ticks[i] = t as RectTransform;
                }
                if (tickImages[i] == null && ticks[i] != null) tickImages[i] = ticks[i].GetComponent<Image>();
            }
            if (centerDot == null)
            {
                var d = transform.Find("CenterDot");
                if (d != null) centerDot = d.GetComponent<Image>();
            }
        }

        private void OnEnable()
        {
            if (hideSystemCursor) Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.visible = true;      // 꺼질 때 커서를 돌려주지 않으면 에디터에서 곤란해진다
        }

        private void LateUpdate()
        {
            // 가방/상자 격자를 다루는 동안엔 조준점 대신 시스템 커서를 쓴다
            bool uiOpen = LootScreenUI.AnyScreenOpen;
            if (_visible == uiOpen) SetVisible(!uiOpen);
            if (uiOpen) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            FollowMouse(mouse.position.ReadValue());
            UpdateSpread();
        }

        private bool _visible = true;

        private void SetVisible(bool on)
        {
            _visible = on;
            for (int i = 0; i < 4; i++) if (ticks[i]) ticks[i].gameObject.SetActive(on);
            if (centerDot) centerDot.gameObject.SetActive(on);
            if (hideSystemCursor) Cursor.visible = !on;
        }

        private void FollowMouse(Vector2 screenPos)
        {
            if (_canvas == null) { _rt.position = screenPos; return; }

            if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _rt.position = screenPos;
                return;
            }

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.transform as RectTransform, screenPos,
                _canvas.worldCamera, out local);
            _rt.localPosition = local;
        }

        private void UpdateSpread()
        {
            float targetGap = maxGap;
            Color color = idleColor;

            if (crossbow != null)
            {
                if (crossbow.IsReloading)
                {
                    targetGap = maxGap * 1.25f;      // 재장전 중엔 크게 벌어져 못 쏜다는 걸 알린다
                    color = reloadColor;
                }
                else if (crossbow.IsCharging)
                {
                    // 차징이 깊을수록 조준점이 좁아진다 - 정확도가 올라간다는 뜻
                    float t = Mathf.Clamp01((crossbow.CurrentLevel + crossbow.ChargeProgress)
                                            / Mathf.Max(1, crossbow.LevelCount - 1));
                    targetGap = Mathf.Lerp(maxGap, minGap, t);
                    bool atFull = crossbow.CurrentLevel >= crossbow.LevelCount - 1;
                    color = atFull ? fullColor : chargingColor;
                }
                else if (player != null && player.IsAiming)
                {
                    targetGap = Mathf.Lerp(maxGap, minGap, 0.45f);
                }
            }

            _gap = Mathf.Lerp(_gap, targetGap, 1f - Mathf.Exp(-gapLerpSpeed * Time.unscaledDeltaTime));

            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < 4; i++)
            {
                ticks[i].anchoredPosition = dirs[i] * (_gap + tickLength * 0.5f);
                tickImages[i].color = color;
            }
            if (centerDot) centerDot.color = color;
        }
    }
}
