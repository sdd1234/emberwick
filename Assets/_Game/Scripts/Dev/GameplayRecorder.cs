#if UNITY_EDITOR
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Capstone.Dev
{
    /// <summary>
    /// 실제 게임 소리까지 함께 녹화한다 (2026-11-03, 사운드 검증용 - Recorder 패키지 없이).
    /// FrameRecorder 는 시간을 프레임에 묶어 소리가 어긋난다. 이건 실시간 그대로 찍는다:
    ///   소리 - 리스너에 나오는 최종 믹스를 OnAudioFilterRead 로 받아 audio.f32 에 그대로 적는다
    ///   화면 - 비동기로 읽어 스레드에서 JPG 로. 시각을 frames.tsv 에 남겨 ffmpeg 로 묶을 때 맞춘다
    ///   기록 - 어떤 파일이 언제 어떤 음량으로 났는지 events.tsv
    /// </summary>
    public class GameplayRecorder : MonoBehaviour
    {
        private const int Fps = 30;
        private static GameplayRecorder _inst;

        private string _dir;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<(double at, float[] data)> _audio = new();
        private readonly List<Task> _pending = new();
        private StreamWriter _frames, _events;
        private Thread _audioThread;
        private volatile bool _running = true;
        private int _channels, _frame;
        private double _firstAudioAt = -1;
        private long _samples;
        private bool _stopped;

        public static void Begin(string dir)
        {
            var go = new GameObject("GameplayRecorder");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<GameplayRecorder>();
            _inst.Open(dir);
        }

        /// <summary>마무리하고 파일을 닫는다.</summary>
        public static void Stop() { if (_inst != null) _inst.Finish(); }

        public static void Mark(string what) { if (_inst != null) _inst.Log("#" + what, 0f); }

        private void Open(string dir)
        {
            _dir = dir;
            Directory.CreateDirectory(Path.Combine(dir, "frames"));
            _frames = new StreamWriter(Path.Combine(dir, "frames.tsv"), false, new UTF8Encoding(false));
            _events = new StreamWriter(Path.Combine(dir, "events.tsv"), false, new UTF8Encoding(false)) { AutoFlush = true };
            FX.SfxBank.Played += Log;
            _audioThread = new Thread(WriteAudio) { IsBackground = true };
            _audioThread.Start();
            var ears = FX.SfxBank.Ears;
            if (ears != null)
            {
                ears.AddComponent<AudioTap>().Sink = this;
                // 녹화를 켜기 전에 이미 돌기 시작한 곡도 기록에 남긴다
                foreach (var s in ears.transform.parent.GetComponents<AudioSource>())
                    if (s.isPlaying && s.clip != null) Log(s.clip.name, s.volume);
            }
            StartCoroutine(Grab());
        }

        private void Log(string clip, float volume)
        {
            if (!_stopped) _events.WriteLine($"{_clock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)}\t{clip}\t{volume.ToString("0.000", CultureInfo.InvariantCulture)}\t{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        }

        // ---------- 소리 ----------
        internal void PushAudio(float[] data, int channels)
        {
            if (!_running) return;
            if (_firstAudioAt < 0) { _firstAudioAt = _clock.Elapsed.TotalSeconds; _channels = channels; }
            var copy = new float[data.Length];
            System.Array.Copy(data, copy, data.Length);
            _audio.Enqueue((_clock.Elapsed.TotalSeconds, copy));   // 블록마다 시각 - 믹서가 잠깐 멈추면 빈 자리를 찾아 메운다
        }

        private void WriteAudio()
        {
            using var file = new FileStream(Path.Combine(_dir, "audio.f32"), FileMode.Create, FileAccess.Write);
            using var index = new StreamWriter(Path.Combine(_dir, "audio_blocks.tsv"), false, new UTF8Encoding(false));
            var bytes = new byte[0];
            while (_running || !_audio.IsEmpty)
            {
                if (!_audio.TryDequeue(out var item)) { Thread.Sleep(5); continue; }
                var block = item.data;
                index.WriteLine($"{item.at.ToString("0.0000", CultureInfo.InvariantCulture)}\t{block.Length}");
                int n = block.Length * 4;
                if (bytes.Length < n) bytes = new byte[n];
                System.Buffer.BlockCopy(block, 0, bytes, 0, n);
                file.Write(bytes, 0, n);
                _samples += block.Length;
            }
        }

        // ---------- 화면 ----------
        private IEnumerator Grab()
        {
            var eof = new WaitForEndOfFrame();
            double next = 0;
            int w = Screen.width, h = Screen.height;
            var free = new Stack<RenderTexture>();
            int inFlight = 0;
            while (!_stopped)
            {
                yield return eof;
                if (_stopped) yield break;
                double now = _clock.Elapsed.TotalSeconds;
                if (now < next || inFlight >= 6) continue;
                next = now + 1.0 / Fps;
                if (Screen.width != w || Screen.height != h) { w = Screen.width; h = Screen.height; free.Clear(); }

                // 씬이 바뀔 때 쓰이지 않는 에셋 정리에 지워지지 않게 표시한다
                var rt = free.Count > 0 ? free.Pop() : new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
                int index = _frame++;
                _frames.WriteLine($"{index}\t{now.ToString("0.000", CultureInfo.InvariantCulture)}");
                inFlight++;
                int fw = w, fh = h;
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    inFlight--;
                    if (_stopped) return;
                    if (rt.width == Screen.width && rt.height == Screen.height) free.Push(rt);
                    if (req.hasError) return;
                    var pixels = req.GetData<byte>().ToArray();
                    string path = Path.Combine(_dir, "frames", $"f{index:D6}.jpg");
                    lock (_pending)
                    {
                        _pending.RemoveAll(t => t.IsCompleted);
                        _pending.Add(Task.Run(() =>
                        {
                            var jpg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)fw, (uint)fh, 0, 88);
                            File.WriteAllBytes(path, jpg);
                        }));
                    }
                });
            }
        }

        private void Finish()
        {
            if (_stopped) return;
            _stopped = true;
            FX.SfxBank.Played -= Log;
            _running = false;
            _audioThread?.Join(5000);
            lock (_pending) Task.WaitAll(_pending.ToArray(), 15000);
            _frames?.Dispose();
            _events?.Dispose();
            File.WriteAllText(Path.Combine(_dir, "audio_info.txt"),
                $"rate={AudioSettings.outputSampleRate}\nchannels={_channels}\nfirst={_firstAudioAt.ToString("0.000", CultureInfo.InvariantCulture)}\nsamples={_samples}\nframes={_frame}\nend={_clock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)}\n");
        }

        private void OnDestroy() => Finish();

        /// <summary>리스너와 같은 오브젝트에 붙어 최종 믹스를 받는다. 소리를 바꾸지는 않는다.</summary>
        private class AudioTap : MonoBehaviour
        {
            public GameplayRecorder Sink;
            private void OnAudioFilterRead(float[] data, int channels) { if (Sink != null) Sink.PushAudio(data, channels); }
        }
    }
}
#endif
