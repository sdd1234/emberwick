using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.FX
{
    /// <summary>
    /// 배경 음악 + 환경음 (2026-11-03). 둘은 항상 같이 돈다. 씬 이름으로 고른다 - 타이틀 · 마을은 장터, 레이드는 숲.
    /// 타이틀 전용 곡이 없어 타이틀도 장터 곡이다. 타이틀에서 마을로 갈 때는 끊기지 않고 이어진다.
    /// 탈출 · 사망 음악이 나는 동안에는 곡을 내리고, 그게 끝난 뒤 다음 씬의 곡을 처음부터 올린다.
    /// 레이드에서 적이 플레이어를 발각해 쫓으면 전투 곡으로 넘어간다 (2026-11-04). 숲 곡은 소리만 내린 채 계속 돌아
    /// 전투가 끝나면 끊긴 자리가 아니라 흐르던 자리로 돌아온다. 적을 따돌려도 몇 초 더 유지해 곡이 들락날락하지 않게 한다.
    /// </summary>
    public class GameMusic : MonoBehaviour
    {
        private const string HubBgm = "Village at Dusk_hub_bgm", HubAmbience = "hub_ambience";
        private const string RaidBgm = "Creeping Shadow_darkforest_bgm", RaidAmbience = "darkforest_ambience";

        private const string BattleBgm = "Pursuit in C Minor_battle_bgm";

        private const float BgmVolume = 0.4f, AmbienceVolume = 0.75f, FadeSeconds = 1.5f;
        private const float BattleVolume = 0.56f, BattleInSeconds = 0.6f, BattleOutSeconds = 3f, BattleHoldSeconds = 6f;

        private class Layer
        {
            public AudioSource now, old;
            public float target;
        }

        private static GameMusic _inst;
        private Layer _bgm, _ambience;
        private float _holdUntil;
        private bool _silencedForResult;
        private AudioSource _battle;
        private bool _inRaid;
        private float _battleUntil;

        /// <summary>결과 음악이 나는 동안 곡을 내린다. 다음 씬으로 넘어가도 최소 seconds 까지는 올리지 않는다.</summary>
        public static void Silence(float seconds)
        {
            if (_inst == null) return;
            _inst._silencedForResult = true;
            _inst._holdUntil = Time.unscaledTime + seconds;
        }

        private void Awake()
        {
            _inst = this;
            _bgm = MakeLayer(BgmVolume);
            _ambience = MakeLayer(AmbienceVolume);
            _battle = NewSource();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private Layer MakeLayer(float volume) => new Layer { now = NewSource(), old = NewSource(), target = volume };

        private AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _silencedForResult = false;
            bool raid = scene.name.StartsWith("Raid");
            _inRaid = raid;
            _battleUntil = 0f;
            Switch(_bgm, raid ? RaidBgm : HubBgm);
            Switch(_ambience, raid ? RaidAmbience : HubAmbience);
        }

        private static void Switch(Layer layer, string name)
        {
            var clip = SfxBank.Find(name);
            if (clip == null || layer.now.clip == clip) return;
            (layer.now, layer.old) = (layer.old, layer.now);    // 지금 곡은 내려가고, 새 곡이 올라온다
            layer.now.Stop();
            layer.now.clip = clip;
            layer.now.volume = 0f;
        }

        private void Update()
        {
            bool held = _silencedForResult || Time.unscaledTime < _holdUntil;
            if (_inRaid && !held && Enemy.EnemyAI.AnyChasingPlayer) _battleUntil = Time.unscaledTime + BattleHoldSeconds;
            bool fighting = !held && Time.unscaledTime < _battleUntil;
            Tick(_bgm, held || fighting);
            Tick(_ambience, false);
            TickBattle(fighting, held);
        }

        private void TickBattle(bool fighting, bool held)
        {
            if (fighting && !_battle.isPlaying)
            {
                _battle.clip = SfxBank.Find(BattleBgm);
                if (_battle.clip == null) return;
                _battle.Play();
                SfxBank.Report(_battle.clip.name, BattleVolume);
            }
            float seconds = fighting ? BattleInSeconds : held ? FadeSeconds : BattleOutSeconds;
            _battle.volume = Mathf.MoveTowards(_battle.volume, fighting ? BattleVolume : 0f, BattleVolume * Time.unscaledDeltaTime / seconds);
            if (!fighting && _battle.volume <= 0f && _battle.isPlaying) _battle.Stop();
        }

        private static void Tick(Layer layer, bool held)
        {
            float step = Time.unscaledDeltaTime / FadeSeconds;
            if (layer.now.clip != null && !layer.now.isPlaying && !held)
            {
                layer.now.Play();
                SfxBank.Report(layer.now.clip.name, layer.target);
            }
            layer.now.volume = Mathf.MoveTowards(layer.now.volume, held ? 0f : layer.target, step * layer.target);
            if (layer.old.isPlaying)
            {
                layer.old.volume = Mathf.MoveTowards(layer.old.volume, 0f, step * layer.target);
                if (layer.old.volume <= 0f) layer.old.Stop();
            }
        }
    }
}
