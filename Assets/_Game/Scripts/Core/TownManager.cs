using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace Capstone.Core
{
    /// <summary>
    /// 마을 (기획서 3 - 레이드와 레이드 사이).
    /// 하는 일은 둘뿐이다. 지난 판이 어떻게 끝났는지 알려주고, 다시 내보낸다.
    /// 판매는 ShopUI 가 맡는다.
    /// </summary>
    public class TownManager : MonoBehaviour
    {
        [Header("씬")]
        [Tooltip("출격이 불러올 씬. Build Settings 에 등록되어 있어야 한다")]
        [SerializeField] private string raidSceneName = "Raid";

        [Header("조각")]
        [Tooltip("지난 판의 결과를 적는 줄")]
        [SerializeField] private TMP_Text headlineText;
        [SerializeField] private Button raidButton;

        [Header("결과 색")]
        [SerializeField] private Color survivedColor = new(0.86f, 0.84f, 0.78f, 1f);
        [SerializeField] private Color diedColor     = new(0.85f, 0.45f, 0.40f, 1f);

        private bool _leaving;

        private void Awake()
        {
            // 레이드에서 물건을 끌던 도중에 죽었을 수도 있다.
            // ItemDrag 는 static 이라 씬을 넘어와도 살아 있으므로 여기서 털어낸다.
            UI.ItemDrag.Clear();
            if (raidButton != null) raidButton.onClick.AddListener(Depart);
        }

        private void OnDestroy()
        {
            if (raidButton != null) raidButton.onClick.RemoveListener(Depart);
        }

        private void Start() => WriteHeadline();

        private void Update()
        {
            if (_leaving) return;

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Depart();
        }

        /// <summary>다시 레이드로. 씬을 새로 여니 필드도 가방도 처음 상태로 돌아간다.</summary>
        public void Depart()
        {
            if (_leaving) return;
            _leaving = true;

            UI.ItemDrag.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(raidSceneName);
        }

        private void WriteHeadline()
        {
            if (headlineText == null) return;

            if (!GameSession.HasRaided)
            {
                headlineText.color = survivedColor;
                headlineText.text = "장이 선 아침이다.  팔 것을 내놓고 다시 나가라.";
                return;
            }

            if (!GameSession.LastRaidSurvived)
            {
                headlineText.color = diedColor;
                headlineText.text = GameSession.LastRaidLost > 0
                    ? $"겨우 목숨만 건졌다.  들고 있던 {GameSession.LastRaidLost}종은 그 자리에 두고 왔다."
                    : "겨우 목숨만 건졌다.  빈손이었던 게 그나마 다행이다.";
                return;
            }

            headlineText.color = survivedColor;
            string line = GameSession.LastRaidHaul > 0
                ? $"무사히 빠져나왔다.  창고에 {GameSession.LastRaidHaul}종 · 값어치 {GameSession.LastRaidValue:N0} G 를 들여놓았다."
                : "무사히 빠져나왔다.  다만 들고 온 것은 없다.";
            if (GameSession.LastRaidLost > 0)
                line += $"  창고가 좁아 {GameSession.LastRaidLost}종은 흘렸다.";
            headlineText.text = line;
        }
    }
}
