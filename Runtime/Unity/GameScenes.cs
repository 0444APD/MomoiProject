using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GBible
{
    // 씬 흐름: 판 데이터의 필드(예: run.scene)가 가리키는 씬을 화면이 따라 연다.
    // 규칙은 행동 안에서 그 필드만 바꾼다(run.scene = "Battle"). 씬을 직접 열지 않는다.
    //   Bootstrap(게임이 켜질 때 한 번): GameScenes.Follow(GameIds.PlayerData.Run, "scene", emptyScene: "Title");
    // → 이어 하기로 판을 불러오면 그 씬이 열리고, 새 판·판 끝으로 값이 비면 emptyScene이 열리고, Undo로 값이 돌아가면 씬도 돌아간다.
    // 연출이 재생 중이면 끝난 뒤에 연다(같은 프레임에 목록이 넣은 사라지는 연출까지). 여는 동안은 연출 대기열의 한 칸이라 GameView.Busy가 참(입력이 막힌다).
    // 전환 연출: GameScenes.Transition = load => FadeAround(load)  (load를 감싸는 코루틴: 페이드 아웃 → yield return load → 페이드 인).
    //   전환 연출이 씬을 열지 않고 끝나면(예외·yield return load를 빠뜨림) 경고하고 전환 없이 연다. Loaded는 씬이 열렸을 때만 쏜다.
    public static class GameScenes
    {
        static IDisposable _watch;
        static string _key;
        static string _path;
        static string _emptyScene;
        static string _loading;

        // 씬을 열고 난 뒤(이름).
        public static event Action<string> Loaded;

        // 씬 열기를 감싸는 전환 연출. 비우면 바로 연다.
        public static Func<IEnumerator, IEnumerator> Transition;

        public static string Current => SceneManager.GetActiveScene().name;

        // 참이면 씬 흐름이 씬을 바꾸지 않는다(테스트 씬을 플레이하는 동안 — 판 값이 다른 씬을 가리켜도 테스트 씬에 머문다).
        public static bool Hold;

        // 판 값이 이름("Battle")이든 경로("Assets/…/Battle.unity")든 지금 씬인가.
        static bool IsCurrent(string scene)
        {
            var active = SceneManager.GetActiveScene();
            return scene == active.name || scene == active.path || System.IO.Path.GetFileNameWithoutExtension(scene) == active.name && scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            GameView.Presented -= OnPresented;
            _watch = null;
            _key = _path = _emptyScene = _loading = null;
            Hold = false;
            Loaded = null;
            Transition = null;
        }

        public static void Follow(string key, string path, string emptyScene = null)
        {
            _watch?.Dispose();
            _key = key;
            _path = path ?? "";
            _emptyScene = emptyScene;
            _watch = GameView.RedrawAfterPresentation(Check, key);
            GameView.Presented -= OnPresented;
            GameView.Presented += OnPresented;
        }

        public static void Stop()
        {
            _watch?.Dispose();
            _watch = null;
            GameView.Presented -= OnPresented;
        }

        // 대기열이 비었는데 씬 열기가 끝나지 않았다(전환 연출이 30초 넘게 나아가지 않아 GameView가 넘겼다 — 버리기에는 끝까지 돈다):
        // 판 값의 씬이 아직 열리지 않았을 수 있으니 다음 프레임 끝에 다시 본다(이미 그 씬이면 아무 일도 없다).
        static void OnPresented()
        {
            if (_loading == null || _watch == null) return;
            _loading = null;
            GameView.MarkDirty(_watch);
        }

        static void Check()
        {
            // 연출 대기열이 비었을 때만 불린다(Redraw가 기다린다 — 같은 프레임에 넣은 사라지는 연출까지) → 앞서 연 씬은 끝났다.
            _loading = null;
            var session = GBibleRuntime.Session;
            if (Hold || session == null || string.IsNullOrEmpty(_key) || !session.Data.IsDefined(_key)) return;
            var root = session.Data.Get(_key);
            var target = (string.IsNullOrEmpty(_path) ? root : FieldPaths.Resolve(root, _path).FirstOrDefault()) as string;
            if (string.IsNullOrEmpty(target)) target = _emptyScene;
            if (string.IsNullOrEmpty(target) || IsCurrent(target)) return;
            if (!Application.CanStreamedLevelBeLoaded(target))
            {
                session.Journal.Append(JournalKind.Warning, "Scenes", $"씬 '{target}'을(를) 열 수 없습니다(Build Settings의 씬 목록에 있는지 확인).");
                return;
            }
            _loading = target;
            // 대기열의 한 칸(여는 동안 Busy)이지만 버리기(ClearPresentation)에도 끝까지 연다.
            GameView.PresentSceneChange(Open(target));
        }

        static IEnumerator Open(string scene)
        {
            var load = new SceneLoad();
            if (Transition != null)
            {
                IEnumerator transition = null;
                try { transition = Transition(load.Run(scene)); }
                catch (Exception e) { Debug.LogException(e); }
                if (transition != null) yield return transition;
                // 전환 연출이 씬 열기(load)를 돌리지 않고 끝났다(연출 안의 예외로 멈춤·yield return load를 빠뜨림) → 전환 없이 연다.
                if (!load.Started) Warn($"전환 연출(GameScenes.Transition)이 씬 '{scene}'을(를) 열지 않고 끝나 전환 없이 엽니다(연출 안의 예외나 yield return load를 빠뜨렸는지 확인).");
            }
            if (!load.Started) yield return load.Run(scene);
            if (_loading == scene) _loading = null;
            // 열렸을 때만 알린다(못 열었는데 쏘면 받는 쪽이 그 씬의 배경음·화면을 띄운다).
            if (load.Done || IsCurrent(scene)) Loaded?.Invoke(scene);
            else Warn($"씬 '{scene}'을(를) 열지 못했습니다(지금 씬 '{Current}').");
        }

        // 씬 열기 한 번 — 전환 연출이 돌렸는지(Started), 끝까지 열었는지(Done) 본다.
        sealed class SceneLoad
        {
            public bool Started;
            public bool Done;

            public IEnumerator Run(string scene)
            {
                Started = true;
                var operation = SceneManager.LoadSceneAsync(scene);
                if (operation == null) yield break;
                while (!operation.isDone) yield return null;
                Done = true;
            }
        }

        static void Warn(string message)
        {
            var session = GBibleRuntime.Session;
            if (session != null) session.Journal.Append(JournalKind.Warning, "Scenes", message);
            else Debug.LogWarning("[MomoiProject] " + message);
        }
    }
}
