using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GBible.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // AI가 Unity를 직접 돌려 보고 화면을 보는 길(TestRequests와 같은 생각 — AI는 Unity 창을 누를 수도, 볼 수도 없다).
    //   요청: Logs/MomoiProject/Play.request 에 한 줄에 하나씩 단계를 적는다(# 뒤는 주석).
    //     scene <경로>            플레이할 씬(비우면 테스트 씬 Assets/MomoiProject/Scenes/Test.unity, 없으면 지금 씬)
    //     scenario <이름>         테스트 상황 불러오기(TestScenarios)
    //     wait <초>               기다리기(실제 초)
    //     command <이름|DB ID>    테스트 명령(DB › 개발 명령의 ID·버튼 이름, 모니터 명령 이름)
    //     open <화면 ID> [값]     화면 열기 · close <화면 ID> · back(뒤로 = Esc)
    //     click <화면 ID>/<요소 이름>[@n]   요소 누르기(n번째 같은 이름 — 0부터). Button은 '제출', 그 밖은 클릭 이벤트
    //     speed <배속>            게임 속도
    //     capture <이름>          게임 화면을 Logs/MomoiProject/Captures/<이름>.png 로(게임 뷰 크기 그대로)
    //     screens                 열린 화면 목록을 결과에 적기
    //     testpanel off|on        테스트 씬의 패널을 숨기기·보이기(찍을 때 HUD를 가리지 않게 — 기본은 숨김)
    //   에디터가 1초마다 보고: 먼저 새로 읽고(방금 쓴 코드·UXML·USS — 새로 읽을 때 난 오류와 XML로 읽히지 않는 UXML은 결과 첫머리에),
    //   열린 씬에 저장하지 않은 변경이 있거나 플레이 중이면 시작하지 않는다(결과 파일에 까닭) —
    //   같은 콘솔 오류는 처음 한 번만 적는다.
    //   플레이에 들어가 단계를 차례로 돌리고, 끝나면 플레이를 멈추고 처음 씬으로 돌아가
    //   Logs/MomoiProject/PlayResults.txt 에 단계마다 결과·찍은 그림 경로·그동안의 콘솔 오류·경고를 쓰고 요청 파일을 지운다.
    //   AI는 결과 파일과 PNG(그림을 읽는다)로 화면을 확인하고 고친다. 개발자가 Unity에서 작업 중이면 요청하지 않는다(플레이에 들어간다).
    //   창을 띄우지 않는다(답할 사람이 없으면 Unity가 멈춘다): DB의 '적용 전' 변경이 있어도 묻지 않고 적용된 데이터로 돌며, 결과에 그 건수를 적는다.
    //   플레이에 들어가지 못하면(다른 도구가 막음) 몇 초 뒤 결과에 까닭을 쓰고 요청을 지운다.
    [InitializeOnLoad]
    public static class PlayRequests
    {
        public const string RequestPath = "Logs/MomoiProject/Play.request";
        public const string ResultPath = "Logs/MomoiProject/PlayResults.txt";
        public const string CaptureFolder = "Logs/MomoiProject/Captures";
        const string TestScenePath = "Assets/MomoiProject/Scenes/Test.unity";

        const string StateKey = "MomoiProject.Play.State";        // "" · "starting" · "running" · "stopping"
        const string StepsKey = "MomoiProject.Play.Steps";
        const string IndexKey = "MomoiProject.Play.Index";
        const string LogKey = "MomoiProject.Play.Log";
        const string ReturnKey = "MomoiProject.Play.Return";
        const string StartedKey = "MomoiProject.Play.StartedAt";
        const string RefreshedKey = "MomoiProject.Play.Refreshed";
        const string ImportKey = "MomoiProject.Play.ImportErrors";
        const double PollInterval = 1.0;
        const double StartDelay = 1.5;    // 플레이에 들어가 첫 화면이 열릴 때까지
        const double StartTimeout = 5.0;  // 플레이에 들어가기를 기다리는 한도(넘으면 막힌 것으로 본다)

        static double _nextPoll;
        static double _nextStep;
        static bool _hooked;
        static readonly List<string> _importErrors = new List<string>();

        static PlayRequests()
        {
            // 도메인을 다시 읽은 직후에는 조금 기다린다(DB의 '적용 전' 작업본이 다음 틱에 되살아난다 — 그 전에 세면 0건).
            _nextPoll = EditorApplication.timeSinceStartup + 2.0;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static string State
        {
            get => SessionState.GetString(StateKey, "");
            set => SessionState.SetString(StateKey, value);
        }

        // AI의 플레이 요청으로 플레이에 들어가는 중(DB의 '적용 전' 확인 창을 띄우지 않는다 — CodexStaging).
        public static bool IsStarting => State == "starting";

        static void Update()
        {
            switch (State)
            {
                case "":
                    Poll();
                    break;
                case "starting":
                    // 플레이에 들어가지 못하고 편집 모드에 머물렀다(다른 도구가 막음).
                    var started = double.TryParse(SessionState.GetString(StartedKey, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at) ? at : 0;
                    if (!EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup - started > StartTimeout)
                        Finish("상태: 플레이에 들어가지 못했습니다(취소되었거나 다른 도구가 막았습니다) — 다시 요청하세요.", SessionState.GetString(LogKey, ""));
                    break;
                case "running":
                    if (EditorApplication.isPlaying) Step();
                    break;
            }
        }

        // ── 시작 ─────────────────────────────────────────────

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollInterval;
            if (!File.Exists(RequestPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            // 요청 하나에 한 번: 방금 쓴 코드·UXML·USS를 먼저 새로 읽는다(Unity는 창을 볼 때만 저절로 새로 읽는다 — 컴파일되면 도메인을 다시 읽고 다음 확인에서 이어서).
            var stamp = File.GetLastWriteTimeUtc(RequestPath).Ticks.ToString();
            if (SessionState.GetString(RefreshedKey, "") != stamp)
            {
                SessionState.SetString(RefreshedKey, stamp);
                _importErrors.Clear();
                Application.logMessageReceived += OnImportLog;
                try { AssetDatabase.Refresh(); }
                finally { Application.logMessageReceived -= OnImportLog; }
                // Unity의 UXML 오류 글에는 파일 이름이 없어, XML로 읽히지 않는 UXML은 여기서 경로와 함께 적는다(그 화면은 빈 채로 열린다).
                foreach (var path in Directory.GetFiles("Assets", "*.uxml", SearchOption.AllDirectories))
                {
                    try { new System.Xml.XmlDocument().Load(path); }
                    catch (System.Xml.XmlException e) { _importErrors.Insert(0, $"   {path.Replace('\\', '/')}: XML로 읽히지 않음 — {e.Message}"); }
                }
                SessionState.SetString(ImportKey, string.Join("\n", _importErrors));
                return;
            }
            if (EditorUtility.scriptCompilationFailed)
            {
                Finish("상태: 컴파일 오류 — 플레이하지 못했습니다(콘솔의 오류를 고친 뒤 다시 요청하세요).", log: "");
                return;
            }
            var steps = File.ReadAllLines(RequestPath).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToList();
            var scene = steps.Where(l => l.StartsWith("scene ", StringComparison.Ordinal)).Select(l => l.Substring(6).Trim()).LastOrDefault();
            steps.RemoveAll(l => l.StartsWith("scene ", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(scene) && File.Exists(TestScenePath)) scene = TestScenePath;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                if (!EditorSceneManager.GetSceneAt(i).isDirty) continue;
                Finish("상태: 열린 씬에 저장하지 않은 변경이 있어 시작하지 않았습니다 — 개발자가 저장한 뒤 다시 요청하세요.", log: "");
                return;
            }
            var current = EditorSceneManager.GetActiveScene().path;
            SessionState.SetString(ReturnKey, !string.IsNullOrEmpty(scene) && scene != current ? current : "");
            if (!string.IsNullOrEmpty(scene) && scene != current)
            {
                if (!File.Exists(scene))
                {
                    Finish($"상태: 씬이 없습니다 — {scene}", log: "");
                    return;
                }
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            }
            SessionState.SetString(StepsKey, string.Join("\n", steps));
            SessionState.SetInt(IndexKey, 0);
            var pending = EditorDatabase.Staging.HasChanges ? EditorDatabase.Staging.Changes().Count : 0;
            var imports = SessionState.GetString(ImportKey, "");
            SessionState.SetString(LogKey, $"씬: {(string.IsNullOrEmpty(scene) ? current : scene)}\n" +
                (pending > 0 ? $"DB '적용 전' 변경 {pending}건 — 이 플레이에는 반영되지 않았습니다(개발자가 DB 창에서 Ctrl+S로 적용).\n" : "") +
                (imports.Length > 0 ? "새로 읽을 때 오류(이 파일은 예전 모습 그대로 — 고친 뒤 다시 요청):\n" + imports + "\n" : ""));
            SessionState.SetString(StartedKey, EditorApplication.timeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
            State = "starting";
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode when State == "starting":
                    State = "running";
                    _nextStep = EditorApplication.timeSinceStartup + StartDelay;
                    HookLog();
                    FocusGameView();
                    TestScene.Hidden = true;   // 찍은 그림을 가리지 않게(요청에서 'testpanel on'으로 다시)
                    break;
                case PlayModeStateChange.EnteredEditMode when State == "running" || State == "stopping":
                    UnhookLog();
                    TestScene.Hidden = false;
                    var back = SessionState.GetString(ReturnKey, "");
                    if (!string.IsNullOrEmpty(back) && File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
                    var log = SessionState.GetString(LogKey, "");
                    var failed = log.Contains("✗ ");
                    Finish(failed ? "상태: 끝남 — 실패한 단계가 있습니다(아래 ✗)." : "상태: 끝남 — 모든 단계를 돌렸습니다.", log);
                    break;
            }
        }

        // ── 단계 ─────────────────────────────────────────────

        static void Step()
        {
            if (EditorApplication.timeSinceStartup < _nextStep) return;
            if (!_hooked) HookLog();   // 도메인을 다시 읽은 뒤
            var steps = SessionState.GetString(StepsKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var index = SessionState.GetInt(IndexKey, 0);
            if (index >= steps.Length)
            {
                State = "stopping";
                EditorApplication.ExitPlaymode();
                return;
            }
            SessionState.SetInt(IndexKey, index + 1);
            var line = steps[index];
            var wait = 0.25;
            string result;
            try { result = Run(line, ref wait); }
            catch (Exception e) { result = "✗ " + (e.InnerException ?? e).Message; }
            Append($"{index + 1}. {line} — {result}");
            _nextStep = EditorApplication.timeSinceStartup + wait;
        }

        static string Run(string line, ref double wait)
        {
            var space = line.IndexOf(' ');
            var verb = space < 0 ? line : line.Substring(0, space);
            var arg = space < 0 ? "" : line.Substring(space + 1).Trim();
            var session = GBibleRuntime.Session;
            switch (verb)
            {
                case "wait":
                    wait = Math.Max(0, double.TryParse(arg, out var seconds) ? seconds : 1);
                    return "기다림";
                case "speed":
                    GameTime.SetSpeed(float.TryParse(arg, out var speed) ? speed : 1f);
                    return "배속 " + GameTime.Speed;
                case "scenario":
                    if (session == null) return "✗ 세션이 아직 없습니다";
                    return TestScenarios.Load(session, arg) ? "불러옴" : "✗ 없는 테스트 상황";
                case "command":
                    return Command(session, arg);
                case "open":
                {
                    var parts = arg.Split(new[] { ' ' }, 2);
                    GameScreens.Open(parts[0], parts.Length > 1 ? parts[1] : null);
                    return GameScreens.IsOpen(parts[0]) ? "열림" : "✗ 열리지 않음";
                }
                case "close":
                    return GameScreens.Close(arg) ? "닫힘" : "✗ 열려 있지 않음";
                case "back":
                    return GameScreens.Back() ? "뒤로" : "할 일 없음";
                case "click":
                    return Click(arg);
                case "capture":
                    wait = Math.Max(wait, 0.5);
                    return Capture(arg);
                case "testpanel":
                    TestScene.Hidden = arg != "on";
                    return TestScene.Hidden ? "테스트 패널 숨김" : "테스트 패널 보임";
                case "screens":
                    return "열린 화면: " + string.Join(", ", GameScreens.All.Select(s => s.Id));
                default:
                    return "✗ 모르는 단계(scene·scenario·wait·command·open·close·back·click·speed·capture·screens·testpanel)";
            }
        }

        static string Command(GameSession session, string arg)
        {
            if (session == null) return "✗ 세션이 아직 없습니다";
            var name = arg;
            if (EditorDatabase.Instance.FindById(arg) is DebugCommandData data) name = data.ButtonName;
            var command = session.Debug.All.FirstOrDefault(c => c.Name == name || $"{c.Group}/{c.Name}" == name);
            if (command == null) return "✗ 없는 명령";
            command.Execute();
            return "돌림";
        }

        static string Click(string arg)
        {
            var slash = arg.IndexOf('/');
            if (slash < 0) return "✗ <화면 ID>/<요소 이름> 으로 적으세요";
            var screen = GameScreens.Get(arg.Substring(0, slash));
            if (screen?.Root == null) return "✗ 화면이 열려 있지 않음";
            var name = arg.Substring(slash + 1);
            var nth = 0;
            var at = name.LastIndexOf('@');
            if (at > 0 && int.TryParse(name.Substring(at + 1), out var n))
            {
                nth = n;
                name = name.Substring(0, at);
            }
            var element = screen.Root.Query<VisualElement>(name).ToList().ElementAtOrDefault(nth);
            if (element == null) return "✗ 요소가 없음";
            if (element.resolvedStyle.display == DisplayStyle.None || !element.enabledInHierarchy) return "✗ 보이지 않거나 꺼진 요소";
            if (element is Button)
            {
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = element;
                    element.SendEvent(submit);
                }
            }
            else
            {
                using (var click = ClickEvent.GetPooled())
                {
                    click.target = element;
                    element.SendEvent(click);
                }
            }
            return "누름";
        }

        static string Capture(string name)
        {
            if (string.IsNullOrEmpty(name)) name = DateTime.Now.ToString("HHmmss");
            Directory.CreateDirectory(CaptureFolder);
            var path = $"{CaptureFolder}/{name}.png";
            if (File.Exists(path)) File.Delete(path);
            FocusGameView();
            ScreenCapture.CaptureScreenshot(path);
            return "찍음 → " + path;
        }

        static void FocusGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (type != null) EditorWindow.GetWindow(type, false, null, false)?.Repaint();
        }

        // ── 콘솔 ─────────────────────────────────────────────

        static void HookLog()
        {
            if (_hooked) return;
            Application.logMessageReceived += OnLog;
            _hooked = true;
        }

        static void UnhookLog()
        {
            Application.logMessageReceived -= OnLog;
            _hooked = false;
        }

        // 같은 오류(글·자리)는 처음 한 번만 적는다(매 프레임 나는 오류가 단계 기록을 밀어내지 않게).
        static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            var first = (message ?? "").Split('\n')[0];
            var where = (stack ?? "").Split('\n').FirstOrDefault(l => l.Contains("Assets/") || l.Contains("Packages/")) ?? "";
            var line = $"   [{(type == LogType.Warning ? "경고" : "오류")}] {first}{(where.Length > 0 ? "  — " + where.Trim() : "")}";
            if (SessionState.GetString(LogKey, "").Contains(line)) return;
            Append(line);
        }

        // 새로 읽을 때(가져오기) 난 오류·경고: 그 에셋 경로와 첫 줄.
        static void OnImportLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log || _importErrors.Count >= 20) return;
            var first = (message ?? "").Split('\n')[0].Trim();
            if (first.Length > 0 && !_importErrors.Contains("   " + first)) _importErrors.Add("   " + first);
        }

        static void Append(string text)
        {
            var log = SessionState.GetString(LogKey, "");
            if (log.Length > 60000) return;
            SessionState.SetString(LogKey, log + text + "\n");
        }

        static void Finish(string status, string log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
                File.WriteAllText(ResultPath, $"# 플레이 결과 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{status}\n\n{log}", new UTF8Encoding(false));
                if (File.Exists(RequestPath)) File.Delete(RequestPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] 플레이 결과를 쓰지 못했습니다: " + e.Message);
            }
            State = "";
            SessionState.EraseString(StepsKey);
            SessionState.EraseString(LogKey);
            SessionState.EraseString(ReturnKey);
            SessionState.EraseString(StartedKey);
            SessionState.EraseString(ImportKey);
        }
    }
}
