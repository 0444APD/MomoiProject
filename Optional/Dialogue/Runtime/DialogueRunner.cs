using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible.Dialogue
{
    // 다이얼로그 화면을 실제로 그리는 쪽(UI·스탠딩·카메라 …). 실행기는 '언제 무엇을'만 정하고 그리기는 여기에 맡긴다.
    public interface IDialogueStage
    {
        // 상태를 전환 없이 바로 적용한다(시작·불러오기·되감기·스킵·시간 이동 뒤).
        void ApplyState(StageState state);

        // 효과 하나를 시작한다. before → after는 상태 효과의 앞뒤 모습(큐 효과는 같다). 재생기가 없으면 null(시간만 흐른다).
        IEffectPlayer Begin(EffectNode effect, EffectContext context, StageState before, StageState after);

        // 대사 줄을 띄운다(글자는 SetVisibleCharacters만큼 보인다). line.speaker는 캐릭터 ID(해설이면 빈 값).
        void ShowLine(LineNode line, string speakerName, string text);
        void SetVisibleCharacters(int count);

        // 선택지를 띄운다(빈 목록이면 숨긴다). 모두 Enabled가 거짓이면(조건에 막혀 고를 것이 없음) 클릭으로 넘어가는 줄이다.
        void ShowChoices(IReadOnlyList<DialogueChoice> choices);

        // 다이얼로그가 끝났다.
        void Hide();
    }

    // 화면이 고를 수 있는 것: 지금 줄의 글만 새로 그린다(언어가 바뀌었을 때 — 목소리·줄 시작 모션 없이). 구현하지 않으면 ShowLine을 다시 부른다.
    public interface IDialogueTextRefresh
    {
        void RefreshLine(LineNode line, string speakerName, string text);
    }

    // 효과 하나의 진행을 그린다. t는 0~1(효과의 움직임 곡선을 이미 적용한 값). 스킵하면 바로 Evaluate(1) 뒤 Finish가 불린다.
    public interface IEffectPlayer
    {
        void Evaluate(float t);
        void Finish();
    }

    public readonly struct DialogueChoice
    {
        public readonly int Index;
        public readonly string OptionId;
        public readonly string Text;
        public readonly bool Enabled;

        public DialogueChoice(int index, string optionId, string text, bool enabled)
        {
            Index = index;
            OptionId = optionId;
            Text = text;
            Enabled = enabled;
        }
    }

    public enum DialoguePhase
    {
        Idle,
        // 줄에 들어왔지만 '대사 시작' 신호를 기다린다(연출 중).
        WaitingForText,
        Typing,
        // 다 나왔고 클릭(또는 자동 넘김)을 기다린다. 선택지가 모두 막힌 줄도 여기(클릭하면 '고를 선택지 없음'으로).
        Shown,
        Choosing,
        Ended,
    }

    // 중간 저장·되감기용: 어느 다이얼로그의 어느 줄, 그때의 화면 상태.
    [Serializable]
    public sealed class DialogueSnapshot
    {
        public string dialogue;
        public string node;
        public StageState state;
    }

    // 대사 로그 한 줄.
    public sealed class DialogueLogEntry
    {
        public string Speaker;
        public string SpeakerName;
        public string Text;
        // 그때의 화자 얼굴(로그 얼굴 → 그때 스탠딩 그림 → 기본 스탠딩). 없으면 null.
        public Sprite Portrait;
        public bool Thought;
        // 플레이어가 고른 선택지.
        public bool Choice;
        // 글의 원본(언어가 바뀌면 다시 만든다).
        internal LineNode SourceLine;
        internal ChoiceOption SourceOption;
    }

    // 다이얼로그 진행 기록(저장용): 어디서 시작했고, 분기·선택지·프로젝트 흐름 노드에서 어느 쪽으로 갔고, 대사를 몇 줄 지났는지.
    // 불러올 때는 같은 길을 조용히 다시 밟아(값 바꾸기·신호는 다시 하지 않고, 조건도 다시 보지 않는다) 그때의 화면을 그대로 만든다.
    [Serializable]
    public sealed class DialogueProgress
    {
        public string dialogue;
        public string entry;
        // 일어난 순서대로: 분기 결과("true"/"false"), 고른 선택지 ID, 프로젝트 흐름 노드가 고른 점("flow:점"),
        // 고를 선택지가 없어 '고를 선택지 없음'으로 간 줄("noChoice").
        public List<string> decisions = new List<string>();
        public int lines;
        // 지금 있는 곳: 다이얼로그(다른 다이얼로그로 넘어갔으면 그것)와 대사 노드(대사에 들어가기 전이면 빈 값). 멀티 서버가 값 바꾸기를 확인할 때 쓴다.
        public string at;
        public string node;

        public DialogueProgress Clone() => new DialogueProgress { dialogue = dialogue, entry = entry, decisions = new List<string>(decisions), lines = lines, at = at, node = node };
    }

    // 다이얼로그 실행기. 흐름(대사 → 선택지 → 분기 …)을 따라가며, 신호로 이어진 효과 체인을 시간에 맞춰 재생한다.
    // 게임 쪽은 Tick(프레임 시간), Advance(클릭), Choose(선택)만 부른다. Unity 컴포넌트 없이도 돈다(테스트·미리보기).
    public sealed class DialogueRunner
    {
        const int MaxSignalDepth = 64;
        const int MaxFlowSteps = 1000;
        // 진행 기록의 결정 표시(DialogueProgress.decisions).
        public const string FlowDecisionPrefix = "flow:";
        public const string NoChoiceDecision = "noChoice";

        static readonly HashSet<string> NoLoops = new HashSet<string>();

        sealed class Scope
        {
            public NodeGraph Graph;
            public EffectContext Context;
            public Scope Parent;
            public Run OwnerRun;
            public int Active;
            // 신호 고리에 든 효과(DialogueChecks.LoopingEffects)와 이 범위에서 한 번이라도 끝난 효과.
            public HashSet<string> Looping = NoLoops;
            public readonly HashSet<string> Finished = new HashSet<string>(StringComparer.Ordinal);
        }

        sealed class Run
        {
            public EffectNode Node;
            public Scope Scope;
            public LineNode Line;
            public float Wait;
            public bool Begun;
            public float Elapsed;
            public int FiredMarks;
            public IEffectPlayer Player;
            public Scope Child;
            public bool Done;
            // 고리를 한 바퀴 돌아 다시 시작한 효과: 클릭을 막지 않는다(끝없이 막지 않게).
            public bool Lap;
            // 조용히 다시 밟는 중에 시작해 아직 화면에 내지 않은 효과: 앞뒤 모습을 들고 있다가 끝날 때 그 시점의 모습으로 그린다.
            public bool Deferred;
            public StageState Before;
            public StageState After;
        }

        // 대사를 떠나는 이동 하나. 흐름의 값 바꾸기가 실패하면(또는 서버가 거절하면) 이 모습으로 돌아가 그 대사에 머문다.
        sealed class Move
        {
            public DialogueData Dialogue;
            public Scope Main;
            public LineNode Line;
            public DialoguePhase Phase;
            public List<DialogueChoice> Choices;
            public string Text;
            public int TextLength;
            public float Visible;
            public int Decisions;
            public int Lines;
            public string At;
            public string Node;
            public int Log;
            public int History;
        }

        // 서버로 보낸 값 바꾸기의 결과를 기다리는 중(멀티 클라이언트).
        sealed class ServerWait
        {
            public GameAction Action;
            public SetNode Node;
            public Scope Main;
        }

        sealed class Release : IDisposable
        {
            Action _done;

            public Release(Action done) => _done = done;

            public void Dispose()
            {
                var done = _done;
                _done = null;
                done?.Invoke();
            }
        }

        readonly Func<string, DialogueData> _dialogues;
        readonly Func<string, EffectPresetData> _presets;
        readonly List<Run> _runs = new List<Run>();
        readonly HashSet<string> _released = new HashSet<string>();
        readonly List<DialogueChoice> _choices = new List<DialogueChoice>();
        readonly List<DialogueSnapshot> _history = new List<DialogueSnapshot>();
        readonly List<DialogueLogEntry> _log = new List<DialogueLogEntry>();
        readonly Dictionary<NodeGraph, (int nodes, int edges, HashSet<string> looping)> _loops = new Dictionary<NodeGraph, (int, int, HashSet<string>)>();
        Scope _main;
        int _depth;
        float _visible;
        float _shownTimer;
        float _cooldown;
        DialogueProgress _progress;
        // 불러오기: 저장한 결정과 들어갈 대사 수. 그 대사에 들어오면 보통 재생으로 돌아간다.
        Queue<string> _replay;
        int _replayLines;
        bool _replayMuted;
        // 조용히 다시 밟기(Quietly — 미리보기 시간 이동): 값 바꾸기·신호를 하지 않는다.
        int _quiet;
        // 화면에 내지 않기(불러오기·스킵·시간 이동): 끝날 때 마지막 모습을 한 번 그린다.
        int _mute;
        // 미리보기 재생(StartAt·StartPreset): 진행 기록을 알리지 않는다(판 저장의 진행 칸을 건드리지 않는다).
        bool _previewRun;
        Move _move;
        int _failures;
        ServerWait _wait;
        // 시작해서 아직 끝나지 않음(첫 대사 전 시작 흐름 포함).
        bool _inDialogue;

        public GameSession Session { get; set; }
        public IDialogueStage Stage { get; set; }
        public float DefaultTypingSpeed = 30f;
        // 클릭을 받은 뒤 다음 클릭을 무시하는 시간(연타로 두 줄씩 넘어가지 않게).
        public float ClickCooldown = 0.12f;
        // 자동 진행: '클릭' 대사가 다 나오고 막는 효과가 없으면 AutoDelay초 뒤 클릭한 것으로 친다.
        public bool AutoMode;
        public float AutoDelay = 1.2f;
        // 자동 진행을 붙잡는 조건(예: 목소리가 아직 나오는 중). 참이면 자동 진행의 지연을 다시 잰다.
        public Func<bool> HoldAuto = () => false;
        bool _simulating;
        // 대사마다 화자 자동 강조(대사 노드에서 '그대로'로 끌 수 있다).
        public bool AutoHighlightSpeaker = true;
        // 장소·스탠딩 프로필 찾기(FromDatabase가 채운다).
        public Func<string, LocationData> Locations = _ => null;
        public Func<string, StandingProfileData> Profiles = _ => null;
        // 멀티 클라이언트: 서버로 보낸 행동의 결과(null = 아직, true = 됨, false = 거절). 다이얼로그 재생기가 GameNet으로 채운다.
        // 비우면 기다리지 않고 바로 이어 간다.
        public Func<GameAction, bool?> ServerResult;
        // 시작(StartAt·Start)부터 흐른 시간. 미리보기의 시간 위치.
        public float Time { get; private set; }

        // 클릭을 막는 효과(효과 시간 보장)가 진행 중인지. 신호 고리를 한 바퀴 돌아 다시 시작한 효과는 막지 않는다.
        public bool InputBlocked => _runs.Any(r => !r.Done && r.Node.blockInput && !r.Lap && !(r.Node is PresetEffect));
        // 다이얼로그 진행 중이거나 효과가 돌고 있다(서버 결과를 기다리는 중 포함).
        public bool IsActive => (Phase != DialoguePhase.Idle && Phase != DialoguePhase.Ended) || _runs.Count > 0 || _wait != null;

        public DialogueData Dialogue { get; private set; }
        public LineNode Line { get; private set; }
        public DialoguePhase Phase { get; private set; }
        public StageState State { get; private set; } = new StageState();
        public string Text { get; private set; } = "";
        public int TextLength { get; private set; }
        public int VisibleCharacters => Mathf.Min(TextLength, Mathf.FloorToInt(_visible));
        public IReadOnlyList<DialogueChoice> Choices => _choices;
        public int RunningEffects => _runs.Count;
        // 지나온 줄(그때의 화면 상태 포함). 대사 로그·되감기에 쓴다.
        public IReadOnlyList<DialogueSnapshot> History => _history;
        // 대사 로그(대사가 나올 때, 선택지를 고를 때 한 줄씩).
        public IReadOnlyList<DialogueLogEntry> Log => _log;
        // 길을 조용히 다시 밟는 중(불러오기·미리보기 시간 이동): 값 바꾸기·신호를 하지 않는다.
        public bool Replaying => _replay != null || _quiet > 0;
        // 서버로 보낸 값 바꾸기의 결과를 기다리는 중(멀티 클라이언트): 그동안 흐름이 멈추고 클릭·선택을 받지 않는다.
        public bool WaitingForServer => _wait != null;

        public event Action<LineNode> LineEntered;
        public event Action<string> Signaled;
        public event Action Ended;
        public event Action<EffectNode> EffectStarted;
        // 진행 기록이 바뀌었다(시작·대사에 들어감·끝남). 저장에 쓴다. 미리보기 재생·조용히 다시 밟는 중에는 오지 않는다.
        public event Action ProgressChanged;
        // 값 바꾸기가 실패해(골드가 모자람 등 GameFailure) 대사에 머물 때의 플레이어용 글. 다이얼로그 재생기는 알림으로 보인다.
        // 받는 쪽이 없으면 세션 기록에 경고로 남긴다.
        public event Action<string> Failed;

        // 지금까지의 진행 기록(저장용 사본). 다이얼로그 중이 아니면 null. 시작 흐름(첫 대사 전)에서도 있다.
        public DialogueProgress Progress => (_inDialogue || IsActive) && _progress != null ? _progress.Clone() : null;

        public DialogueRunner(Func<string, DialogueData> dialogues, Func<string, EffectPresetData> presets)
        {
            _dialogues = dialogues ?? (_ => null);
            _presets = presets ?? (_ => null);
        }

        // 게임 데이터베이스에서 다이얼로그·프리셋을 찾는 실행기.
        public static DialogueRunner FromDatabase(GameDatabase database)
        {
            var dialogues = database == null ? new Dictionary<string, DialogueData>() : database.All<DialogueData>().Where(d => d != null).ToDictionary(d => d.Id);
            var presets = database == null ? new Dictionary<string, EffectPresetData>() : database.All<EffectPresetData>().Where(p => p != null).ToDictionary(p => p.Id);
            var locations = database == null ? new Dictionary<string, LocationData>() : database.All<LocationData>().Where(l => l != null).ToDictionary(l => l.Id);
            var profiles = database == null ? new Dictionary<string, StandingProfileData>() : database.All<StandingProfileData>().Where(p => p != null).ToDictionary(p => p.Id);
            return new DialogueRunner(id => id != null && dialogues.TryGetValue(id, out var d) ? d : null,
                id => id != null && presets.TryGetValue(id, out var p) ? p : null)
            {
                Locations = id => id != null && locations.TryGetValue(id, out var l) ? l : null,
                Profiles = id => id != null && profiles.TryGetValue(id, out var p) ? p : null,
            };
        }

        public DialogueData FindDialogue(string id) => _dialogues(id);

        // 시작할 수 있는지: 시작 ID가 있고, 첫 대사 전 흐름의 값 바꾸기가 실패하지 않는지(미리보기로 해 본다 — 아무것도 남지 않는다). 실패 글은 Failed로.
        public bool CanStart(DialogueData dialogue, string entryId = "start")
        {
            var entry = dialogue?.Entry(entryId);
            if (entry == null) return false;
            if (Preflight(dialogue, dialogue.graph.Next(entry, StartNode.NextPort), out var failure)) return true;
            Report(failure);
            return false;
        }

        // ── 시작·끝 ───────────────────────────────────────────

        // 시작 흐름(첫 대사 전)의 값 바꾸기가 실패하면 시작하지 않고 거짓(실패 글은 Failed로).
        public bool Start(DialogueData dialogue, string entryId = "start")
        {
            var entry = dialogue?.Entry(entryId);
            if (entry == null)
            {
                Debug.LogWarning($"[다이얼로그] '{dialogue?.Id}'에 시작 ID '{entryId}'가 없습니다.");
                return false;
            }
            if (!Preflight(dialogue, dialogue.graph.Next(entry, StartNode.NextPort), out var failure))
            {
                Report(failure);
                return false;
            }
            Reset();
            _previewRun = false;
            _inDialogue = true;
            _progress = new DialogueProgress { dialogue = dialogue.Id, entry = entryId, at = dialogue.Id, node = "" };
            ApplyStage();
            Enter(dialogue);
            NotifyProgress();
            Flow(() =>
            {
                Fire(_main, entry, StartNode.StartedPort);
                Continue(dialogue.graph.Next(entry, StartNode.NextPort));
            });
            return true;
        }

        // 저장한 진행 기록에서 이어 간다: 처음부터 같은 길을 조용히 밟아 저장한 대사에 들어간다.
        // 값 바꾸기·신호·조건·프로젝트 노드의 Run을 다시 하지 않고 기록한 결정을 따른다(산 뒤 골드가 줄어 숨은 선택지도 그대로).
        // 지나온 연출은 화면에 내지 않고 끝 모습만 한 번 그린다. 길이 맞지 않아 끝나면 거짓(저장한 진행은 그대로 둔다).
        public bool Restore(DialogueProgress progress)
        {
            var dialogue = _dialogues(progress?.dialogue);
            if (dialogue?.Entry(progress.entry) == null) return false;
            _replay = new Queue<string>(progress.decisions ?? new List<string>());
            _replayLines = Math.Max(1, progress.lines);
            _mute++;
            _replayMuted = true;
            var ok = false;
            try
            {
                Start(dialogue, progress.entry);
                for (var guard = 0; guard < 100000 && _replay != null; guard++)
                {
                    FinishAll(null);
                    if (_replay == null) break;
                    bool moved;
                    switch (Phase)
                    {
                        case DialoguePhase.WaitingForText:
                            BeginTyping();
                            moved = true;
                            break;
                        case DialoguePhase.Typing:
                            Typed();
                            moved = true;
                            break;
                        case DialoguePhase.Shown:
                            moved = TryMove(NextPortOf(Line), Line.options.Count > 0 ? NoChoiceDecision : null);
                            break;
                        case DialoguePhase.Choosing:
                            moved = ReplayChoice();
                            break;
                        default:
                            moved = false;
                            break;
                    }
                    if (!moved) break;
                }
                ok = IsActive && Phase != DialoguePhase.Ended;
            }
            finally
            {
                FinishReplay(ok);
            }
            if (ok) NotifyProgress();
            return ok;
        }

        public bool Start(string dialogueId, string entryId = "start")
        {
            var dialogue = _dialogues(dialogueId);
            if (dialogue != null) return Start(dialogue, entryId);
            Debug.LogWarning($"[다이얼로그] 다이얼로그 '{dialogueId}'가 없습니다(시작 ID '{entryId}').");
            return false;
        }

        // 저장해 둔 줄에서 다시 시작한다(화면 상태를 바로 적용).
        public bool Resume(DialogueSnapshot snapshot)
        {
            var dialogue = _dialogues(snapshot?.dialogue);
            if (!(dialogue?.graph.Find(snapshot.node) is LineNode line)) return false;
            Reset();
            _previewRun = false;
            // 스냅샷에는 지나온 길(분기·선택)이 없어 진행 기록을 만들 수 없다 → 이전 재생의 기록을 이어 쓰지 않는다(판 저장의 진행 칸은 비워진다).
            _progress = null;
            State = snapshot.state?.Clone() ?? new StageState();
            ApplyStage();
            Enter(dialogue);
            Flow(() => EnterLine(line));
            return true;
        }

        public DialogueSnapshot Snapshot() =>
            Dialogue == null || Line == null ? null : new DialogueSnapshot { dialogue = Dialogue.Id, node = Line.guid, state = _history.LastOrDefault()?.state?.Clone() ?? State.Clone() };

        public void Stop()
        {
            if (!IsActive) return;
            End();
        }

        // 그래프의 아무 노드에서 시작한다(미리보기). 시작·대사·흐름 노드는 그 자리부터 흐름을 따르고, 효과 노드는 그 효과 체인만 재생한다.
        // 진행 기록은 남기지 않는다(판 저장의 진행 칸을 건드리지 않는다).
        public bool StartAt(DialogueData dialogue, string nodeGuid)
        {
            var node = dialogue?.graph.Find(nodeGuid);
            if (node == null) return false;
            Reset();
            _previewRun = true;
            _progress = null;
            ApplyStage();
            Enter(dialogue);
            Flow(() =>
            {
                switch (node)
                {
                    case StartNode start:
                        Fire(_main, start, StartNode.StartedPort);
                        Continue(dialogue.graph.Next(start, StartNode.NextPort));
                        break;
                    case EffectNode effect:
                        Play(_main, effect, null);
                        break;
                    default:
                        Continue(node);
                        break;
                }
            });
            return true;
        }

        // 연출 프리셋 하나만 재생한다(프리셋 창 미리보기).
        public bool StartPreset(string presetId)
        {
            if (_presets(presetId) == null) return false;
            Reset();
            _previewRun = true;
            _progress = null;
            ApplyStage();
            Dialogue = null;
            _main = NewScope(new NodeGraph(), NewContext(), null, null);
            Play(_main, new PresetEffect { preset = presetId }, null);
            return true;
        }

        // 조용히 다시 밟기(미리보기 시간 이동): 이 안에서 시작·Simulate하면 값 바꾸기·신호를 하지 않고, 지나간 한 번짜리 연출(효과음·이펙트·
        // 번쩍임·흔들기·UI 모션·프로젝트 효과·목소리)도 화면에 내지 않는다. 끝날 때(Dispose) 마지막 모습 — 상태, 진행 중인 효과의 그 시점, 지금 대사·선택지 — 을 한 번 그린다.
        //   using (runner.Quietly()) { runner.StartAt(dialogue, node); runner.Simulate(time, autoClick: true); }
        public IDisposable Quietly()
        {
            _quiet++;
            _mute++;
            return new Release(() =>
            {
                _quiet--;
                Unmute();
            });
        }

        // 지금부터 seconds만큼 시간을 흘린다(미리보기의 시간 이동: 다시 시작한 뒤 원하는 시점까지 감는다).
        // autoClick: '클릭' 대사도 글이 다 나오고 막는 효과가 끝나면 clickDelay초 뒤 클릭한 것으로 치고 넘어간다(선택지에서는 멈춘다).
        // 감는 동안 시작한 효과는 화면에 내지 않고, 다 감은 뒤 아직 진행 중인 것만 그 시점의 모습으로 그린다(끝난 것은 끝 모습만).
        public void Simulate(float seconds, float step = 1f / 60f, bool autoClick = false, float clickDelay = 0f)
        {
            var auto = AutoMode;
            var delay = AutoDelay;
            _simulating = true;
            _mute++;
            if (autoClick)
            {
                AutoMode = true;
                AutoDelay = clickDelay;
            }
            try
            {
                while (seconds > 1e-5f)
                {
                    var dt = Mathf.Min(step, seconds);
                    Tick(dt);
                    seconds -= dt;
                }
            }
            finally
            {
                AutoMode = auto;
                AutoDelay = delay;
                _simulating = false;
                Unmute();
            }
        }

        // 다음 선택지 직전까지(선택지가 뜬 상태, 또는 다이얼로그 끝까지) 건너뛴다. 막는 효과도 끝 모습으로 보낸다.
        // 지나가는 줄의 한 번짜리 연출·목소리는 내지 않고, 멈춘 뒤 마지막 모습만 그린다. 값 바꾸기·신호는 그대로 한다(게임 규칙).
        public void SkipToChoice()
        {
            _mute++;
            try
            {
                for (var guard = 0; guard < 10000; guard++)
                {
                    FinishAll(null);
                    bool moved;
                    switch (Phase)
                    {
                        case DialoguePhase.WaitingForText:
                            BeginTyping();
                            moved = true;
                            break;
                        case DialoguePhase.Typing:
                            Typed();
                            moved = true;
                            break;
                        case DialoguePhase.Shown:
                            // 값 바꾸기가 실패했거나 서버 결과를 기다리면 거기서 멈춘다.
                            moved = TryMove(NextPortOf(Line), Line.options.Count > 0 ? NoChoiceDecision : null);
                            break;
                        default:
                            moved = false;
                            break;
                    }
                    if (!moved) break;
                }
            }
            finally
            {
                Unmute();
            }
        }

        void Reset()
        {
            foreach (var run in _runs.ToList()) run.Player?.Finish();
            _runs.Clear();
            _released.Clear();
            Time = 0;
            _cooldown = 0;
            _choices.Clear();
            _history.Clear();
            _log.Clear();
            State = new StageState();
            Line = null;
            Text = "";
            TextLength = 0;
            Phase = DialoguePhase.Idle;
            _move = null;
            _wait = null;
            _inDialogue = false;
        }

        void Enter(DialogueData dialogue)
        {
            Dialogue = dialogue;
            _main = NewScope(dialogue.graph, NewContext(), null, null);
        }

        Scope NewScope(NodeGraph graph, EffectContext context, Scope parent, Run owner) =>
            new Scope { Graph = graph, Context = context, Parent = parent, OwnerRun = owner, Looping = LoopingOf(graph) };

        // 그래프의 신호 고리(그래프가 바뀌면 다시 센다).
        HashSet<string> LoopingOf(NodeGraph graph)
        {
            if (graph == null) return NoLoops;
            if (_loops.TryGetValue(graph, out var known) && known.nodes == graph.nodes.Count && known.edges == graph.edges.Count) return known.looping;
            var looping = DialogueChecks.LoopingEffects(graph);
            _loops[graph] = (graph.nodes.Count, graph.edges.Count, looping);
            return looping;
        }

        // 로그 얼굴: 프로필의 로그 얼굴 → 지금 스탠딩 그림(예외 스프라이트·스탠딩 ID) → 기본 스탠딩.
        Sprite PortraitOf(string speaker)
        {
            var profile = Profiles(speaker);
            if (profile == null) return null;
            if (profile.logIcon != null) return profile.logIcon;
            var standing = State.Standing(speaker);
            if (standing != null && standing.sprite != null) return standing.sprite;
            return profile.SpriteOf(standing?.expression ?? profile.defaultExpression);
        }

        EffectContext NewContext() => new EffectContext { Locations = id => Locations(id), Profiles = id => Profiles(id) };

        // 화면에 보일 화자 이름: 다른 이름(번역 키 '<대사 키>.speaker') → 프로필 이름('<프로필 ID>.name') → 해설이면 빈 값.
        public string SpeakerNameOf(LineNode line)
        {
            if (line == null) return "";
            if (!string.IsNullOrEmpty(line.speakerName))
            {
                var key = line.lineId + ".speaker";
                return Texts.Has(key) ? Texts.Get(key, TextArguments()) : Texts.Catalog.FormatTemplate(line.speakerName, TextArguments());
            }
            return string.IsNullOrEmpty(line.speaker) ? "" : Texts.NameOf(line.speaker);
        }

        void End()
        {
            FinishAll(null);
            // 고리를 돌며 다시 시작한 효과까지 멈춘다(끝난 다이얼로그가 활성으로 남지 않게).
            KillRuns(null);
            _choices.Clear();
            _released.Clear();
            Line = null;
            Phase = DialoguePhase.Ended;
            _move = null;
            _wait = null;
            _inDialogue = false;
            HideStage();
            // 불러오는 중에 끝났으면(저장한 길과 그래프가 맞지 않음) 다이얼로그가 보인 적이 없으므로 끝 알림을 내지 않는다(Restore가 거짓).
            if (_replay == null) Ended?.Invoke();
            NotifyProgress();
        }

        // 진행 기록이 바뀌었다고 알린다(미리보기 재생·조용히 다시 밟는 중에는 알리지 않는다).
        void NotifyProgress()
        {
            if (Replaying || _previewRun) return;
            ProgressChanged?.Invoke();
        }

        // ── 흐름 ─────────────────────────────────────────────

        // 이동 밖에서 흐름을 돌릴 때(시작·미리보기): 예상하지 못한 오류(신호를 받은 코드 등)가 나도 반쯤 진행된 채로 남지 않게 끝낸다.
        void Flow(Action flow)
        {
            try
            {
                flow();
            }
            catch (Exception e)
            {
                if (_move == null) FailMove(e);
                else Debug.LogException(e);
            }
        }

        void Continue(GraphNode node)
        {
            for (var steps = 0; steps < MaxFlowSteps; steps++)
            {
                switch (node)
                {
                    case null:
                    case EndNode _:
                        End();
                        return;
                    case LineNode line:
                        EnterLine(line);
                        return;
                    case BranchNode branch:
                    {
                        // 불러오기 중이면 저장할 때 간 쪽으로(지금 값으로 조건을 다시 보지 않는다).
                        bool pass;
                        if (_replay != null && _replay.Count > 0 && (_replay.Peek() == "true" || _replay.Peek() == "false")) pass = _replay.Dequeue() == "true";
                        else pass = branch.condition == null || Session == null || branch.condition.Evaluate(Session);
                        _progress?.decisions.Add(pass ? "true" : "false");
                        node = Dialogue.graph.Next(branch, pass ? BranchNode.TruePort : BranchNode.FalsePort);
                        break;
                    }
                    case SetNode set:
                    {
                        if (Session == null || Replaying)
                        {
                            node = Dialogue.graph.Next(set, SetNode.NextPort);
                            break;
                        }
                        var scope = _main;
                        var error = GrantAsAction(set, out var sent);
                        if (error != null)
                        {
                            // 실패: 바꾼 것은 모두 취소됐다. 떠나기 전 대사로 돌아가 머문다(시작 흐름이면 끝).
                            FailMove(error);
                            return;
                        }
                        // 값 바꾸기('다이얼로그 재생')가 다이얼로그를 새로 열었거나 끝냈으면 그쪽이 흐름을 맡는다.
                        if (!ReferenceEquals(scope, _main) || Phase == DialoguePhase.Ended) return;
                        if (sent != null && ServerResult != null)
                        {
                            // 멀티 클라이언트: 서버가 값을 바꾸고 결과가 온 뒤에 이어 간다(뒤의 조건 분기·{값}이 바뀐 값을 읽고, 거절되면 대사로 돌아간다).
                            _wait = new ServerWait { Action = sent, Node = set, Main = _main };
                            return;
                        }
                        node = Dialogue.graph.Next(set, SetNode.NextPort);
                        break;
                    }
                    case SignalNode signal:
                    {
                        var scope = _main;
                        if (!Replaying) Signaled?.Invoke(signal.signal);
                        // 신호를 받은 쪽이 다른 다이얼로그를 열었거나 멈췄으면 여기서 끝.
                        if (!ReferenceEquals(scope, _main) || Phase == DialoguePhase.Ended) return;
                        node = Dialogue.graph.Next(signal, SignalNode.NextPort);
                        break;
                    }
                    case JumpNode jump:
                    {
                        var target = string.IsNullOrEmpty(jump.dialogue) ? Dialogue : _dialogues(jump.dialogue);
                        var entry = target?.Entry(jump.entryId);
                        if (entry == null)
                        {
                            Debug.LogWarning($"[다이얼로그] 이동할 곳이 없습니다: {jump.dialogue} · {jump.entryId}");
                            End();
                            return;
                        }
                        if (target != Dialogue)
                        {
                            FinishAll(null);
                            KillRuns(null);
                            Enter(target);
                        }
                        if (_progress != null)
                        {
                            _progress.at = target.Id;
                            _progress.node = "";
                        }
                        Fire(_main, entry, StartNode.StartedPort);
                        node = target.graph.Next(entry, StartNode.NextPort);
                        break;
                    }
                    case CustomFlowNode custom:
                    {
                        string port;
                        if (_replay != null && _replay.Count > 0 && _replay.Peek().StartsWith(FlowDecisionPrefix, StringComparison.Ordinal))
                        {
                            // 불러오기: 저장할 때 고른 점으로(Run을 다시 부르지 않는다 — 값이 달라져 다른 점을 고르거나 부수 효과를 다시 하지 않게).
                            var recorded = _replay.Dequeue().Substring(FlowDecisionPrefix.Length);
                            port = recorded.Length == 0 ? null : recorded;
                        }
                        else
                        {
                            var scope = _main;
                            port = custom.Run(this);
                            if (!ReferenceEquals(scope, _main) || Phase == DialoguePhase.Ended) return;
                        }
                        _progress?.decisions.Add(FlowDecisionPrefix + (port ?? ""));
                        node = port == null ? null : Dialogue.graph.Next(custom, port);
                        break;
                    }
                    default:
                        Debug.LogWarning($"[다이얼로그] 흐름에서 처리할 수 없는 노드: {node.GetType().Name}");
                        End();
                        return;
                }
            }
            Debug.LogWarning("[다이얼로그] 흐름이 너무 길게 이어집니다(대사 없이 도는 고리가 있는지 확인하세요).");
            End();
        }

        // '값 바꾸기' 노드: 행동 하나로 실행한다(Undo 한 단위·변경 한 묶음). 이미 행동 안이면(프로젝트 코드가 행동 안에서 연 다이얼로그) 그 자리에서.
        // 실패하면 그 예외를 돌려준다(바꾼 것은 모두 취소된다). sent: 멀티 클라이언트가 서버로 보낸 행동(결과는 나중에 온다).
        Exception GrantAsAction(SetNode set, out GameAction sent)
        {
            sent = null;
            if (Session.Data.InTransaction)
            {
                var point = Session.Data.Mark();
                try
                {
                    foreach (var reward in set.rewards)
                        reward?.Grant(Session);
                    return null;
                }
                catch (Exception e)
                {
                    Session.Data.RollbackTo(point);
                    return e;
                }
            }
            var action = new DialogueSetAction { dialogue = Dialogue?.Id, node = set.guid, Rewards = set.rewards };
            try
            {
                var record = Session.Do(action);
                if (record != null && record.Status == ActionStatus.Sent) sent = action;
                else if (record != null && record.Status == ActionStatus.Failed)
                    return new GameFailure("momoi.dialogue.notSent", null, record.Error ?? "값 바꾸기를 보내지 못했습니다.");
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        void EnterLine(LineNode line)
        {
            // '대사 시작' 신호가 이 줄에 들어오기 전에 먼저 왔으면 바로 말한다. 다른 줄 앞으로 온 신호는 여기서 버린다(다시 찾은 줄이 기다림을 건너뛰지 않게).
            var preReleased = _released.Remove(line.guid);
            _released.Clear();
            Line = line;
            _main.Context.Speaker = line.speaker;
            _choices.Clear();
            ShowChoicesOnStage();
            Text = ResolveText(line.lineId, line.text);
            TextLength = VisibleLength(Text);
            _visible = 0;
            _shownTimer = 0;
            Phase = DialoguePhase.WaitingForText;
            _history.Add(new DialogueSnapshot { dialogue = Dialogue.Id, node = line.guid, state = State.Clone() });
            if (_progress != null)
            {
                _progress.lines++;
                _progress.at = Dialogue.Id;
                _progress.node = line.guid;
            }
            // 불러오기: 저장한 대사에 들어왔다 → 여기서부터 보통 재생(지나온 연출의 끝 모습을 한 번 그리고, 이 줄의 연출은 그대로 재생).
            if (_replay != null && _progress != null && _progress.lines >= _replayLines) FinishReplay(true);

            // 줄에 적은 화자 스탠딩(ID 또는 예외 스프라이트)은 짧은 스탠딩 바꾸기 효과로 바꾼다.
            if ((!string.IsNullOrEmpty(line.expression) || line.expressionSprite != null) && !string.IsNullOrEmpty(line.speaker))
                Play(_main, new ExpressionEffect { character = line.speaker, expression = line.expression, sprite = line.expressionSprite, duration = 0.15f }, null);

            LineEntered?.Invoke(line);
            Fire(_main, line, LineNode.EnterPort);
            if (Line != line) return;
            // 화자 자동 강조(진입 신호로 등장한 캐릭터까지 본 뒤).
            if (AutoHighlightSpeaker && line.highlight == SpeakerHighlight.Auto && SpeakerHighlightEffect.Changes(State, line.speaker))
                Play(_main, new SpeakerHighlightEffect { speaker = line.speaker }, line);
            var gated = Dialogue.graph.To(line.guid, LineNode.TextStartPort).Any();
            if (!gated || preReleased) BeginTyping();
            NotifyProgress();
        }

        void BeginTyping()
        {
            if (Phase != DialoguePhase.WaitingForText) return;
            Phase = DialoguePhase.Typing;
            var speakerName = SpeakerNameOf(Line);
            _log.Add(new DialogueLogEntry { Speaker = Line.speaker, SpeakerName = speakerName, Text = Text, Portrait = PortraitOf(Line.speaker), Thought = Line.thought, SourceLine = Line });
            ShowLineOnStage(speakerName);
            SetVisibleOnStage(0);
            if (TextLength == 0) Typed();
        }

        void Typed()
        {
            _visible = TextLength;
            SetVisibleOnStage(TextLength);
            var line = Line;
            if (line.options.Count > 0)
            {
                BuildChoices();
                // 고를 수 있는 선택지가 없으면(모두 조건에 막힘) 선택지 없는 줄처럼 클릭으로 '고를 선택지 없음' 점으로 간다(멈춤 방지).
                Phase = _choices.Any(c => c.Enabled) ? DialoguePhase.Choosing : DialoguePhase.Shown;
            }
            else Phase = DialoguePhase.Shown;
            Fire(_main, line, LineNode.TypedPort);
        }

        void BuildChoices()
        {
            _choices.Clear();
            for (var i = 0; i < Line.options.Count; i++)
            {
                var option = Line.options[i];
                // 불러오는 중에는 조건을 보지 않는다(저장할 때 고른 것을 그대로 따른다).
                var open = _replay != null || option.condition == null || Session == null || option.condition.Evaluate(Session);
                if (!open && !option.showWhenLocked) continue;
                _choices.Add(new DialogueChoice(i, option.id, ResolveText(option.lineId, option.text), open));
            }
            ShowChoicesOnStage();
        }

        // 넘길 때의 점: 선택지가 있는 줄(고를 것이 없을 때)은 '고를 선택지 없음', 아니면 '다음'.
        static string NextPortOf(LineNode line) => line.options.Count > 0 ? LineNode.NoChoicePort : LineNode.NextPort;

        // 대사를 떠나 port 쪽으로 간다(클릭·선택·시간 진행·스킵·불러오기가 함께 쓴다). 흐름의 값 바꾸기가 실패하면 그 대사에 머물고 거짓.
        // decision·logEntry(고른 선택지)는 이동이 끝까지 됐을 때만 남는다(실패하면 되돌린다). 서버 결과를 기다리게 되면 참(이동 중).
        bool TryMove(string port, string decision = null, DialogueLogEntry logEntry = null)
        {
            var line = Line;
            if (line == null || _wait != null || Phase == DialoguePhase.Ended) return false;
            // 먼저 해 본다: 다음 대사까지의 값 바꾸기가 실패하면 아무것도 하지 않고 머문다(떠나는 연출도 내지 않는다).
            if (!Preflight(Dialogue, Dialogue.graph.Next(line, port), out var failure))
            {
                Report(failure);
                return false;
            }
            var move = new Move
            {
                Dialogue = Dialogue, Main = _main, Line = line, Phase = Phase, Choices = new List<DialogueChoice>(_choices),
                Text = Text, TextLength = TextLength, Visible = _visible,
                Decisions = _progress?.decisions.Count ?? 0, Lines = _progress?.lines ?? 0, At = _progress?.at, Node = _progress?.node,
                Log = _log.Count, History = _history.Count,
            };
            var failures = _failures;
            _move = move;
            try
            {
                if (decision != null) _progress?.decisions.Add(decision);
                if (logEntry != null) _log.Add(logEntry);
                Leave(port);
            }
            catch (Exception e)
            {
                // 흐름 중 예상하지 못한 오류(신호를 받은 코드·프로젝트 효과 등): 그 대사로 돌아간다.
                if (ReferenceEquals(_move, move)) FailMove(e);
                else Debug.LogException(e);
                return false;
            }
            if (ReferenceEquals(_move, move) && _wait == null) _move = null;
            return _failures == failures;
        }

        // 이동 중 값 바꾸기가 실패했다(또는 서버가 거절했다): 떠나기 전 대사로 돌아가 머문다. 돌아갈 대사가 없으면(시작 흐름) 끝낸다.
        void FailMove(Exception error)
        {
            _failures++;
            Report(error);
            var move = _move;
            _move = null;
            _wait = null;
            if (move?.Line == null)
            {
                End();
                return;
            }
            var otherLine = Line != move.Line;
            Dialogue = move.Dialogue;
            _main = move.Main;
            Line = move.Line;
            Phase = move.Phase;
            Text = move.Text;
            TextLength = move.TextLength;
            _visible = move.Visible;
            _shownTimer = 0;
            _choices.Clear();
            _choices.AddRange(move.Choices);
            if (_progress != null)
            {
                if (_progress.decisions.Count > move.Decisions) _progress.decisions.RemoveRange(move.Decisions, _progress.decisions.Count - move.Decisions);
                _progress.lines = move.Lines;
                _progress.at = move.At;
                _progress.node = move.Node;
            }
            if (_log.Count > move.Log) _log.RemoveRange(move.Log, _log.Count - move.Log);
            if (_history.Count > move.History) _history.RemoveRange(move.History, _history.Count - move.History);
            _main.Context.Speaker = Line.speaker;
            // 대사 글은 화면에 그대로 남아 있다(떠나며 지운 선택지만 다시 보인다). 다른 줄에 들어간 뒤의 오류면 줄도 다시 띄운다.
            if (otherLine)
            {
                ShowLineOnStage(SpeakerNameOf(Line));
                SetVisibleOnStage(VisibleCharacters);
                NotifyProgress();
            }
            ShowChoicesOnStage();
        }

        // 플레이어용 실패(GameFailure)는 Failed로(받는 쪽이 없으면 기록에 경고), 그 밖의 오류는 콘솔에 남긴다.
        void Report(Exception error)
        {
            if (error == null) return;
            if (error is GameFailure failure)
            {
                if (Failed != null) Failed(failure.Message);
                else Session?.Journal.Append(JournalKind.Warning, "Dialogue", "다이얼로그 값 바꾸기 실패: " + failure.Message);
                return;
            }
            Debug.LogException(error);
            Session?.Journal.Append(JournalKind.Error, "Dialogue", "다이얼로그 흐름 오류: " + error.Message);
        }

        // 떠나기 전에: 그 점에서 다음 대사(끝·프로젝트 노드)까지의 값 바꾸기를 미리보기로 해 본다(아무것도 남지 않는다). 실패하면 그 예외.
        bool Preflight(DialogueData dialogue, GraphNode start, out Exception failure)
        {
            failure = null;
            if (Session == null || Replaying || dialogue == null || Session.Data.InTransaction || !ReachesSet(dialogue, start)) return true;
            try
            {
                Session.Actions.Preview<object>(new DialogueProbeAction { Walk = () => Probe(dialogue, start) }, null);
                return true;
            }
            catch (Exception e)
            {
                failure = e;
                return false;
            }
        }

        // 다음 대사까지 '값 바꾸기'가 있을 수 있는지(조건은 보지 않고 모든 갈래). 없으면 미리보기를 하지 않는다.
        bool ReachesSet(DialogueData dialogue, GraphNode start)
        {
            var stack = new Stack<(DialogueData dialogue, GraphNode node)>();
            stack.Push((dialogue, start));
            var seen = new HashSet<GraphNode>();
            while (stack.Count > 0)
            {
                var (d, node) = stack.Pop();
                if (node == null || !seen.Add(node)) continue;
                switch (node)
                {
                    case SetNode _:
                        return true;
                    case BranchNode branch:
                        stack.Push((d, d.graph.Next(branch, BranchNode.TruePort)));
                        stack.Push((d, d.graph.Next(branch, BranchNode.FalsePort)));
                        break;
                    case SignalNode signal:
                        stack.Push((d, d.graph.Next(signal, SignalNode.NextPort)));
                        break;
                    case JumpNode jump:
                        var target = string.IsNullOrEmpty(jump.dialogue) ? d : _dialogues(jump.dialogue);
                        var entry = target?.Entry(jump.entryId);
                        if (entry != null) stack.Push((target, target.graph.Next(entry, StartNode.NextPort)));
                        break;
                }
            }
            return false;
        }

        // 미리보기 안에서 실제와 같은 길을 밟으며 값 바꾸기를 한다(신호는 보내지 않고, 프로젝트 노드에서는 멈춘다).
        void Probe(DialogueData dialogue, GraphNode node)
        {
            var graph = dialogue.graph;
            for (var steps = 0; node != null && steps < MaxFlowSteps; steps++)
            {
                switch (node)
                {
                    case BranchNode branch:
                        var pass = branch.condition == null || branch.condition.Evaluate(Session);
                        node = graph.Next(branch, pass ? BranchNode.TruePort : BranchNode.FalsePort);
                        break;
                    case SetNode set:
                        foreach (var reward in set.rewards)
                            reward?.Grant(Session);
                        node = graph.Next(set, SetNode.NextPort);
                        break;
                    case SignalNode signal:
                        node = graph.Next(signal, SignalNode.NextPort);
                        break;
                    case JumpNode jump:
                        var target = string.IsNullOrEmpty(jump.dialogue) ? dialogue : _dialogues(jump.dialogue);
                        var entry = target?.Entry(jump.entryId);
                        if (entry == null) return;
                        dialogue = target;
                        graph = target.graph;
                        node = graph.Next(entry, StartNode.NextPort);
                        break;
                    default:
                        return;
                }
            }
        }

        void Leave(string port)
        {
            var line = Line;
            Fire(_main, line, LineNode.LeavePort);
            // 이 줄에서 시작한 효과는 (계속 설정이 아니면) 끝 모습으로 마무리한다. 고리를 돌아 다시 시작한 것도 여기서 멈춘다.
            Func<Run, bool> ofLine = run => run.Line == line && !run.Node.keepAcrossLines;
            FinishAll(ofLine);
            KillRuns(ofLine);
            _choices.Clear();
            ShowChoicesOnStage();
            if (Line == line) Continue(Dialogue.graph.Next(line, port));
        }

        // 불러오기: 저장할 때 고른 것을 따른다(고를 선택지가 없어 넘어간 줄이면 그 점으로).
        bool ReplayChoice()
        {
            if (_replay == null || _replay.Count == 0) return false;
            var decision = _replay.Peek();
            if (decision == NoChoiceDecision)
            {
                _replay.Dequeue();
                return TryMove(LineNode.NoChoicePort, NoChoiceDecision);
            }
            if (Line.options.All(o => o.id != decision)) return false;
            _replay.Dequeue();
            return PickOption(decision);
        }

        bool PickOption(string optionId)
        {
            var option = Line.options.FirstOrDefault(o => o.id == optionId);
            var picked = _choices.FirstOrDefault(c => c.OptionId == optionId);
            var text = picked.Text ?? (option != null ? ResolveText(option.lineId, option.text) : "");
            return TryMove(LineNode.OptionPrefix + optionId, optionId, new DialogueLogEntry { Text = text, Choice = true, SourceOption = option });
        }

        // 불러오기를 마친다: 저장한 대사에 들어왔거나 더 따라갈 수 없을 때. 성공이면 마지막 모습을 한 번 그리고, 실패면 화면을 감춘다.
        void FinishReplay(bool ok)
        {
            var replaying = _replay != null;
            _replay = null;
            // 따라갈 결정이 끝나 선택지에서 멈췄으면 지금 값으로 다시 만든다(불러오는 동안은 조건을 보지 않고 모두 열어 두었다).
            if (replaying && Phase == DialoguePhase.Choosing && Line != null)
            {
                BuildChoices();
                if (!_choices.Any(c => c.Enabled)) Phase = DialoguePhase.Shown;
            }
            if (!_replayMuted) return;
            _replayMuted = false;
            if (ok)
            {
                Unmute();
                return;
            }
            if (_mute > 0 && --_mute == 0) Stage?.Hide();
        }

        // ── 입력 ─────────────────────────────────────────────

        // 클릭. 받아들였으면 true.
        //  · 클릭을 막는 효과가 진행 중이거나(효과 시간 보장), 방금 클릭했으면(연타 방지), 서버 결과를 기다리면 무시한다.
        //  · 연출 대기 중: 막지 않는 효과를 끝 모습으로 보내고 대사를 시작한다.
        //  · 타이핑 중: 글자를 다 보인다. / 다 나옴: 다음 대사로(흐름의 값 바꾸기가 실패하면 머물고 거짓).
        //  · '시간 진행(클릭 무시)' 대사는 클릭을 받지 않는다.
        public bool Advance()
        {
            if (_cooldown > 0 || InputBlocked || Line == null || _wait != null) return false;
            var timedOnly = Line.advance == LineAdvance.Timed;
            switch (Phase)
            {
                case DialoguePhase.WaitingForText:
                    if (timedOnly) return false;
                    var line = Line;
                    FinishAll(run => run.Line == line);
                    if (Phase == DialoguePhase.WaitingForText && Line == line) BeginTyping();
                    break;
                case DialoguePhase.Typing:
                    if (timedOnly) return false;
                    Typed();
                    break;
                case DialoguePhase.Shown:
                    if (timedOnly) return false;
                    _cooldown = ClickCooldown;
                    return TryMove(NextPortOf(Line), Line.options.Count > 0 ? NoChoiceDecision : null);
                default:
                    return false;
            }
            _cooldown = ClickCooldown;
            return true;
        }

        // 선택지 고르기(Choices의 순서). 클릭을 막는 효과가 진행 중이면 받지 않는다. 흐름의 값 바꾸기가 실패하면 그 대사에 머물고 거짓.
        public bool Choose(int choiceIndex)
        {
            if (_cooldown > 0 || InputBlocked || _wait != null) return false;
            if (Phase != DialoguePhase.Choosing || choiceIndex < 0 || choiceIndex >= _choices.Count || !_choices[choiceIndex].Enabled) return false;
            _cooldown = ClickCooldown;
            return PickOption(_choices[choiceIndex].OptionId);
        }

        // 진행 중인 연출을 모두 끝 모습으로 보내고 글자를 다 보인다(빨리 감기).
        public void FastForward()
        {
            FinishAll(null);
            if (Phase == DialoguePhase.WaitingForText) BeginTyping();
            if (Phase == DialoguePhase.Typing) Typed();
        }

        // 언어가 바뀌었을 때: 지금 대사·선택지·로그의 글을 새 언어로 다시 만들어 화면에 다시 그린다(목소리·모션은 다시 내지 않는다).
        public void RefreshTexts()
        {
            foreach (var entry in _log)
            {
                if (entry.SourceOption != null) entry.Text = ResolveText(entry.SourceOption.lineId, entry.SourceOption.text);
                else if (entry.SourceLine != null)
                {
                    entry.Text = ResolveText(entry.SourceLine.lineId, entry.SourceLine.text);
                    entry.SpeakerName = SpeakerNameOf(entry.SourceLine);
                }
            }
            if (Line == null || Phase == DialoguePhase.Ended || Phase == DialoguePhase.Idle) return;
            Text = ResolveText(Line.lineId, Line.text);
            TextLength = VisibleLength(Text);
            _visible = Phase == DialoguePhase.Typing || Phase == DialoguePhase.WaitingForText ? Mathf.Min(_visible, TextLength) : TextLength;
            for (var i = 0; i < _choices.Count; i++)
            {
                var choice = _choices[i];
                if (choice.Index < 0 || choice.Index >= Line.options.Count) continue;
                var option = Line.options[choice.Index];
                _choices[i] = new DialogueChoice(choice.Index, choice.OptionId, ResolveText(option.lineId, option.text), choice.Enabled);
            }
            if (Muted || Stage == null) return;
            if (Phase != DialoguePhase.WaitingForText)
            {
                if (Stage is IDialogueTextRefresh refresh) refresh.RefreshLine(Line, SpeakerNameOf(Line), Text);
                else Stage.ShowLine(Line, SpeakerNameOf(Line), Text);
                Stage.SetVisibleCharacters(VisibleCharacters);
            }
            if (_choices.Count > 0) Stage.ShowChoices(_choices);
        }

        // ── 시간 ─────────────────────────────────────────────

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0) deltaTime = 0;
            Time += deltaTime;
            if (_cooldown > 0) _cooldown -= deltaTime;
            foreach (var run in _runs.ToList())
            {
                if (run.Done) continue;
                if (!run.Begun)
                {
                    run.Wait -= deltaTime;
                    if (run.Wait > 0) continue;
                    var overflow = -run.Wait;
                    Begin(run);
                    if (run.Done || !run.Begun) continue;
                    Step(run, overflow);
                    continue;
                }
                Step(run, deltaTime);
            }

            if (_wait != null)
            {
                PollServer();
                return;
            }
            if (Phase == DialoguePhase.Typing)
            {
                var speed = Line.typingSpeed > 0 ? Line.typingSpeed : DefaultTypingSpeed;
                _visible += speed * deltaTime;
                if (_visible >= TextLength) Typed();
                else SetVisibleOnStage(VisibleCharacters);
            }
            else if (Phase == DialoguePhase.Shown && Line != null)
            {
                // 다 나온 뒤: 시간 진행 대사는 머무는 시간 뒤, 자동 모드면 AutoDelay 뒤 넘어간다.
                // 막는 효과가 도는 동안은 재지 않는다(효과가 끝난 뒤부터 머문다).
                if (InputBlocked || (AutoMode && !_simulating && Line.advance == LineAdvance.Click && HoldAuto())) _shownTimer = 0;
                else
                {
                    _shownTimer += deltaTime;
                    var wait = Line.advance != LineAdvance.Click ? Line.holdSeconds : AutoMode ? AutoDelay : float.PositiveInfinity;
                    // 넘기지 못했으면(값 바꾸기 실패) 머무는 시간을 다시 잰다.
                    if (_shownTimer >= wait && !TryMove(NextPortOf(Line), Line.options.Count > 0 ? NoChoiceDecision : null)) _shownTimer = 0;
                }
            }
        }

        // 서버 결과가 왔으면 이어 가거나(됨) 대사로 돌아간다(거절 — 실패 글은 GameNet이 알린다).
        void PollServer()
        {
            var wait = _wait;
            var result = ServerResult == null ? true : ServerResult(wait.Action);
            if (result == null) return;
            _wait = null;
            if (result == false)
            {
                FailMove(null);
                return;
            }
            var move = _move;
            try
            {
                if (ReferenceEquals(wait.Main, _main) && Phase != DialoguePhase.Ended) Continue(Dialogue.graph.Next(wait.Node, SetNode.NextPort));
            }
            catch (Exception e)
            {
                if (ReferenceEquals(_move, move)) FailMove(e);
                else Debug.LogException(e);
                return;
            }
            if (ReferenceEquals(_move, move) && _wait == null) _move = null;
        }

        // ── 신호와 효과 ───────────────────────────────────────

        void Fire(Scope scope, GraphNode from, string port)
        {
            if (from == null || scope == null) return;
            if (_depth >= MaxSignalDepth)
            {
                Debug.LogWarning("[다이얼로그] 신호가 너무 깊게 이어집니다(시간 0인 효과끼리 고리가 있는지 확인하세요).");
                return;
            }
            _depth++;
            try
            {
                foreach (var edge in scope.Graph.From(from.guid, port).ToList())
                {
                    var target = scope.Graph.Find(edge.toNode);
                    switch (target)
                    {
                        case EffectNode effect when edge.toPort == EffectNode.PlayPort:
                            Play(scope, effect, Line);
                            break;
                        case LineNode line when edge.toPort == LineNode.TextStartPort:
                            if (Line == line && Phase == DialoguePhase.WaitingForText) BeginTyping();
                            else _released.Add(line.guid);
                            break;
                        case PresetOutputNode output when scope.OwnerRun != null:
                            Fire(scope.Parent, scope.OwnerRun.Node, PresetEffect.OutputPrefix + output.signal);
                            break;
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        void Play(Scope scope, EffectNode effect, LineNode line)
        {
            // 고리에 든 효과가 한 번 끝난 뒤 다시 시작하면 '바퀴': 클릭을 막지 않는다.
            var lap = scope.Looping.Contains(effect.guid) && scope.Finished.Contains(effect.guid);
            var run = new Run { Node = effect, Scope = scope, Line = line ?? Line, Wait = effect.delay, Lap = lap };
            scope.Active++;
            _runs.Add(run);
            if (effect.delay <= 0) Begin(run);
        }

        void Begin(Run run)
        {
            run.Begun = true;
            var effect = run.Node;
            EffectStarted?.Invoke(effect);

            if (effect is PresetEffect presetEffect)
            {
                var preset = _presets(presetEffect.preset);
                var context = run.Scope.Context.With(run.Scope.Context.Character(presetEffect.character));
                run.Child = NewScope(preset?.graph ?? new NodeGraph(), context, run.Scope, run);
                Fire(run.Scope, effect, EffectNode.StartedPort);
                // 안의 효과가 모두 끝나야 완료. 아무것도 시작하지 않으면 바로 완료.
                run.Child.Active++;
                foreach (var entry in run.Child.Graph.OfType<PresetEntryNode>().ToList())
                    Fire(run.Child, entry, PresetEntryNode.StartPort);
                run.Child.Active--;
                CheckScope(run.Child);
                return;
            }

            var before = State.Clone();
            if (effect.IsState) effect.ApplyTo(State, run.Scope.Context);
            if (Muted)
            {
                // 조용히 다시 밟는 중: 화면에 내지 않는다(한 번짜리 연출 없음). 끝날 때 아직 진행 중이면 그 시점의 모습으로 그린다.
                run.Deferred = Stage != null;
                if (run.Deferred)
                {
                    run.Before = before;
                    run.After = State.Clone();
                }
            }
            else run.Player = Stage?.Begin(effect, run.Scope.Context, before, State.Clone());
            Fire(run.Scope, effect, EffectNode.StartedPort);
            if (!run.Done && (!effect.HasDuration || effect.EffectiveDuration <= 0)) Complete(run);
        }

        void Step(Run run, float deltaTime)
        {
            if (run.Node is PresetEffect) return;
            run.Elapsed += deltaTime;
            var duration = run.Node.EffectiveDuration;
            var t = duration <= 0 ? 1f : Mathf.Clamp01(run.Elapsed / duration);
            run.Player?.Evaluate(Easing.Apply(run.Node.ease, t));
            FireMarks(run, t);
            if (t >= 1f && !run.Done) Complete(run);
        }

        void FireMarks(Run run, float t)
        {
            var marks = run.Node.marks.OrderBy(m => m.percent).ToList();
            while (run.FiredMarks < marks.Count && marks[run.FiredMarks].percent / 100f <= t + 1e-6f)
            {
                var mark = marks[run.FiredMarks++];
                Fire(run.Scope, run.Node, EffectNode.MarkPrefix + mark.id);
            }
        }

        void Complete(Run run)
        {
            if (run.Done) return;
            if (!run.Begun) Begin(run);
            if (run.Done) return;
            if (run.Child != null && run.Child.Active > 0)
            {
                // 프리셋: 안의 효과를 모두 끝낸다(각 효과 완료가 차례로 이 실행을 완료시킨다).
                FinishAll(r => IsInside(r.Scope, run.Child));
                if (run.Done) return;
            }
            run.Done = true;
            run.Scope.Finished.Add(run.Node.guid);
            if (!(run.Node is PresetEffect))
            {
                FireMarks(run, 1f);
                run.Player?.Evaluate(1f);
                run.Player?.Finish();
            }
            _runs.Remove(run);
            Fire(run.Scope, run.Node, EffectNode.DonePort);
            run.Scope.Active--;
            CheckScope(run.Scope);
        }

        void CheckScope(Scope scope)
        {
            if (scope.OwnerRun == null || scope.Active > 0 || scope.OwnerRun.Done) return;
            Complete(scope.OwnerRun);
        }

        static bool IsInside(Scope scope, Scope ancestor)
        {
            for (var s = scope; s != null; s = s.Parent)
                if (s == ancestor)
                    return true;
            return false;
        }

        // 조건에 맞는 실행 중 효과를 끝 모습으로 보낸다. 끝나며 이어진 효과도(같은 조건이면) 차례로 끝낸다.
        // 한 번 훑는 동안 이미 끝낸 효과가 신호 고리로 다시 시작하면, 시작은 하되 다시 끝내지 않는다(고리에서 끝없이 돌지 않게).
        void FinishAll(Func<Run, bool> filter)
        {
            var existing = new HashSet<Run>(_runs);
            var finished = new HashSet<(Scope, EffectNode)>();
            bool Eligible(Run r) => !r.Done && (existing.Contains(r) || !finished.Contains((r.Scope, r.Node))) && (filter == null || filter(r));
            for (var guard = 0; guard < 10000; guard++)
            {
                var run = _runs.FirstOrDefault(r => !(r.Node is PresetEffect) && Eligible(r)) ?? _runs.FirstOrDefault(Eligible);
                if (run == null) return;
                existing.Remove(run);
                finished.Add((run.Scope, run.Node));
                Complete(run);
            }
        }

        // 남은 효과를 신호 없이 멈춘다(끝 모습으로 그리고 지운다): 고리를 돌며 다시 시작한 효과가 줄·다이얼로그를 넘어 끝없이 남지 않게.
        void KillRuns(Func<Run, bool> filter)
        {
            var killed = _runs.Where(r => !r.Done && (filter == null || filter(r))).ToList();
            foreach (var run in killed)
            {
                run.Done = true;
                if (run.Begun && !(run.Node is PresetEffect))
                {
                    run.Player?.Evaluate(1f);
                    run.Player?.Finish();
                }
                _runs.Remove(run);
                run.Scope.Active--;
            }
            if (filter == null) return;
            // 안의 효과를 멈춰 비게 된 프리셋(멈추지 않은 것)은 완료시킨다 — 끝나지 않은 채 남지 않게.
            foreach (var scope in killed.Select(r => r.Scope).Distinct().ToList()) CheckScope(scope);
        }

        // ── 화면에 내기 ──────────────────────────────────────
        // 조용히 다시 밟는 동안(불러오기·스킵·시간 이동)은 화면에 보내지 않고, 끝날 때 마지막 모습을 한 번 그린다(PresentAll).

        bool Muted => _mute > 0;

        void ApplyStage()
        {
            if (!Muted) Stage?.ApplyState(State);
        }

        void ShowLineOnStage(string speakerName)
        {
            if (!Muted) Stage?.ShowLine(Line, speakerName, Text);
        }

        void SetVisibleOnStage(int count)
        {
            if (!Muted) Stage?.SetVisibleCharacters(count);
        }

        void ShowChoicesOnStage()
        {
            if (!Muted) Stage?.ShowChoices(_choices);
        }

        void HideStage()
        {
            if (!Muted) Stage?.Hide();
        }

        void Unmute()
        {
            if (_mute <= 0) return;
            if (--_mute == 0) PresentAll();
        }

        // 마지막 모습을 한 번 그린다: 상태 → 아직 진행 중인 효과(그 시점의 진행도로) → 지금 대사·선택지(목소리는 이 줄 것만).
        void PresentAll()
        {
            var stage = Stage;
            if (stage == null) return;
            if (Phase == DialoguePhase.Ended)
            {
                stage.Hide();
                return;
            }
            stage.ApplyState(State);
            foreach (var run in _runs.ToList())
            {
                if (!run.Deferred || run.Done) continue;
                run.Deferred = false;
                run.Player = stage.Begin(run.Node, run.Scope.Context, run.Before ?? State, run.After ?? State);
                run.Before = null;
                run.After = null;
                var duration = run.Node.EffectiveDuration;
                var t = duration <= 0 ? 1f : Mathf.Clamp01(run.Elapsed / duration);
                run.Player?.Evaluate(Easing.Apply(run.Node.ease, t));
            }
            if (Line == null) return;
            if (Phase != DialoguePhase.WaitingForText)
            {
                stage.ShowLine(Line, SpeakerNameOf(Line), Text);
                stage.SetVisibleCharacters(VisibleCharacters);
            }
            stage.ShowChoices(_choices);
        }

        // ── 글자 ─────────────────────────────────────────────

        string ResolveText(string key, string source)
        {
            var args = TextArguments();
            if (!string.IsNullOrEmpty(key) && Texts.Has(key)) return Texts.Get(key, args);
            return Texts.Catalog.FormatTemplate(source ?? "", args);
        }

        // 문장 틀의 {값}: 플레이어 데이터(예: {player.gold}).
        public IReadOnlyDictionary<string, object> TextArguments()
        {
            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            if (Session != null)
                foreach (var key in Session.Data.Keys)
                {
                    try
                    {
                        args[key] = Session.Data.Get(key);
                    }
                    catch (Exception)
                    {
                        // 읽을 수 없는 값은 건너뛴다.
                    }
                }
            return args;
        }

        // 태그를 뺀 글자 수(TextMeshPro의 maxVisibleCharacters와 같은 기준). 알려진 리치 텍스트 태그만 뺀다("<주인공>"은 글자).
        public static int VisibleLength(string text) => RichText.VisibleLength(text);
    }

    // 다이얼로그의 '값 바꾸기' 노드 실행 = 행동 하나(Undo 한 단위). 바꿀 값(보상 블록)은 노드의 것을 그대로 쓴다.
    // 기록·전송에는 다이얼로그 ID·노드만 남는다 → 받은 행동(다시 돌리기·멀티)은 DB의 그 노드에서 보상 블록을 다시 찾는다.
    // 멀티 서버는 보낸 플레이어의 진행(DialogueProgressAction)에서 그 노드에 닿는지 먼저 본다(DialogueGuard).
    [Serializable]
    public sealed class DialogueSetAction : GameAction
    {
        public string dialogue;
        public string node;
        [NonSerialized] public List<Reward> Rewards;

        public override string Describe() => $"다이얼로그 값 바꾸기 ({dialogue})";

        protected override void Run()
        {
            var session = Game.Session;
            var refusal = DialogueGuard.CheckSet(session, actorId, dialogue, node);
            if (refusal != null)
                throw new GameFailure(DialogueGuard.RefusedKey, TextArgs.From(("reason", refusal)), "다이얼로그 진행과 맞지 않아 값 바꾸기를 거절했습니다({reason}).");
            var rewards = Rewards ?? (DialogueLookup.Find(dialogue)?.graph?.Find(node) as SetNode)?.rewards;
            if (rewards == null)
            {
                session.Journal.Append(JournalKind.Warning, "Dialogue", $"다이얼로그 값 바꾸기: '{dialogue}'의 노드 '{node}'를 찾지 못해 아무것도 바꾸지 않았습니다.");
                return;
            }
            foreach (var reward in rewards)
                reward?.Grant(session);
            DialogueGuard.Granted(session, actorId, dialogue, node);
        }
    }

    // 이동 전 확인(Preflight)용: 흐름 한 구간의 값 바꾸기를 미리보기 안에서 해 본다. 미리보기 전용이라 기록·전송되지 않는다.
    [Serializable, SystemAction]
    sealed class DialogueProbeAction : GameAction
    {
        [NonSerialized] public Action Walk;

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => "다이얼로그 값 바꾸기 확인";

        protected override void Run() => Walk?.Invoke();
    }
}
