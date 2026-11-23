using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 자동 플레이 테스트 메뉴 (2026-10-31). 마을 씬을 열고 플레이를 켠 뒤 PlaytestBot 을 붙인다.
    /// 플레이에 들어가면 도메인이 다시 올라와 static 이 지워지므로 설정은 SessionState 로 넘긴다.
    /// 결과: 프로젝트/Logs/Playtest/날짜시각/ (Logs 는 .gitignore 에 들어 있다)
    /// </summary>
    [InitializeOnLoad]
    public static class PlaytestMenu
    {
        private const string KeyRuns = "Capstone.Playtest.Runs";
        private const string KeyScale = "Capstone.Playtest.TimeScale";
        private const string KeyScene = "Capstone.Playtest.RaidScene";
        private const string KeyShowcase = "Capstone.Playtest.Showcase";
        private const string TownPath = "Assets/_Game/Scenes/Town.unity";
        private const string IntroPath = "Assets/_Game/Scenes/Intro.unity";

        static PlaytestMenu() => EditorApplication.playModeStateChanged += OnPlayMode;

        [MenuItem("Capstone/플레이테스트/봇 1판 (빠르게 2배속)")]
        private static void RunOne() => Run(1, 2f);

        [MenuItem("Capstone/플레이테스트/봇 3판 (2배속)")]
        private static void RunThree() => Run(3, 2f);

        [MenuItem("Capstone/플레이테스트/소리 검증 촬영 - 탈출까지")]
        private static void FilmExtract() => Film(1);

        [MenuItem("Capstone/플레이테스트/소리 검증 촬영 - 사망까지")]
        private static void FilmDeath() => Film(2);

        /// <summary>타이틀부터 봇이 한 판을 돌고, 게임 화면과 실제 소리를 Captures/sound_날짜시각 에 남긴다.</summary>
        private static void Film(int kind)
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(IntroPath);
            SessionState.SetInt(KeyShowcase, kind);
            EditorApplication.isPlaying = true;
        }

        public static void Run(int runs, float timeScale, string raidScene = "Raid_Map1")
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Playtest] 플레이 중에는 시작할 수 없다 - 멈추고 다시"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(TownPath);
            SessionState.SetInt(KeyRuns, runs);
            SessionState.SetFloat(KeyScale, timeScale);
            SessionState.SetString(KeyScene, raidScene);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            int film = SessionState.GetInt(KeyShowcase, 0);
            if (film > 0)
            {
                SessionState.EraseInt(KeyShowcase);
                Application.runInBackground = true;
                EditorUtility.audioMasterMute = false;
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Captures", "sound_" + System.DateTime.Now.ToString("HHmmss") + (film == 2 ? "_death" : "_extract"));
                Dev.GameplayRecorder.Begin(dir);
                new GameObject("PlaytestBot").AddComponent<Dev.PlaytestBot>().ConfigureShowcase(film == 2);
                Debug.Log("[Playtest] 촬영 시작 - " + dir);
                return;
            }
            int runs = SessionState.GetInt(KeyRuns, 0);
            if (runs <= 0) return;
            SessionState.EraseInt(KeyRuns);
            var bot = new GameObject("PlaytestBot").AddComponent<Dev.PlaytestBot>();
            bot.Configure(runs, SessionState.GetFloat(KeyScale, 2f), SessionState.GetString(KeyScene, "Raid_Map1"));
        }
    }
}
