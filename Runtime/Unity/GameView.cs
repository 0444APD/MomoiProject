using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible
{
    // 화면 쪽 도우미. 규칙(행동)은 즉시 끝나고, 화면은 그 결과를 따라간다.
    //
    // 1) 다시 그리기: 플레이어 데이터나 언어가 바뀌면 다음 프레임 끝에 한 번만 다시 그린다.
    //    값이 바뀌었다는 알림은 한 행동에도 여러 번 온다(중간 Sync, 실패한 행동의 되돌림, Undo). 알림마다 그리면 같은 화면을
    //    한 프레임에 여러 번 계산하게 되므로, 표시만 남겼다가 프레임 끝(행동 밖)에서 모아 한 번 그린다.
    //      void OnEnable()  => _view = GameView.Redraw(Redraw, GameIds.PlayerData.Run);   // 키를 비우면 어느 값이 바뀌어도
    //      void OnDisable() => _view.Dispose();
    //    처음 한 번은 등록한 프레임 끝에 바로 그린다(연출 중이어도 — 새로 생긴 화면·씬 전환 뒤의 새 씬은 그 전 모습이 없다).
    //    그 뒤로는 연출이 재생 중이면 끝날 때까지 기다린다(죽는 연출 전에 적이 사라지지 않게).
    //    기다리지 않을 화면(자원 표시 등)은 Redraw(draw, waitForPresentation: false, keys).
    //
    // 2) 연출 대기열: 연출(피해 숫자·카드 이동·죽는 모습)을 차례로 하나씩 재생한다.
    //      Game.Session.Events.SubscribeAfterAction<Damaged>(e => GameView.Present(ShowDamage(e)));   // 코루틴(IEnumerator)
    //      GameView.Present(done => card.MoveTo(slot, done));                                          // 끝나면 done()
    //    연출이 남아 있으면 IsPresenting이 참: 그동안 입력을 막는다(Game.IsWaiting과 함께 → Busy).
    //    연출 코드는 시간을 GameView.DeltaTime(속도 반영)이나 GameView.Wait(초)로 잰다 → PresentationSpeed·Skip이 먹는다.
    //    연출 하나의 한 걸음(yield 하나)이 30초(실제 초, 게임이 멈춘 동안은 세지 않음) 넘게 끝나지 않으면 경고하고 다음 연출로 넘어간다
    //    (yield return x.StartCoroutine(…)으로 기다린 코루틴이 x가 없어져 멈추면 Unity가 기다리던 쪽을 깨우지 않는다).
    //
    // 3) 화면 오브젝트 찾기: 데이터 객체(id) ↔ 화면 오브젝트. ViewList가 자동으로 등록한다.
    //      var enemyView = GameView.Find<EnemyView>(e.targetId);
    //    UI Toolkit 화면의 요소는 ElementList가 등록한다: GameView.FindElement(e.targetId)(피해 숫자를 그 칸 위에).
    public static class GameView
    {
        sealed class Watch : IDisposable
        {
            public Action Draw;
            public HashSet<string> Keys;
            public bool WaitForPresentation;
            public bool Dirty = true;
            public bool Disposed;
            // 한 번이라도 그렸는가. 처음 그리기는 연출을 기다리지 않는다(새로 생긴 화면·씬 전환 뒤의 새 씬 — 그 전 모습이 없으니 지금 값으로).
            public bool Drawn;
            // 처음 그리기도 연출이 끝나기를 기다린다(씬 흐름: 앞 씬 전환이 끝난 뒤에만 다음 씬을 본다). 다른 다시 그리기 뒤에 돈다(FrameEnd).
            public bool WaitFirst;

            public void Dispose()
            {
                Disposed = true;
                Watches.Remove(this);
            }
        }

        static readonly List<Watch> Watches = new List<Watch>();
        // 대기열의 칸: 연출을 만드는 함수 + 버리기(ClearPresentation)에도 끝까지 돌릴지(씬 흐름의 씬 열기).
        static readonly Queue<(Func<IEnumerator> make, bool keep)> Steps = new Queue<(Func<IEnumerator>, bool)>();
        static readonly Dictionary<string, GameObject> Views = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        static readonly Dictionary<string, VisualElement> Elements = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        static GameSession _session;
        static bool _running;
        static Coroutine _runner;
        static bool _skipping;
        // 대기열을 버리거나(ClearPresentation) 멈춘 연출을 넘길 때마다 1 늘린다 — 그 전의 러너·연출(안쪽 코루틴까지)은 다음 걸음에서 멈춘다.
        static int _generation;
        // 재생 중인 연출 하나(없으면 null).
        static Step _step;
        static int _stallCheckedFrame = -1;

        // 연출 하나의 한 걸음이 이만큼(실제 초, 게임이 멈춘 동안은 세지 않음) 끝나지 않으면 넘어간다(완료 콜백의 기본 제한 시간과 같다).
        // 시험(PlayMode)이 줄여서 본다 — 플레이를 다시 시작하면(Reset) 원래 값.
        internal const float DefaultStallSeconds = 30f;
        internal static float StallSeconds = DefaultStallSeconds;

        // 재생 중인 연출 하나의 상태(안쪽 코루틴도 같은 것을 쓴다).
        sealed class Step
        {
            public int Generation;
            // 버리기(ClearPresentation)에도 끝까지 돈다(씬 흐름의 씬 열기).
            public bool Keep;
            // 마지막으로 한 걸음 나아간 뒤 흐른 실제 초.
            public float Stalled;
            // 지금 Unity에 맡겨 기다리는 것(Coroutine·WaitForSeconds …) — 경고 글에 쓴다.
            public object Waiting;
        }

        // ── 다시 그리기 ─────────────────────────────────────────

        public static IDisposable Redraw(Action draw, params string[] keys) => Redraw(draw, true, keys);

        public static IDisposable Redraw(Action draw, bool waitForPresentation, params string[] keys) => Watching(draw, waitForPresentation, false, keys);

        // 처음 그리기도 연출이 끝나기를 기다린다(GameScenes).
        internal static IDisposable RedrawAfterPresentation(Action draw, params string[] keys) => Watching(draw, true, true, keys);

        static IDisposable Watching(Action draw, bool waitForPresentation, bool waitFirst, string[] keys)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            Hook();
            var watch = new Watch
            {
                Draw = draw, WaitForPresentation = waitForPresentation, WaitFirst = waitFirst,
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

        // 플레이어마다 데이터: 'bag:2'(이 화면 플레이어의 몫)가 바뀌면 'bag'을 지켜보는 다시 그리기도 그린다.
        static void OnChanged(string key)
        {
            string shared = null;
            var colon = key?.LastIndexOf(':') ?? -1;
            if (colon > 0 && _session != null && _session.Data.IsPerPlayer(key.Substring(0, colon)) && PlayerDataStore.KeyFor(key.Substring(0, colon), _session.LocalPlayer) == key)
                shared = key.Substring(0, colon);
            foreach (var watch in Watches)
                if (watch.Keys == null || watch.Keys.Contains(key) || (shared != null && watch.Keys.Contains(shared))) watch.Dirty = true;
        }

        static void OnLanguage()
        {
            foreach (var watch in Watches) watch.Dirty = true;
        }

        // 모든 화면을 다음 프레임 끝에 다시 그린다(밸런싱 값처럼 플레이어 데이터가 아닌 것이 바뀌었을 때).
        public static void RedrawAll()
        {
            foreach (var watch in Watches) watch.Dirty = true;
        }

        // 그 다시 그리기 하나를 다음 프레임 끝에 다시 돌린다(GameScenes: 씬을 여는 연출이 끝나지 못하고 버려졌을 때).
        internal static void MarkDirty(IDisposable watch)
        {
            if (watch is Watch target && !target.Disposed) target.Dirty = true;
        }

        // 프레임 끝(GBibleHost.LateUpdate)에서 부른다. 행동 중이면 다음 프레임으로 미룬다.
        internal static void FrameEnd()
        {
            if (Watches.Count == 0) return;
            Hook();
            if (_session != null && _session.Data.InTransaction) return;
            var presenting = IsPresenting;
            var watches = Watches.ToArray();
            foreach (var watch in watches)
                if (!watch.WaitFirst && !(presenting && watch.WaitForPresentation && watch.Drawn)) DrawWatch(watch);
            // 처음 그리기도 연출을 기다리는 것(씬 흐름)은 맨 뒤에, 연출이 남았는지 다시 보고: 위의 그리기가 방금 넣은 연출
            // (ViewList·ElementList의 OnRemove — 마지막 적의 죽는 모습)이 있으면 그것이 끝난 뒤(대기열이 빈 프레임)로 미룬다.
            // 씬 흐름은 게임이 켜질 때 먼저 등록되므로 한 번에 돌리면, 같은 행동이 적을 없애고 씬 값을 바꿨을 때 씬부터 열어
            // 죽는 모습을 틀 오브젝트가 씬과 함께 없어진다.
            foreach (var watch in watches)
                if (watch.WaitFirst && !(watch.WaitForPresentation && IsPresenting)) DrawWatch(watch);
        }

        static void DrawWatch(Watch watch)
        {
            if (!watch.Dirty || watch.Disposed) return;
            watch.Dirty = false;
            watch.Drawn = true;
            try { watch.Draw(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ── 연출 대기열 ─────────────────────────────────────────

        // 남은 연출이 있다(재생 중 포함).
        public static bool IsPresenting => _running || Steps.Count > 0;

        // 입력을 막아야 할 때: 연출 재생 중이거나 선택을 기다리는 중, 또는 모듈 · 프로젝트가 더한 때(다이얼로그가 열려 있는 동안 …).
        public static bool Busy => IsPresenting || (GBibleRuntime.Game?.IsWaiting ?? false) || BusyByOthers();

        // '입력을 막아야 하는 때'를 더한다(다이얼로그 모듈: 대사가 열려 있는 동안). 플레이를 시작할 때마다 비워지므로 AfterAssembliesLoaded에서 더한다.
        public static void AddBusy(Func<bool> busy)
        {
            if (busy != null && !BusyChecks.Contains(busy)) BusyChecks.Add(busy);
        }

        static readonly List<Func<bool>> BusyChecks = new List<Func<bool>>();

        static bool BusyByOthers()
        {
            for (var i = 0; i < BusyChecks.Count; i++)
            {
                try { if (BusyChecks[i]()) return true; }
                catch (Exception e) { Debug.LogException(e); }
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBusyChecks() => BusyChecks.Clear();

        // 대기열이 비었을 때(마지막 연출이 끝난 순간).
        public static event Action Presented;

        // 대기열을 버렸을 때(ClearPresentation — 차례를 기다리던 연출이 버려진 순간). ViewList·ElementList가 받는다.
        internal static event Action Cleared;

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

        // 씬 흐름의 씬 열기(GameScenes): 대기열의 한 칸이지만 버리기(ClearPresentation)에도 끝까지 돈다. 씬 열기는 연출이 아니라
        // 판 값을 따라가는 일이라, 중간에 멈추면 씬이 바뀌다 말거나(Loaded 없음·다시 열면 두 번 열림) 전환 연출의 페이드가 어두운 채 남는다.
        internal static void PresentSceneChange(IEnumerator routine)
        {
            if (routine == null) return;
            var used = false;
            Enqueue(() =>
            {
                if (used) return null;
                used = true;
                return routine;
            }, keep: true);
        }

        // 대기열 밖에서 바로 튼다(실시간 게임의 맞는 효과·피해 숫자·방치형 클릭 숫자). 여럿이 동시에 돌고,
        // IsPresenting·Busy·다시 그리기 미루기와 상관없다(입력을 막지 않는다). 시간은 DeltaTime·Wait로(속도·건너뛰기 반영).
        public static void Play(IEnumerator routine)
        {
            if (routine == null) return;
            var host = GBibleHost.Instance;
            if (host == null) { Debug.LogWarning("[MomoiProject] GameView.Play: 게임이 시작되지 않아 연출을 틀 수 없습니다."); return; }
            host.StartCoroutine(routine);
        }

        // 끝나면 done()을 부르는 연출을 대기열 밖에서 바로(timeout 실제 초 안에 done이 없으면 경고).
        public static void Play(Action<Action> step, float timeout = 30f)
        {
            if (step != null) Play(Callback(step, timeout));
        }

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

        // 남은 연출을 버린다(씬을 떠날 때 등). 재생 중인 것은 그 자리에서 멈춘다(안쪽 코루틴까지 — 다음 yield에서).
        // 씬 흐름이 씬을 여는 중이면 그것만은 끝까지 열고(PresentSceneChange), 대기열은 그 뒤에 빈다(그때 Presented).
        public static void ClearPresentation()
        {
            Steps.Clear();
            // 차례를 기다리던 연출은 이제 돌지 않는다 — 그 연출 뒤에 치우기로 한 것(ViewList·ElementList의 사라지는 오브젝트·칸)을 지금 치우게 알린다
            // (알리지 않으면 31초 안전장치까지 남아, Undo로 되살아난 새 오브젝트와 겹쳐 보인다).
            if (Cleared != null)
                foreach (Action handler in Cleared.GetInvocationList())
                {
                    try { handler(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            if (_step != null && _step.Keep) return;
            // 러너를 StopCoroutine으로 멈추면 재생 중인 연출(안쪽 코루틴)은 따로 계속 돈다 → 세대만 바꿔, 러너와 연출이 다음 걸음에서
            // 스스로 끝나게 한다(연출은 Safe가 닫아 finally가 돈다. 옛 러너는 대기열을 더 꺼내지 않고 Presented도 쏘지 않는다).
            _generation++;
            Finish();
        }

        static void Enqueue(Func<IEnumerator> step, bool keep = false)
        {
            Steps.Enqueue((step, keep));
            Pump();
        }

        // 대기열을 돌리기 시작한다(호스트가 아직 없을 때 들어온 연출은 호스트가 매 프레임 이것으로 시작한다).
        // 진행이 멈춘 연출도 여기서 넘긴다(호스트가 매 프레임 부른다).
        internal static void Pump()
        {
            CheckStalled();
            if (_running || Steps.Count == 0 || GBibleHost.Instance == null) return;
            StartRunner();
        }

        static void StartRunner()
        {
            _running = true;
            _runner = null;
            var generation = _generation;
            var runner = GBibleHost.Instance.StartCoroutine(RunSteps(generation));
            // 첫 yield 전에 대기열이 다 돌았으면(Finish) 끝난 코루틴을 들고 있지 않는다(그 사이 새로 선 러너가 있으면 그쪽).
            if (_running && generation == _generation && _runner == null) _runner = runner;
        }

        static IEnumerator RunSteps(int generation)
        {
            while (generation == _generation && Steps.Count > 0)
            {
                var next = Steps.Dequeue();
                IEnumerator routine = null;
                try { routine = next.make(); }
                catch (Exception e) { Debug.LogException(e); }
                if (routine == null) continue;
                var step = _step = new Step { Generation = generation, Keep = next.keep };
                yield return Safe(routine, step);
                if (_step == step) _step = null;
            }
            if (generation == _generation) Finish();
        }

        static void Finish()
        {
            var was = _running;
            _running = false;
            _runner = null;
            _step = null;
            _skipping = false;
            if (was) Presented?.Invoke();
        }

        // 연출 하나가 예외를 내도 대기열이 멈추지 않게(그 연출만 끝낸다). 안쪽 코루틴(yield return 다른연출())도 같은 방식으로 감싼다.
        // 대기열을 버렸거나 넘긴 연출(세대가 바뀜)은 다음 걸음에서 멈춘다 — 코루틴을 닫아 그 안의 finally(using)는 돌게 한다(만든 것을 치울 길).
        // 걸음마다 진행을 적어 둔다(CheckStalled).
        static IEnumerator Safe(IEnumerator inner, Step step)
        {
            while (true)
            {
                if (step.Generation != _generation)
                {
                    try { (inner as IDisposable)?.Dispose(); }
                    catch (Exception e) { Debug.LogException(e); }
                    yield break;
                }
                step.Stalled = 0f;
                step.Waiting = null;
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
                if (current is IEnumerator nested)
                {
                    yield return Safe(nested, step);
                    continue;
                }
                step.Waiting = current;
                yield return current;
            }
        }

        // 진행이 멈춘 연출을 넘긴다(한 프레임에 한 번). 연출이 yield return x.StartCoroutine(…)으로 기다린 코루틴이 x가 없어지거나 꺼지거나
        // 예외로 멈추면 Unity가 기다리던 연출을 다시 깨우지 않는다 → 대기열이 영영 멈추고 Busy가 풀리지 않는다.
        // 한 걸음(yield 하나)이 StallSeconds를 넘기면 경고하고 그 연출을 버린 뒤 남은 연출을 이어서 돌린다.
        static void CheckStalled()
        {
            var step = _step;
            if (step == null || Time.frameCount == _stallCheckedFrame) return;
            _stallCheckedFrame = Time.frameCount;
            if (!GameTime.IsPaused) step.Stalled += Time.unscaledDeltaTime;
            if (step.Stalled <= StallSeconds) return;
            Debug.LogWarning(step.Waiting is Coroutine
                ? $"[MomoiProject] 연출이 {StallSeconds:0.#}초 동안 나아가지 않아 넘어갑니다(yield return x.StartCoroutine(…)으로 기다린 코루틴이 x가 없어지거나 꺼지거나 예외로 멈췄는지 확인 — 안쪽 연출은 yield return 연출()로 직접 넘기세요)."
                : $"[MomoiProject] 연출이 {StallSeconds:0.#}초 동안 나아가지 않아 넘어갑니다(yield return {step.Waiting?.GetType().Name ?? "null"}이(가) 끝나지 않았습니다).");
            _generation++;
            _step = null;
            var host = GBibleHost.Instance;
            if (host != null && _runner != null) host.StopCoroutine(_runner);
            _runner = null;
            if (Steps.Count > 0 && host != null) StartRunner();
            else Finish();
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
            // 기다린 시간은 게임이 멈춰 있지 않은 동안만 센다(일시정지가 길어도 연출을 건너뛰지 않게).
            var waited = 0f;
            while (!done)
            {
                if (!GameTime.IsPaused) waited += Time.unscaledDeltaTime;
                if (timeout > 0f && waited > timeout)
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

        // UI Toolkit 요소(화면의 칸) — ElementList가 항목 id로 등록한다.
        public static void RegisterElement(string id, VisualElement element)
        {
            if (!string.IsNullOrEmpty(id) && element != null) Elements[id] = element;
        }

        public static void UnregisterElement(string id, VisualElement element = null)
        {
            if (string.IsNullOrEmpty(id) || !Elements.TryGetValue(id, out var current)) return;
            if (element == null || current == element) Elements.Remove(id);
        }

        // 화면에 붙어 있는 요소만(닫힌 화면의 요소는 null).
        public static VisualElement FindElement(string id)
        {
            if (string.IsNullOrEmpty(id) || !Elements.TryGetValue(id, out var element)) return null;
            if (element.panel != null) return element;
            Elements.Remove(id);
            return null;
        }

        // 플레이를 다시 시작할 때(도메인 리로드를 끈 설정) 이전 등록을 지운다.
        internal static void Reset()
        {
            if (_session != null) _session.Data.Changed -= OnChanged;
            _session = null;
            Watches.Clear();
            Steps.Clear();
            Views.Clear();
            Elements.Clear();
            _running = false;
            _runner = null;
            _generation++;
            _step = null;
            _stallCheckedFrame = -1;
            _skipping = false;
            StallSeconds = DefaultStallSeconds;
            Presented = null;
            Cleared = null;
            PresentationSpeed = 1f;
            GameLanguage.Changed -= OnLanguage;
        }
    }
}
