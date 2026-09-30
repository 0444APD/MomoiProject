using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // ????붾㈃???ㅼ젣濡?洹몃━??履?UI쨌?ㅽ깲?㈑룹뭅硫붾씪 ??. ?ㅽ뻾湲곕뒗 '?몄젣 臾댁뾿??留??뺥븯怨?洹몃━湲곕뒗 ?ш린??留↔릿??
    public interface IDialogueStage
    {
        // ?곹깭瑜??꾪솚 ?놁씠 諛붾줈 ?곸슜?쒕떎(?쒖옉쨌遺덈윭?ㅺ린쨌?섍컧湲?.
        void ApplyState(StageState state);

        // ?④낵 ?섎굹瑜??쒖옉?쒕떎. before ??after???곹깭 ?④낵???욌뮘 紐⑥뒿(???④낵??媛숇떎). ?ъ깮湲곌? ?놁쑝硫?null(?쒓컙留??먮Ⅸ??.
        IEffectPlayer Begin(EffectNode effect, EffectContext context, StageState before, StageState after);

        // ???以꾩쓣 ?꾩슫??湲?먮뒗 SetVisibleCharacters留뚰겮 蹂댁씤??. speaker??罹먮┃??ID(?댁꽕?대㈃ 鍮?媛?.
        void ShowLine(string speaker, string speakerName, string text);
        void SetVisibleCharacters(int count);

        // ?좏깮吏瑜??꾩슫??鍮?紐⑸줉?대㈃ ?④릿??.
        void ShowChoices(IReadOnlyList<DialogueChoice> choices);

        // ??붽? ?앸궗??
        void Hide();
    }

    // ?④낵 ?섎굹??吏꾪뻾??洹몃┛?? t??0~1. ?ㅽ궢?섎㈃ 諛붾줈 Evaluate(1) ??Finish媛 遺덈┛??
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
        // 以꾩뿉 ?ㅼ뼱?붿?留?'????쒖옉' ?좏샇瑜?湲곕떎由곕떎(?곗텧 以?.
        WaitingForText,
        Typing,
        // ???섏솕怨??대┃(?먮뒗 ?먮룞 ?섍?)??湲곕떎由곕떎.
        Shown,
        Choosing,
        Ended,
    }

    // 以묎컙 ??Β룸릺媛먭린?? ?대뒓 ??붿쓽 ?대뒓 以? 洹몃븣???붾㈃ ?곹깭.
    [Serializable]
    public sealed class DialogueSnapshot
    {
        public string dialogue;
        public string node;
        public StageState state;
    }

    // ????ㅽ뻾湲? ?먮쫫(??????좏깮吏 ??遺꾧린 ?????곕씪媛硫? ?좏샇濡??댁뼱吏??④낵 泥댁씤???쒓컙??留욎떠 ?ъ깮?쒕떎.
    // 寃뚯엫 履쎌? Tick(?꾨젅???쒓컙), Advance(?대┃), Choose(?좏깮)留?遺瑜몃떎. Unity 而댄룷?뚰듃 ?놁씠???덈떎(?뚯뒪?맞룸?由щ낫湲?.
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
        Scope _main;
        int _depth;
        float _visible;
        float _autoTimer;

        public GameSession Session { get; set; }
        public IDialogueStage Stage { get; set; }
        public float DefaultTypingSpeed = 30f;

        public DialogueData Dialogue { get; private set; }
        public LineNode Line { get; private set; }
        public DialoguePhase Phase { get; private set; }
        public StageState State { get; private set; } = new StageState();
        public string Text { get; private set; } = "";
        public int TextLength { get; private set; }
        public int VisibleCharacters => Mathf.Min(TextLength, Mathf.FloorToInt(_visible));
        public IReadOnlyList<DialogueChoice> Choices => _choices;
        public int RunningEffects => _runs.Count;
        // 吏?섏삩 以?洹몃븣???붾㈃ ?곹깭 ?ы븿). ???濡쒓렇쨌?섍컧湲곗뿉 ?대떎.
        public IReadOnlyList<DialogueSnapshot> History => _history;

        public event Action<LineNode> LineEntered;
        public event Action<string> Signaled;
        public event Action Ended;
        public event Action<EffectNode> EffectStarted;

        public DialogueRunner(Func<string, DialogueData> dialogues, Func<string, EffectPresetData> presets)
        {
            _dialogues = dialogues ?? (_ => null);
            _presets = presets ?? (_ => null);
        }

        // 寃뚯엫 ?곗씠?곕쿋?댁뒪?먯꽌 ??붋룻봽由ъ뀑??李얜뒗 ?ㅽ뻾湲?
        public static DialogueRunner FromDatabase(GameDatabase database)
        {
            var dialogues = database == null ? new Dictionary<string, DialogueData>() : database.All<DialogueData>().Where(d => d != null).ToDictionary(d => d.Id);
            var presets = database == null ? new Dictionary<string, EffectPresetData>() : database.All<EffectPresetData>().Where(p => p != null).ToDictionary(p => p.Id);
            return new DialogueRunner(id => id != null && dialogues.TryGetValue(id, out var d) ? d : null,
                id => id != null && presets.TryGetValue(id, out var p) ? p : null);
        }

        // ?? ?쒖옉쨌?????????????????????????????????????????????

        public bool Start(DialogueData dialogue, string entryId = "start")
        {
            var entry = dialogue?.Entry(entryId);
            if (entry == null)
            {
                Debug.LogWarning($"[??? '{dialogue?.Id}'???쒖옉 ID '{entryId}'媛 ?놁뒿?덈떎.");
                return false;
            }
            Reset();
            Stage?.ApplyState(State);
            Enter(dialogue);
            Fire(_main, entry, StartNode.StartedPort);
            Continue(dialogue.graph.Next(entry, StartNode.NextPort));
            return true;
        }

        public bool Start(string dialogueId, string entryId = "start") => Start(_dialogues(dialogueId), entryId);

        // ??ν빐 ??以꾩뿉???ㅼ떆 ?쒖옉?쒕떎(?붾㈃ ?곹깭瑜?諛붾줈 ?곸슜).
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
            if (Phase == DialoguePhase.Idle || Phase == DialoguePhase.Ended) return;
            End();
        }

        void Reset()
        {
            foreach (var run in _runs.ToList()) run.Player?.Finish();
            _runs.Clear();
            _released.Clear();
            _choices.Clear();
            _history.Clear();
            State = new StageState();
            Line = null;
            Text = "";
            TextLength = 0;
            Phase = DialoguePhase.Idle;
        }

        void Enter(DialogueData dialogue)
        {
            Dialogue = dialogue;
            _main = new Scope { Graph = dialogue.graph, Context = new EffectContext() };
        }

        void End()
        {
            FinishAll(null);
            _choices.Clear();
            Line = null;
            Phase = DialoguePhase.Ended;
            Stage?.Hide();
            Ended?.Invoke();
        }

        // ?? ?먮쫫 ?????????????????????????????????????????????

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
                        var pass = branch.condition == null || Session == null || branch.condition.Evaluate(Session);
                        node = Dialogue.graph.Next(branch, pass ? BranchNode.TruePort : BranchNode.FalsePort);
                        break;
                    case SetNode set:
                        if (Session != null)
                            foreach (var reward in set.rewards)
                                reward?.Grant(Session);
                        node = Dialogue.graph.Next(set, SetNode.NextPort);
                        break;
                    case SignalNode signal:
                        Signaled?.Invoke(signal.signal);
                        node = Dialogue.graph.Next(signal, SignalNode.NextPort);
                        break;
                    case JumpNode jump:
                        var target = string.IsNullOrEmpty(jump.dialogue) ? Dialogue : _dialogues(jump.dialogue);
                        var entry = target?.Entry(jump.entryId);
                        if (entry == null)
                        {
                            Debug.LogWarning($"[??? ?대룞??怨녹씠 ?놁뒿?덈떎: {jump.dialogue} 쨌 {jump.entryId}");
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
                    default:
                        Debug.LogWarning($"[??? ?먮쫫?먯꽌 泥섎━?????녿뒗 ?몃뱶: {node.GetType().Name}");
                        End();
                        return;
                }
            }
            Debug.LogWarning("[??? ?먮쫫???덈Т 湲멸쾶 ?댁뼱吏묐땲??????놁씠 ?꾨뒗 怨좊━媛 ?덈뒗吏 ?뺤씤?섏꽭??.");
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
            _autoTimer = 0;
            Phase = DialoguePhase.WaitingForText;
            _history.Add(new DialogueSnapshot { dialogue = Dialogue.Id, node = line.guid, state = State.Clone() });

            // ?붿옄 ?쒖젙: 以꾩뿉 ?곸? ?쒖젙? 吏㏃? ?쒖젙 ?④낵濡?諛붽씔??
            if (!string.IsNullOrEmpty(line.expression) && !string.IsNullOrEmpty(line.speaker))
                Play(_main, new ExpressionEffect { character = line.speaker, expression = line.expression, duration = 0.15f }, null);

            LineEntered?.Invoke(line);
            Fire(_main, line, LineNode.EnterPort);
            if (Line != line) return;
            var gated = Dialogue.graph.To(line.guid, LineNode.TextStartPort).Any();
            if (!gated || _released.Remove(line.guid)) BeginTyping();
        }

        void BeginTyping()
        {
            if (Phase != DialoguePhase.WaitingForText) return;
            Phase = DialoguePhase.Typing;
            Stage?.ShowLine(Line.speaker, string.IsNullOrEmpty(Line.speaker) ? "" : Texts.NameOf(Line.speaker), Text);
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
            // ??以꾩뿉???쒖옉???④낵??(怨꾩냽 ?ㅼ젙???꾨땲硫? ??紐⑥뒿?쇰줈 留덈Т由ы븳??
            FinishAll(run => run.Line == line && !run.Node.keepAcrossLines);
            _choices.Clear();
            Stage?.ShowChoices(_choices);
            if (Line == line) Continue(Dialogue.graph.Next(line, port));
        }

        // ?? ?낅젰 ?????????????????????????????????????????????

        // ?대┃: ?곗텧 ?湲?以묒씠硫??곗텧???앸궡怨? ??댄븨 以묒씠硫?湲?먮? ??蹂댁씠怨? ???섏솕?쇰㈃ ?ㅼ쓬?쇰줈.
        public void Advance()
        {
            switch (Phase)
            {
                case DialoguePhase.WaitingForText:
                    var line = Line;
                    FinishAll(run => run.Line == line);
                    if (Phase == DialoguePhase.WaitingForText && Line == line) BeginTyping();
                    break;
                case DialoguePhase.Typing:
                    Typed();
                    break;
                case DialoguePhase.Shown:
                    Leave(LineNode.NextPort);
                    break;
            }
        }

        // ?좏깮吏 怨좊Ⅴ湲?Choices???쒖꽌).
        public bool Choose(int choiceIndex)
        {
            if (Phase != DialoguePhase.Choosing || choiceIndex < 0 || choiceIndex >= _choices.Count || !_choices[choiceIndex].Enabled) return false;
            Leave(LineNode.OptionPrefix + _choices[choiceIndex].OptionId);
            return true;
        }

        // 吏꾪뻾 以묒씤 ?곗텧??紐⑤몢 ??紐⑥뒿?쇰줈 蹂대궡怨?湲?먮? ??蹂댁씤??鍮⑤━ 媛먭린).
        public void FastForward()
        {
            FinishAll(null);
            if (Phase == DialoguePhase.WaitingForText) BeginTyping();
            if (Phase == DialoguePhase.Typing) Typed();
        }

        // ?? ?쒓컙 ?????????????????????????????????????????????

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0) deltaTime = 0;
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
                    Progress(run, overflow);
                    continue;
                }
                Progress(run, deltaTime);
            }

            if (Phase == DialoguePhase.Typing)
            {
                var speed = Line.typingSpeed > 0 ? Line.typingSpeed : DefaultTypingSpeed;
                _visible += speed * deltaTime;
                if (_visible >= TextLength) Typed();
                else Stage?.SetVisibleCharacters(VisibleCharacters);
            }
            else if (Phase == DialoguePhase.Shown && Line != null && Line.autoAdvance >= 0)
            {
                _autoTimer += deltaTime;
                if (_autoTimer >= Line.autoAdvance) Leave(LineNode.NextPort);
            }
        }

        // ?? ?좏샇? ?④낵 ???????????????????????????????????????

        void Fire(Scope scope, GraphNode from, string port)
        {
            if (from == null || scope == null) return;
            if (_depth >= MaxSignalDepth)
            {
                Debug.LogWarning("[??? ?좏샇媛 ?덈Т 源딄쾶 ?댁뼱吏묐땲???쒓컙 0???④낵?쇰━ 怨좊━媛 ?덈뒗吏 ?뺤씤?섏꽭??.");
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
                var context = new EffectContext { Speaker = run.Scope.Context.Character(presetEffect.character) };
                run.Child = new Scope { Graph = preset?.graph ?? new NodeGraph(), Context = context, Parent = run.Scope, OwnerRun = run };
                Fire(run.Scope, effect, EffectNode.StartedPort);
                // ?덉쓽 ?④낵媛 紐⑤몢 ?앸굹???꾨즺. ?꾨Т寃껊룄 ?쒖옉?섏? ?딆쑝硫?諛붾줈 ?꾨즺.
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

        void Progress(Run run, float deltaTime)
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
                // ?꾨━?? ?덉쓽 ?④낵瑜?紐⑤몢 ?앸궦??媛??④낵 ?꾨즺媛 李⑤?濡????ㅽ뻾???꾨즺?쒗궓??.
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

        // 議곌굔??留욌뒗 ?ㅽ뻾 以??④낵瑜???紐⑥뒿?쇰줈 蹂대궦?? ?앸굹硫??댁뼱吏??④낵??媛숈? 議곌굔?대㈃) 李⑤?濡??앸궦??
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

        // ?? 湲???????????????????????????????????????????????

        string ResolveText(string key, string source)
        {
            var args = TextArguments();
            if (!string.IsNullOrEmpty(key) && Texts.Has(key)) return Texts.Get(key, args);
            return Texts.Catalog.FormatTemplate(source ?? "", args);
        }

        // 臾몄옣 ???{媛?: ?뚮젅?댁뼱 ?곗씠???? {player.gold}).
        IReadOnlyDictionary<string, object> TextArguments()
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
                        // ?쎌쓣 ???녿뒗 媛믪? 嫄대꼫?대떎.
                    }
                }
            return args;
        }

        // ?쒓렇(<??)瑜?類 湲????TextMeshPro??maxVisibleCharacters? 媛숈? 湲곗?).
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
