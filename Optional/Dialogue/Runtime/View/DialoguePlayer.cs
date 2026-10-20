using System;
using System.Collections.Generic;
using GBible.Data;
using UnityEngine;
using UnityEngine.Events;

namespace GBible.Dialogue
{
    // 씬에 두는 다이얼로그 재생기: 실행기(DialogueRunner) + 다이얼로그 화면(DialogueStage) + 입력 연결.
    //   GetComponent<DialoguePlayer>().Play("shopIntro");
    // 클릭·Space·Enter는 다이얼로그 UI가 받는다. 다른 입력(게임패드 등)은 Advance/Choose를 직접 부른다.
    [AddComponentMenu("MomoiProject/다이얼로그 재생기")]
    public sealed class DialoguePlayer : MonoBehaviour
    {
        public DialogueStage stage;
        [Tooltip("초당 글자 수(대사 노드에 따로 정하지 않았을 때)")] public float typingSpeed = 30f;
        [Tooltip("다이얼로그 중 게임 시간을 멈춘다(GameTime.Pause). 다이얼로그 연출과 다이얼로그가 만든 스탠딩·이펙트는 멈추지 않는 시간으로 돈다")] public bool pauseGame = true;
        public UnityEvent onStarted = new UnityEvent();
        [Tooltip("다이얼로그 그래프의 '신호 보내기' 노드")] public UnityEvent<string> onSignal = new UnityEvent<string>();
        public UnityEvent onEnded = new UnityEvent();
        [Tooltip("다이얼로그 진행을 판 저장(run.json)에 함께 남긴다. 불러온 뒤 Dialogues.ResumeSaved()로 이어 간다.")]
        public bool saveProgress = true;

        // 판 저장에 들어가는 진행 기록 키(세션 구역, JSON 글). 멀티에서는 플레이어마다 '<키>:<번호>'(DialogueProgressKeys).
        public const string ProgressKey = "dialogue.progress";
        const string PauseReason = "다이얼로그";
        DialogueRunner _runner;

        public DialogueRunner Runner
        {
            get
            {
                EnsureRunner();
                return _runner;
            }
        }

        [Tooltip("자동 진행: '클릭' 대사가 다 나오면 잠시 뒤 스스로 넘어간다")] public bool autoMode;
        [Tooltip("자동 진행에서 넘어가기까지 기다리는 시간(초)")] public float autoDelay = 1.2f;
        [Tooltip("미리보기 시간 이동에서 클릭 대기 대사를 넘기기까지의 시간(초). 이동 중에는 클릭 대사도 이만큼 뒤 넘어간 것으로 감는다")] public float previewClickDelay;

        public bool IsPlaying => _runner != null && _runner.IsActive;
        // 일시정지: 시간이 흐르지 않는다(효과·타이핑·자동 넘김 모두 멈춤).
        public bool Paused { get; set; }

        // 미리보기: 마지막으로 시작한 위치(시간 이동 때 여기서 다시 시작해 감는다).
        DialogueData _previewDialogue;
        string _previewNode;
        string _previewPreset;

        // 멀티 클라이언트: 서버가 거절한 행동(값 바꾸기 결과 기다림에 쓴다).
        NetClient _client;
        readonly HashSet<GameAction> _rejected = new HashSet<GameAction>();
        string _lastSentProgress;
        GameSession _lastSentSession;

        public event Action<string> Signaled;

        void Awake() => EnsureRunner();

        void OnEnable()
        {
            if (stage != null && stage.ui != null)
            {
                stage.ui.AdvanceRequested += OnAdvance;
                stage.ui.ChoiceRequested += OnChoice;
                stage.ui.LogSource = () => Runner.Log;
                stage.ui.LogOpened += OnLogOpened;
                stage.ui.LogClosed += OnLogClosed;
                stage.ui.AutoRequested += OnAutoRequested;
                stage.ui.SkipRequested += SkipToChoice;
            }
            GameLanguage.Changed += OnLanguageChanged;
        }

        void OnDisable()
        {
            if (stage != null && stage.ui != null)
            {
                stage.ui.AdvanceRequested -= OnAdvance;
                stage.ui.ChoiceRequested -= OnChoice;
                stage.ui.LogOpened -= OnLogOpened;
                stage.ui.LogClosed -= OnLogClosed;
                stage.ui.AutoRequested -= OnAutoRequested;
                stage.ui.SkipRequested -= SkipToChoice;
            }
            GameLanguage.Changed -= OnLanguageChanged;
            HookClient(null);
            if (IsPlaying) Stop();
        }

        void EnsureRunner()
        {
            if (_runner != null) return;
            var database = GameDatabase.Load();
            _runner = DialogueRunner.FromDatabase(database);
            _runner.Stage = stage;
            _runner.DefaultTypingSpeed = typingSpeed;
            if (stage != null)
            {
                var characters = database == null ? null : new Dictionary<string, StandingProfileData>();
                if (database != null)
                    foreach (var character in database.All<StandingProfileData>())
                        if (character != null && !string.IsNullOrEmpty(character.Id)) characters[character.Id] = character;
                stage.Characters = id => id != null && characters != null && characters.TryGetValue(id, out var c) ? c : null;
            }
            if (stage != null)
            {
                stage.TextArguments = () => _runner.TextArguments();
                stage.CurrentLocation = () => _runner.State.location;
                stage.Locations = id => _runner.Locations(id);
            }
            _runner.LineEntered += _ => stage?.ui?.ShowStaging();
            _runner.ProgressChanged += SaveProgress;
            _runner.HoldAuto = () => GameAudio.IsVoicePlaying;
            // 값 바꾸기가 실패해 대사에 머물 때(골드가 모자람 등): 화면의 실패 알림으로.
            _runner.Failed += GameScreens.ShowFailure;
            // 멀티 클라이언트: 서버로 보낸 값 바꾸기의 결과가 올 때까지 흐름을 멈춘다.
            _runner.ServerResult = ServerResultOf;
            _runner.Signaled += signal =>
            {
                Signaled?.Invoke(signal);
                onSignal.Invoke(signal);
            };
            _runner.Ended += () =>
            {
                if (pauseGame) GameTime.Resume(PauseReason);
                onEnded.Invoke();
            };
        }

        // 시작할 수 없으면(없는 다이얼로그·시작 ID, 시작 흐름의 값 바꾸기 실패) 거짓. onStarted는 시작할 수 있을 때만 부른다.
        public bool Play(string dialogueId, string entryId = "start")
        {
            EnsureRunner();
            _runner.Session = GBibleRuntime.Session;
            _runner.DefaultTypingSpeed = typingSpeed;
            var dialogue = _runner.FindDialogue(dialogueId);
            // 없는 다이얼로그·시작 ID: 실행기가 경고를 남기고 거짓을 돌려준다(게임 시간·onStarted는 건드리지 않는다 — 입력이 막힌 채로 남지 않게).
            if (dialogue?.Entry(entryId) == null) return _runner.Start(dialogueId, entryId);
            // 첫 대사 전 흐름의 값 바꾸기가 실패하면 열지 않는다(실패 글은 알림으로).
            if (!_runner.CanStart(dialogue, entryId)) return false;
            if (pauseGame) GameTime.Pause(PauseReason);
            onStarted.Invoke();
            if (_runner.Start(dialogue, entryId)) return true;
            if (pauseGame) GameTime.Resume(PauseReason);
            onEnded.Invoke();
            return false;
        }

        public bool Resume(DialogueSnapshot snapshot)
        {
            EnsureRunner();
            _runner.Session = GBibleRuntime.Session;
            if (pauseGame) GameTime.Pause(PauseReason);
            if (_runner.Resume(snapshot)) return true;
            if (pauseGame) GameTime.Resume(PauseReason);
            return false;
        }

        public bool Advance() => IsPlaying && !Paused && _runner.Advance();

        // ── 저장 ─────────────────────────────────────────────

        // 저장소에 진행 기록 칸을 만든다(판 저장에 들어간다). 게임이 시작될 때 다이얼로그 관리자가 부른다.
        public static void DefineProgress(GameSession session)
        {
            if (session != null && !session.Data.IsDefined(ProgressKey)) session.Data.Define(ProgressKey, DataZone.Session, "");
        }

        // 혼자 하기: 진행 칸에 행동 밖에서 바로 쓴다(대사마다 Undo 단위가 생기지 않게).
        // 멀티: 행동(DialogueProgressAction)으로 보낸다 — 서버가 플레이어마다 주인만 보는 칸에 남긴다(클라이언트의 행동 밖 변경은 서버 값에 덮인다).
        void SaveProgress()
        {
            var session = GBibleRuntime.Session;
            if (!saveProgress || session == null) return;
            var progress = _runner.Progress;
            if (progress != null) progress.resume = Dialogues.ResumeJson();
            var json = progress == null ? "" : JsonUtility.ToJson(progress);
            if (session.Role == SessionRole.Local)
            {
                DefineProgress(session);
                session.Data.Set(ProgressKey, json);
                return;
            }
            if (json == _lastSentProgress && ReferenceEquals(session, _lastSentSession)) return;
            _lastSentProgress = json;
            _lastSentSession = session;
            try
            {
                session.Do(new DialogueProgressAction { progress = json });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // 진행 중인 다이얼로그 ID(없으면 null).
        public string CurrentDialogue => _runner?.Progress?.dialogue;

        // 지금 대사의 진행 기록(한 채널의 끼어들기가 남긴다). 없으면 null.
        internal DialogueProgress CurrentProgress => IsPlaying ? _runner.Progress : null;

        // 저장된 진행 기록이 있으면 그 대사부터 이어 간다(판을 불러온 뒤 부른다). 이어 가지 못하면 저장된 진행은 그대로 둔다.
        public bool ResumeSaved() => ResumeSaved(out _);

        // resume = 그 기록에 함께 저장된 한 채널의 돌아갈 곳(다이얼로그 관리자가 되살린다).
        public bool ResumeSaved(out string resume)
        {
            resume = "";
            var session = GBibleRuntime.Session;
            var key = DialogueProgressKeys.For(session, session?.LocalPlayer ?? 0);
            if (session == null || !session.Data.IsDefined(key)) return false;
            var json = session.Data.Get<string>(key);
            if (string.IsNullOrEmpty(json)) return false;
            var progress = JsonUtility.FromJson<DialogueProgress>(json);
            resume = progress?.resume ?? "";
            return Restore(progress);
        }

        // 진행 기록으로 그 대사에 다시 들어간다(조용히 같은 길을 밟는다 — 값 바꾸기 · 신호는 다시 하지 않는다). 한 채널이 끼어든 뒤 돌아갈 때도 쓴다.
        public bool Restore(DialogueProgress progress)
        {
            if (progress == null) return false;
            var session = GBibleRuntime.Session;
            EnsureRunner();
            _runner.Session = session;
            _runner.DefaultTypingSpeed = typingSpeed;
            if (pauseGame) GameTime.Pause(PauseReason);
            if (_runner.Restore(progress))
            {
                onStarted.Invoke();
                return true;
            }
            if (pauseGame) GameTime.Resume(PauseReason);
            return false;
        }

        // 다음 선택지 직전까지 건너뛴다.
        public void SkipToChoice()
        {
            if (IsPlaying) _runner.SkipToChoice();
        }

        // ── 미리보기(그래프 창) ──────────────────────────────

        // 그래프의 노드에서 시작해 time초 지점으로 간다(그 사이에 시작된 효과는 모두 그 시점의 모습으로).
        // time > 0이면 그 지점까지는 조용히 감는다(값 바꾸기·신호 없이, 지나간 한 번짜리 연출 없이).
        public bool PlayFrom(DialogueData dialogue, string nodeGuid, float time = 0f)
        {
            EnsureRunner();
            _previewDialogue = dialogue;
            _previewNode = nodeGuid;
            _previewPreset = null;
            return Restart(time, seeking: time > 0f);
        }

        public bool PlayPreset(string presetId, float time = 0f)
        {
            EnsureRunner();
            _previewDialogue = null;
            _previewNode = null;
            _previewPreset = presetId;
            return Restart(time, seeking: time > 0f);
        }

        // 미리보기 시간 이동: 처음부터 다시 시작해 time까지 클릭 없이 조용히 감는다(살아 있는 세션의 값을 다시 바꾸거나 신호를 다시 보내지 않는다).
        public bool Seek(float time) => (_previewDialogue != null || _previewPreset != null) && Restart(time, seeking: true);

        bool Restart(float time, bool seeking)
        {
            _runner.Session = GBibleRuntime.Session;
            _runner.DefaultTypingSpeed = typingSpeed;
            bool started;
            using (seeking ? _runner.Quietly() : null)
            {
                started = _previewPreset != null ? _runner.StartPreset(_previewPreset) : _runner.StartAt(_previewDialogue, _previewNode);
                if (started && time > 0) _runner.Simulate(time, autoClick: true, clickDelay: previewClickDelay);
            }
            if (!started) return false;
            stage?.Sync();
            return true;
        }

        public bool Choose(int index) => IsPlaying && _runner.Choose(index);

        public void FastForward()
        {
            if (IsPlaying) _runner.FastForward();
        }

        public void Stop()
        {
            if (_runner != null && IsPlaying) _runner.Stop();
        }

        void OnChoice(int index) => Choose(index);

        void OnAdvance() => Advance();

        // 언어가 바뀌면 지금 대사·선택지·로그를 새 언어로.
        void OnLanguageChanged()
        {
            if (_runner == null) return;
            if (IsPlaying) _runner.RefreshTexts();
            stage?.ui?.RefreshLog();
        }

        // ── 멀티 ─────────────────────────────────────────────

        // 서버로 보낸 행동의 결과: 거절됐으면 거짓, 아직 기다리는 행동이 있으면 null, 모두 왔으면 참(바뀐 값은 결과보다 먼저 온다).
        bool? ServerResultOf(GameAction action)
        {
            var client = GameNet.Client;
            HookClient(client);
            if (action != null && _rejected.Remove(action))
            {
                _rejected.Clear();
                return false;
            }
            if (client == null || client.PendingActions == 0)
            {
                _rejected.Clear();
                return true;
            }
            return null;
        }

        void HookClient(NetClient client)
        {
            if (ReferenceEquals(client, _client)) return;
            if (_client != null) _client.ActionFailed -= OnActionFailed;
            _client = client;
            if (_client != null) _client.ActionFailed += OnActionFailed;
        }

        void OnActionFailed(NetActionFailure failure)
        {
            if (failure?.Action != null) _rejected.Add(failure.Action);
        }

        // 로그를 보는 동안은 다이얼로그 시간이 흐르지 않는다(자동 진행도 멈춤).
        bool _pausedByLog;

        void OnLogOpened()
        {
            if (Paused) return;
            Paused = true;
            _pausedByLog = true;
        }

        void OnLogClosed()
        {
            if (!_pausedByLog) return;
            _pausedByLog = false;
            Paused = false;
        }

        // UI의 자동 버튼: 자동 진행을 켜고 끈다(다음 Update에서 실행기에 넘어가고, UI에 dialogue--auto가 붙는다).
        void OnAutoRequested() => autoMode = !autoMode;

        void Update()
        {
            if (_runner == null) return;
            // 서버 거절을 놓치지 않게 결과보다 먼저 받는 쪽을 단다.
            HookClient(GameNet.Client);
            _runner.AutoMode = autoMode;
            _runner.AutoDelay = autoDelay;
            if (stage != null && stage.ui != null) stage.ui.SetAuto(autoMode);
            if (IsPlaying && !Paused) _runner.Tick(Time.unscaledDeltaTime);
        }
    }
}
