using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 되돌리기 방식.
    public enum HistoryMode
    {
        // 행동마다 바뀐 값을 기록해 한 행동씩 Undo한다(턴제·퍼즐 등). 기본.
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
        // Actions: Undo할 수 있는 최대 행동 수. 0이면 무제한(행동마다 바뀐 값의 이전 모습을 보관하므로 메모리가 늘어난다).
        public int HistoryLimit;
        // Snapshots: 스냅샷 간격(게임 시간 초)과 보관 개수.
        public double SnapshotInterval = 1.0;
        public int SnapshotLimit = 30;
        // 저장 데이터 버전의 최솟값. 보통 그대로 두면 된다: 실제 버전은 이 값과 Migrations에 등록한 변환의 가장 높은 버전 중 큰 쪽(GameSession.SaveVersion).
        public int SaveVersion = 1;
        // 저장 버전 변환. 불러오기 전에 등록되도록 옵션에 둔다(GBibleRuntime.Configuring에서 등록).
        public SaveMigrations Migrations = new SaveMigrations();
        // Modify를 거치지 않은 데이터 수정 검사(행동마다·프레임마다 값 전체 비교). 출시 빌드에서는 끈다.
        public bool DetectUntrackedChanges = true;
        // 고정 틱(실시간): 초당 틱 수. 0이면 프레임마다(가변 시간). 켜면 게임 시간이 1/틱 초씩 일정하게 흐르고
        // 시간 규칙(예약·상태 효과·시간 조건)과 session.Ticked가 틱마다 돈다 → 프레임 속도와 상관없이 같은 결과(재현·멀티).
        public int TickRate;
        // 현실 시각의 출처(Game.Now). 비우면 이 컴퓨터의 시계(DateTime.UtcNow). 시험에서는 정해진 시각을, 서버·Steam 시각을 쓸 때는 그것을 준다.
        public Func<DateTime> RealClock;
        // 날짜 번호(Game.Day·Week)를 셀 시간대. 비우면 이 컴퓨터의 시간대.
        public TimeZoneInfo TimeZone;
    }

    // 한 플레이어(또는 한 판)의 모든 시스템을 묶는 인스턴스. static 전역 상태를 두지 않는다.
    // 멀티플레이어에서는 플레이어·매치마다 세션을 만들고, 테스트에서는 독립된 세션을 만든다.
    //
    // 행동은 session.Do로 실행되는 즉시 끝난다. 되돌리기는 session.Undo로, 방식(행동 단위·스냅샷)과 상관없이 같다.
    // 되돌리면 행동이 바꾼 값·난수·(스냅샷이면 시간까지) 돌아가고, 도전과제 달성과 보상은 유지된다.
    public sealed class GameSession : INameResolver
    {
        public Journal Journal { get; }
        public PlayerDataStore Data { get; }
        public EventBus Events { get; }
        public GameClock Clock { get; }
        public RandomStreams Random { get; }
        public ActionDispatcher Actions { get; }
        // 트리거 감시(로그·도전과제·능력 … 공용): 이벤트 받기·조건 충족·사용을 검사해 발동한다.
        public TriggerSystem Triggers { get; }
        public StatSystem Stats { get; }
        public AchievementSystem Achievements { get; }
        // 능력(DB › 능력): 아이템 사용 효과·스킬 등. Game.Use로 쓴다.
        public AbilitySystem Abilities { get; }
        // 능력치 Modifier(Game.Attribute·AddModifier의 저장소).
        public AttributeSystem Attributes { get; }
        // 상태 효과(Game.ApplyStatus)와 예약(Game.ScheduleAfter·ScheduleOn).
        public StatusSystem Statuses { get; }
        public PassiveSystem Passives { get; }
        public ScheduleSystem Schedules { get; }
        // 선택 대기(Game.Ask·Answer).
        public ChoiceSystem Choices { get; }
        // 밸런스 값(Game.Balance)·가중치 표(Game.Roll). 런타임이 DB로 채운다.
        public BalanceSystem Balance { get; }
        public WeightTableSystem Tables { get; }
        // 월드 상태(Game.World·SetWorld).
        public WorldStateSystem World { get; }
        // 게임 코드의 창구(All·Find·Random·Do·Use). Block 안에서는 Game 속성으로 쓴다.
        public GameApi Game { get; }
        public DebugCommands Debug { get; }
        public SessionOptions Options { get; }
        public SaveMigrations Migrations => Options.Migrations;
        // 지금 게임의 저장 버전: 옵션의 값과 등록한 변환의 가장 높은 버전 중 큰 쪽.
        public int SaveVersion => Math.Max(Options.SaveVersion, Options.Migrations.LatestVersion);
        // 저장 파일에 함께 남길 데이터 구조(키·필드 경로 → 형식). 불러올 때 호환 여부를 판단하는 데 쓴다.
        // 런타임이 데이터베이스(플레이어 데이터·수치·도전과제)로부터 채운다. 비어 있으면 남기지 않는다.
        public Dictionary<string, string> SaveSchema;

        // 인스턴스(DB › 인스턴스에서 만든 인스턴스: 아이템 A, 버프 B …)의 원본을 ID로 찾는다. 런타임이 데이터베이스로 채운다.
        // 보상 등은 원본이 아니라 CopyInstance의 깊은 사본을 넣는다(비교 코드가 있으면 생성 코드로, 리플렉션 없음).
        public Func<string, object> Instances;

        public object CopyInstance(string id)
        {
            var source = string.IsNullOrEmpty(id) ? null : Instances?.Invoke(id);
            if (source == null) return null;
            return Mirror.Find(source.GetType()) != null && Mirror.Supports(source.GetType(), out _) ? Mirror.Clone(source) : DataModel.Clone(source, null);
        }

        // 행동별 실행 전 난수 상태. Undo하면 난수도 되돌려 같은 행동이 같은 결과를 낸다(결정론, Undo 악용 차단).
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
            Data = new PlayerDataStore(Journal)
            {
                DetectUntrackedChanges = Options.DetectUntrackedChanges,
                RecordHistory = Options.History == HistoryMode.Actions,
                HistoryLimit = Math.Max(0, Options.HistoryLimit),
            };
            Events = new EventBus(Journal);
            Random = new RandomStreams(Options.Seed);
            Actions = new ActionDispatcher(Journal, Data, Clock) { RealClock = Options.RealClock };
            Triggers = new TriggerSystem(this);
            Stats = new StatSystem(Data, Triggers, Journal);
            Achievements = new AchievementSystem(this);
            Debug = new DebugCommands();
            Abilities = new AbilitySystem();
            Game = new GameApi(this);
            Attributes = new AttributeSystem(this);
            Statuses = new StatusSystem(this);
            Passives = new PassiveSystem(this);
            Schedules = new ScheduleSystem(this);
            Choices = new ChoiceSystem(this);
            Balance = new BalanceSystem();
            Tables = new WeightTableSystem(this);
            World = new WorldStateSystem(this);
            // 처리를 클래스 안에 쓴 행동(Run)은 등록 없이 실행한다. 실행 직전에 참조 필드(id가 있는 객체)를 지금 데이터의 객체로 다시 찾는다.
            Actions.SelfRun = action => action.RunIn(Game);
            Actions.Prepare = action => ActionReferences.Resolve(action, Game);

            // 행동이 끝나기 직전: 데이터에서 사라진 개체(죽은 적)에 걸린 상태 효과·이번 판 Modifier를 그 행동 안에서 정리한다.
            Actions.Committing += () =>
            {
                if (!Statuses.HasAny && !Attributes.HasSessionModifiers) return;
                Data.Sync();
                Statuses.PruneMissing();
                Attributes.PruneMissing();
            };

            // 테스트 이벤트는 게임 행동과 같은 경로로 처리된다(Undo·기록 동일).
            Actions.Register<SimulatedEventAction>(action =>
            {
                foreach (var evt in action.Events) Events.PublishObject(evt);
            });

            // 행동이 성공한 뒤에 받는 쪽(Events.SubscribeAfterAction): 바깥 행동이 끝나면 모아 둔 이벤트를 주고, 실패하면 버린다.
            Events.InAction = () => Data.InTransaction;
            Actions.Executed += record =>
            {
                if (!record.Nested && record.Status == ActionStatus.Done) Events.FlushAfterAction();
            };
            Actions.TransactionAborted += _ => Events.DropAfterAction();

            Actions.TransactionStarted += id => _randomBefore[id] = Random.ExportStates();
            Actions.TransactionAborted += RestoreRandom;
            Data.TransactionUndone += tx => RestoreRandom(tx.Id);
            Data.TransactionForgotten += tx => _randomBefore.Remove(tx.Id);
            // 이력에 남지 않는 행동(스냅샷·없음 방식)의 난수 상태는 바로 버린다.
            Actions.Executed += record =>
            {
                if (Options.History != HistoryMode.Actions) _randomBefore.Remove(record.TransactionId);
            };

            Debug.Register("세션", "Undo (Undo)", () => Undo(), "마지막 행동(스냅샷 방식이면 마지막 스냅샷)으로 되돌립니다.");
            Debug.Register("세션", "이번 판 수치 초기화", () => Stats.ResetRun());
        }

        public IEnumerable<IInspectable> Inspectables
        {
            get
            {
                yield return Stats;
                yield return Achievements;
                yield return Actions;
                yield return Data;
                yield return Random;
            }
        }

        // 행동을 실행한다. 실행되는 즉시 끝난다(대기·확정 단계 없음).
        public ActionRecord Do(GameAction action)
        {
            // 행동 안에서 부르면 바깥 행동의 일부로 그 자리에서 실행된다(Sync·도전과제는 바깥 행동이 끝날 때).
            var nested = Data.InTransaction;
            var record = Actions.Execute(action);
            if (nested) return record;
            Data.Sync();
            Achievements.Flush();
            return record;
        }

        // 되돌릴 수 있는 횟수(행동 방식: 이력의 행동 수, 스냅샷 방식: 보관된 스냅샷 수).
        public int UndoCount =>
            Options.History == HistoryMode.Actions ? Data.HistoryCount :
            Options.History == HistoryMode.Snapshots ? _snapshots.Count : 0;

        // 마지막 행동(스냅샷 방식이면 마지막 스냅샷)으로 되돌린다. 되돌릴 것이 없으면 false.
        public bool Undo()
        {
            switch (Options.History)
            {
                case HistoryMode.Actions:
                    var undone = Data.UndoLast();
                    if (undone) Achievements.Flush();
                    return undone;
                case HistoryMode.Snapshots:
                    return RewindSnapshot();
                default:
                    return false;
            }
        }

        // 되돌리기 지점(행동 방식): 지금까지의 행동 이력 끝. UndoTo(지점)이 그 뒤의 행동을 모두 되돌린다.
        //   turnStart = session.HistoryMark;          // 내 턴이 시작될 때(턴 시작 행동이 끝난 뒤)
        //   session.UndoTo(turnStart);               // "이번 턴 처음으로" — 되감기 횟수 같은 규칙은 그 뒤 행동으로 센다
        // 지점은 이 세션 안에서만 뜻이 있다(판 저장에 Undo 이력은 들어가지 않는다). 스냅샷 방식이면 지금의 스냅샷 수.
        public long HistoryMark =>
            Options.History == HistoryMode.Actions ? (Data.HistoryCount == 0 ? 0 : Data.History[Data.HistoryCount - 1].Id) :
            Options.History == HistoryMode.Snapshots ? _snapshots.Count : 0;

        // 지점 뒤의 행동(스냅샷)을 모두 되돌린다. 되돌린 횟수를 돌려준다(지점이 이력에서 잘려 나갔으면 남은 만큼만).
        public int UndoTo(long mark)
        {
            if (Data.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 안에서는 되돌릴 수 없습니다(행동 밖에서 부르세요).");
            var count = 0;
            while (UndoCount > 0 && HistoryMark > mark && Undo()) count++;
            return count;
        }

        // ── 스냅샷(실시간 게임) ─────────────────────────────────

        // 지금 상태를 스냅샷으로 남긴다. 스냅샷 방식이면 EndFrame이 간격마다 자동으로 부른다(중요한 순간에 직접 불러도 된다).
        public void CaptureSnapshot()
        {
            if (Data.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 스냅샷을 남길 수 없습니다.");
            _snapshots.Add(new Snapshot
            {
                JournalId = Journal.LastId,
                Tick = Clock.Tick,
                Time = Clock.Time,
                Session = Data.Export(DataZone.Session, report: false, keepReferences: true),
                Meta = Data.Export(DataZone.Meta, report: false, keepReferences: true),
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
                Data.Restore(snapshot.Session, DataZone.Session, "되감기");
                Data.Restore(snapshot.Meta, DataZone.Meta, "되감기");
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
        // 값을 덮어쓰지 않고 실제 이벤트를 쏴서 게임과 같은 연쇄 반응을 일으킨다. 하나의 행동으로 기록된다.

        public ActionRecord SimulateEvents(IEnumerable<object> events, string reason = null)
        {
            var action = new SimulatedEventAction { reason = reason };
            action.Events.AddRange(events);
            if (action.Events.Count == 0) throw new ArgumentException("[MomoiProject] 쏠 이벤트가 없습니다.");
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

        // 도전과제를 게임과 같은 경로로 발동시킨다: 부족한 로그를 이벤트로 채워 조건을 맞추고, 트리거의 이벤트(값 조건을 맞춘 것)를 쏜다.
        // 필요한 이벤트 전체가 하나의 행동(Undo 단위)이 된다. 자동으로 못 만드는 트리거(사용·NOT 등)는 모니터의 강제 달성을 쓴다.
        public bool TrySimulateAchievement(string achievementId, out string error)
        {
            if (!EventSimulator.TryPlanForAchievement(this, achievementId, out var steps, out var triggerEvents, out error))
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
            // 조건이 맞은 뒤에 트리거의 이벤트를 받아야 AND가 같은 순간에 참이 된다.
            events.AddRange(triggerEvents);
            SimulateEvents(events, $"'{AchievementName(achievementId)}' 트리거 발동");
            return true;
        }

        void RestoreRandom(long transactionId)
        {
            if (!_randomBefore.TryGetValue(transactionId, out var states)) return;
            Random.Restore(states);
            _randomBefore.Remove(transactionId);
        }

        // 시간이 흐를 때(고정 틱이면 틱마다 1/틱 초, 아니면 프레임마다 그 프레임 시간). 행동 밖이다: 규칙은 행동(Do)으로 바꾼다.
        public event Action<double> Ticked;

        // 고정 틱: 다음 틱까지 지난 비율(0~1). 화면이 두 틱 사이를 부드럽게 그릴 때(보간).
        public double TickAlpha { get; private set; }

        // 한 프레임에 따라잡는 최대 틱 수(느린 프레임에서 따라잡느라 멈추지 않게). 넘으면 남은 시간은 버린다.
        public const int MaxTicksPerFrame = 8;

        double _tickCarry;

        // 호스트(Unity 등)가 매 프레임 호출한다.
        public void EndFrame(double deltaTime)
        {
            if (Options.TickRate > 0)
            {
                var step = 1.0 / Options.TickRate;
                _tickCarry += Math.Max(0, deltaTime);
                var steps = 0;
                // 프레임 시간을 더해 가며 생기는 아주 작은 오차로 틱이 하나 밀리지 않게(60fps와 20fps가 같은 틱을 내도록).
                var tolerance = step * 1e-6;
                while (_tickCarry + tolerance >= step && steps < MaxTicksPerFrame)
                {
                    _tickCarry = Math.Max(0, _tickCarry - step);
                    steps++;
                    Clock.Advance(step);
                    Achievements.Poll(Clock.Time);
                    if (Data.InTransaction) continue;
                    Statuses.Tick();
                    Schedules.Tick();
                    Ticked?.Invoke(step);
                }
                if (steps == MaxTicksPerFrame && _tickCarry + tolerance >= step) _tickCarry = 0;
                TickAlpha = _tickCarry / step;
                if (Data.InTransaction) return;
            }
            else
            {
                Clock.Advance(deltaTime);
                Achievements.Poll(Clock.Time);
                if (Data.InTransaction) return;
                Statuses.Tick();
                Schedules.Tick();
                Ticked?.Invoke(deltaTime);
            }
            Data.Sync();
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
                ["version"] = (double)SaveVersion,
                ["state"] = Data.Export(DataZone.Meta),
            };
            AddSchema(root);
            return MiniJson.Serialize(root);
        }

        public void ImportMeta(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> root)) throw new FormatException("[MomoiProject] 저장 데이터 형식이 올바르지 않습니다.");
            var version = root.TryGetValue("version", out var v) && v is double number ? (int)number : 1;
            if (version > SaveVersion)
                throw new FormatException($"[MomoiProject] 저장 데이터가 이 게임보다 새 버전입니다(저장 v{version}, 게임 v{SaveVersion}). 게임을 업데이트하세요.");
            if (root.TryGetValue("state", out var state) && state is Dictionary<string, object> values)
            {
                if (version < SaveVersion) Migrations.Run(values, version, SaveVersion, Journal);
                Migrations.EveryLoad?.Invoke(values);
                Data.Import(values, DataZone.Meta);
            }
            Journal.Append(JournalKind.System, "Save", $"프로필 데이터 불러옴 ({(root.TryGetValue("state", out var s) && s is Dictionary<string, object> d ? d.Count : 0)}개 값)");
        }

        // ── 판(런) 저장 ─────────────────────────────────────────
        // 싱글 게임의 '이어하기'용. 세션 구역(이번 판 내용) + 난수 상태 + 게임 시간을 저장한다.
        // 프로필(메타)은 ExportMeta로 따로 저장한다(판을 지워도 프로필은 남도록). Undo 이력은 저장하지 않는다.

        public string ExportRun()
        {
            if (Data.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 판을 저장할 수 없습니다.");
            var random = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in Random.ExportStates())
                random[pair.Key] = pair.Value.Select(v => (object)v.ToString("x16", System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var root = new Dictionary<string, object>
            {
                ["version"] = (double)SaveVersion,
                ["kind"] = "run",
                ["seed"] = Random.MasterSeed.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
                ["tick"] = (double)Clock.Tick,
                ["time"] = Clock.Time,
                ["random"] = random,
                ["state"] = Data.Export(DataZone.Session),
            };
            AddSchema(root);
            return MiniJson.Serialize(root);
        }

        // 저장한 판을 이어서 진행한다. 아직 등록되지 않은 키의 값은 등록되는 순간 적용된다.
        public void ImportRun(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> root)) throw new FormatException("[MomoiProject] 판 저장 데이터 형식이 올바르지 않습니다.");
            var version = root.TryGetValue("version", out var v) && v is double number ? (int)number : 1;
            if (version > SaveVersion)
                throw new FormatException($"[MomoiProject] 판 저장 데이터가 이 게임보다 새 버전입니다(저장 v{version}, 게임 v{SaveVersion}).");

            if (root.TryGetValue("seed", out var seed) && seed is string seedText)
                Random.Reseed(ulong.Parse(seedText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
            if (root.TryGetValue("random", out var random) && random is Dictionary<string, object> streams)
            {
                var states = new Dictionary<string, ulong[]>(StringComparer.Ordinal);
                foreach (var pair in streams)
                    if (pair.Value is List<object> parts && parts.Count == 4)
                        states[pair.Key] = parts.Select(p => ulong.Parse((string)p, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                Random.ImportStates(states);
            }
            var tick = root.TryGetValue("tick", out var t) && t is double tickValue ? (long)tickValue : 0;
            var time = root.TryGetValue("time", out var s) && s is double timeValue ? timeValue : 0;
            Clock.Rewind(tick, time);

            if (root.TryGetValue("state", out var state) && state is Dictionary<string, object> values)
            {
                if (version < SaveVersion) Migrations.Run(values, version, SaveVersion, Journal);
                Migrations.EveryLoad?.Invoke(values);
                Data.Import(values, DataZone.Session);
            }
            // 불러온 시점부터 새로 시작한다(이전 판의 Undo 이력·스냅샷은 버린다).
            ClearUndo();
            Journal.Append(JournalKind.System, "Save", $"판 불러옴 (틱 {tick}, {time:0.0}초)");
            Achievements.Flush();
        }

        // 새 판: 세션 구역을 등록할 때의 값으로 되돌리고 Undo 이력을 비운다(프로필은 그대로).
        public void NewRun()
        {
            Data.ResetZone(DataZone.Session);
            ClearUndo();
            Journal.Append(JournalKind.System, "Session", "새 판 시작");
        }

        void AddSchema(Dictionary<string, object> root)
        {
            if (SaveSchema == null || SaveSchema.Count == 0) return;
            var schema = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in SaveSchema) schema[pair.Key] = pair.Value;
            root["schema"] = schema;
        }

        // 저장 파일(프로필·판)이 지금 데이터 구조와 호환되는지. 구조 정보가 없는 파일은 호환으로 본다.
        // 개발 중에는 호환되지 않는 저장을 변환하지 않고 지운다(GBibleRuntime).
        public static bool IsSaveCompatible(string json, IDictionary<string, string> current, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(json) || current == null) return true;
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> root))
            {
                reason = "저장 파일 형식이 올바르지 않습니다";
                return false;
            }
            if (!root.TryGetValue("schema", out var value) || !(value is Dictionary<string, object> saved)) return true;
            var savedSchema = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in saved) savedSchema[pair.Key] = pair.Value as string ?? "";
            return DataModel.IsCompatible(savedSchema, current, out reason);
        }

        // Undo 기록(행동 이력·스냅샷)을 비운다. 이 시점 이전으로는 되돌릴 수 없다.
        public void ClearUndo()
        {
            Data.ClearHistory();
            _snapshots.Clear();
            _lastSnapshotTime = double.NegativeInfinity;
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
