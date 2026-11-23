using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Enemy
{
    /// <summary>
    /// 적이 화면에 보이는 방식 (수정사항 2026-11-10 - 5. 적 표시 방식).
    ///   C  바로 옆 (1.5m)                     → 모습이 다 보인다
    ///   A  켜 둔 등불이 닿는 범위 (벽에 안 가림)  → 모습이 다 보인다
    ///   D  맵 광원 · 적 횃불 · 빛 플라스크 안     → 멀리서도 모습이 다 보인다
    ///   B  플레이어 시야각 안 (빛은 안 닿음)      → 회색 실루엣
    ///   E · F  어두운 건물 안 · 등 뒤 어둠       → 안 보인다 (소리로만 안다)
    /// 판정만 하고 그림은 건드리지 않는다 - 본체 SpriteRenderer 를 켜고 끄고, 실루엣은 자식 렌더러가 따라 그린다.
    /// 피격 번쩍임(HitFeel)이 본체 색을 만지므로 색으로 숨기지 않는다.
    /// </summary>
    [DefaultExecutionOrder(200)]   // EnemySpriteAnimator(그림 고르기) 다음에 돈다
    public class EnemyVisibility : MonoBehaviour
    {
        public enum Look { Hidden, Silhouette, Full }

        [SerializeField] private float closeRadius = 1.5f;
        [Tooltip("광원 반경 중 이만큼 안쪽이면 빛 안으로 친다 (가장자리는 어둑하다)")]
        [SerializeField, Range(0.3f, 1f)] private float lightCoverage = 0.8f;
        [Tooltip("이보다 약한 빛은 몸을 드러내지 못한다")]
        [SerializeField] private float minLightIntensity = 0.25f;
        [Tooltip("실루엣이 나타나고 사라지는 속도 (1/초)")]
        [SerializeField] private float fadeSpeed = 8f;

        public Look Current { get; private set; } = Look.Full;

        private SpriteRenderer _body;
        private SpriteRenderer _ghost;
        private Canvas[] _canvases;
        private Transform _player;
        private Player.VisionController _vision;
        private Player.TorchFuel _torch;
        private LayerMask _walls;
        private float _ghostAlpha;
        private float _nextCheck;

        private static Material _silhouetteMat;
        private static readonly List<Light2D> _lights = new();
        private static float _lightsAt = -1f;

        private void Start()
        {
            _body = GetComponent<SpriteRenderer>();
            if (_body == null) { enabled = false; return; }
            _canvases = GetComponentsInChildren<Canvas>(true);
            _walls = Combat.ProjectileOrigin.DefaultWallMask(0);

            var pc = FindFirstObjectByType<Player.PlayerController>();
            if (pc != null)
            {
                _player = pc.transform;
                _vision = pc.GetComponent<Player.VisionController>();
                _torch = pc.GetComponent<Player.TorchFuel>();
            }

            if (_silhouetteMat == null) _silhouetteMat = Resources.Load<Material>("EnemySilhouette");
            var go = new GameObject("Silhouette");
            go.transform.SetParent(transform, false);
            _ghost = go.AddComponent<SpriteRenderer>();
            _ghost.sharedMaterial = _silhouetteMat != null ? _silhouetteMat : FX.PixelFX.UnlitMaterial;
            _ghost.spriteSortPoint = _body.spriteSortPoint;
            _ghost.sortingLayerID = _body.sortingLayerID;
            _ghost.sortingOrder = _body.sortingOrder;
            _ghost.enabled = false;
            if (_silhouetteMat == null) _ghost.color = new Color(0.45f, 0.46f, 0.5f, 0.85f);
        }

        private void LateUpdate()
        {
            if (_player == null) return;
            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + 0.05f;
                Current = Evaluate();
            }
            Apply();
        }

        private void OnDisable()
        {
            // 꺼질 때(씬 정리 · 시체 처리) 숨긴 채로 남지 않게
            if (_body) _body.enabled = true;
            if (_ghost) _ghost.enabled = false;
        }

        private Vector2 Chest => (Vector2)transform.position + Vector2.up * 0.9f;

        public Look Evaluate()
        {
            Vector2 me = transform.position;
            Vector2 eye = _player.position;
            float d = Vector2.Distance(me, eye);

            if (d <= closeRadius) return Look.Full;                                    // C

            bool clear = Physics2D.Linecast(eye + Vector2.up * 0.9f, Chest, _walls).collider == null;
            bool lit = _torch != null && _torch.IsLit;
            if (lit && _vision != null && d <= _vision.LanternRadius * lightCoverage && clear) return Look.Full;   // A

            if (InWorldLight(me)) return Look.Full;                                      // D

            if (_vision != null && clear && d <= _vision.ViewRange)                      // B
            {
                var pc = _player.GetComponent<Player.PlayerController>();
                Vector2 aim = pc != null ? pc.AimDirection : Vector2.right;
                if (Vector2.Angle(aim, me - eye) <= _vision.ViewHalfAngle) return Look.Silhouette;
            }
            return Look.Hidden;                                                          // E · F
        }

        /// <summary>플레이어 것이 아닌 빛 - 길가 등불 · 모닥불 · 적 횃불 · 빛 플라스크 - 안에 서 있는가.</summary>
        private bool InWorldLight(Vector2 p)
        {
            if (Time.time - _lightsAt > 0.25f || _lightsAt < 0f)
            {
                _lightsAt = Time.time;
                _lights.Clear();
                foreach (var l in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
                {
                    if (l.lightType != Light2D.LightType.Point) continue;
                    if (l.GetComponentInParent<Player.PlayerController>() != null) continue;
                    _lights.Add(l);
                }
            }
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null || !l.isActiveAndEnabled || l.intensity < minLightIntensity) continue;
                float r = l.pointLightOuterRadius * lightCoverage;
                if (((Vector2)l.transform.position - p).sqrMagnitude > r * r) continue;
                // 빛이 벽 너머에서 새어 들어오는 건 치지 않는다 (그림자가 지는 빛만)
                if (l.shadowsEnabled && Physics2D.Linecast(l.transform.position, p + Vector2.up * 0.3f, _walls).collider != null) continue;
                return true;
            }
            return false;
        }

        private void Apply()
        {
            bool full = Current == Look.Full;
            _body.enabled = full;

            float target = Current == Look.Silhouette ? 1f : 0f;
            _ghostAlpha = Mathf.MoveTowards(_ghostAlpha, target, fadeSpeed * Time.deltaTime);
            if (full) _ghostAlpha = 0f;
            _ghost.enabled = _ghostAlpha > 0.01f;
            if (_ghost.enabled)
            {
                _ghost.sprite = _body.sprite;
                _ghost.flipX = _body.flipX;
                _ghost.transform.localPosition = _body.transform == transform ? Vector3.zero : _body.transform.localPosition;
                _ghost.transform.localRotation = Quaternion.identity;
                var c = _ghost.color; c.a = _ghostAlpha; _ghost.color = c;
            }

            // 머리 위 ! · ? 와 공격 예고도 안 보이는 적에게선 감춘다 - 어둠 속 위치를 알려 주면 안 된다
            bool showUi = Current != Look.Hidden;
            for (int i = 0; i < _canvases.Length; i++)
                if (_canvases[i] != null && _canvases[i].enabled != showUi) _canvases[i].enabled = showUi;
        }
    }
}
