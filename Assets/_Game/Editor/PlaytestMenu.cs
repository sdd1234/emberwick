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
        private const string TownPath = "Assets/_Game/Scenes/Town.unity";

        static PlaytestMenu() => EditorApplication.playModeStateChanged += OnPlayMode;

        [MenuItem("Capstone/플레이테스트/봇 1판 (빠르게 2배속)")]
        private static void RunOne() => Run(1, 2f);

        [MenuItem("Capstone/플레이테스트/봇 3판 (2배속)")]
        private static void RunThree() => Run(3, 2f);

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
            int runs = SessionState.GetInt(KeyRuns, 0);
            if (runs <= 0) return;
            SessionState.EraseInt(KeyRuns);
            var bot = new GameObject("PlaytestBot").AddComponent<Dev.PlaytestBot>();
            bot.Configure(runs, SessionState.GetFloat(KeyScale, 2f), SessionState.GetString(KeyScene, "Raid_Map1"));
        }
    }
}
