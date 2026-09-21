using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 저장소를 새로 클론해서 프로젝트를 처음 열면 Unity 는 빈 씬을 띄운다.
// 마지막으로 열었던 씬 기록이 Library/ 에 있는데 그건 저장소에 올리지 않기 때문이다.
// 빈 씬은 카메라 배경이 파란색이라 "파란 화면만 뜨고 아무것도 안 된다" 로 보인다.
// 그래서 이 컴퓨터에서 이 프로젝트를 처음 여는 경우에만 시작 씬을 대신 열어 준다.
[InitializeOnLoad]
static class FirstOpenScene
{
    const string StartScene = "Assets/_Game/Scenes/Intro.unity";

    static FirstOpenScene()
    {
        EditorApplication.delayCall += Open;
    }

    static void Open()
    {
        // 프로젝트 경로별로 기억한다. 한 번 열어 준 뒤에는 다시 참견하지 않는다
        var key = "Emberwick.FirstOpenScene." + Application.dataPath.GetHashCode();
        if (EditorPrefs.GetBool(key, false)) return;
        EditorPrefs.SetBool(key, true);

        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // 이미 진짜 씬이 열려 있거나 편집 중이면 그대로 둔다
        var current = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(current.path)) return;
        if (current.isDirty) return;

        if (System.IO.File.Exists(StartScene))
            EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Single);
    }
}
