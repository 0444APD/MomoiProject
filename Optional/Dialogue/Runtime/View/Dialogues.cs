using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GBible.Data;
using UnityEngine;

namespace GBible.Dialogue
{
    // 다이얼로그 관리자: 어디서든 Dialogues.Play("다이얼로그 ID")로 다이얼로그를 연다.
    // 씬에 DialoguePlayer가 있으면 그것을, 없으면 설정의 프리팹을 만들어 씬이 바뀌어도 남겨 두고(숨김) 필요할 때 바로 보인다.
    public static class Dialogues
    {
        static DialoguePlayer _player;

        // 설정 읽기(테스트는 바꿔 끼운다).
        public static Func<DialogueSettings> SettingsSource = DialogueSettings.Load;

        public static event Action<string> Started;
        public static event Action Ended;
        public static event Action<string> Signaled;

        // 대사가 열려 있는 동안 게임 입력을 막는다(GameView.Busy가 참 — 화면 버튼의 행동이 무시된다). 걸으면서 듣는 대사처럼 막지 않을 게임은 거짓으로.
        public static bool BlocksInput = true;

        // ── 한 채널(대사는 늘 하나만 — 병렬 없음) ─────────────────
        // 대사가 열린 동안 다른 행동이 다이얼로그를 열면 끼어든다: 지금 대사의 진행 기록만 남기고 멈춘 뒤 새 다이얼로그를 열고,
        // 그것이 끝나면 남긴 기록으로 이전 다이얼로그를 다시 연다(진행 기록을 조용히 다시 밟아 그 대사로 — 값 바꾸기 · 신호는 다시 하지 않는다).
        // 같은 행동이 한꺼번에 연 것(마지막 칸 이벤트 대사 + 엔딩)은 요청한 순서대로 앞이 끝난 뒤 연다(끼어들지 않는다).
        // 그래프의 '다른 다이얼로그로' 노드는 돌아오지 않는 이동이라 여기에 쌓이지 않는다. 끼어든 것도 판 저장에 들어간다(진행 기록의 resume).
        sealed class Frame
        {
            public DialogueProgress Progress;
            public List<(string dialogue, string entry)> Waiting;
        }

        // 지금 대사 뒤에 차례로 열 것(같은 행동이 연 것).
        static readonly Queue<(string dialogue, string entry)> Waiting = new Queue<(string dialogue, string entry)>();
        // 끼어든 다이얼로그가 끝나면 돌아갈 곳(마지막이 가장 최근).
        static readonly List<Frame> Frames = new List<Frame>();
        // 끼어들기를 쌓는 깊이 한도(대화가 서로를 끝없이 부르는 고리 막기) — 넘으면 끼어들지 않고 지금 대사 뒤에 연다.
        public const int MaxDepth = 8;
        // 지금 대사를 연 프레임(같은 프레임에 온 요청 = 같은 행동이 연 것).
        static int _batchFrame = -1;
        // 끼어들려고 지금 대사를 멈추는 중(끝 알림을 '다 끝남'으로 보지 않는다).
        static bool _switching;

        public static int WaitingCount => Waiting.Count;
        // 끼어든 다이얼로그가 끝나면 돌아갈 대화 수.
        public static int InterruptedCount => Frames.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _player = null;
            SettingsSource = DialogueSettings.Load;
            Started = null;
            Ended = null;
            Signaled = null;
            BlocksInput = true;
            Waiting.Clear();
            Frames.Clear();
            _batchFrame = -1;
            _switching = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void HookSessions()
        {
            GameView.AddBusy(() => BlocksInput && IsPlaying);
            GBibleRuntime.Started += Prepare;
            // 판을 불러오면 앞 판에서 기다리던 대사 · 돌아갈 곳은 버린다(불러온 판의 것은 ResumeSaved가 진행 기록에서 되살린다).
            GBibleRuntime.RunLoaded += _ => ClearChannel();
            Prepare(GBibleRuntime.Session);
            // 멀티 서버: 플레이어가 들어오면 그 사람의 진행 칸(주인만 보는 칸)을 둔다 — 판을 불러온 뒤 저장된 진행이 그 사람에게 간다.
            GameNet.ServerStarted += WatchServer;
            GameNet.RoomStarting += room => WatchServer(room?.Server);
        }

        // 세션마다: 판 저장에 다이얼로그 진행 칸이 들어가도록 정의하고(불러오기 전에 있어야 한다),
        // 보상 '다이얼로그 재생'의 요청을 행동이 성공한 뒤에 받는다(멀티 클라이언트는 서버가 보낸 것을).
        static void Prepare(GameSession session)
        {
            if (session == null) return;
            // 새 세션(슬롯 바꾸기 · 새 판 · 멀티 들어가기): 앞 세션에서 기다리던 대사 · 돌아갈 곳은 버린다.
            if (ReferenceEquals(session, GBibleRuntime.Session)) ClearChannel();
            DialoguePlayer.DefineProgress(session);
            session.Events.SubscribeAfterAction<DialoguePlayRequested>(request => OnPlayRequested(session, request));
        }

        static void WatchServer(NetServer server)
        {
            if (server == null) return;
            foreach (var player in server.Players) DialogueProgressKeys.Prepare(server.Session, player.Id);
            server.PlayerJoined += player => DialogueProgressKeys.Prepare(server.Session, player.Id);
        }

        static void OnPlayRequested(GameSession session, DialoguePlayRequested request)
        {
            if (request == null || !Application.isPlaying || !ReferenceEquals(session, GBibleRuntime.Session)) return;
            // 멀티: 그 행동을 한 플레이어 화면에서만 연다(서버는 그 사람에게만 보낸다 — 방장 화면에서는 방장 것만). 0이면 모두.
            if (request.player != 0 && request.player != session.LocalPlayer) return;
            Request(request.dialogue, string.IsNullOrEmpty(request.entryId) ? "start" : request.entryId);
        }

        // 한 채널로 연다: 열린 대사가 없으면 바로, 같은 행동이 연 것(같은 프레임)이면 지금 대사 뒤에, 다른 행동이 연 것이면 끼어든다.
        static bool Request(string dialogue, string entry)
        {
            if (!IsPlaying)
            {
                Waiting.Enqueue((dialogue, entry));
                return Next();
            }
            if (Time.frameCount == _batchFrame || Frames.Count >= MaxDepth)
            {
                if (Frames.Count >= MaxDepth) Debug.LogWarning($"[다이얼로그] 끼어들기가 {MaxDepth}겹을 넘어 '{dialogue}'은(는) 지금 대사 뒤에 엽니다(다이얼로그가 서로를 부르는 고리인지 확인).");
                Waiting.Enqueue((dialogue, entry));
                return true;
            }
            // 끼어들기: 지금 대사의 진행 기록과 그 뒤에 기다리던 것을 남기고 멈춘 뒤 새 다이얼로그를 연다.
            var progress = _player?.CurrentProgress;
            if (progress != null) Frames.Add(new Frame { Progress = progress, Waiting = new List<(string, string)>(Waiting) });
            Waiting.Clear();
            _switching = true;
            try { _player?.Stop(); }
            finally { _switching = false; }
            Waiting.Enqueue((dialogue, entry));
            return Next();
        }

        // 다음 것을 연다: 지금 대사 뒤에 기다리던 것 → 없으면 끼어들기 전의 다이얼로그로 돌아간다. 연 것이 있으면 참.
        // 재생기가 꺼지는 중(씬을 닫음 · 끔 — 끄면서 대사를 멈춰 끝 알림이 온다)이면 열지 않는다 — 꺼지는 재생기에서 연 대사는 사라지고 게임 시간 멈춤만 남는다.
        // 기다리던 것은 다음 요청 때 이어 연다.
        static bool Next()
        {
            if (_player != null && !_player.isActiveAndEnabled) return false;
            while (!IsPlaying)
            {
                if (Waiting.Count > 0)
                {
                    var (dialogue, entry) = Waiting.Dequeue();
                    if (StartNow(dialogue, entry)) return true;
                    continue;
                }
                if (Frames.Count == 0) return false;
                var frame = Frames[Frames.Count - 1];
                Frames.RemoveAt(Frames.Count - 1);
                foreach (var waiting in frame.Waiting) Waiting.Enqueue(waiting);
                var player = Ensure();
                if (player != null && player.Restore(frame.Progress))
                {
                    _batchFrame = Time.frameCount;
                    Started?.Invoke(frame.Progress.dialogue);
                    return true;
                }
            }
            return true;
        }

        static void ClearChannel()
        {
            Waiting.Clear();
            Frames.Clear();
            _batchFrame = -1;
        }

        // 판 저장에 함께 넣을 돌아갈 곳 · 기다리던 것(진행 기록의 resume 칸 — JSON). 없으면 빈 글.
        internal static string ResumeJson()
        {
            if (Frames.Count == 0 && Waiting.Count == 0) return "";
            var state = new DialogueResumeState();
            foreach (var frame in Frames)
                state.frames.Add(new DialogueResumeFrame { progress = JsonUtility.ToJson(frame.Progress), waiting = frame.Waiting.Select(w => w.dialogue + "|" + w.entry).ToList() });
            state.waiting = Waiting.Select(w => w.dialogue + "|" + w.entry).ToList();
            return JsonUtility.ToJson(state);
        }

        // 저장된 진행 기록을 이어 열 때: 돌아갈 곳 · 기다리던 것도 되살린다.
        internal static void LoadResume(string json)
        {
            ClearChannel();
            if (string.IsNullOrEmpty(json)) return;
            DialogueResumeState state;
            try { state = JsonUtility.FromJson<DialogueResumeState>(json); }
            catch (Exception) { return; }
            if (state == null) return;
            (string, string) Parse(string text)
            {
                var bar = text?.IndexOf('|') ?? -1;
                return bar < 0 ? (text ?? "", "start") : (text.Substring(0, bar), text.Substring(bar + 1));
            }
            foreach (var frame in state.frames ?? new List<DialogueResumeFrame>())
            {
                var progress = string.IsNullOrEmpty(frame.progress) ? null : JsonUtility.FromJson<DialogueProgress>(frame.progress);
                if (progress != null) Frames.Add(new Frame { Progress = progress, Waiting = (frame.waiting ?? new List<string>()).Select(Parse).ToList() });
            }
            foreach (var waiting in state.waiting ?? new List<string>()) Waiting.Enqueue(Parse(waiting));
            _batchFrame = Time.frameCount;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var settings = SettingsSource();
            if (settings != null && settings.createOnStart) Ensure();
        }

        // 지금 쓰는 다이얼로그 재생기(없으면 만든다). 만들 수 없으면 null.
        public static DialoguePlayer Player => Ensure();

        public static bool IsPlaying => _player != null && _player.IsPlaying;

        static DialoguePlayer Ensure()
        {
            if (_player != null) return _player;
            var found = UnityEngine.Object.FindAnyObjectByType<DialoguePlayer>();
            if (found == null)
            {
                var settings = SettingsSource();
                if (settings == null || settings.playerPrefab == null)
                {
                    Debug.LogWarning("[다이얼로그] 다이얼로그 재생기가 없습니다. 씬에 다이얼로그 UI 프리팹을 두세요(AI가 DialogueTemplate.Create로 만든다).");
                    return null;
                }
                var instance = UnityEngine.Object.Instantiate(settings.playerPrefab);
                instance.name = "[다이얼로그] " + settings.playerPrefab.name;
                UnityEngine.Object.DontDestroyOnLoad(instance);
                found = instance.GetComponent<DialoguePlayer>();
                if (found == null)
                {
                    Debug.LogWarning("[다이얼로그] 설정의 프리팹에 DialoguePlayer가 없습니다.", settings);
                    UnityEngine.Object.Destroy(instance);
                    return null;
                }
            }
            Attach(found);
            return _player;
        }

        static void Attach(DialoguePlayer player)
        {
            _player = player;
            player.Signaled += signal => Signaled?.Invoke(signal);
            player.onEnded.AddListener(() =>
            {
                if (_switching) return;   // 끼어들려고 멈춘 것 — 다 끝난 것이 아니다
                // 다음 것(기다리던 것 · 돌아갈 곳)이 열리면 '끝'이 아니다. 아무것도 남지 않았을 때만 Ended.
                if (!Next()) Ended?.Invoke();
            });
        }

        // 다이얼로그를 연다(한 채널 — 위 '한 채널'). 열린 대사가 없으면 바로 열고 참. 열린 대사가 있으면 끼어들거나(다른 행동 · 코드가 연 것)
        // 지금 대사 뒤에 연다(같은 프레임에 연 것). 없는 다이얼로그 · 시작 ID면 거짓.
        public static bool Play(string dialogueId, string entryId = "start") => Request(dialogueId, string.IsNullOrEmpty(entryId) ? "start" : entryId);

        // 바로 연다(채널 규칙 없이 — Next가 쓴다).
        static bool StartNow(string dialogueId, string entryId)
        {
            var player = Ensure();
            if (player == null) return false;
            if (!player.Play(dialogueId, entryId)) return false;
            _batchFrame = Time.frameCount;
            Started?.Invoke(dialogueId);
            return true;
        }

        // 판을 불러온 뒤: 저장된 다이얼로그 진행이 있으면 그 대사부터 이어 간다.
        public static bool ResumeSaved()
        {
            var player = Ensure();
            if (player == null || !player.ResumeSaved(out var resume)) return false;
            LoadResume(resume);
            Started?.Invoke(player.CurrentDialogue);
            return true;
        }

        public static bool Advance() => _player != null && _player.Advance();
        public static bool Choose(int index) => _player != null && _player.Choose(index);
        public static void SkipToChoice() => _player?.SkipToChoice();

        // 대사를 끝낸다(기다리던 것 · 돌아갈 곳도 모두 버린다).
        public static void Stop()
        {
            ClearChannel();
            if (_player != null) _player.Stop();
        }
    }

    // 행동이 성공한 뒤에 쏠 요청(세션마다). 실패한 행동의 것은 버린다. 세션이 사라지면 함께 사라진다.
    sealed class AfterActionRequests
    {
        static readonly ConditionalWeakTable<GameSession, AfterActionRequests> Table = new ConditionalWeakTable<GameSession, AfterActionRequests>();
        readonly List<object> _pending = new List<object>();

        AfterActionRequests(GameSession session)
        {
            session.Actions.Executed += record =>
            {
                if (record.Nested || _pending.Count == 0) return;
                var ready = _pending.ToArray();
                _pending.Clear();
                if (record.Status != ActionStatus.Done) return;
                foreach (var evt in ready) session.Events.PublishObject(evt);
            };
            session.Actions.TransactionAborted += _ => _pending.Clear();
        }

        public static void Add(GameSession session, object evt) => Table.GetValue(session, s => new AfterActionRequests(s))._pending.Add(evt);
    }

    // 보상 '다이얼로그 재생'이 낸 요청(행동이 성공한 뒤 그 플레이어 화면에서 연다 — SubscribeAfterAction으로 받는다).
    // 멀티: 서버가 그 행동을 한 플레이어에게만 보낸다(IOwnedEvent, 0이면 모두).
    [Serializable, GameEvent("다이얼로그 재생 요청")]
    public sealed class DialoguePlayRequested : IOwnedEvent
    {
        public string dialogue;
        public string entryId;
        public int player;

        public int Owner => player;
    }

    // 보상·값 바꾸기 블록에서 다이얼로그를 연다(도전과제 달성 연출, 다이얼로그에서 다른 다이얼로그로 넘기기 등).
    // 규칙 안에서 바로 열지 않고 요청 이벤트를 쏜다: 행동이 성공한 뒤에 열리고(실패·미리보기에서는 열리지 않음),
    // 멀티에서는 그 행동을 한 플레이어 화면에서 열린다. 스냅샷 되감기가 보상을 다시 줄 때는 다시 열지 않는다.
    [Serializable]
    [BlockInfo("다이얼로그 재생", Category = "다이얼로그 시스템", Description = "다이얼로그를 엽니다(다이얼로그 관리자). 행동이 성공한 뒤, 그 행동을 한 플레이어 화면에서 열립니다.", Sentence = "다이얼로그 {dialogue} · {entryId}")]
    public sealed class PlayDialogueReward : Reward
    {
        [FieldLabel("다이얼로그"), DataRef(typeof(DialogueData))] public string dialogue = "";
        [FieldLabel("시작 ID")] public string entryId = "start";

        // 게임 상태를 바꾸지 않는 연출이다(되돌리기에 영향 없음).
        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session)
        {
            if (session == null || string.IsNullOrEmpty(dialogue) || session.Actions.IsPreviewing || FromRewind(session)) return;
            var request = new DialoguePlayRequested { dialogue = dialogue, entryId = entryId, player = ActorOf(session) };
            // 행동 안이지만 되돌리지 않는 구역(도전과제 보상 — Data.Permanent)에서는 이벤트가 바로 나가므로(연출·전송 모두), 그 행동이 성공한 뒤에 쏜다.
            if (!session.Data.InTransaction && InsideAction(session)) AfterActionRequests.Add(session, request);
            else session.Events.Publish(request);
        }

        // 지금 일어나는 일의 원인(기록)에 되감기·Undo가 있는가(스냅샷 되감기가 도전과제 보상을 다시 줄 때).
        static bool FromRewind(GameSession session)
        {
            foreach (var entry in session.Journal.CauseChain(session.Journal.CurrentCause))
                if (entry.Kind == JournalKind.Undo) return true;
            return false;
        }

        // 행동을 실행하는 중인가(원인 사슬에 행동이 있다 — 행동이 끝난 뒤의 조건 충족 달성은 아니다).
        static bool InsideAction(GameSession session)
        {
            foreach (var entry in session.Journal.CauseChain(session.Journal.CurrentCause))
                if (entry.Kind == JournalKind.Action) return true;
            return false;
        }

        // 지금 행동을 한 플레이어(원인 사슬의 맨 바깥 행동 — 멀티 서버는 받은 행동의 번호를 보낸 사람으로 덮는다). 행동 밖이면 0(모두).
        static int ActorOf(GameSession session)
        {
            foreach (var entry in session.Journal.CauseChain(session.Journal.CurrentCause))
                if (entry.Kind == JournalKind.Action && entry.Payload is GameAction action) return action.actorId;
            return 0;
        }

        public override string Describe(INameResolver names) => $"다이얼로그 {dialogue} · {entryId}";
        public override string Natural(INameResolver names) => $"다이얼로그 '{dialogue}' 재생";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(dialogue)) context.Error("재생할 다이얼로그가 선택되지 않았습니다.");
        }
    }
}
