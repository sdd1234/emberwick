using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace Capstone.Core
{
    /// <summary>
    /// 레이드 한 판의 진행을 관리한다 (기획서 3 - 코어 루프의 프로토타입판).
    /// 지금은 사망 / 리스폰 / 제한시간만 다룬다. 탈출과 정산은 탈출 지점이 생기면 붙인다.
    /// </summary>
    public class RaidManager : MonoBehaviour
    {
        [Header("플레이어")]
        [SerializeField] private GameObject player;
        [Tooltip("리스폰 지점. 비우면 시작 위치를 쓴다")]
        [SerializeField] private Transform spawnPoint;

        [Header("제한 시간 (기획서 6.5 - 레이드 20분)")]
        [SerializeField] private float raidMinutes = 20f;
        [SerializeField] private bool useTimeLimit = true;

        [Header("결과 색")]
        [SerializeField] private Color failColor    = new(0.85f, 0.30f, 0.26f, 1f);
        [SerializeField] private Color successColor = new(0.45f, 0.92f, 0.58f, 1f);

        [Header("탈출 (기획서 5.2)")]
        [SerializeField] private Map.ExtractionPoint extraction;
        [SerializeField] private TMP_Text extractText;

        [Header("UI")]
        [SerializeField] private GameObject deathPanel;
        [SerializeField] private TMP_Text deathText;
        [SerializeField] private TMP_Text timerText;

        public bool IsDead { get; private set; }
        public bool IsExtracted { get; private set; }
        public float TimeRemaining { get; private set; }

        private Combat.Health _health;
        private Vector3 _startPosition;

        private void Awake()
        {
            if (!player)
            {
                var pc = FindFirstObjectByType<Capstone.Player.PlayerController>();
                if (pc) player = pc.gameObject;
            }
            if (!player) { Debug.LogError("[RaidManager] player 가 비어 있습니다."); enabled = false; return; }

            _startPosition = player.transform.position;
            _health = player.GetComponent<Combat.Health>();
            if (_health != null) _health.OnDeath += HandleDeath;

            if (!extraction) extraction = FindFirstObjectByType<Map.ExtractionPoint>();
            if (extraction != null)
            {
                extraction.OnExtracted += HandleExtracted;
                extraction.OnProgress += HandleExtractProgress;
            }

            TimeRemaining = raidMinutes * 60f;
            if (deathPanel) deathPanel.SetActive(false);
            if (extractText) extractText.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_health != null) _health.OnDeath -= HandleDeath;
            if (extraction != null)
            {
                extraction.OnExtracted -= HandleExtracted;
                extraction.OnProgress -= HandleExtractProgress;
            }
        }

        /// <summary>탈출 지점에 서 있는 동안 차오르는 진행도를 보여준다.</summary>
        private void HandleExtractProgress(float p)
        {
            if (extractText == null) return;
            bool show = p > 0.001f && !IsExtracted && !IsDead;
            if (extractText.gameObject.activeSelf != show) extractText.gameObject.SetActive(show);
            if (show) extractText.text = $"탈출 중...  {p * 100f:0}%";
        }

        private void HandleExtracted()
        {
            if (IsExtracted || IsDead) return;
            IsExtracted = true;

            SetPlayerControlEnabled(false);
            var interactor = player.GetComponent<Items.Interactor>();
            if (interactor) interactor.StopSearching();
            if (extractText) extractText.gameObject.SetActive(false);
            CloseMinimap();

            int carried = 0;
            float weight = 0f;
            var inv = player.GetComponent<Items.PlayerInventory>();
            if (inv != null) { carried = inv.Grid.Items.Count; weight = inv.CurrentWeight; }

            if (deathPanel)
            {
                deathPanel.transform.SetAsLastSibling();
                deathPanel.SetActive(true);
            }
            if (deathText)
            {
                deathText.color = successColor;
                deathText.text = $"탈출 성공\n\n회수 {carried}종 · {weight:0.0}kg\n\n[R] 다시 시도";
            }

            Debug.Log($"[RaidManager] 탈출 완료 - {carried}종 {weight:0.0}kg 회수");
        }

        private void Update()
        {
            if (useTimeLimit && !IsDead && !IsExtracted)
            {
                TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
                if (timerText)
                    timerText.text = $"{Mathf.FloorToInt(TimeRemaining / 60f):00}:{Mathf.FloorToInt(TimeRemaining % 60f):00}";
                if (TimeRemaining <= 0f) HandleDeath();     // 시간 초과 = 생존 실패
            }

            if (!IsDead && !IsExtracted) return;

            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) Respawn();
        }

        private void HandleDeath()
        {
            if (IsDead) return;
            IsDead = true;

            // 죽은 동안 조작을 막는다
            SetPlayerControlEnabled(false);

            // 뒤지던 격자 화면이 열려 있으면 닫는다 - 안 그러면 사망 화면과 겹친다
            var interactor = player.GetComponent<Items.Interactor>();
            if (interactor) interactor.StopSearching();
            CloseMinimap();

            if (deathPanel)
            {
                deathPanel.transform.SetAsLastSibling();   // 무엇보다 위에
                deathPanel.SetActive(true);
            }
            if (deathText)
            {
                deathText.color = failColor;
                deathText.text = "생존 실패\n\n[R] 다시 시도";
            }
            Debug.Log("[RaidManager] 플레이어 사망 - R 키로 리스폰");
        }

        public void Respawn()
        {
            IsDead = false;
            IsExtracted = false;
            if (deathPanel) deathPanel.SetActive(false);

            player.transform.position = spawnPoint ? spawnPoint.position : _startPosition;

            var body = player.GetComponent<Rigidbody2D>();
            if (body) body.linearVelocity = Vector2.zero;

            // 체력을 되살린다. Health 는 재사용을 염두에 두고 Revive 를 노출한다.
            var hp = player.GetComponent<Combat.Health>();
            if (hp != null) hp.Revive();

            var torch = player.GetComponent<Capstone.Player.TorchFuel>();
            if (torch != null) { torch.SetLit(false); torch.Refill(100f); }

            SetPlayerControlEnabled(true);
            TimeRemaining = raidMinutes * 60f;
        }

        /// <summary>결과 화면이 지도에 가리지 않도록 닫는다.</summary>
        private void CloseMinimap()
        {
            var map = FindFirstObjectByType<UI.MinimapUI>();
            if (map != null) map.SetOpen(false);
        }

        private void SetPlayerControlEnabled(bool on)
        {
            var pc = player.GetComponent<Capstone.Player.PlayerController>();
            if (pc) pc.enabled = on;

            var xbow = player.GetComponent<Combat.Crossbow>();
            if (xbow) xbow.enabled = on;

            var body = player.GetComponent<Rigidbody2D>();
            if (body && !on) body.linearVelocity = Vector2.zero;
        }
    }
}
