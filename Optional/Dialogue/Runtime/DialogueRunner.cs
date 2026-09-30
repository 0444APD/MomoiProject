using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // 대화 화면을 실제로 그리는 쪽(UI·스탠딩·카메라 …). 실행기는 '언제 무엇을'만 정하고 그리기는 여기에 맡긴다.
    public interface IDialogueStage
    {
        // 상태를 전환 없이 바로 적용한다(시작·불러오기·되감기).
        void ApplyState(StageState state);

        // 효과 하나를 시작한다. before → after는 상태 효과의 앞뒤 모습(큐 효과는 같다). 재생기가 없으면 null(시간만 흐른다).
        IEffectPlayer Begin(EffectNode effect, EffectContext context, StageState before, StageState after);

        // 대사 줄을 띄운다(글자는 SetVisibleCharacters만큼 보인다). line.speaker는 캐릭터 ID(해설이면 빈 값).
        void ShowLine(LineNode line, string speakerName, string text);
        void SetVisibleCharacters(int count);

        // 선택지를 띄운다(빈 목록이면 숨긴다).
        void ShowChoices(IReadOnlyList<DialogueChoice> choices);

        // 대화가 끝났다.
        void Hide();
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
        // 다 나왔고 클릭(또는 자동 넘김)을 기다린다.
        Shown,
        Choosing,
        Ended,
    }

    // 중간 저장·되감기용: 어느 대화의 어느 줄, 그때의 화면 상태.
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
    }

    // 대화 진행 기록(저장용): 어디서 시작했고, 분기·선택지에서 어느 쪽으로 갔고, 대사를 몇 줄 지났는지.
    // 불러올 때는 같은 길을 조용히 다시 밟아(값 바꾸기·신호는 다시 하지 않는다) 그때의 화면을 그대로 만든다.
    [Serializable]
    public sealed class DialogueProgress
    {
        public string dialogue;
        public string entry;
        // 분기 결과("true"/"false")와 고른 선택지 ID, 일어난 순서대로.
        public List<string> decisions = new List<string>();
        public int lines;

        public DialogueProgress Clone() => new DialogueProgress { dialogue = dialogue, entry = entry, decisions = new List<string>(decisions), lines = lines };
    }

    // 대화 실행기. 흐름(대사 → 선택지 → 분기 …)을 따라가며, 신호로 이어진 효과 체인을 시간에 맞춰 재생한다.
    // 게임 쪽은 Tick(프레임 시간), Advance(클릭), Choose(선택)만 부른다. Unity 컴포넌트 없이도 돈다(테스트·미리보기).
    public sealed class DialogueRunner
    {
        const int MaxSignalDepth = 64;
        const int MaxFlowSteps = 1000;

        sealed class Scope
        {
            public NodeGraph Graph;
            public EffectContext Context;
            public Scope Parent;
            public Run OwnerRun;
            public int Active;
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
        }

        readonly Func<string, DialogueData> _dialogues;
        readonly Func<string, EffectPresetData> _presets;
        readonly List<Run> _runs = new List<Run>();
        readonly HashSet<string> _released = new HashSet<string>();
        readonly List<DialogueChoice> _choices = new List<DialogueChoice>();
        readonly List<DialogueSnapshot> _history = new List<DialogueSnapshot>();
        readonly List<DialogueLogEntry> _log = new List<DialogueLogEntry>();
        Scope _main;
        int _depth;
        float _visible;
        float _shownTimer;
        float _cooldown;
        DialogueProgress _progress;
        Queue<string> _replay;

        public GameSession Session { get; set; }
        public IDialogueStage Stage { get; set; }
        public float DefaultTypingSpeed = 30f;
        // 클릭을 받은 뒤 다음 클릭을 무시하는 시간(연타로 두 줄씩 넘어가지 않게).
        public float ClickCooldown = 0.12f;
        // 자동 진행: '클릭' 대사가 다 나오고 막는 효과가 없으면 AutoDelay초 뒤 클릭한 것으로 친다.
        public bool AutoMode;
        public float AutoDelay = 1.2f;
        // 대사마다 화자 자동 강조(대사 노드에서 '그대로'로 끌 수 있다).
        public bool AutoHighlightSpeaker = true;
        // 장소·스탠딩 프로필 찾기(FromDatabase가 채운다).
        public Func<string, LocationData> Locations = _ => null;
        public Func<string, StandingProfileData> Profiles = _ => null;
        // 시작(StartAt·Start)부터 흐른 시간. 미리보기의 시간 위치.
        public float Time { get; private set; }

        // 클릭을 막는 효과(효과 시간 보장)가 진행 중인지.
        public bool InputBlocked => _runs.Any(r => !r.Done && r.Node.blockInput && !(r.Node is PresetEffect));
        // 대화 진행 중이거나 효과가 돌고 있다.
        public bool IsActive => (Phase != DialoguePhase.Idle && Phase != DialoguePhase.Ended) || _runs.Count > 0;

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

        public event Action<LineNode> LineEntered;
        public event Action<string> Signaled;
        public event Action Ended;
        public event Action<EffectNode> EffectStarted;
        // 진행 기록이 바뀌었다(대사에 들어감·끝남). 저장에 쓴다.
        public event Action ProgressChanged;

        // 지금까지의 진행 기록(저장용 사본). 대화 중이 아니면 null.
        public DialogueProgress Progress => IsActive && _progress != null ? _progress.Clone() : null;
        // 불러오기 위해 길을 다시 밟는 중(값 바꾸기·신호를 하지 않는다).
        public bool Replaying => _replay != null;
        public DialogueRunner(Func<string, DialogueData> dialogues, Func<string, EffectPresetData> presets)
        {
            _dialogues = dialogues ?? (_ => null);
            _presets = presets ?? (_ => null);
        }

        // 게임 데이터베이스에서 대화·프리셋을 찾는 실행기.
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

        // ── 시작·끝 ───────────────────────────────────────────

        public bool Start(DialogueData dialogue, string entryId = "start")
        {
            var entry = dialogue?.Entry(entryId);
            if (entry == null)
            {
                Debug.LogWarning($"[대화] '{dialogue?.Id}'에 시작 ID '{entryId}'가 없습니다.");
                return false;
            }
            Reset();
            _progress = new DialogueProgress { dialogue = dialogue.Id, entry = entryId };
            Stage?.ApplyState(State);
            Enter(dialogue);
            Fire(_main, entry, StartNode.StartedPort);
            Continue(dialogue.graph.Next(entry, StartNode.NextPort));
            return true;
        }

        // 저장한 진행 기록에서 이어 간다: 처음부터 같은 길을 조용히 밟아 저장한 대사에 들어간다.
        public bool Restore(DialogueProgress progress)
        {
            var dialogue = _dialogues(progress?.dialogue);
            if (dialogue == null) return false;
            _replay = new Queue<string>(progress.decisions ?? new List<string>());
            try
            {
                if (!Start(dialogue, progress.entry)) return false;
                for (var guard = 0; guard < 100000 && _progress != null && _progress.lines < progress.lines; guard++)
                {
                    FinishAll(null);
                    switch (Phase)
                    {
                        case DialoguePhase.WaitingForText:
                            BeginTyping();
                            break;
                        case DialoguePhase.Typing:
                            Typed();
                            break;
                        case DialoguePhase.Shown:
                            Leave(LineNode.NextPort);
                            break;
                        case DialoguePhase.Choosing:
                            if (_replay.Count == 0) return IsActive;
                            var optionId = _replay.Dequeue();
                            if (_choices.All(c => c.OptionId != optionId)) return IsActive;
                            Pick(optionId);
                            break;
                        default:
                            return IsActive;
                    }
                }
            }
            finally
            {
                _replay = null;
            }
            ProgressChanged?.Invoke();
            return IsActive;
        }

        public bool Start(string dialogueId, string entryId = "start") => Start(_dialogues(dialogueId), entryId);

        // 저장해 둔 줄에서 다시 시작한다(화면 상태를 바로 적용).
        public bool Resume(DialogueSnapshot snapshot)
        {
            var dialogue = _dialogues(snapshot?.dialogue);
            if (!(dialogue?.graph.Find(snapshot.node) is LineNode line)) return false;
            Reset();
            State = snapshot.state?.Clone() ?? new StageState();
            Stage?.ApplyState(State);
            Enter(dialogue);
            EnterLine(line);
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
        public bool StartAt(DialogueData dialogue, string nodeGuid)
        {
            var node = dialogue?.graph.Find(nodeGuid);
            if (node == null) return false;
            Reset();
            _progress = null;
            Stage?.ApplyState(State);
            Enter(dialogue);
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
            return true;
        }

        // 연출 프리셋 하나만 재생한다(프리셋 창 미리보기).
        public bool StartPreset(string presetId)
        {
            if (_presets(presetId) == null) return false;
            Reset();
            _progress = null;
            Stage?.ApplyState(State);
            Dialogue = null;
            _main = new Scope { Graph = new NodeGraph(), Context = NewContext() };
            Play(_main, new PresetEffect { preset = presetId }, null);
            return true;
        }

        // 지금부터 seconds만큼 시간을 흘린다(미리보기의 시간 이동: 다시 시작한 뒤 원하는 시점까지 감는다).
        // autoClick: '클릭' 대사도 글이 다 나오고 막는 효과가 끝나면 clickDelay초 뒤 클릭한 것으로 치고 넘어간다(선택지에서는 멈춘다).
        public void Simulate(float seconds, float step = 1f / 60f, bool autoClick = false, float clickDelay = 0f)
        {
            var auto = AutoMode;
            var delay = AutoDelay;
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
            }
        }

        // 다음 선택지 직전까지(선택지가 뜬 상태, 또는 대화 끝까지) 건너뛴다. 막는 효과도 끝 모습으로 보낸다.
        public void SkipToChoice()
        {
            for (var guard = 0; guard < 10000; guard++)
            {
                FinishAll(null);
                switch (Phase)
                {
                    case DialoguePhase.WaitingForText:
                        BeginTyping();
                        break;
                    case DialoguePhase.Typing:
                        Typed();
                        break;
                    case DialoguePhase.Shown:
                        Leave(LineNode.NextPort);
                        break;
                    default:
                        return;
                }
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
        }

        void Enter(DialogueData dialogue)
        {
            Dialogue = dialogue;
            _main = new Scope { Graph = dialogue.graph, Context = NewContext() };
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
            _choices.Clear();
            Line = null;
            Phase = DialoguePhase.Ended;
            Stage?.Hide();
            Ended?.Invoke();
            if (!Replaying) ProgressChanged?.Invoke();
        }

        // ── 흐름 ─────────────────────────────────────────────

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
                        // 불러오기 중이면 저장할 때 간 쪽으로.
                        var pass = Replaying && _replay.Count > 0
                            ? _replay.Dequeue() == "true"
                            : branch.condition == null || Session == null || branch.condition.Evaluate(Session);
                        _progress?.decisions.Add(pass ? "true" : "false");
                        node = Dialogue.graph.Next(branch, pass ? BranchNode.TruePort : BranchNode.FalsePort);
                        break;
                    case SetNode set:
                        if (Session != null && !Replaying)
                            foreach (var reward in set.rewards)
                                reward?.Grant(Session);
                        node = Dialogue.graph.Next(set, SetNode.NextPort);
                        break;
                    case SignalNode signal:
                        if (!Replaying) Signaled?.Invoke(signal.signal);
                        node = Dialogue.graph.Next(signal, SignalNode.NextPort);
                        break;
                    case JumpNode jump:
                        var target = string.IsNullOrEmpty(jump.dialogue) ? Dialogue : _dialogues(jump.dialogue);
                        var entry = target?.Entry(jump.entryId);
                        if (entry == null)
                        {
                            Debug.LogWarning($"[대화] 이동할 곳이 없습니다: {jump.dialogue} · {jump.entryId}");
                            End();
                            return;
                        }
                        if (target != Dialogue)
                        {
                            FinishAll(null);
                            Enter(target);
                        }
                        Fire(_main, entry, StartNode.StartedPort);
                        node = target.graph.Next(entry, StartNode.NextPort);
                        break;
                    case CustomFlowNode custom:
                        var port = custom.Run(this);
                        node = port == null ? null : Dialogue.graph.Next(custom, port);
                        break;
                    default:
                        Debug.LogWarning($"[대화] 흐름에서 처리할 수 없는 노드: {node.GetType().Name}");
                        End();
                        return;
                }
            }
            Debug.LogWarning("[대화] 흐름이 너무 길게 이어집니다(대사 없이 도는 고리가 있는지 확인하세요).");
            End();
        }

        void EnterLine(LineNode line)
        {
            Line = line;
            _main.Context.Speaker = line.speaker;
            _choices.Clear();
            Stage?.ShowChoices(_choices);
            Text = ResolveText(line.lineId, line.text);
            TextLength = VisibleLength(Text);
            _visible = 0;
            _shownTimer = 0;
            Phase = DialoguePhase.WaitingForText;
            _history.Add(new DialogueSnapshot { dialogue = Dialogue.Id, node = line.guid, state = State.Clone() });
            if (_progress != null) _progress.lines++;

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
            if (!gated || _released.Remove(line.guid)) BeginTyping();
            if (!Replaying) ProgressChanged?.Invoke();
        }

        void BeginTyping()
        {
            if (Phase != DialoguePhase.WaitingForText) return;
            Phase = DialoguePhase.Typing;
            var speakerName = SpeakerNameOf(Line);
            _log.Add(new DialogueLogEntry { Speaker = Line.speaker, SpeakerName = speakerName, Text = Text, Portrait = PortraitOf(Line.speaker), Thought = Line.thought });
            Stage?.ShowLine(Line, speakerName, Text);
            Stage?.SetVisibleCharacters(0);
            if (TextLength == 0) Typed();
        }

        void Typed()
        {
            _visible = TextLength;
            Stage?.SetVisibleCharacters(TextLength);
            var line = Line;
            if (Line.options.Count > 0)
            {
                Phase = DialoguePhase.Choosing;
                BuildChoices();
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
                var open = option.condition == null || Session == null || option.condition.Evaluate(Session);
                if (!open && !option.showWhenLocked) continue;
                _choices.Add(new DialogueChoice(i, option.id, ResolveText(option.lineId, option.text), open));
            }
            Stage?.ShowChoices(_choices);
        }

        void Leave(string port)
        {
            var line = Line;
            Fire(_main, line, LineNode.LeavePort);
            // 이 줄에서 시작한 효과는 (계속 설정이 아니면) 끝 모습으로 마무리한다.
            FinishAll(run => run.Line == line && !run.Node.keepAcrossLines);
            _choices.Clear();
            Stage?.ShowChoices(_choices);
            if (Line == line) Continue(Dialogue.graph.Next(line, port));
        }

        // ── 입력 ─────────────────────────────────────────────

        // 클릭. 받아들였으면 true.
        //  · 클릭을 막는 효과가 진행 중이거나(효과 시간 보장), 방금 클릭했으면(연타 방지) 무시한다.
        //  · 연출 대기 중: 막지 않는 효과를 끝 모습으로 보내고 대사를 시작한다.
        //  · 타이핑 중: 글자를 다 보인다. / 다 나옴: 다음 대사로.
        //  · '시간 진행(클릭 무시)' 대사는 클릭을 받지 않는다.
        public bool Advance()
        {
            if (_cooldown > 0 || InputBlocked || Line == null) return false;
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
                    Leave(LineNode.NextPort);
                    break;
                default:
                    return false;
            }
            _cooldown = ClickCooldown;
            return true;
        }

        // 선택지 고르기(Choices의 순서). 클릭을 막는 효과가 진행 중이면 받지 않는다.
        public bool Choose(int choiceIndex)
        {
            if (_cooldown > 0 || InputBlocked) return false;
            if (Phase != DialoguePhase.Choosing || choiceIndex < 0 || choiceIndex >= _choices.Count || !_choices[choiceIndex].Enabled) return false;
            _cooldown = ClickCooldown;
            Pick(_choices[choiceIndex].OptionId);
            return true;
        }

        void Pick(string optionId)
        {
            _progress?.decisions.Add(optionId);
            var picked = _choices.FirstOrDefault(c => c.OptionId == optionId);
            _log.Add(new DialogueLogEntry { Text = picked.Text ?? "", Choice = true });
            Leave(LineNode.OptionPrefix + optionId);
        }

        // 진행 중인 연출을 모두 끝 모습으로 보내고 글자를 다 보인다(빨리 감기).
        public void FastForward()
        {
            FinishAll(null);
            if (Phase == DialoguePhase.WaitingForText) BeginTyping();
            if (Phase == DialoguePhase.Typing) Typed();
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

            if (Phase == DialoguePhase.Typing)
            {
                var speed = Line.typingSpeed > 0 ? Line.typingSpeed : DefaultTypingSpeed;
                _visible += speed * deltaTime;
                if (_visible >= TextLength) Typed();
                else Stage?.SetVisibleCharacters(VisibleCharacters);
            }
            else if (Phase == DialoguePhase.Shown && Line != null)
            {
                // 다 나온 뒤: 시간 진행 대사는 머무는 시간 뒤, 자동 모드면 AutoDelay 뒤 넘어간다.
                // 막는 효과가 도는 동안은 재지 않는다(효과가 끝난 뒤부터 머문다).
                if (InputBlocked) _shownTimer = 0;
                else
                {
                    _shownTimer += deltaTime;
                    var wait = Line.advance != LineAdvance.Click ? Line.holdSeconds : AutoMode ? AutoDelay : float.PositiveInfinity;
                    if (_shownTimer >= wait) Leave(LineNode.NextPort);
                }
            }
        }

        // ── 신호와 효과 ───────────────────────────────────────

        void Fire(Scope scope, GraphNode from, string port)
        {
            if (from == null || scope == null) return;
            if (_depth >= MaxSignalDepth)
            {
                Debug.LogWarning("[대화] 신호가 너무 깊게 이어집니다(시간 0인 효과끼리 고리가 있는지 확인하세요).");
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
            var run = new Run { Node = effect, Scope = scope, Line = line ?? Line, Wait = effect.delay };
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
                run.Child = new Scope { Graph = preset?.graph ?? new NodeGraph(), Context = context, Parent = run.Scope, OwnerRun = run };
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
            run.Player = Stage?.Begin(effect, run.Scope.Context, before, State.Clone());
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
        void FinishAll(Func<Run, bool> filter)
        {
            for (var guard = 0; guard < 10000; guard++)
            {
                var run = _runs.FirstOrDefault(r => !r.Done && !(r.Node is PresetEffect) && (filter == null || filter(r)))
                          ?? _runs.FirstOrDefault(r => !r.Done && (filter == null || filter(r)));
                if (run == null) return;
                Complete(run);
            }
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
                foreach (var key in Session.State.Keys)
                {
                    try
                    {
                        args[key] = Session.State.Get(key);
                    }
                    catch (Exception)
                    {
                        // 읽을 수 없는 값은 건너뛴다.
                    }
                }
            return args;
        }

        // 태그(<…>)를 뺀 글자 수(TextMeshPro의 maxVisibleCharacters와 같은 기준).
        public static int VisibleLength(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var count = 0;
            var inTag = false;
            foreach (var c in text)
            {
                if (c == '<') inTag = true;
                else if (c == '>' && inTag) inTag = false;
                else if (!inTag) count++;
            }
            return count;
        }
    }
}
