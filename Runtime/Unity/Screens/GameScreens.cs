using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GBible
{
    // 화면(UI Toolkit) 관리. 화면 하나 = DB › 화면 항목(UXML·층·뒤로 닫기 …) + [BindTo]로 이은 GameScreen 코드(없어도 된다).
    //   GameScreens.Open(GameIds.Screen.Shop);             // 열기(이미 열려 있으면 그대로)
    //   GameScreens.Close(GameIds.Screen.Shop);
    //   GameScreens.Back();                                // 맨 위의 닫을 수 있는 화면 닫기(Esc가 부른다)
    //   GameScreens.Toast("save.done");                    // 알림(텍스트 키 또는 글)
    //   GameScreens.Confirm("slot.deleteAsk", () => …);    // 예/아니오
    //   GameScreens.Do(() => new EndTurn());               // 화면 입력 → 행동(연출·선택 대기 중이면 무시, 플레이어용 실패는 알림)
    // 기본 화면(코드로 만든 범용 화면, 모양은 테마 USS로): 고르기 창(선택 대기가 생기면 저절로) · 확인 · 설정(DB › 설정에서) · 슬롯 · 일시정지.
    //   같은 ID로 DB › 화면 항목을 만들면 기본 화면 대신 그 화면이 열린다.
    // 모든 화면은 한 UIDocument(DB › 화면 설정의 Panel Settings) 안의 층(바탕 → 창 → 팝업 → 알림)에 뜬다. 같은 층에서는 나중에 연 것이 위.
    // 화면은 플레이어 데이터를 읽기만 하고, 값을 바꾸는 입력은 모두 행동으로 보낸다.
    public static class GameScreens
    {
        // 기본 화면 ID.
        public const string Choice = "momoi.choice";
        public const string Confirmation = "momoi.confirm";
        public const string Settings = "momoi.settings";
        public const string Slots = "momoi.slots";
        public const string Pause = "momoi.pause";

        public static event Action<GameScreen> Opened;
        public static event Action<GameScreen> Closed;

        // 일시정지 메뉴의 '타이틀로'가 할 일(비우면 그 버튼이 없다).
        public static Action QuitToTitle;

        sealed class Entry
        {
            public GameScreen Screen;
            public ScreenLayer Layer;
            public bool CloseOnBack;
            public bool PauseTime;
            public VisualElement Host;
            public IDisposable Watch;
            // 같은 화면이 닫히는 중(OnClose)에 다시 열렸다: 열림 알림(Opened)을 닫힘 알림(Closed) 뒤로 미뤄 둔다.
            public bool OpenedLater;
            public readonly List<(VisualElement element, ScreenBinding binding, string template)> Bindings = new List<(VisualElement, ScreenBinding, string)>();
        }

        static readonly Dictionary<string, ScreenData> Data = new Dictionary<string, ScreenData>(StringComparer.Ordinal);
        static readonly Dictionary<string, GameScreen> Controllers = new Dictionary<string, GameScreen>(StringComparer.Ordinal);
        static readonly List<Entry> OpenEntries = new List<Entry>();
        // OnClose를 도는 중인 화면 코드(그 안에서 같은 화면을 다시 열 수 있다 — 확인 창의 '아니오'가 확인 창을 다시 띄움).
        static readonly List<GameScreen> Closing = new List<GameScreen>();
        static readonly List<(VisualElement element, float until)> Toasts = new List<(VisualElement, float)>();
        static ScreenSettingsData _settings;
        static UIDocument _document;
        static VisualElement[] _layers;
        static VisualElement _toasts;
        static bool _initialized;
        static int _backFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Opened = null;
            Closed = null;
            QuitToTitle = null;
            Reroll = null;
            Data.Clear();
            Controllers.Clear();
            OpenEntries.Clear();
            Closing.Clear();
            Toasts.Clear();
            _settings = null;
            _document = null;
            _layers = null;
            _toasts = null;
            _initialized = false;
            _backFrame = -1;
            _backHolds = 0;
            _backReleased = -1;
            _language = null;
        }

        // 게임이 켜질 때(GBibleRuntime.Start) 한 번: DB의 화면을 읽고, 화면 코드를 만들고(리플렉션은 여기서만), 시작·씬 화면을 연다.
        internal static void Initialize(GameDatabase database)
        {
            if (_initialized) return;
            _initialized = true;
            if (database != null)
            {
                foreach (var screen in database.All<ScreenData>())
                    if (screen != null && !string.IsNullOrEmpty(screen.Id)) Data[screen.Id] = screen;
                _settings = database.All<ScreenSettingsData>().FirstOrDefault();
            }
            foreach (var id in Data.Keys) Controllers[id] = MakeController(id);
            foreach (var id in BuiltInIds)
                if (!Controllers.ContainsKey(id)) Controllers[id] = BuiltIn(id);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            foreach (var screen in Data.Values.Where(s => s.openOnStart)) Open(screen.Id);
            ShowForScene(SceneManager.GetActiveScene().name);
        }

        // ── 열기 · 닫기 ─────────────────────────────────────────

        public static GameScreen Open(string id, object argument = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("화면 ID가 비었습니다.", nameof(id));
            var open = EntryOf(id);
            if (open != null) return open.Screen;
            Data.TryGetValue(id, out var data);
            var spec = data == null ? BuiltInSpec(id) : null;
            if (data == null && spec == null) throw new InvalidOperationException($"[MomoiProject] 없는 화면 '{id}'입니다(DB › 화면).");
            if (!Controllers.TryGetValue(id, out var screen) || screen == null) Controllers[id] = screen = data != null ? MakeController(id) : BuiltIn(id);
            if (!EnsureRoot()) return null;

            var tree = data?.layout != null ? (VisualElement)data.layout.Instantiate() : new VisualElement();
            tree.AddToClassList("momoi-screen");
            tree.pickingMode = PickingMode.Ignore;
            Fill(tree);
            if (data != null)
                foreach (var sheet in data.styles)
                    if (sheet != null) tree.styleSheets.Add(sheet);

            var layer = data?.layer ?? spec.Value.layer;
            var block = layer == ScreenLayer.Popup || (layer == ScreenLayer.Window && (data?.blockInput ?? spec.Value.block));
            var host = new VisualElement { name = id, pickingMode = PickingMode.Ignore };
            host.AddToClassList("momoi-screen-host");
            Fill(host);
            var backdropKind = data?.backdrop ?? spec.Value.backdrop;
            if (block || backdropKind != ScreenBackdrop.Plain)
            {
                // 뒷배경(UiGuide 3-1): 일반 = 투명(장면이 그대로) · 블러 = 흐림 + 아주 옅게 어둡게(알림만) · UI 배경 = 화면 전체 단색. 입력을 막는 화면이면 아래를 누르지 못하게 덮는다.
                var backdrop = new VisualElement();
                backdrop.AddToClassList("momoi-backdrop");
                backdrop.AddToClassList(backdropKind == ScreenBackdrop.Blur ? "momoi-backdrop--blur" : backdropKind == ScreenBackdrop.Ui ? "momoi-backdrop--ui" : "momoi-backdrop--plain");
                if (!block) backdrop.pickingMode = PickingMode.Ignore;
                Fill(backdrop);
                host.Add(backdrop);
            }
            host.Add(tree);
            _layers[(int)layer].Add(host);
            ScreenMotion.Enter(host);   // 짧게 나타나기(UiGuide 3-5)

            var entry = new Entry
            {
                Screen = screen, Layer = layer, Host = host,
                CloseOnBack = data?.closeOnBack ?? spec.Value.closeOnBack,
                PauseTime = data?.pauseTime ?? spec.Value.pause,
            };
            screen.Id = id;
            screen.Root = tree;
            screen.Argument = argument;
            screen.Data = data;
            screen.IsOpen = true;
            OpenEntries.Add(entry);
            CollectBindings(entry, tree);
            if (entry.PauseTime) GameTime.Pause("화면 " + id);

            try { screen.OnOpen(); }
            catch (Exception e) { Debug.LogException(e); }
            // OnOpen 안에서 닫았으면(Close()) 이미 다 정리됐다: 다시 그리기를 걸지도 열림을 알리지도 않는다
            // (걸면 아무도 끊지 않는 다시 그리기가 남아, 이 화면을 다음에 열면 값이 바뀔 때마다 Draw가 두 번 돈다).
            if (!OpenEntries.Contains(entry)) return screen;
            // 다시 그릴 키: 코드가 정한 것, 없으면 binding-path의 키 후보들("stat:kills"는 "stat:kills"와 "stat" — 그것도 없으면 어느 값이 바뀌어도).
            var keys = screen.Watch ?? entry.Bindings.Where(b => b.binding.HasData).SelectMany(b => b.binding.Keys).Distinct(StringComparer.Ordinal).ToArray();
            entry.Watch = GameView.Redraw(() => DrawEntry(entry), screen.WaitForPresentation, keys);
            // 같은 화면의 OnClose 안에서 다시 연 것이면 닫힘을 먼저 알린 뒤에 알린다(CloseEntry).
            if (Closing.Contains(screen)) entry.OpenedLater = true;
            else Opened?.Invoke(screen);
            return screen;
        }

        public static bool Close(string id)
        {
            var entry = EntryOf(id);
            if (entry == null) return false;
            CloseEntry(entry);
            return true;
        }

        public static void Close(GameScreen screen)
        {
            if (screen != null) Close(screen.Id);
        }

        // 그 층(비우면 모든 층)의 화면을 나중에 연 것부터 닫는다.
        public static void CloseAll(ScreenLayer? layer = null)
        {
            foreach (var entry in OpenEntries.Where(e => layer == null || e.Layer == layer).Reverse().ToList()) CloseEntry(entry);
        }

        public static bool IsOpen(string id) => EntryOf(id) != null;
        public static GameScreen Get(string id) => EntryOf(id)?.Screen;
        public static T Get<T>() where T : GameScreen => OpenEntries.Select(e => e.Screen).OfType<T>().FirstOrDefault();
        public static IEnumerable<GameScreen> All => OpenEntries.Select(e => e.Screen).ToList();

        // 뒤로(Esc)를 잠시 막는다: 키 바꾸기처럼 Esc를 '취소'로 쓰는 동안 같은 Esc가 창을 닫거나 일시정지 메뉴를 열지 않게(GameInput.Rebind가 잡는다).
        // 놓은 프레임에도 막는다 — 입력 처리(키 바꾸기 취소)가 화면보다 먼저 돌아 그 프레임의 Esc가 화면에도 온다.
        public static IDisposable HoldBack()
        {
            _backHolds++;
            return new BackHold();
        }

        static int _backHolds;
        static int _backReleased = -1;

        static bool BackHeld => _backHolds > 0 || Time.frameCount == _backReleased;

        sealed class BackHold : IDisposable
        {
            bool _released;

            public void Dispose()
            {
                if (_released) return;
                _released = true;
                if (_backHolds > 0) _backHolds--;
                _backReleased = Time.frameCount;
            }
        }

        // 맨 위의 창·팝업을 닫는다(Esc·뒤로). 그 화면이 '뒤로로 닫기'가 꺼져 있으면 아무것도 하지 않는다(고르기 창 등).
        // 닫을 화면이 없으면 DB › 화면 설정 'Esc로 일시정지 메뉴'일 때 일시정지 메뉴를 연다. 무엇이든 했으면 참.
        public static bool Back()
        {
            if (BackHeld) return false;
            if (Time.frameCount == _backFrame) return true;   // 한 프레임에 두 번(키 입력 + UI 이벤트) 오지 않게
            _backFrame = Time.frameCount;
            var top = OpenEntries.Where(e => e.Layer == ScreenLayer.Window || e.Layer == ScreenLayer.Popup)
                                 .OrderByDescending(e => (int)e.Layer).ThenByDescending(e => OpenEntries.IndexOf(e)).FirstOrDefault();
            if (top != null)
            {
                if (top.CloseOnBack) CloseEntry(top);
                return true;
            }
            if (_settings != null && _settings.pauseMenu && GBibleRuntime.Game != null)
            {
                Open(Pause);
                return true;
            }
            return false;
        }

        // ── 알림 · 확인 · 입력 ──────────────────────────────────

        // 알림 한 줄(텍스트 키면 번역). 입력을 막지 않고 seconds 뒤에 사라진다.
        public static void Toast(string text, float seconds = 2.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!EnsureRoot())
            {
                Debug.Log("[MomoiProject] " + text);
                return;
            }
            var label = new Label(ScreenUi.Text(text)) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("momoi-toast");
            _toasts.Add(label);
            Toasts.Add((label, Time.unscaledTime + Mathf.Max(0.1f, seconds)));
            while (Toasts.Count > 4)
            {
                Toasts[0].element.RemoveFromHierarchy();
                Toasts.RemoveAt(0);
            }
        }

        // 예/아니오 팝업. 뒤로(Esc)는 아니오.
        public static void Confirm(string message, Action yes, Action no = null, string yesText = null, string noText = null)
        {
            Close(Confirmation);
            Open(Confirmation, new ConfirmRequest { message = message, yes = yes, no = no, yesText = yesText, noText = noText });
        }

        // 화면 입력 → 행동. 연출·선택 대기 중(GameView.Busy)이면 하지 않고, 플레이어용 실패는 알림('실패를 알림으로'가 꺼져 있으면 경고 로그).
        // whileChoosing: 선택을 기다리는 동안에도 보낸다(다시 뽑기처럼 고르기 창 안의 버튼). 연출 중에는 그래도 막는다.
        // 프로그래머 실수(InvalidOperationException 등)는 그대로 던진다. 행동을 했으면 참.
        public static bool Do(Func<GameAction> make, bool whileChoosing = false)
        {
            var game = GBibleRuntime.Game;
            // 고르는 동안(whileChoosing): 시간이 멈춰 남은 연출이 끝나지 않을 수 있으므로 멈춤 중이면 연출을 기다리지 않는다.
            if (game == null || make == null || (whileChoosing ? GameView.IsPresenting && !GameTime.IsPaused : GameView.Busy)) return false;
            try
            {
                game.Do(make());
                return true;
            }
            catch (GameFailure failure)
            {
                ShowFailure(failure.Message);
                return false;
            }
        }

        // 플레이어용 실패 글을 보인다('실패를 알림으로'가 켜져 있으면 알림, 아니면 경고 로그). 멀티 클라이언트는 서버가 거절한 행동도 이것으로(GameNet).
        public static void ShowFailure(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_settings == null || _settings.failureToast) Toast(message);
            else Debug.LogWarning("[MomoiProject] " + message);
        }

        // 기본 고르기 창의 '다시 뽑기' 버튼: 프로젝트가 정하고 그 선택에 행동을 돌려주면 버튼이 보인다.
        //   표로 만든 선택('가중치 표에서 고르기'): 행동이 비용·남은 횟수를 내고 Game.RerollChoice(choice.id)를 부른다. null을 돌려주면 버튼을 숨긴다(다시 뽑을 수 없음).
        //   GameScreens.Reroll = choice => !string.IsNullOrEmpty(choice.table) && Game.Data().Run.rerolls > 0 ? new RerollReward { choice = choice.id } : null;
        //   표로 만들지 않은 선택(코드 풀 — Game.Ask)도 행동을 돌려주면 버튼이 보인다: 그 행동이 Game.CancelChoice 뒤 새 후보로 다시 Game.Ask.
        public static Func<PendingChoice, GameAction> Reroll;

        // 화면 맨 위의 겹침 층(입력을 막지 않음): 피해 숫자처럼 화면 위에 잠깐 띄우는 연출 요소를 여기에 둔다.
        public static VisualElement Overlay => EnsureRoot() ? _layers[(int)ScreenLayer.Toast] : null;

        // 게임 끄기(에디터에서는 플레이 멈춤).
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // 매 프레임 끝(GBibleHost): 알림 지우기, 고르기 창 열고 닫기, Esc.
        internal static void Pump()
        {
            if (!_initialized) return;
            // 언어가 바뀌면 기본 화면(코드로 만든 화면)의 고정 글(제목·버튼)도 다시 만든다 — 그 글은 열 때(OnOpen) 만든다.
            var language = Texts.Catalog?.Language;
            if (!string.Equals(language, _language, StringComparison.Ordinal))
            {
                var changed = _language != null;
                _language = language;
                if (changed) Relocalize();
            }
            if (OpenEntries.Count > 0)
            {
                var dt = Time.unscaledDeltaTime;
                foreach (var entry in OpenEntries.ToList())
                {
                    try { if (entry.Screen.IsOpen) entry.Screen.Tick(dt); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            for (int i = Toasts.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= Toasts[i].until)
                {
                    Toasts[i].element.RemoveFromHierarchy();
                    Toasts.RemoveAt(i);
                }
            var game = GBibleRuntime.Game;
            if (game != null && (_settings == null || _settings.choiceWindow))
            {
                var waiting = game.IsWaiting;
                var open = IsOpen(Choice);
                if (waiting && !open)
                {
                    // 이 프레임의 그리기는 이미 지났으니 바로 그린다(빈 창이 한 프레임 보이지 않게).
                    Open(Choice);
                    var entry = EntryOf(Choice);
                    if (entry != null) DrawEntry(entry);
                }
                else if (!waiting && open) Close(Choice);
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
#elif GBIBLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            // 새 Input System만 켠 프로젝트: 포커스된 칸이 없으면 UI Toolkit의 취소 이벤트가 오지 않으므로 키를 직접 본다.
            if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) Back();
#endif
        }

        // ── 안쪽 ────────────────────────────────────────────────

        static Entry EntryOf(string id) => id == null ? null : OpenEntries.FirstOrDefault(e => e.Screen.Id == id);

        static void CloseEntry(Entry entry)
        {
            if (!OpenEntries.Remove(entry)) return;
            entry.Watch?.Dispose();
            var screen = entry.Screen;
            // 이번 열림의 정리할 것·버튼 연결은 떼어 들고 정리한다. 화면 코드 인스턴스는 열 때마다 다시 쓰므로, OnClose 안에서 같은 화면을
            // 다시 열면(확인 창을 Esc로 닫았더니 '아니오'가 확인 창을 다시 띄움) 새 열림이 같은 목록에 쌓는다 — 그것까지 끊지 않게.
            var owned = screen.Owned.ToList();
            screen.Owned.Clear();
            var clicks = screen.Clicks.ToList();
            screen.Clicks.Clear();
            Closing.Add(screen);
            try { screen.OnClose(); }
            catch (Exception e) { Debug.LogException(e); }
            finally { Closing.Remove(screen); }
            var reopened = OpenEntries.Exists(e => e.Screen == screen);
            if (!reopened)
            {
                // OnClose가 맡긴 것도 이번 열림의 것이다.
                owned.AddRange(screen.Owned);
                screen.Owned.Clear();
                clicks.AddRange(screen.Clicks);
                screen.Clicks.Clear();
            }
            foreach (var item in owned)
            {
                try { item.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            foreach (var (button, handler) in clicks) button.clicked -= handler;
            ScreenMotion.Leave(entry.Host);   // 짧게 사라진 뒤 떼어 낸다(UiGuide 3-5)
            // 다시 열렸으면 열림 표시·시간 멈춤은 새 열림의 것이다(같은 이유 글자로 멈췄으니 여기서 풀면 새 화면이 열린 채 시간이 흐른다).
            if (!reopened)
            {
                if (entry.PauseTime) GameTime.Resume("화면 " + screen.Id);
                screen.IsOpen = false;
                screen.Root = null;
                screen.Argument = null;
                screen.Data = null;
            }
            Closed?.Invoke(screen);
            // OnClose 안에서 다시 연 것의 열림 알림은 닫힘 알림 뒤에(Opened → Closed 순이면 받는 쪽은 닫힌 것으로 안다).
            foreach (var later in OpenEntries.Where(e => e.Screen == screen && e.OpenedLater).ToList())
            {
                later.OpenedLater = false;
                Opened?.Invoke(screen);
            }
        }

        static string _language;

        static void Relocalize()
        {
            foreach (var entry in OpenEntries.ToList())
            {
                if (!entry.Screen.IsOpen) continue;
                if (entry.Screen.Data == null)
                {
                    entry.Screen.Root.Clear();
                    try { entry.Screen.OnOpen(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                DrawEntry(entry);
            }
        }

        static void DrawEntry(Entry entry)
        {
            // 화면 코드 인스턴스는 열 때마다 다시 쓰므로 IsOpen만으로는 닫힌 열림(entry)을 가리지 못한다.
            if (!entry.Screen.IsOpen || !OpenEntries.Contains(entry)) return;
            var session = GBibleRuntime.Session;
            foreach (var (element, binding, template) in entry.Bindings)
            {
                try { Apply(element, binding, template, session); }
                catch (Exception e) { Debug.LogWarning($"[MomoiProject] 화면 '{entry.Screen.Id}'의 binding-path '{binding}': {e.Message}"); }
            }
            try { entry.Screen.Draw(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // binding-path가 있는 칸을 모은다(열 때 한 번). 원래 글자(토글·슬라이더는 이름표)는 틀로 쓰려고 남겨 둔다.
        static void CollectBindings(Entry entry, VisualElement tree)
        {
            tree.Query<BindableElement>().ForEach(element =>
            {
                var binding = ScreenBinding.Parse(element.bindingPath);
                if (binding == null) return;
                var template = element is TextElement text ? text.text
                    : element is AbstractProgressBar bar ? bar.title
                    : element is Toggle toggle ? toggle.label
                    : element is Slider slider ? slider.label
                    : element is SliderInt sliderInt ? sliderInt.label
                    : null;
                entry.Bindings.Add((element, binding, template));
            });
        }

        // 값을 칸에 넣는다: 글자 칸(Label·Button)은 글, 진행 막대는 값(+글), 토글·슬라이더는 값(보이기만, 바꾸는 입력은 행동으로) + 이름표.
        // 값은 데이터를 가리킬 때만 넣는다: '@텍스트키'만 적은 토글·슬라이더는 값이 없으니 거짓·0으로 덮지 않고 이름표(label)만 번역한다.
        internal static void Apply(VisualElement element, ScreenBinding binding, string template, GameSession session)
        {
            switch (element)
            {
                case AbstractProgressBar bar:
                    if (binding.HasData) bar.value = (float)ScreenBinding.Number(binding.Read(session));
                    if (HasText(binding, template)) bar.title = binding.Text(session, template);
                    return;
                case Toggle toggle:
                    if (binding.HasData) toggle.SetValueWithoutNotify(ScreenBinding.Flag(binding.Read(session)));
                    if (HasText(binding, template)) toggle.label = binding.Text(session, template);
                    return;
                case Slider slider:
                    if (binding.HasData) slider.SetValueWithoutNotify((float)ScreenBinding.Number(binding.Read(session)));
                    if (HasText(binding, template)) slider.label = binding.Text(session, template);
                    return;
                case SliderInt slider:
                    if (binding.HasData) slider.SetValueWithoutNotify((int)GameNumbers.Round(ScreenBinding.Number(binding.Read(session))));
                    if (HasText(binding, template)) slider.label = binding.Text(session, template);
                    return;
                case TextElement text:
                    text.text = binding.Text(session, template);
                    return;
            }
        }

        // 제목·이름표도 바꾸는가: 텍스트 키가 있거나 원래 글에 {value}가 있을 때(값만 보이는 막대·토글·슬라이더의 원래 글은 그대로 둔다).
        static bool HasText(ScreenBinding binding, string template) => binding.TextKey.Length > 0 || (template != null && template.Contains("{value}"));

        static bool EnsureRoot()
        {
            if (_document != null && _layers != null && _document.rootVisualElement != null) return true;
            if (!Application.isPlaying || GBibleRuntime.IsHeadless) return false;
            var go = new GameObject("[MomoiProject] 화면");
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            _document = go.AddComponent<UIDocument>();
            _document.panelSettings = _settings != null && _settings.panelSettings != null ? _settings.panelSettings : FallbackPanel();
            _document.sortingOrder = 100;
            go.SetActive(true);
            var root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[MomoiProject] 화면: UIDocument의 루트를 만들지 못했습니다(Panel Settings를 확인하세요).");
                return false;
            }
            var basic = Resources.Load<StyleSheet>("MomoiProject/Screens");
            if (basic != null) root.styleSheets.Add(basic);
            if (_settings != null)
                foreach (var sheet in _settings.theme)
                    if (sheet != null) root.styleSheets.Add(sheet);
            if (_settings != null && _settings.fontAsset != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(_settings.fontAsset);   // 굵기 표가 있는 글꼴 에셋이 먼저
            else if (_settings != null && _settings.font != null) root.style.unityFontDefinition = FontDefinition.FromFont(_settings.font);
            root.AddToClassList("momoi-root");
            _layers = new VisualElement[4];
            for (int i = 0; i < _layers.Length; i++)
            {
                var layer = new VisualElement { name = "momoi-layer-" + ((ScreenLayer)i).ToString().ToLowerInvariant(), pickingMode = PickingMode.Ignore };
                Fill(layer);
                root.Add(layer);
                _layers[i] = layer;
            }
            _toasts = new VisualElement { name = "momoi-toasts", pickingMode = PickingMode.Ignore };
            _toasts.AddToClassList("momoi-toasts");
            _layers[(int)ScreenLayer.Toast].Add(_toasts);
            root.RegisterCallback<NavigationCancelEvent>(_ => Back(), TrickleDown.TrickleDown);
            return true;
        }

        static PanelSettings FallbackPanel()
        {
            Debug.LogWarning("[MomoiProject] DB › 화면 설정의 Panel Settings가 없어 테마 없는 임시 설정으로 화면을 띄웁니다. 에디터를 다시 열면 저절로 만들어집니다(ScreenSetup.Ensure).");
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.match = 0.5f;
            return panel;
        }

        static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) ShowForScene(scene.name);
        }

        // '열 씬'이 있는 화면: 그 씬이면 열고, 아니면 닫는다.
        static void ShowForScene(string scene)
        {
            foreach (var screen in Data.Values.Where(s => s.scenes.Count > 0).ToList())
            {
                if (screen.scenes.Contains(scene)) Open(screen.Id);
                else Close(screen.Id);
            }
        }

        static GameScreen MakeController(string id)
        {
            var type = BoundOutcomes.ClassFor(id, typeof(GameScreen));
            foreach (var other in BoundOutcomes.For(id).Where(c => c.Method == null && !c.IsOutcome && !typeof(GameScreen).IsAssignableFrom(c.Type)))
                Debug.LogWarning($"[MomoiProject] [BindTo(\"{id}\")] '{other.Type.FullName}': 화면에 잇는 클래스는 GameScreen을 상속해야 합니다.");
            if (type == null) return BuiltIn(id) ?? new PlainScreen();
            try { return (GameScreen)Activator.CreateInstance(type); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new PlainScreen();
            }
        }

        static readonly string[] BuiltInIds = { Choice, Confirmation, Settings, Slots, Pause };

        static GameScreen BuiltIn(string id)
        {
            switch (id)
            {
                case Choice: return new ChoiceWindow();
                case Confirmation: return new ConfirmWindow();
                case Settings: return new SettingsWindow();
                case Slots: return new SlotsWindow();
                case Pause: return new PauseWindow();
                default: return null;
            }
        }

        // 기본 화면의 뒷배경(개발자 2026-10-13, UiGuide 3-1): 확인 = 블러(알림성) · 고르기 · 일시정지 = 일반 · 설정 · 세이브 슬롯 = UI 배경(나눈 화면 — ScreenUi.Split).
        static (ScreenLayer layer, bool closeOnBack, bool pause, bool block, ScreenBackdrop backdrop)? BuiltInSpec(string id)
        {
            switch (id)
            {
                case Choice: return (ScreenLayer.Popup, false, _settings != null && _settings.pauseWhileChoosing, true, ScreenBackdrop.Plain);
                case Confirmation: return (ScreenLayer.Popup, true, false, true, ScreenBackdrop.Blur);
                case Settings: return (ScreenLayer.Window, true, false, true, ScreenBackdrop.Ui);
                case Slots: return (ScreenLayer.Window, true, false, true, ScreenBackdrop.Ui);
                case Pause: return (ScreenLayer.Window, true, true, true, ScreenBackdrop.Plain);
                default: return null;
            }
        }
    }
}
