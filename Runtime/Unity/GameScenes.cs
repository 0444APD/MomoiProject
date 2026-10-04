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
    // 연출이 재생 중이면 끝난 뒤에 연다. 여는 동안은 연출 대기열의 한 칸이라 GameView.Busy가 참(입력이 막힌다).
    // 전환 연출: GameScenes.Transition = load => FadeAround(load)  (load를 감싸는 코루틴: 페이드 아웃 → yield return load → 페이드 인).
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _watch = null;
            _key = _path = _emptyScene = _loading = null;
            Loaded = null;
            Transition = null;
        }

        public static void Follow(string key, string path, string emptyScene = null)
        {
            _watch?.Dispose();
            _key = key;
            _path = path ?? "";
            _emptyScene = emptyScene;
            _watch = GameView.Redraw(Check, key);
        }

        public static void Stop()
        {
            _watch?.Dispose();
            _watch = null;
        }

        static void Check()
        {
            // 연출 대기열이 비었을 때만 불린다(Redraw가 기다린다) → 앞서 연 씬은 끝났다.
            _loading = null;
            var session = GBibleRuntime.Session;
            if (session == null || string.IsNullOrEmpty(_key) || !session.Data.IsDefined(_key)) return;
            var root = session.Data.Get(_key);
            var target = (string.IsNullOrEmpty(_path) ? root : FieldPaths.Resolve(root, _path).FirstOrDefault()) as string;
            if (string.IsNullOrEmpty(target)) target = _emptyScene;
            if (string.IsNullOrEmpty(target) || target == Current) return;
            if (!Application.CanStreamedLevelBeLoaded(target))
            {
                session.Journal.Append(JournalKind.Warning, "Scenes", $"씬 '{target}'을(를) 열 수 없습니다(Build Settings의 씬 목록에 있는지 확인).");
                return;
            }
            _loading = target;
            GameView.Present(Open(target));
        }

        static IEnumerator Open(string scene)
        {
            var load = Load(scene);
            yield return Transition != null ? Transition(load) : load;
            if (_loading == scene) _loading = null;
            Loaded?.Invoke(scene);
        }

        static IEnumerator Load(string scene)
        {
            var operation = SceneManager.LoadSceneAsync(scene);
            while (operation != null && !operation.isDone) yield return null;
        }
    }
}
