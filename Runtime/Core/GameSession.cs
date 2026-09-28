using System;
using System.Collections.Generic;

namespace GameSystems
{
    public sealed class SessionOptions
    {
        public ulong Seed = 1;
        // true: 행동마다 즉시 확정(되감기가 없는 일반 게임).
        // false: Commit()을 부를 때까지 확정 대기(턴제 무르기·되감기 게임).
        public bool AutoCommit = true;
        public int JournalCapacity = 4096;
    }

    // 한 플레이어(또는 한 판)의 모든 시스템을 묶는 인스턴스. static 전역 상태를 두지 않는다.
    // 멀티플레이어에서는 플레이어·매치마다 세션을 만들고, 테스트에서는 독립된 세션을 만든다.
    public sealed class GameSession : INameResolver
    {
        public Journal Journal { get; }
        public StateStore State { get; }
        public EventBus Events { get; }
        public GameClock Clock { get; }
        public RandomStreams Random { get; }
        public ActionDispatcher Actions { get; }
        public StatSystem Stats { get; }
        public AchievementSystem Achievements { get; }
        public DebugCommands Debug { get; }
        public SessionOptions Options { get; }

        // 확정 전 행동별 난수 상태. 무르면 난수도 되돌려 같은 행동이 같은 결과를 낸다(결정론, 무르기 악용 차단).
        readonly Dictionary<long, Dictionary<string, ulong[]>> _randomBefore = new Dictionary<long, Dictionary<string, ulong[]>>();

        public GameSession(SessionOptions options = null)
        {
            Options = options ?? new SessionOptions();
            Clock = new GameClock();
            Journal = new Journal(Options.JournalCapacity) { TickSource = () => Clock.Tick };
            State = new StateStore(Journal);
            Events = new EventBus(Journal);
            Random = new RandomStreams(Options.Seed);
            Actions = new ActionDispatcher(Journal, State, Clock);
            Stats = new StatSystem(State, Events, Journal);
            Achievements = new AchievementSystem(this);
            Debug = new DebugCommands();

            // 테스트 이벤트는 게임 행동과 같은 경로로 처리된다(무르기·확정·기록 동일).
            Actions.Register<SimulatedEventAction>(action =>
            {
                foreach (var evt in action.Events) Events.PublishObject(evt);
            });

            Actions.TransactionStarted += id => _randomBefore[id] = Random.ExportStates();
            Actions.TransactionAborted += RestoreRandom;
            State.TransactionUndone += tx => RestoreRandom(tx.Id);
            State.TransactionCommitted += tx => _randomBefore.Remove(tx.Id);

            Debug.Register("세션", "확정 (Commit)", Commit, "확정 대기 중인 행동을 모두 확정합니다.");
            Debug.Register("세션", "무르기 (Undo)", () => Undo(), "확정되지 않은 마지막 행동을 되돌립니다.");
            Debug.Register("세션", "이번 판 수치 초기화", () => Stats.ResetRun());
        }

        public IEnumerable<IInspectable> Inspectables
        {
            get
            {
                yield return Stats;
                yield return Achievements;
                yield return Actions;
                yield return State;
                yield return Random;
            }
        }

        public ActionRecord Do(GameAction action)
        {
            var record = Actions.Execute(action);
            // 행동 직후 평가: 도전과제는 확정을 기다리지 않고 달성된다(보상은 확정 시 지급).
            Achievements.Flush();
            if (Options.AutoCommit) Commit();
            return record;
        }

        public void Commit()
        {
            State.Commit();
            Achievements.GrantDeferred();
            Achievements.Flush();
        }

        public bool Undo() => State.UndoLast();

        // ── 테스트 이벤트 ──────────────────────────────────────
        // 값을 덮어쓰지 않고 실제 이벤트를 발생시켜 게임과 같은 연쇄 반응을 일으킨다. commit이면 곧바로 확정까지 한다.

        public ActionRecord SimulateEvents(IEnumerable<object> events, string reason = null, bool commit = false)
        {
            var action = new SimulatedEventAction { reason = reason };
            action.Events.AddRange(events);
            if (action.Events.Count == 0) throw new ArgumentException("[GameSystems] 발생시킬 이벤트가 없습니다.");
            var record = Do(action);
            if (commit && !Options.AutoCommit) Commit();
            return record;
        }

        public bool TrySimulateStat(string statId, double amount, bool commit, out string description, out string error)
        {
            description = null;
            if (!EventSimulator.TryPlanForStat(Stats, statId, amount, out var plan, out error)) return false;
            description = plan.Description;
            SimulateEvents(plan.Events, $"{StatName(statId)} {(Stats.Find(statId).Aggregate == StatAggregate.Sum ? "+" : "기록 ")}{amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}", commit);
            return true;
        }

        // 도전과제 조건을 이벤트로 채운다. 필요한 이벤트 전체가 하나의 행동(무르기 단위)이 된다.
        public bool TrySimulateAchievement(string achievementId, bool commit, out string error)
        {
            if (!EventSimulator.TryPlanForAchievement(this, achievementId, out var steps, out error))
            {
                if (error == null) error = "이미 조건을 만족합니다.";
                return false;
            }
            var events = new List<object>();
            foreach (var (stat, amount) in steps)
            {
                if (!EventSimulator.TryPlanForStat(Stats, stat, amount, out var plan, out error)) return false;
                events.AddRange(plan.Events);
            }
            SimulateEvents(events, $"'{AchievementName(achievementId)}' 조건 채우기", commit);
            return true;
        }

        void RestoreRandom(long transactionId)
        {
            if (!_randomBefore.TryGetValue(transactionId, out var states)) return;
            Random.Restore(states);
            _randomBefore.Remove(transactionId);
        }

        // 호스트(Unity 등)가 매 프레임 호출한다.
        public void EndFrame(double deltaTime)
        {
            Clock.Advance(deltaTime);
            Achievements.Poll(Clock.Time);
            if (!State.InTransaction) Achievements.Flush();
        }

        // ── 저장 ───────────────────────────────────────────────
        // 메타 구역(프로필)만 저장한다. 세션 구역은 판이 끝나면 사라지는 게임 내용이다.

        public string ExportMeta()
        {
            if (State.UncommittedCount > 0) Commit();
            var root = new Dictionary<string, object>
            {
                ["version"] = 1.0,
                ["state"] = State.Export(StateZone.Meta),
            };
            return MiniJson.Serialize(root);
        }

        public void ImportMeta(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> root)) throw new FormatException("[GameSystems] 저장 데이터 형식이 올바르지 않습니다.");
            if (root.TryGetValue("state", out var state) && state is Dictionary<string, object> values)
                State.Import(values, StateZone.Meta);
            Journal.Append(JournalKind.System, "Save", $"메타 상태 불러옴 ({(root.TryGetValue("state", out var s) && s is Dictionary<string, object> d ? d.Count : 0)}개 값)");
        }

        // ── 이름 조회 ───────────────────────────────────────────

        public string StatName(string id)
        {
            var definition = Stats.Find(id);
            return definition?.DisplayName ?? (string.IsNullOrEmpty(id) ? "(수치 없음)" : id);
        }

        public string AchievementName(string id)
        {
            var definition = Achievements.Find(id);
            return definition?.DisplayName ?? (string.IsNullOrEmpty(id) ? "(도전과제 없음)" : id);
        }
    }
}
