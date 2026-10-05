using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible
{
    // 테스트 씬: 프로젝트마다 AI가 만드는 씬(Assets/Scenes/Test.unity)에 이 컴포넌트를 하나 둔다.
    //   플레이하면 ① 'test' 슬롯으로 옮겨(개발자의 실제 저장을 건드리지 않음) ② 시작 테스트 상황을 불러오고(없으면 새 판)
    //   ③ 게임 화면 위에 테스트 패널을 띄운다: 테스트 명령(DB › 개발 명령·코드 등록) 버튼 · 상황 저장·불러오기 · 시간 · Undo.
    // 에디터와 개발 빌드에서만 돈다(출시 빌드에서는 아무것도 하지 않는다). 다른 컴포넌트보다 먼저 돈다.
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("MomoiProject/테스트 씬")]
    public sealed class TestScene : MonoBehaviour
    {
        [Tooltip("시작할 테스트 상황(Assets/MomoiProjectData/Tests). 비우면 새 판")] public string scenario = "";
        [Tooltip("글자 시드. 비우면 상황의 시드(상황이 없으면 무작위)")] public string seed = "";
        [Tooltip("켜면 'test' 슬롯에서 돈다 — 개발자의 실제 저장(기본 슬롯)을 건드리지 않는다")] public bool separateSlot = true;
        [Tooltip("시작할 때의 게임 속도"), Range(0f, 8f)] public float speed = 1f;
        [Tooltip("시작할 때 테스트 패널을 펼친다")] public bool showPanel = true;
        [Tooltip("테스트 패널을 펼치고 접는 키")] public KeyCode panelKey = KeyCode.F1;

        public const string Slot = "test";
        const int WindowId = 0x6D6F6D6F;   // IMGUI 창 번호(다른 창과 겹치지 않게 고정)

        static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        bool _open;
        Vector2 _scroll;
        string _saveName = "";
        string _message = "";
        Rect _window = new Rect(12, 12, 340, 560);

        void Awake()
        {
            if (!Allowed)
            {
                enabled = false;
                return;
            }
            _open = showPanel;
            if (separateSlot) GBibleRuntime.UseSlot(Slot, remember: false);
            var session = GBibleRuntime.Session;
            if (session == null) return;
            ulong? fixedSeed = string.IsNullOrWhiteSpace(seed) ? (ulong?)null : RandomStreams.SeedOf(seed);
            if (!string.IsNullOrWhiteSpace(scenario))
            {
                if (!TestScenarios.Load(session, scenario, fixedSeed)) Debug.LogWarning($"[MomoiProject] 테스트 씬: 테스트 상황 '{scenario}'를 불러오지 못했습니다(콘솔·모니터 저널).");
            }
            else GBibleRuntime.NewRun(session, fixedSeed);
            GameTime.SetSpeed(speed);
        }

        void OnGUI()
        {
            if (!Allowed || GBibleRuntime.Session == null) return;
            // 키는 IMGUI 이벤트로 받는다(입력 방식 — 예전 Input·Input System — 과 상관없이 돈다).
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == panelKey)
            {
                _open = !_open;
                Event.current.Use();
            }
            if (!_open)
            {
                if (GUI.Button(new Rect(12, 12, 90, 24), $"테스트({panelKey})")) _open = true;
                return;
            }
            _window = GUILayout.Window(WindowId, _window, DrawWindow, "테스트 패널");
        }

        void DrawWindow(int id)
        {
            var session = GBibleRuntime.Session;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"슬롯 '{GBibleRuntime.Slot}' · 시각 {session.Clock.Time:0.0}초");
            if (GUILayout.Button("접기", GUILayout.Width(48))) _open = false;
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);

            // 시간·Undo
            GUILayout.Label("시간");
            GUILayout.BeginHorizontal();
            foreach (var s in new[] { 0.5f, 1f, 2f, 4f })
                if (GUILayout.Toggle(Mathf.Approximately(GameTime.Speed, s), $"×{s}", "Button")) GameTime.SetSpeed(s);
            if (GUILayout.Button(GameTime.IsPaused ? "계속" : "멈춤")) GameTime.TogglePause("테스트");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.enabled = session.UndoCount > 0 && !session.Data.InTransaction;
            if (GUILayout.Button($"Undo ({session.UndoCount})")) Run(() => session.Undo());
            GUI.enabled = true;
            if (GUILayout.Button("새 판")) Run(() => GBibleRuntime.NewRun(session));
            GUILayout.EndHorizontal();

            // 테스트 상황
            GUILayout.Space(6);
            GUILayout.Label("테스트 상황");
            GUILayout.BeginHorizontal();
            _saveName = GUILayout.TextField(_saveName);
            GUI.enabled = !string.IsNullOrWhiteSpace(_saveName);
            if (GUILayout.Button("지금 저장", GUILayout.Width(80))) Run(() => TestScenarios.Save(session, _saveName), $"저장: {_saveName}");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            foreach (var name in TestScenarios.Names())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(name);
                if (GUILayout.Button("불러오기", GUILayout.Width(80))) Run(() => TestScenarios.Load(session, name), $"불러옴: {name}");
                GUILayout.EndHorizontal();
            }

            // 테스트 명령(DB › 개발 명령 · 코드 등록)
            foreach (var group in session.Debug.All.GroupBy(c => c.Group ?? "개발"))
            {
                GUILayout.Space(6);
                GUILayout.Label(group.Key);
                foreach (var command in group)
                    if (GUILayout.Button(new GUIContent(command.Name, command.Description ?? ""))) Run(command.Execute, command.Name);
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(6);
                GUILayout.Label(_message);
            }
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        void Run(Action action, string done = null)
        {
            try
            {
                action?.Invoke();
                _message = done ?? "";
            }
            catch (GameFailure failure)
            {
                _message = "플레이어용 실패: " + failure.Message;
            }
            catch (Exception e)
            {
                _message = "오류: " + e.Message;
                Debug.LogException(e);
            }
        }
    }
}
