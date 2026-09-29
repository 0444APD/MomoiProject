using System;
using System.Collections.Generic;

namespace GameSystems
{
    // 되돌리기 방식.
    public enum HistoryMode
    {
        // 행동마다 바뀐 값을 기록해 한 행동씩 무른다(턴제·퍼즐 등). 기본.
        Actions,
        // 행동을 기록하지 않고 일정 간격 스냅샷을 쌓아 한 스냅샷씩 되감는다(실시간 게임).
        Snapshots,
        // 되돌리기를 쓰지 않는다(기록 비용 없음).
        None,
    }

    public sealed class SessionOptions
    {
        public ulong Seed = 1;
        public int JournalCapacity = 4096;
        // 되돌리기 방식. 어느 방식이든 게임 코드는 session.Undo() 하나로 되돌린다.
        public HistoryMode History = HistoryMode.Actions;
        // Actions: 무를 수 있는 최대 행동 수. 0이면 무제한(행동마다 바뀐 값의 이전 모습을 보관하므로 메모리가 늘어난다).
        public int HistoryLimit;
        // Snapshots: 스냅샷 간격(게임 시간 초)과 보관 개수.
        public double SnapshotInterval = 1.0;
        public int SnapshotLimit = 30;
        // 저장 데이터 버전. 출시 후 데이터 구조를 바꾸면 올리고 Migrations에 변환을 등록한다.
        public int SaveVersion = 1;
        // 저장 버전 변환. 불러오기 전에 등록되도록 옵션에 둔다(GameSystemsRuntime.Configuring에서 등록).
        public SaveMigrations Migrations = new SaveMigrations();
        // Modify를 거치지 않은 데이터 수정 검사(행동마다·프레임마다 값 전체 비교). 출시 빌드에서는 끈다.
        public bool DetectUntrackedChanges = true;
    }

    // 한 플레이어(또는 한 판)의 모든 시스템을 묶는 인스턴스. static 전역 상태를 두지 않는다.
    // 멀티플레이어에서는 플레이어·매치마다 세션을 만들고, 테스트에서는 독립된 세션을 만든다.
    //
    // 행동은 session.Do로 실행되는 즉시 끝난다. 되돌리기는 session.Undo로, 방식(행동 단위·스냅샷)과 상관없이 같다.
    // 되돌리면 행동이 바꾼 값·난수·(스냅샷이면 시간까지) 돌아가고, 도전과제 달성과 보상은 유지된다.
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
        public SaveMigrations Migrations => Options.Migrations;

        // 행동별 실행 전 난수 상태. 무르면 난수도 되돌려 같은 행동이 같은 결과를 낸다(결정론, 무르기 악용 차단).
        readonly Dictionary<long, Dictionary<string, ulong[]>> _randomBefore = new Dictionary<long, Dictionary<string, ulong[]>>();

        sealed class Snapshot
        {
            // 찍은 시점의 마지막 기록 id. 같은 틱 안에서도 이후에 일어난 일을 가려낸다.
            public long JournalId;
            public long Tick;
            public double Time;
            public Dictionary<string, object> Session;
            public Dictionary<string, object> Meta;
            public Dictionary<string, ulong[]> Random;
        }

        readonly List<Snapshot> _snapshots = new List<Snapshot>();
        double _lastSnapshotTime = double.NegativeInfinity;

        public GameSession(SessionOptions options = null)
        {
            Options = options ?? new SessionOptions();
            Clock = new GameClock();
            Journal = new Journal(Options.JournalCapacity) { TickSource = () => Clock.Tick };
            State = new StateStore(Journal)
            {
                DetectUntrackedChanges = Options.DetectUntrackedChanges,
                RecordHistory = Options.History == HistoryMode.Actions,
                HistoryLimit = Math.Max(0, Options.HistoryLimit),
            };
            Events = new EventBus(Journal);
            Random = new RandomStreams(Options.Seed);
            Actions = new ActionDispatcher(Journal, State, Clock);
            Stats = new StatSystem(State, Events, Journal);
            Achievements = new AchievementSystem(this);
            Debug = new DebugCommands();

            // 테스트 이벤트는 게임 행동과 같은 경로로 처리된다(무르기·기록 동일).
            Actions.Register<SimulatedEventAction>(action =>
            {
                foreach (var evt in action.Events) Events.PublishObject(evt);
            });

            Actions.TransactionStarted += id => _randomBefore[id] = Random.ExportStates();
            Actions.TransactionAborted += RestoreRandom;
            State.TransactionUndone += tx => RestoreRandom(tx.Id);
            State.TransactionForgotten += tx => _randomBefore.Remove(tx.Id);
            // 이력에 남지 않는 행동(스냅샷·없음 방식)의 난수 상태는 바로 버린다.
            Actions.Executed += record =>
            {
                if (Options.History != HistoryMode.Actions) _randomBefore.Remove(record.TransactionId);
            };

            Debug.Register("세션", "무르기 (Undo)", () => Undo(), "마지막 행동(스냅샷 방식이면 마지막 스냅샷)으로 되돌립니다.");
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

        // 행동을 실행한다. 실행되는 즉시 끝난다(대기·확정 단계 없음).
        public ActionRecord Do(GameAction action)
        {
            var record = Actions.Execute(action);
            if (State.DetectUntrackedChanges) State.CheckUntrackedChanges();
            Achievements.Flush();
            return record;
        }

        // 되돌릴 수 있는 횟수(행동 방식: 이력의 행동 수, 스냅샷 방식: 보관된 스냅샷 수).
        public int UndoCount =>
            Options.History == HistoryMode.Actions ? State.HistoryCount :
            Options.History == HistoryMode.Snapshots ? _snapshots.Count : 0;

        // 마지막 행동(스냅샷 방식이면 마지막 스냅샷)으로 되돌린다. 되돌릴 것이 없으면 false.
        public bool Undo()
        {
            switch (Options.History)
            {
                case HistoryMode.Actions:
                    var undone = State.UndoLast();
                    if (undone) Achievements.Flush();
                    return undone;
                case HistoryMode.Snapshots:
                    return RewindSnapshot();
                default:
                    return false;
            }
        }

        // ── 스냅샷(실시간 게임) ─────────────────────────────────

        // 지금 상태를 스냅샷으로 남긴다. 스냅샷 방식이면 EndFrame이 간격마다 자동으로 부른다(중요한 순간에 직접 불러도 된다).
        public void CaptureSnapshot()
        {
            if (State.InTransaction) throw new InvalidOperationException("[GameSystems] 행동 실행 중에는 스냅샷을 남길 수 없습니다.");
            _snapshots.Add(new Snapshot
            {
                JournalId = Journal.LastId,
                Tick = Clock.Tick,
                Time = Clock.Time,
                Session = State.Export(StateZone.Session, report: false),
                Meta = State.Export(StateZone.Meta, report: false),
                Random = Random.ExportStates(),
            });
            _lastSnapshotTime = Clock.Time;
            var limit = Math.Max(1, Options.SnapshotLimit);
            while (_snapshots.Count > limit) _snapshots.RemoveAt(0);
        }

        bool RewindSnapshot()
        {
            if (_snapshots.Count == 0) return false;
            var snapshot = _snapshots[_snapshots.Count - 1];
            _snapshots.RemoveAt(_snapshots.Count - 1);
            // 찍은 뒤로 아무 일도 없었으면 되감아도 달라지는 게 없으므로 한 칸 더 뒤로 간다.
            if (snapshot.JournalId >= Journal.LastId && _snapshots.Count > 0)
            {
                snapshot = _snapshots[_snapshots.Count - 1];
                _snapshots.RemoveAt(_snapshots.Count - 1);
            }

            var entry = Journal.Append(JournalKind.Undo, "Session", $"되감기: 틱 {snapshot.Tick} ({Clock.Time - snapshot.Time:0.0}초 전)");
            using (Journal.Cause(entry.Id))
            {
                State.Restore(snapshot.Session, StateZone.Session, "되감기");
                State.Restore(snapshot.Meta, StateZone.Meta, "되감기");
                Random.Restore(snapshot.Random);
                Clock.Rewind(snapshot.Tick, snapshot.Time);
                Actions.MarkUndoneAfter(snapshot.JournalId);
                // 되감은 구간에 달성한 도전과제는 유지한다(달성·보상 다시 적용).
                Achievements.ReapplyUnlocksAfter(snapshot.JournalId);
            }
            _lastSnapshotTime = snapshot.Time;
            Achievements.Flush();
            return true;
        }

        // ── 테스트 이벤트 ──────────────────────────────────────
        // 값을 덮어쓰지 않고 실제 이벤트를 발생시켜 게임과 같은 연쇄 반응을 일으킨다. 하나의 행동으로 기록된다.

        public ActionRecord SimulateEvents(IEnumerable<object> events, string reason = null)
        {
            var action = new SimulatedEventAction { reason = reason };
            action.Events.AddRange(events);
            if (action.Events.Count == 0) throw new ArgumentException("[GameSystems] 발생시킬 이벤트가 없습니다.");
            return Do(action);
        }

        public bool TrySimulateStat(string statId, double amount, out string description, out string error)
        {
            description = null;
            if (!EventSimulator.TryPlanForStat(Stats, statId, amount, out var plan, out error)) return false;
            description = plan.Description;
            SimulateEvents(plan.Events, $"{StatName(statId)} {(Stats.Find(statId).Aggregate == StatAggregate.Sum ? "+" : "기록 ")}{amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
            return true;
        }

        // 도전과제 조건을 이벤트로 채운다. 필요한 이벤트 전체가 하나의 행동(무르기 단위)이 된다.
        public bool TrySimulateAchievement(string achievementId, out string error)
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
            SimulateEvents(events, $"'{AchievementName(achievementId)}' 조건 채우기");
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
            if (State.InTransaction) return;
            if (State.DetectUntrackedChanges) State.CheckUntrackedChanges();
            Achievements.Flush();
            if (Options.History == HistoryMode.Snapshots && Clock.Time - _lastSnapshotTime >= Options.SnapshotInterval)
                CaptureSnapshot();
        }
        // ── 저장 ───────────────────────────────────────────────
        // 메타 구역(프로필)만 저장한다. 세션 구역은 판이 끝나면 사라지는 게임 내용이다.

        public string ExportMeta()
        {
            var root = new Dictionary<string, object>
            {
                ["version"] = (double)Options.SaveVersion,
                ["state"] = State.Export(StateZone.Meta),
            };
            return MiniJson.Serialize(root);
        }

        public void ImportMeta(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> root)) throw new FormatException("[GameSystems] 저장 데이터 형식이 올바르지 않습니다.");
            var version = root.TryGetValue("version", out var v) && v is double number ? (int)number : 1;
            if (version > Options.SaveVersion)
                throw new FormatException($"[GameSystems] 저장 데이터가 이 게임보다 새 버전입니다(저장 v{version}, 게임 v{Options.SaveVersion}). 게임을 업데이트하세요.");
            if (root.TryGetValue("state", out var state) && state is Dictionary<string, object> values)
            {
                if (version < Options.SaveVersion) Migrations.Run(values, version, Options.SaveVersion, Journal);
                State.Import(values, StateZone.Meta);
            }
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
