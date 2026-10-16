using System;
using System.Collections.Generic;
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

        // 행동이 연 다이얼로그(결과 '다이얼로그 재생')가 이미 열린 대사가 있을 때 기다리는 줄 — 앞 대사가 끝나면 차례로 연다(끊지 않는다).
        // 코드에서 바로 부른 Play는 기다리지 않고 지금 대사를 바꾼다.
        static readonly Queue<(string dialogue, string entry)> Waiting = new Queue<(string dialogue, string entry)>();
        public static int WaitingCount => Waiting.Count;

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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void HookSessions()
        {
            GameView.AddBusy(() => BlocksInput && IsPlaying);
            GBibleRuntime.Started += Prepare;
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
            var entry = string.IsNullOrEmpty(request.entryId) ? "start" : request.entryId;
            // 이미 열린 대사가 있으면(같은 행동이 대화를 둘 요청 — 마지막 칸 이벤트 + 엔딩 …) 끝난 뒤 차례로.
            if (IsPlaying || Waiting.Count > 0) Waiting.Enqueue((request.dialogue, entry));
            else Play(request.dialogue, entry);
        }

        // 대사가 끝나면 기다리던 다음 대사를 연다.
        static void PlayNextWaiting()
        {
            while (Waiting.Count > 0 && !IsPlaying)
            {
                var (dialogue, entry) = Waiting.Dequeue();
                if (Play(dialogue, entry)) return;
            }
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
                Ended?.Invoke();
                PlayNextWaiting();
            });
        }

        public static bool Play(string dialogueId, string entryId = "start")
        {
            var player = Ensure();
            if (player == null) return false;
            if (!player.Play(dialogueId, entryId)) return false;
            Started?.Invoke(dialogueId);
            return true;
        }

        // 판을 불러온 뒤: 저장된 다이얼로그 진행이 있으면 그 대사부터 이어 간다.
        public static bool ResumeSaved()
        {
            var player = Ensure();
            if (player == null || !player.ResumeSaved()) return false;
            Started?.Invoke(player.CurrentDialogue);
            return true;
        }

        public static bool Advance() => _player != null && _player.Advance();
        public static bool Choose(int index) => _player != null && _player.Choose(index);
        public static void SkipToChoice() => _player?.SkipToChoice();

        public static void Stop()
        {
            Waiting.Clear();
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
