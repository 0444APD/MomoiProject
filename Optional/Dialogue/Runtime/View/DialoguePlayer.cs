using System;
using GameSystems.Data;
using UnityEngine;
using UnityEngine.Events;

namespace GameSystems.Dialogue
{
    // 씬에 두는 대화 재생기: 실행기(DialogueRunner) + 대화 화면(DialogueStage) + 입력 연결.
    //   GetComponent<DialoguePlayer>().Play("shopIntro");
    // 클릭·Space·Enter는 대화 UI가 받는다. 다른 입력(게임패드 등)은 Advance/Choose를 직접 부른다.
    [AddComponentMenu("Game Systems/대화 재생기")]
    public sealed class DialoguePlayer : MonoBehaviour
    {
        public DialogueStage stage;
        [Tooltip("초당 글자 수(대사 노드에 따로 정하지 않았을 때)")] public float typingSpeed = 30f;
        [Tooltip("대화 중 게임 시간을 멈춘다(GameTime.Pause, 대화 연출은 멈추지 않는 시간으로 돈다)")] public bool pauseGame = true;
        public UnityEvent onStarted = new UnityEvent();
        [Tooltip("대화 그래프의 '신호 보내기' 노드")] public UnityEvent<string> onSignal = new UnityEvent<string>();
        public UnityEvent onEnded = new UnityEvent();

        const string PauseReason = "대화";
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

        public event Action<string> Signaled;

        void Awake() => EnsureRunner();

        void OnEnable()
        {
            if (stage != null && stage.ui != null)
            {
                stage.ui.AdvanceRequested += OnAdvance;
                stage.ui.ChoiceRequested += OnChoice;
            }
        }

        void OnDisable()
        {
            if (stage != null && stage.ui != null)
            {
                stage.ui.AdvanceRequested -= OnAdvance;
                stage.ui.ChoiceRequested -= OnChoice;
            }
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
                var characters = database == null ? null : new System.Collections.Generic.Dictionary<string, StandingProfileData>();
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

        public bool Play(string dialogueId, string entryId = "start")
        {
            EnsureRunner();
            _runner.Session = GameSystemsRuntime.Session;
            _runner.DefaultTypingSpeed = typingSpeed;
            if (pauseGame) GameTime.Pause(PauseReason);
            onStarted.Invoke();
            if (_runner.Start(dialogueId, entryId)) return true;
            if (pauseGame) GameTime.Resume(PauseReason);
            return false;
        }

        public bool Resume(DialogueSnapshot snapshot)
        {
            EnsureRunner();
            _runner.Session = GameSystemsRuntime.Session;
            if (pauseGame) GameTime.Pause(PauseReason);
            if (_runner.Resume(snapshot)) return true;
            if (pauseGame) GameTime.Resume(PauseReason);
            return false;
        }

        public bool Advance() => IsPlaying && !Paused && _runner.Advance();

        // 다음 선택지 직전까지 건너뛴다.
        public void SkipToChoice()
        {
            if (IsPlaying) _runner.SkipToChoice();
        }

        // ── 미리보기(그래프 창) ──────────────────────────────

        // 그래프의 노드에서 시작해 time초 지점으로 간다(그 사이에 시작된 효과는 모두 그 시점의 모습으로).
        public bool PlayFrom(DialogueData dialogue, string nodeGuid, float time = 0f)
        {
            EnsureRunner();
            _previewDialogue = dialogue;
            _previewNode = nodeGuid;
            _previewPreset = null;
            return Restart(time);
        }

        public bool PlayPreset(string presetId, float time = 0f)
        {
            EnsureRunner();
            _previewDialogue = null;
            _previewNode = null;
            _previewPreset = presetId;
            return Restart(time);
        }

        // 미리보기 시간 이동: 처음부터 다시 시작해 time까지 클릭 없이 감는다.
        public bool Seek(float time) => (_previewDialogue != null || _previewPreset != null) && Restart(time);

        bool Restart(float time)
        {
            _runner.Session = GameSystemsRuntime.Session;
            _runner.DefaultTypingSpeed = typingSpeed;
            var started = _previewPreset != null ? _runner.StartPreset(_previewPreset) : _runner.StartAt(_previewDialogue, _previewNode);
            if (!started) return false;
            if (time > 0) _runner.Simulate(time, autoClick: true, clickDelay: previewClickDelay);
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

        void Update()
        {
            if (_runner == null) return;
            _runner.AutoMode = autoMode;
            _runner.AutoDelay = autoDelay;
            if (IsPlaying && !Paused) _runner.Tick(Time.unscaledDeltaTime);
        }
    }
}
