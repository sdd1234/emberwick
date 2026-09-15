using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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
        private bool _restarting;

        private void Awake()
        {
            if (!player)
            {
                var pc = FindFirstObjectByType<Capstone.Player.PlayerController>();
                if (pc) player = pc.gameObject;
            }
            if (!player) { Debug.LogError("[RaidManager] player 가 비어 있습니다."); enabled = false; return; }

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

        /// <summary>
        /// 다시 시도. 씬을 통째로 다시 불러온다.
        ///
        /// 예전에는 여기서 위치 · 체력 · 횃불 · 제한시간만 되돌렸는데, 그러면 <b>가방이 그대로 남는다</b>.
        /// 죽거나 탈출해서 판이 끝났는데도 챙긴 전리품을 그대로 들고 새 판을 시작하는 셈이라,
        /// 익스트랙션 슈터의 전제(들고 나가야 내 것이 된다)가 통째로 무너진다.
        /// 남는 건 가방만이 아니다 - 이미 턴 상자는 비어 있고, 죽은 적은 죽은 채, 시체는 시체인 채,
        /// 지도의 안개도 걷힌 채로 남는다. 하나씩 되돌리는 것보다 씬을 다시 여는 쪽이 확실하다.
        /// (마을 · 창고가 생기면 탈출 성공분만 여기서 창고로 옮긴 뒤 씬을 다시 열면 된다)
        /// </summary>
        public void Respawn()
        {
            if (_restarting) return;
            _restarting = true;

            // 씬을 갈아엎어도 static 은 살아남는다.
            // 끌던 물건 상태를 남겨두면 새 판이 시작하자마자 이미 사라진 아이템을 붙잡고 있게 된다.
            UI.ItemDrag.Clear();
            Time.timeScale = 1f;

            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);      // 빌드 세팅에 없으면 이름으로 (에디터 대비)
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
