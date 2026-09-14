using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace Capstone.UI
{
    /// <summary>인트로 / 타이틀 화면.</summary>
    public class TitleMenu : MonoBehaviour
    {
        [Header("씬")]
        [Tooltip("시작 버튼이 불러올 씬 이름. Build Settings 에 등록되어 있어야 한다")]
        [SerializeField] private string gameSceneName = "Raid";

        [Header("버튼")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button quitButton;

        [Header("연출")]
        [SerializeField] private CanvasGroup fadeGroup;
        [SerializeField] private float fadeInTime = 1.2f;
        [SerializeField] private TMP_Text titleText;
        [Tooltip("타이틀이 은은하게 밝아졌다 어두워지는 주기(초)")]
        [SerializeField] private float titlePulsePeriod = 3.5f;

        private float _fadeTimer;
        private bool _leaving;

        private void Awake()
        {
            if (startButton) startButton.onClick.AddListener(StartGame);
            if (quitButton)  quitButton.onClick.AddListener(QuitGame);
            if (fadeGroup)   fadeGroup.alpha = 1f;      // 검정에서 밝아진다
        }

        private void Update()
        {
            if (fadeGroup && !_leaving && _fadeTimer < fadeInTime)
            {
                _fadeTimer += Time.deltaTime;
                fadeGroup.alpha = 1f - Mathf.Clamp01(_fadeTimer / fadeInTime);
            }

            if (titleText && titlePulsePeriod > 0f)
            {
                float t = (Mathf.Sin(Time.time * Mathf.PI * 2f / titlePulsePeriod) + 1f) * 0.5f;
                var c = titleText.color;
                c.a = Mathf.Lerp(0.72f, 1f, t);
                titleText.color = c;
            }
        }

        public void StartGame()
        {
            if (_leaving) return;
            _leaving = true;
            if (string.IsNullOrEmpty(gameSceneName))
            {
                Debug.LogError("[TitleMenu] gameSceneName 이 비어 있습니다.");
                _leaving = false;
                return;
            }
            SceneManager.LoadScene(gameSceneName);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
