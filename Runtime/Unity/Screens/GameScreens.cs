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
            public readonly List<(VisualElement element, ScreenBinding binding, string template)> Bindings = new List<(VisualElement, ScreenBinding, string)>();
        }

        static readonly Dictionary<string, ScreenData> Data = new Dictionary<string, ScreenData>(StringComparer.Ordinal);
        static readonly Dictionary<string, GameScreen> Controllers = new Dictionary<string, GameScreen>(StringComparer.Ordinal);
        static readonly List<Entry> OpenEntries = new List<Entry>();
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
            Data.Clear();
            Controllers.Clear();
            OpenEntries.Clear();
            Toasts.Clear();
            _settings = null;
            _document = null;
            _layers = null;
            _toasts = null;
            _initialized = false;
            _backFrame = -1;
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
            if (block)
            {
                // 아래 층을 누르지 못하게 덮는다(팝업은 어둡게).
                var backdrop = new VisualElement();
                backdrop.AddToClassList(layer == ScreenLayer.Popup ? "momoi-backdrop" : "momoi-backdrop--clear");
                Fill(backdrop);
                host.Add(backdrop);
            }
            host.Add(tree);
            _layers[(int)layer].Add(host);

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
            // 다시 그릴 키: 코드가 정한 것, 없으면 binding-path의 키들(그것도 없으면 어느 값이 바뀌어도).
            var keys = screen.Watch ?? entry.Bindings.Where(b => b.binding.HasData).Select(b => b.binding.Key).Distinct(StringComparer.Ordinal).ToArray();
            entry.Watch = GameView.Redraw(() => DrawEntry(entry), screen.WaitForPresentation, keys);
            Opened?.Invoke(screen);
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

        // 맨 위의 창·팝업을 닫는다(Esc·뒤로). 그 화면이 '뒤로로 닫기'가 꺼져 있으면 아무것도 하지 않는다(고르기 창 등).
        // 닫을 화면이 없으면 DB › 화면 설정 'Esc로 일시정지 메뉴'일 때 일시정지 메뉴를 연다. 무엇이든 했으면 참.
        public static bool Back()
        {
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
            var label = new Label(ScreenUi.Text(text));
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
        // 프로그래머 실수(InvalidOperationException 등)는 그대로 던진다. 행동을 했으면 참.
        public static bool Do(Func<GameAction> make)
        {
            var game = GBibleRuntime.Game;
            if (game == null || make == null || GameView.Busy) return false;
            try
            {
                game.Do(make());
                return true;
            }
            catch (GameFailure failure)
            {
                if (_settings == null || _settings.failureToast) Toast(failure.Message);
                else Debug.LogWarning("[MomoiProject] " + failure.Message);
                return false;
            }
        }

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
                if (waiting && !open) Open(Choice);
                else if (!waiting && open) Close(Choice);
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
#endif
        }

        // ── 안쪽 ────────────────────────────────────────────────

        static Entry EntryOf(string id) => id == null ? null : OpenEntries.FirstOrDefault(e => e.Screen.Id == id);

        static void CloseEntry(Entry entry)
        {
            if (!OpenEntries.Remove(entry)) return;
            entry.Watch?.Dispose();
            var screen = entry.Screen;
            try { screen.OnClose(); }
            catch (Exception e) { Debug.LogException(e); }
            foreach (var owned in screen.Owned)
            {
                try { owned.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            screen.Owned.Clear();
            foreach (var (button, handler) in screen.Clicks) button.clicked -= handler;
            screen.Clicks.Clear();
            entry.Host.RemoveFromHierarchy();
            if (entry.PauseTime) GameTime.Resume("화면 " + screen.Id);
            screen.IsOpen = false;
            screen.Root = null;
            screen.Argument = null;
            screen.Data = null;
            Closed?.Invoke(screen);
        }

        static void DrawEntry(Entry entry)
        {
            if (!entry.Screen.IsOpen) return;
            var session = GBibleRuntime.Session;
            foreach (var (element, binding, template) in entry.Bindings)
            {
                try { Apply(element, binding, template, session); }
                catch (Exception e) { Debug.LogWarning($"[MomoiProject] 화면 '{entry.Screen.Id}'의 binding-path '{binding}': {e.Message}"); }
            }
            try { entry.Screen.Draw(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // binding-path가 있는 칸을 모은다(열 때 한 번). 원래 글자는 틀로 쓰려고 남겨 둔다.
        static void CollectBindings(Entry entry, VisualElement tree)
        {
            tree.Query<BindableElement>().ForEach(element =>
            {
                var binding = ScreenBinding.Parse(element.bindingPath);
                if (binding == null) return;
                var template = element is TextElement text ? text.text : element is AbstractProgressBar bar ? bar.title : null;
                entry.Bindings.Add((element, binding, template));
            });
        }

        // 값을 칸에 넣는다: 글자 칸(Label·Button)은 글, 진행 막대는 값(+글), 토글·슬라이더는 값(보이기만, 바꾸는 입력은 행동으로).
        static void Apply(VisualElement element, ScreenBinding binding, string template, GameSession session)
        {
            switch (element)
            {
                case AbstractProgressBar bar:
                    if (binding.HasData) bar.value = (float)ScreenBinding.Number(binding.Read(session));
                    if (binding.TextKey.Length > 0 || (template != null && template.Contains("{value}"))) bar.title = binding.Text(session, template);
                    return;
                case Toggle toggle:
                    toggle.SetValueWithoutNotify(ScreenBinding.Flag(binding.Read(session)));
                    return;
                case Slider slider:
                    slider.SetValueWithoutNotify((float)ScreenBinding.Number(binding.Read(session)));
                    return;
                case SliderInt slider:
                    slider.SetValueWithoutNotify((int)Math.Round(ScreenBinding.Number(binding.Read(session))));
                    return;
                case TextElement text:
                    text.text = binding.Text(session, template);
                    return;
            }
        }

        static bool EnsureRoot()
        {
            if (_document != null && _layers != null && _document.rootVisualElement != null) return true;
            if (!Application.isPlaying) return false;
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
            if (_settings != null && _settings.font != null) root.style.unityFontDefinition = FontDefinition.FromFont(_settings.font);
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
            Debug.LogWarning("[MomoiProject] DB › 화면 설정의 Panel Settings가 없어 테마 없는 임시 설정으로 화면을 띄웁니다. 메뉴 MomoiProject › 화면 › 화면 준비를 한 번 누르세요.");
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

        static (ScreenLayer layer, bool closeOnBack, bool pause, bool block)? BuiltInSpec(string id)
        {
            switch (id)
            {
                case Choice: return (ScreenLayer.Popup, false, _settings != null && _settings.pauseWhileChoosing, true);
                case Confirmation: return (ScreenLayer.Popup, true, false, true);
                case Settings: return (ScreenLayer.Window, true, false, true);
                case Slots: return (ScreenLayer.Window, true, false, true);
                case Pause: return (ScreenLayer.Window, true, true, true);
                default: return null;
            }
        }
    }
}
