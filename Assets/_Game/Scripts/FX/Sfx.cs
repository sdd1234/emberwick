using System.Collections.Generic;
using UnityEngine;

namespace Capstone.FX
{
    /// <summary>
    /// 효과음 재생 (2026-10-29). 이름으로 부른다 - Sfx.Play("slam", 위치).
    /// 탑다운 2D 라 3D 감쇠 대신 플레이어와의 거리로 크기를 줄이고(20m 에서 0), 좌우만 살짝 벌린다.
    /// 소리는 SfxBank(씬에 하나, 맵 빌더가 채움)가 들고 있다. 같은 소리가 한 프레임에 겹치면 한 번만 낸다.
    /// </summary>
    public class SfxBank : MonoBehaviour
    {
        [SerializeField] private AudioClip[] clips;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;
        [SerializeField] private float hearDistance = 20f;

        private static SfxBank _inst;
        private readonly Dictionary<string, AudioClip> _map = new();
        private readonly Dictionary<string, int> _lastFrame = new();
        private AudioSource[] _pool;
        private int _next;
        private Transform _listener;

        public void SetClips(AudioClip[] c) => clips = c;

        private void Awake()
        {
            _inst = this;
            foreach (var c in clips) if (c != null) _map[c.name] = c;
            _pool = new AudioSource[12];
            for (int i = 0; i < _pool.Length; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _pool[i] = s;
            }
        }

        public static void Play(string name, Vector2 at, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (_inst == null) return;
            _inst.PlayCore(name, at, volume, pitchJitter);
        }

        private void PlayCore(string name, Vector2 at, float volume, float jitter)
        {
            if (!_map.TryGetValue(name, out var clip)) return;
            if (_lastFrame.TryGetValue(name, out int f) && f == Time.frameCount) return;
            _lastFrame[name] = Time.frameCount;
            if (_listener == null) { var pc = FindFirstObjectByType<Player.PlayerController>(); if (pc) _listener = pc.transform; }
            Vector2 from = _listener ? (Vector2)_listener.position : at;
            float d = Vector2.Distance(from, at);
            float k = 1f - Mathf.Clamp01(d / hearDistance);
            if (k <= 0.01f) return;
            var s = _pool[_next]; _next = (_next + 1) % _pool.Length;
            s.clip = clip;
            s.volume = masterVolume * volume * k * k;
            s.panStereo = Mathf.Clamp((at.x - from.x) / 12f, -0.6f, 0.6f);
            s.pitch = 1f + Random.Range(-jitter, jitter);
            s.Play();
        }
    }
}
