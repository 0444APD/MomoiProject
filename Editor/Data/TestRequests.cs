using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
#if GBIBLE_TEST_FRAMEWORK
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
#endif

namespace GBible.EditorTools
{
    // AI가 Unity를 연 채로 규칙 테스트를 돌리고 결과를 읽는 길(DbStatusReport와 같은 생각 — AI는 Test Runner 창을 누를 수 없다).
    //   요청: Logs/MomoiProject/RunTests.request 를 만든다(내용: 비우면 프로젝트의 EditMode 테스트 어셈블리 — Assets 아래, "all"이면 GBible 것까지,
    //         어셈블리 이름을 줄마다 적으면 그것만).
    //   에디터가 1초마다 보고: 먼저 에셋을 새로 읽어(방금 쓴 코드를 컴파일) → 컴파일이 끝나면 EditMode 테스트를 돌리고
    //   → Logs/MomoiProject/TestResults.txt 에 결과(통과·실패 수, 실패한 테스트의 메시지·호출 위치)를 쓰고 요청 파일을 지운다.
    //   플레이 중이면 플레이가 끝날 때까지 기다린다. Test Framework 패키지가 없으면 결과 파일에 그렇게 쓴다.
    [InitializeOnLoad]
    public static class TestRequests
    {
        public const string RequestPath = "Logs/MomoiProject/RunTests.request";
        public const string ResultPath = "Logs/MomoiProject/TestResults.txt";
        const string RefreshedKey = "MomoiProject.TestRequest.Refreshed";
        const string RunningKey = "MomoiProject.TestRequest.Running";
        const double Interval = 1.0;
        static double _next;

        static TestRequests()
        {
            EditorApplication.update += Poll;
#if GBIBLE_TEST_FRAMEWORK
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new Callbacks());
#endif
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + Interval;
            if (!File.Exists(RequestPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            // manifest.json이 바뀌었으면 패키지부터 다시 푼다(창이 뒤에 있으면 Unity가 저절로 풀지 않는다 — 다 들어온 뒤 다음 확인에서 이어서).
            if (PackageSync.ResolveIfChanged()) return;
#if GBIBLE_TEST_FRAMEWORK
            if (SessionState.GetBool(RunningKey, false)) return;
            // 요청 하나에 한 번: 방금 쓴 코드가 아직 컴파일되지 않았을 수 있으니 먼저 새로 읽는다(컴파일되면 도메인을 다시 읽고 다음 확인에서 이어서).
            var stamp = File.GetLastWriteTimeUtc(RequestPath).Ticks.ToString();
            if (SessionState.GetString(RefreshedKey, "") != stamp)
            {
                SessionState.SetString(RefreshedKey, stamp);
                AssetDatabase.Refresh();
                return;
            }
            if (EditorUtility.scriptCompilationFailed)
            {
                Finish("상태: 컴파일 오류 — 테스트를 돌리지 못했습니다(콘솔의 오류를 고친 뒤 다시 요청하세요).");
                return;
            }
            var names = Assemblies(File.ReadAllText(RequestPath));
            if (names != null && names.Length == 0)
            {
                Finish("상태: 돌릴 테스트 어셈블리가 없습니다.");
                return;
            }
            SessionState.SetBool(RunningKey, true);
            var filter = new Filter { testMode = TestMode.EditMode };
            if (names != null) filter.assemblyNames = names;
            ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(filter));
#else
            Finish("상태: Test Framework 패키지(com.unity.test-framework)가 없어 테스트를 돌릴 수 없습니다.");
#endif
        }

        static void Finish(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
                File.WriteAllText(ResultPath, $"# 테스트 결과 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{text}\n", new UTF8Encoding(false));
                if (File.Exists(RequestPath)) File.Delete(RequestPath);
            }
            catch (Exception)
            {
                // 다른 프로그램이 잡고 있으면 다음 확인에서 다시 쓴다.
            }
        }

#if GBIBLE_TEST_FRAMEWORK
        // null = 거르지 않음(모두), 빈 배열 = 없음.
        static string[] Assemblies(string request)
        {
            var lines = request.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            if (lines.Any(l => l.Equals("all", StringComparison.OrdinalIgnoreCase))) return null;
            if (lines.Count > 0) return lines.ToArray();
            // 프로젝트의 테스트 어셈블리: Assets 아래 코드이고 NUnit을 참조한다.
            return CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .Where(a => a.sourceFiles.Any(f => f.StartsWith("Assets/", StringComparison.Ordinal))
                            && a.compiledAssemblyReferences.Any(r => r.EndsWith("nunit.framework.dll", StringComparison.OrdinalIgnoreCase)))
                .Select(a => a.name).ToArray();
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(RunningKey, false)) return;
                SessionState.EraseBool(RunningKey);
                SessionState.EraseString(RefreshedKey);
                var failed = new List<ITestResultAdaptor>();
                Collect(result, failed);
                var body = new StringBuilder();
                body.AppendLine(failed.Count == 0 ? $"상태: 모두 통과 — 통과 {result.PassCount} · 건너뜀 {result.SkipCount}"
                    : $"상태: 실패 {result.FailCount} · 통과 {result.PassCount} · 건너뜀 {result.SkipCount}");
                body.AppendLine($"걸린 시간: {result.Duration:0.00}초");
                foreach (var test in failed)
                {
                    body.AppendLine();
                    body.AppendLine($"## 실패: {test.Test.FullName}");
                    body.AppendLine((test.Message ?? "").Trim());
                    var stack = (test.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(8);
                    foreach (var line in stack) body.AppendLine("  " + line);
                }
                Finish(body.ToString().TrimEnd());
            }

            static void Collect(ITestResultAdaptor result, List<ITestResultAdaptor> failed)
            {
                if (result.HasChildren)
                {
                    foreach (var child in result.Children) Collect(child, failed);
                    return;
                }
                if (result.TestStatus == TestStatus.Failed) failed.Add(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
#endif
    }
}
