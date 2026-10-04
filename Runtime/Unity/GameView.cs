using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GBible
{
    // 화면 쪽 도우미. 규칙(행동)은 즉시 끝나고, 화면은 그 결과를 따라간다.
    //
    // 1) 다시 그리기: 플레이어 데이터나 언어가 바뀌면 다음 프레임 끝에 한 번만 다시 그린다.
    //    값이 바뀌었다는 알림은 한 행동에도 여러 번 온다(중간 Sync, 실패한 행동의 되돌림, Undo). 알림마다 그리면 같은 화면을
    //    한 프레임에 여러 번 계산하게 되므로, 표시만 남겼다가 프레임 끝(행동 밖)에서 모아 한 번 그린다.
    //      void OnEnable()  => _view = GameView.Redraw(Redraw, GameIds.PlayerData.Run);   // 키를 비우면 어느 값이 바뀌어도
    //      void OnDisable() => _view.Dispose();
    //    처음 한 번은 등록한 프레임 끝에 바로 그린다. 연출이 재생 중이면 끝날 때까지 기다린다(죽는 연출 전에 적이 사라지지 않게).
    //    기다리지 않을 화면(자원 표시 등)은 Redraw(draw, waitForPresentation: false, keys).
    //
    // 2) 연출 대기열: 연출(피해 숫자·카드 이동·죽는 모습)을 차례로 하나씩 재생한다.
    //      Game.Session.Events.SubscribeAfterAction<Damaged>(e => GameView.Present(ShowDamage(e)));   // 코루틴(IEnumerator)
    //      GameView.Present(done => card.MoveTo(slot, done));                                          // 끝나면 done()
    //    연출이 남아 있으면 IsPresenting이 참: 그동안 입력을 막는다(Game.IsWaiting과 함께 → Busy).
    //    연출 코드는 시간을 GameView.DeltaTime(속도 반영)이나 GameView.Wait(초)로 잰다 → PresentationSpeed·Skip이 먹는다.
    //
    // 3) 화면 오브젝트 찾기: 데이터 객체(id) ↔ 화면 오브젝트. ViewList가 자동으로 등록한다.
    //      var enemyView = GameView.Find<EnemyView>(e.targetId);
    public static class GameView
    {
        sealed class Watch : IDisposable
        {
            public Action Draw;
            public HashSet<string> Keys;
            public bool WaitForPresentation;
            public bool Dirty = true;
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
                Watches.Remove(this);
            }
        }

        static readonly List<Watch> Watches = new List<Watch>();
        static readonly Queue<Func<IEnumerator>> Steps = new Queue<Func<IEnumerator>>();
        static readonly Dictionary<string, GameObject> Views = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        static GameSession _session;
        static bool _running;
        static Coroutine _runner;
        static bool _skipping;

        // ── 다시 그리기 ─────────────────────────────────────────

        public static IDisposable Redraw(Action draw, params string[] keys) => Redraw(draw, true, keys);

        public static IDisposable Redraw(Action draw, bool waitForPresentation, params string[] keys)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            Hook();
            var watch = new Watch
            {
                Draw = draw, WaitForPresentation = waitForPresentation,
                Keys = keys == null || keys.Length == 0 ? null : new HashSet<string>(keys, StringComparer.Ordinal),
            };
            Watches.Add(watch);
            return watch;
        }

        static void Hook()
        {
            if (_session == GBibleRuntime.Session) return;
            if (_session != null) _session.Data.Changed -= OnChanged;
            _session = GBibleRuntime.Session;
            if (_session != null) _session.Data.Changed += OnChanged;
            GameLanguage.Changed -= OnLanguage;
            GameLanguage.Changed += OnLanguage;
            // 세션이 바뀌면(슬롯 바꾸기·다시 시작) 모두 다시 그린다.
            foreach (var watch in Watches) watch.Dirty = true;
        }

        static void OnChanged(string key)
        {
            foreach (var watch in Watches)
                if (watch.Keys == null || watch.Keys.Contains(key)) watch.Dirty = true;
        }

        static void OnLanguage()
        {
            foreach (var watch in Watches) watch.Dirty = true;
        }

        // 프레임 끝(GBibleHost.LateUpdate)에서 부른다. 행동 중이면 다음 프레임으로 미룬다.
        internal static void FrameEnd()
        {
            if (Watches.Count == 0) return;
            Hook();
            if (_session != null && _session.Data.InTransaction) return;
            var presenting = IsPresenting;
            foreach (var watch in Watches.ToArray())
            {
                if (!watch.Dirty || watch.Disposed || (presenting && watch.WaitForPresentation)) continue;
                watch.Dirty = false;
                try { watch.Draw(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ── 연출 대기열 ─────────────────────────────────────────

        // 남은 연출이 있다(재생 중 포함).
        public static bool IsPresenting => _running || Steps.Count > 0;

        // 입력을 막아야 할 때: 연출 재생 중이거나 선택을 기다리는 중.
        public static bool Busy => IsPresenting || (GBibleRuntime.Game?.IsWaiting ?? false);

        // 대기열이 비었을 때(마지막 연출이 끝난 순간).
        public static event Action Presented;

        // 연출 속도(2 = 두 배). 연출 코드는 DeltaTime·Wait로 시간을 재야 이 값을 따른다.
        public static float PresentationSpeed = 1f;

        public static float DeltaTime => Time.deltaTime * PresentationSpeed * (_skipping ? 10f : 1f);

        public static void Present(IEnumerator routine)
        {
            if (routine == null) return;
            var used = false;
            Enqueue(() =>
            {
                if (used) return null;
                used = true;
                return routine;
            });
        }

        // 끝나면 done()을 부르는 연출(트윈 라이브러리의 완료 콜백 등). timeout(실제 초) 안에 done이 오지 않으면 경고하고 넘어간다
        // (오브젝트가 먼저 없어져 done을 부르지 못한 경우 대기열이 멈추지 않게). 0이면 끝없이 기다린다.
        public static void Present(Action<Action> step, float timeout = 30f)
        {
            if (step != null) Enqueue(() => Callback(step, timeout));
        }

        // 잠깐 쉬기(속도 반영).
        public static void Present(float seconds) => Enqueue(() => Wait(seconds));

        // 연출 코드 안에서 쓰는 기다리기(속도·건너뛰기 반영). WaitForSeconds 대신 이것을 쓴다.
        public static IEnumerator Wait(float seconds)
        {
            for (var left = seconds; left > 0f; left -= DeltaTime) yield return null;
        }

        // 남은 연출을 빠르게(×10) 끝낸다. 대기열이 비면 원래 속도로 돌아온다.
        public static void SkipPresentation()
        {
            if (IsPresenting) _skipping = true;
        }

        // 남은 연출을 버린다(씬을 떠날 때 등). 재생 중인 것은 그 자리에서 멈춘다.
        public static void ClearPresentation()
        {
            Steps.Clear();
            if (GBibleHost.Instance != null && _runner != null) GBibleHost.Instance.StopCoroutine(_runner);
            Finish();
        }

        static void Enqueue(Func<IEnumerator> step)
        {
            Steps.Enqueue(step);
            Pump();
        }

        // 대기열을 돌리기 시작한다(호스트가 아직 없을 때 들어온 연출은 호스트가 매 프레임 이것으로 시작한다).
        internal static void Pump()
        {
            if (_running || Steps.Count == 0 || GBibleHost.Instance == null) return;
            _running = true;
            _runner = GBibleHost.Instance.StartCoroutine(RunSteps());
        }

        static IEnumerator RunSteps()
        {
            while (Steps.Count > 0)
            {
                IEnumerator routine = null;
                try { routine = Steps.Dequeue()(); }
                catch (Exception e) { Debug.LogException(e); }
                if (routine != null) yield return Safe(routine);
            }
            Finish();
        }

        static void Finish()
        {
            var was = _running;
            _running = false;
            _runner = null;
            _skipping = false;
            if (was) Presented?.Invoke();
        }

        // 연출 하나가 예외를 내도 대기열이 멈추지 않게(그 연출만 끝낸다). 안쪽 코루틴도 같은 방식으로 감싼다.
        static IEnumerator Safe(IEnumerator inner)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    yield break;
                }
                yield return current is IEnumerator nested ? Safe(nested) : current;
            }
        }

        static IEnumerator Callback(Action<Action> step, float timeout)
        {
            var done = false;
            try { step(() => done = true); }
            catch (Exception e)
            {
                Debug.LogException(e);
                yield break;
            }
            var until = Time.realtimeSinceStartup + timeout;
            while (!done)
            {
                if (timeout > 0f && Time.realtimeSinceStartup > until)
                {
                    Debug.LogWarning($"[MomoiProject] 연출이 {timeout:0.#}초 안에 끝나지 않아 넘어갑니다(done()을 부르지 않았거나 오브젝트가 먼저 없어졌는지 확인).");
                    yield break;
                }
                yield return null;
            }
        }

        // ── 화면 오브젝트 찾기 ──────────────────────────────────

        public static void Register(string id, GameObject view)
        {
            if (!string.IsNullOrEmpty(id) && view != null) Views[id] = view;
        }

        // view를 주면 그 오브젝트로 등록돼 있을 때만 지운다(같은 id로 새 오브젝트가 먼저 등록된 경우).
        public static void Unregister(string id, GameObject view = null)
        {
            if (string.IsNullOrEmpty(id) || !Views.TryGetValue(id, out var current)) return;
            if (view == null || current == view) Views.Remove(id);
        }

        public static GameObject Find(string id)
        {
            if (string.IsNullOrEmpty(id) || !Views.TryGetValue(id, out var view)) return null;
            if (view != null) return view;
            Views.Remove(id);   // 없어진 오브젝트
            return null;
        }

        public static T Find<T>(string id) where T : Component
        {
            var view = Find(id);
            return view == null ? null : view.GetComponent<T>();
        }

        // 플레이를 다시 시작할 때(도메인 리로드를 끈 설정) 이전 등록을 지운다.
        internal static void Reset()
        {
            if (_session != null) _session.Data.Changed -= OnChanged;
            _session = null;
            Watches.Clear();
            Steps.Clear();
            Views.Clear();
            _running = false;
            _runner = null;
            _skipping = false;
            Presented = null;
            PresentationSpeed = 1f;
            GameLanguage.Changed -= OnLanguage;
        }
    }
}
