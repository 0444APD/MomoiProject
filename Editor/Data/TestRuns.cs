using UnityEditor;
#if GBIBLE_TEST_FRAMEWORK
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
#endif

namespace GBible.EditorTools
{
    // Unity Test Runner로 테스트를 돌리는 동안만 테스트 어셈블리의 형식([BindTo]·키워드·Mirror 비교 코드)도 훑게 한다.
    //   에디터·게임은 평소 테스트 어셈블리를 뺀다(GameAssemblies.Use(true) — DB 창·검증에 테스트용 형식이 섞이지 않게).
    //   실행 하나의 시작·끝에서 바꾸므로 모든 테스트 어셈블리(GBible.Tests · 모듈 테스트 · 프로젝트의 MomoiProject.Tests · PlayMode)에 함께 걸린다.
    //   그동안 게임 세션을 만들어도(GBibleRuntime이 Use(true)를 부른다) 빠지지 않게 GameAssemblies.KeepTests를 켠다.
    //   PlayMode 테스트는 플레이에 들어가며 도메인을 다시 읽으므로 '도는 중'을 SessionState에 남긴다(StartSetup이 다시 읽을 때 이 값을 본다).
    // Test Framework 패키지(com.unity.test-framework 1.1 이상)가 없으면 아무것도 하지 않는다(GBIBLE_TEST_FRAMEWORK — GBible.Editor.asmdef의 versionDefines).
    [InitializeOnLoad]
    public static class TestRuns
    {
        const string RunningKey = "MomoiProject.TestRunning";

        // 지금 Test Runner의 실행이 도는 중인가(도메인을 다시 읽어도 유지, Unity를 닫으면 사라진다).
        public static bool Running => SessionState.GetBool(RunningKey, false);

#if GBIBLE_TEST_FRAMEWORK
        static TestRuns()
        {
            GameAssemblies.KeepTests = Running;
            // 콜백 목록은 도메인을 다시 읽으면 비므로 읽을 때마다 등록한다.
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new Callbacks());
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                SessionState.SetBool(RunningKey, true);
                GameAssemblies.KeepTests = true;
                GameAssemblies.Use(skipTests: false);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.EraseBool(RunningKey);
                GameAssemblies.KeepTests = false;
                GameAssemblies.Use(skipTests: true);
            }

            // 도중에 도메인을 다시 읽었으면(PlayMode) 테스트마다 다시 넣는다(이미 넣었으면 아무것도 하지 않는다).
            public void TestStarted(ITestAdaptor test)
            {
                if (Running) GameAssemblies.Use(skipTests: false);
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
#endif
    }
}
