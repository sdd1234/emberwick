using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Items
{
    /// <summary>
    /// F 키 상호작용 (기획서 8.2).
    /// 줍기는 한 번 누르면 끝나고, 루팅은 F 로 켜고 끄는 토글이다.
    /// 뒤지는 도중에 멀어지거나 F 를 다시 누르면 중단되며, 진행도는 남아 있어 이어서 뒤질 수 있다.
    ///
    /// 사거리 안에 대상이 여러 개 겹칠 수 있다 (적이 상자 위에 쓰러지는 경우가 흔하다).
    /// 그래서 가장 가까운 하나만 잡지 않고 후보 목록을 들고 Q 로 순환한다.
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        [SerializeField] private float radius = 1.6f;
        [SerializeField] private LayerMask interactableMask = ~0;
        [Tooltip("뒤지기를 시작한 뒤 이 거리를 넘어가면 자동으로 중단된다")]
        [SerializeField] private float breakOffDistance = 2.2f;

        /// <summary>지금 조준 중인 대상. HUD 프롬프트에 쓴다.</summary>
        public IInteractable Current { get; private set; }
        /// <summary>토글로 뒤지고 있는 상자. 없으면 null.</summary>
        public LootContainer Searching { get; private set; }

        /// <summary>사거리 안의 상호작용 가능한 대상 수. 1 보다 크면 HUD 에 전환 안내를 띄운다.</summary>
        public int CandidateCount => _candidates.Count;
        /// <summary>Current 가 후보 목록에서 몇 번째인지 (1-base). 없으면 0.</summary>
        public int CurrentIndex => Current == null ? 0 : _candidates.IndexOf(Current) + 1;

        public string CurrentPrompt
        {
            get
            {
                if (Searching != null) return Searching.GetPrompt(gameObject);
                if (Current == null) return string.Empty;

                string prompt = Current.GetPrompt(gameObject);
                // 겹쳐 있을 때만 전환 안내를 붙인다. 하나뿐이면 군더더기다.
                if (_candidates.Count > 1) prompt += $"    [Q] 다음 대상 ({CurrentIndex}/{_candidates.Count})";
                return prompt;
            }
        }

        private readonly Collider2D[] _hits = new Collider2D[12];
        // 거리 순으로 정렬된 후보들. 매 프레임 다시 채운다.
        private readonly List<IInteractable> _candidates = new();
        private readonly List<float> _distances = new();

        private void Update()
        {
            var kb = Keyboard.current;

            RefreshCandidates();

            // Q - 겹친 대상 사이를 순환한다. 뒤지는 중에는 대상을 못 바꾼다.
            if (kb != null && kb.qKey.wasPressedThisFrame && Searching == null) CycleNext();

            if (kb == null) return;
            bool pressed = kb.fKey.wasPressedThisFrame;

            // --- 이미 뒤지는 중 ---
            if (Searching != null)
            {
                // 끌고 있는 도중에 화면을 닫으면 격자 블록이 파괴돼 OnEndDrag 가 영영 안 불린다.
                // 그러면 커서를 따라다니던 블록이 화면에 잔상으로 남는다. 손을 뗄 때까지 기다린다.
                bool dragging = UI.ItemDrag.IsDragging;

                if (pressed && !dragging) { StopSearching(); return; }  // 다시 누르면 중단
                // 다 털었다고 창을 닫지 않는다. 마지막 물건을 옮긴 직후 화면이 사라지면
                // 방금 뭘 챙겼는지 확인할 새도 없고, 가방을 정리하던 손도 끊긴다. 닫는 건 사람이 정한다.
                if (!dragging && Vector2.Distance(transform.position, Searching.transform.position) > breakOffDistance)
                { StopSearching(); return; }                           // 멀어지면 중단

                Searching.Interact(gameObject);                        // 매 프레임 진행
                return;
            }

            // --- 새로 시작 ---
            if (!pressed || Current == null || !Current.CanInteract(gameObject)) return;

            if (Current is LootContainer box)
            {
                Searching = box;
                box.Interact(gameObject);
            }
            else
            {
                Current.Interact(gameObject);                          // 줍기 등 단발형
            }
        }

        public void StopSearching()
        {
            if (Searching == null) return;
            Searching.StopSearching();
            Searching = null;
        }

        /// <summary>사거리 안 후보를 거리 순으로 다시 모으고, 기존 선택을 최대한 유지한다.</summary>
        private void RefreshCandidates()
        {
            _candidates.Clear();
            _distances.Clear();

            var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = interactableMask };
            int count = Physics2D.OverlapCircle(transform.position, radius, filter, _hits);

            for (int i = 0; i < count; i++)
            {
                var candidate = _hits[i].GetComponentInParent<IInteractable>();
                if (candidate == null || !candidate.CanInteract(gameObject)) continue;
                // 한 대상이 콜라이더를 여러 개 들고 있을 수 있다 (몸통 + 허트박스). 중복 제거.
                if (_candidates.Contains(candidate)) continue;

                float d = Vector2.SqrMagnitude(_hits[i].transform.position - transform.position);

                // 삽입 정렬 - 후보는 많아야 서넛이라 이 편이 GC 없이 간단하다.
                int at = _candidates.Count;
                while (at > 0 && _distances[at - 1] > d) at--;
                _candidates.Insert(at, candidate);
                _distances.Insert(at, d);
            }

            // 고른 대상이 아직 사거리 안이면 그대로 둔다.
            // 매 프레임 가장 가까운 걸로 되돌리면 Q 로 바꾼 선택이 한 프레임 만에 풀린다.
            if (Current != null && _candidates.Contains(Current)) return;

            Current = _candidates.Count > 0 ? _candidates[0] : null;
        }

        private void CycleNext()
        {
            if (_candidates.Count <= 1) return;
            int next = (_candidates.IndexOf(Current) + 1) % _candidates.Count;
            Current = _candidates[next];
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
