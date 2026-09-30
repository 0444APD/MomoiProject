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

        public bool IsPlaying => _runner != null && _runner.Phase != DialoguePhase.Idle && _runner.Phase != DialoguePhase.Ended;

        public event Action<string> Signaled;

        void Awake() => EnsureRunner();

        void OnEnable()
        {
            if (stage != null && stage.ui != null)
            {
                stage.ui.AdvanceRequested += Advance;
                stage.ui.ChoiceRequested += OnChoice;
            }
        }

        void OnDisable()
        {
            if (stage != null && stage.ui != null)
            {
                stage.ui.AdvanceRequested -= Advance;
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
                var characters = database == null ? null : new System.Collections.Generic.Dictionary<string, CharacterData>();
                if (database != null)
                    foreach (var character in database.All<CharacterData>())
                        if (character != null && !string.IsNullOrEmpty(character.Id)) characters[character.Id] = character;
                stage.Characters = id => id != null && characters != null && characters.TryGetValue(id, out var c) ? c : null;
            }
            if (stage != null) stage.TextArguments = () => _runner.TextArguments();
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

        public void Advance()
        {
            if (IsPlaying) _runner.Advance();
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

        void Update()
        {
            if (IsPlaying) _runner.Tick(Time.unscaledDeltaTime);
        }
    }
}
