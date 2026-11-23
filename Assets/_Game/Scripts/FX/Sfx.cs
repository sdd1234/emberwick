using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Capstone.FX
{
    /// <summary>
    /// 효과음 재생 (2026-10-29, 11-03 팀 사운드로 교체). 이름으로 부른다 - Sfx.Play("slam", 위치).
    /// 탑다운 2D 라 3D 감쇠 대신 플레이어와의 거리로 크기를 줄이고(20m 에서 0), 좌우만 살짝 벌린다.
    /// 끝에 숫자가 붙은 파일은 묶음이다 - "walk_dirt" 를 부르면 walk_dirt1~4 중 하나를 고른다 (같은 게 연달아 나지 않게).
    ///
    /// 소리는 Resources/Sounds 에 있고, 게임이 켜질 때 GameAudio 오브젝트가 씬과 상관없이 하나 살아 있는다
    /// (탈출 음악처럼 씬이 바뀌어도 끝까지 나야 한다). 리스너도 여기 하나만 쓴다 - 타이틀 씬엔 리스너가 없다.
    /// 씬에 굽힌 SfxBank 는 합성음을 넘겨주고 사라진다. 같은 소리가 한 프레임에 겹치면 한 번만 낸다.
    /// </summary>
    public class SfxBank : MonoBehaviour
    {
        [SerializeField] private AudioClip[] clips;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.75f;
        [SerializeField] private float hearDistance = 20f;

        private static SfxBank _inst;
        private readonly Dictionary<string, AudioClip> _map = new();
        private readonly Dictionary<string, List<AudioClip>> _groups = new();
        private readonly Dictionary<string, AudioClip> _lastPick = new();
        private readonly Dictionary<string, int> _lastFrame = new();
        private readonly Dictionary<string, AudioSource> _channels = new();
        private AudioSource[] _pool;
        private int _next;
        private Transform _listener;
        private AudioListener _ears;

#if UNITY_EDITOR
        /// <summary>소리가 날 때마다 (파일 이름, 음량). 촬영 · 검증용.</summary>
        public static System.Action<string, float> Played;
#endif
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Report(string clip, float volume)
        {
#if UNITY_EDITOR
            Played?.Invoke(clip, volume);
#endif
        }

        public void SetClips(AudioClip[] c) => clips = c;
        /// <summary>리스너가 붙은 오브젝트. 녹화가 여기서 최종 믹스를 받는다.</summary>
        public static GameObject Ears => _inst != null && _inst._ears != null ? _inst._ears.gameObject : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            // 리스너는 소리 내는 AudioSource 들과 다른 오브젝트에 둔다 (OnAudioFilterRead 가 어느 쪽 소리인지 헷갈리지 않게)
            var ears = new GameObject("Ears");
            ears.transform.SetParent(go.transform, false);
            ears.AddComponent<AudioListener>();
            go.AddComponent<SfxBank>();
            go.AddComponent<GameMusic>();
        }

        private void Awake()
        {
            if (_inst != null && _inst != this)
            {
                _inst.AddClips(clips);      // 씬에 굽힌 합성음
                Destroy(this);              // 같은 오브젝트의 VfxBank 는 그대로 둔다
                return;
            }
            _inst = this;
            _ears = GetComponentInChildren<AudioListener>();
            AddClips(Resources.LoadAll<AudioClip>("Sounds"));
            AddClips(clips);
            _pool = new AudioSource[16];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = NewSource();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_inst == this) { _inst = null; SceneManager.sceneLoaded -= OnSceneLoaded; }
        }

        private AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        private void AddClips(AudioClip[] list)
        {
            if (list == null) return;
            foreach (var c in list)
            {
                if (c == null) continue;
                _map[c.name] = c;
                string key = c.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (key.Length == c.name.Length) continue;
                if (!_groups.TryGetValue(key, out var g)) _groups[key] = g = new List<AudioClip>();
                if (!g.Contains(c)) g.Add(c);
            }
        }

        /// <summary>씬마다 - 리스너를 하나로 줄이고, 모든 버튼에 누름 소리를 건다.</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _listener = null;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l != _ears) l.enabled = false;
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                b.onClick.AddListener(Click);
        }

        /// <summary>버튼 누름. 나중에 만드는 버튼(거래창 줄)은 직접 건다.</summary>
        public static void Click() => Play2D("button_press");

        public static AudioClip Find(string name) => _inst != null ? _inst.Pick(name) : null;

        private AudioClip Pick(string name)
        {
            if (_groups.TryGetValue(name, out var g) && g.Count > 0)
            {
                _lastPick.TryGetValue(name, out var last);
                AudioClip c;
                do c = g[Random.Range(0, g.Count)]; while (g.Count > 1 && c == last);
                _lastPick[name] = c;
                return c;
            }
            return _map.TryGetValue(name, out var one) ? one : null;
        }

        /// <summary>자리가 있는 소리. 멀수록 작아진다.</summary>
        public static void Play(string name, Vector2 at, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (_inst == null) return;
            _inst.PlayCore(name, at, volume, pitchJitter);
        }

        /// <summary>내가 일으킨 멀리 있는 소리. 좌우 방향만 따르고 거리로 작아지지 않는다.</summary>
        public static void PlayOwn(string name, Vector2 at, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (_inst == null) return;
            _inst.Fire(name, volume, _inst.PanTo(at), pitchJitter);
        }

        /// <summary>내 몸 · UI 에서 나는 소리. 거리 감쇠 없음.</summary>
        public static void Play2D(string name, float volume = 1f, float pitchJitter = 0f)
        {
            if (_inst == null) return;
            _inst.Fire(name, volume, 0f, pitchJitter);
        }

        /// <summary>전용 채널에 튼다 - 끊을 수 있고 다른 소리에게 자리를 뺏기지 않는다 (차징음 · 판결 음악).</summary>
        public static void PlayOn(string channel, string name, float volume = 1f)
        {
            if (_inst == null) return;
            var clip = _inst.Pick(name);
            if (clip == null) return;
            var s = _inst.Channel(channel);
            s.loop = false;
            s.clip = clip;
            s.volume = _inst.masterVolume * volume;
            s.Play();
            Report(clip.name, s.volume);
        }

        /// <summary>on 인 동안 반복한다. 매 프레임 불러도 된다.</summary>
        public static void Loop(string channel, string name, float volume, bool on)
        {
            if (_inst == null) return;
            var s = _inst.Channel(channel);
            if (!on) { if (s.isPlaying) s.Stop(); return; }
            if (s.isPlaying) return;
            var clip = _inst.Pick(name);
            if (clip == null) return;
            s.loop = true;
            s.clip = clip;
            s.volume = _inst.masterVolume * volume;
            s.Play();
            Report(clip.name, s.volume);
        }

        public static void StopChannel(string channel)
        {
            if (_inst != null && _inst._channels.TryGetValue(channel, out var s)) s.Stop();
        }

        private AudioSource Channel(string channel)
        {
            if (!_channels.TryGetValue(channel, out var s)) _channels[channel] = s = NewSource();
            return s;
        }

        private void PlayCore(string name, Vector2 at, float volume, float jitter)
        {
            if (_listener == null) { var pc = FindFirstObjectByType<Player.PlayerController>(); if (pc) _listener = pc.transform; }
            Vector2 from = _listener ? (Vector2)_listener.position : at;
            float d = Vector2.Distance(from, at);
            float k = 1f - Mathf.Clamp01(d / hearDistance);
            if (k <= 0.01f) return;
            Fire(name, volume * k * k, PanTo(at), jitter);
        }

        private float PanTo(Vector2 at)
        {
            if (_listener == null) { var pc = FindFirstObjectByType<Player.PlayerController>(); if (pc) _listener = pc.transform; }
            return _listener ? Mathf.Clamp((at.x - _listener.position.x) / 12f, -0.6f, 0.6f) : 0f;
        }

        private void Fire(string name, float volume, float pan, float jitter)
        {
            if (_lastFrame.TryGetValue(name, out int f) && f == Time.frameCount) return;
            var clip = Pick(name);
            if (clip == null) return;
            _lastFrame[name] = Time.frameCount;
            var s = FreeSource();
            s.clip = clip;
            s.volume = masterVolume * volume;
            s.panStereo = pan;
            s.pitch = 1f + Random.Range(-jitter, jitter);
            s.Play();
            Report(clip.name, s.volume);
        }

        private AudioSource FreeSource()
        {
            foreach (var p in _pool) if (!p.isPlaying) return p;
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            return s;
        }
    }
}
