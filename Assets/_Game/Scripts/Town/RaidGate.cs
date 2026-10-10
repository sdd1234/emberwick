using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Capstone.Town
{
    /// <summary>
    /// 마을의 레이드 이동 지역 (피드백).
    /// 들어서면 "레이드 지역으로 이동하겠습니까?" 와 예/아니오 창이 뜬다. 벗어나면 창이 사라진다.
    /// 아니오를 누르면 한 번 나갔다 다시 들어올 때까지 다시 묻지 않는다 - 서성일 때마다 뜨면 귀찮다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RaidGate : MonoBehaviour
    {
        [SerializeField] private GameObject popup;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private string question = "레이드 지역으로 이동하겠습니까?";
        [SerializeField] private Core.TownManager town;

        private Collider2D _zone;
        private Transform _player;
        private bool _inside;
        private bool _declined;

        public bool PopupShown => popup != null && popup.activeSelf;

        private void Awake()
        {
            _zone = GetComponent<Collider2D>();
            _zone.isTrigger = true;
            if (!town) town = FindFirstObjectByType<Core.TownManager>();
            if (popup) popup.SetActive(false);
            if (questionText) questionText.text = question;
            if (yesButton) yesButton.onClick.AddListener(Accept);
            if (noButton) noButton.onClick.AddListener(Decline);
        }

        private void Start()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            if (pc) _player = pc.transform;
        }

        private void OnDestroy()
        {
            if (yesButton) yesButton.onClick.RemoveListener(Accept);
            if (noButton) noButton.onClick.RemoveListener(Decline);
        }

        private void Update()
        {
            if (_player == null) return;

            // 트리거 이벤트 대신 발 위치로 잰다. 플레이어는 콜라이더가 둘(발자국 · 허트박스)이라
            // 들어오고 나가는 이벤트가 두 번씩 와서 세기 번거롭다
            bool inside = _zone.OverlapPoint(_player.position);
            if (inside == _inside) return;
            _inside = inside;

            if (!inside) _declined = false;
            Show(inside && !_declined);
        }

        private void Show(bool on)
        {
            if (popup == null) return;
            popup.SetActive(on);
            Cursor.visible = true;
        }

        public void Accept()
        {
            Show(false);
            if (town != null) town.Depart();
        }

        public void Decline()
        {
            _declined = true;
            Show(false);
        }
    }
}
