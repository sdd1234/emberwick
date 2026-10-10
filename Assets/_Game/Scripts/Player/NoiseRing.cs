using UnityEngine;

namespace Capstone.Player
{
    /// <summary>
    /// 발소리가 닿는 최대 거리를 보여주는 반투명 원 (피드백 - 커지는 효과 없이 최대 거리만).
    /// 플레이어 자식으로 하나만 붙어 있고, 걷는 동안 그 반경(걷기 3m / 뛰기 10m)에 고정으로 떠 있다.
    /// 멈추면 옅어지며 사라진다. 반경은 즉시 바뀐다 - 퍼지는 연출은 일부러 뺐다.
    /// LineRenderer 에 언릿 머티리얼을 써서 어둠 속에서도 보인다.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class NoiseRing : MonoBehaviour
    {
        [SerializeField] private int segments = 64;
        [SerializeField] private float width = 0.04f;
        [SerializeField] private Color walkColor = new(0.85f, 0.88f, 0.95f, 0.22f);
        [SerializeField] private Color runColor  = new(1f, 0.62f, 0.38f, 0.32f);
        [Tooltip("나타나고 사라지는 속도 (초당 알파)")]
        [SerializeField] private float fadeSpeed = 3f;

        private LineRenderer _line;
        private float _radius = -1f;
        private Color _color;
        private float _alpha;
        private bool _visible;
        private float _holdUntil;
        private Transform _follow;

        /// <summary>
        /// 따라다닐 발. 플레이어 자식으로 두지 않는 이유 - 플레이어는 SortingGroup 으로 묶여 있어서
        /// 자식이 되면 원 전체가 플레이어 발 위치로 정렬돼 남쪽 벽 밑에 깔린다.
        /// </summary>
        public void Follow(Transform feet) => _follow = feet;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.loop = true;
            _line.useWorldSpace = false;
            _line.positionCount = segments;
            _line.widthMultiplier = width;
            _color = walkColor;
            Apply();
        }

        /// <summary>매 프레임 부른다. visible=false 면 옅어지며 사라진다.</summary>
        public void Show(float radius, bool loud, bool visible)
        {
            // 사격처럼 잠깐 띄운 원이 남아 있는 동안은 발소리 원으로 덮지 않는다
            if (Time.time < _holdUntil) return;
            _visible = visible && radius > 0f;
            if (!_visible) return;
            _color = loud ? runColor : walkColor;
            SetRadius(radius);
        }

        /// <summary>사격 등 한 번 나는 소리 - 그 반경을 잠깐 띄웠다가 사라진다.</summary>
        public void Flash(float radius, bool loud, float seconds = 0.6f)
        {
            _color = loud ? runColor : walkColor;
            SetRadius(radius);
            _visible = true;
            _holdUntil = Time.time + seconds;
        }

        private void SetRadius(float r)
        {
            if (Mathf.Approximately(r, _radius)) return;
            _radius = r;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                _line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        private void LateUpdate()
        {
            if (_follow != null) transform.position = _follow.position;
            if (Time.time >= _holdUntil && _holdUntil > 0f) { _holdUntil = 0f; _visible = false; }
            _alpha = Mathf.MoveTowards(_alpha, _visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (_line == null) return;
            var c = _color; c.a *= _alpha;
            _line.startColor = _line.endColor = c;
            _line.enabled = _alpha > 0.001f && _radius > 0f;
        }
    }
}
