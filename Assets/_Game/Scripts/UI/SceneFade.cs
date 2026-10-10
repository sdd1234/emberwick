using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Capstone.UI
{
    /// <summary>
    /// 장면 전환 페이드. 화면을 덮는 검은 Image 에 붙인다.
    /// 씬이 열리면 검정에서 밝아지고(fadeInOnStart), FadeOutAndLoad 로 어두워진 뒤 다음 씬을 연다.
    /// 레이드 → 마을, 마을 → 레이드 전환이 뚝 끊기지 않게 한다 (피드백 - 탈출 성공 후 바로 마을로).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SceneFade : MonoBehaviour
    {
        public static SceneFade Instance { get; private set; }

        [SerializeField] private bool fadeInOnStart = true;
        [SerializeField] private float fadeInSeconds = 0.8f;
        [SerializeField] private float fadeOutSeconds = 0.9f;

        private Image _image;
        private bool _leaving;

        public bool IsLeaving => _leaving;

        private void Awake()
        {
            Instance = this;
            _image = GetComponent<Image>();
            _image.raycastTarget = false;
            SetAlpha(fadeInOnStart ? 1f : 0f);
            transform.SetAsLastSibling();                 // 무엇보다 위에
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            if (!fadeInOnStart) yield break;
            for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
            {
                if (_leaving) yield break;
                SetAlpha(1f - t / fadeInSeconds);
                yield return null;
            }
            if (!_leaving) SetAlpha(0f);
        }

        /// <summary>delay 초 기다렸다가 어두워지고 씬을 연다. 이미 나가는 중이면 무시.</summary>
        public void FadeOutAndLoad(string scene, float delay = 0f)
        {
            if (_leaving) return;
            _leaving = true;
            StartCoroutine(Leave(scene, delay));
        }

        private IEnumerator Leave(string scene, float delay)
        {
            transform.SetAsLastSibling();
            for (float t = 0f; t < delay; t += Time.unscaledDeltaTime) yield return null;
            float from = _image.color.a;
            for (float t = 0f; t < fadeOutSeconds; t += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(from, 1f, t / fadeOutSeconds));
                yield return null;
            }
            SetAlpha(1f);
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }

        private void SetAlpha(float a)
        {
            if (_image == null) return;
            var c = _image.color; c.r = c.g = c.b = 0f; c.a = Mathf.Clamp01(a);
            _image.color = c;
            _image.enabled = c.a > 0.001f;
        }

        /// <summary>페이드가 있으면 페이드로, 없으면 바로 씬을 연다.</summary>
        public static void Go(string scene, float delay = 0f)
        {
            if (Instance != null) Instance.FadeOutAndLoad(scene, delay);
            else { Time.timeScale = 1f; SceneManager.LoadScene(scene); }
        }
    }
}
