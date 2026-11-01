#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace Capstone.Dev
{
    /// <summary>
    /// 플레이 화면을 프레임 단위 JPG 로 떨군다 (2026-11-01, 영상 확인용 - Recorder 패키지 없이).
    /// Time.captureFramerate 로 게임 시간을 프레임에 묶어서, 저장이 느려도 영상은 제 속도로 나온다.
    /// 묶은 뒤 ffmpeg 로 mp4 를 만든다.
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
        private string _dir;
        private float _seconds;
        private int _fps;
        private int _frame;

        public static FrameRecorder Begin(string dir, float seconds, int fps = 30)
        {
            var go = new GameObject("FrameRecorder");
            DontDestroyOnLoad(go);
            var r = go.AddComponent<FrameRecorder>();
            r._dir = dir; r._seconds = seconds; r._fps = fps;
            Directory.CreateDirectory(dir);
            Time.captureFramerate = fps;
            r.StartCoroutine(r.Grab());
            return r;
        }

        public bool Done { get; private set; }

        private IEnumerator Grab()
        {
            var eof = new WaitForEndOfFrame();
            int total = Mathf.RoundToInt(_seconds * _fps);
            while (_frame < total)
            {
                yield return eof;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_dir, $"f{_frame:D5}.jpg"), tex.EncodeToJPG(90));
                Destroy(tex);
                _frame++;
            }
            Time.captureFramerate = 0;
            Done = true;
            Destroy(gameObject);
        }

        private void OnDestroy() { if (!Done) Time.captureFramerate = 0; }
    }
}
#endif
