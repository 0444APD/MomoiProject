using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 기록 글을 만들 때 값을 읽다가 던지는 이벤트(게임 코드의 속성).
    [GameEvent("던지는 값")]
    public sealed class TgThrowingValue
    {
        public int amount = 3;
        public string Label => throw new InvalidOperationException("값 읽기 실패");
    }

    // 트리거·결과: 로그·도전과제·능력이 같은 두 칸(트리거 AND·OR 중첩 + 결과 목록)으로 돈다.
    [GameEvent("보스 처치")]
    public sealed class TgBossKilled
    {
        public int Damage;
        public string Boss;
    }

    // 코딩한 결과: 트리거가 된 이벤트의 값을 본다.
    [Serializable, BlockInfo("처치 피해만큼 골드")]
    public sealed class TgGoldFromEvent : Reward
    {
        public override RewardEffect Effect => RewardEffect.Reversible;
        public override string Describe(INameResolver names) => "처치 피해만큼 골드";

        public override void Grant(GameSession session)
        {
            var damage = Context?.Event is TgBossKilled killed ? killed.Damage : 0;
            session.Data.Set("gold", session.Data.GetNumber("gold") + damage);
        }
    }

    // 사용 조건: 사용자가 살아 있을 때만(트리거 순간의 사용자를 본다).
    [Serializable, BlockInfo("사용자 체력 있음")]
    public sealed class TgUserAlive : SimpleCondition
    {
        protected override bool Check() => Context?.User is TbUnit unit && unit.hp > 0;
    }

    public class TriggerTests
    {
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 1 });
            _session.Data.Define("gold", DataZone.Meta, 0.0);
            _session.Data.Define("board", DataZone.Session, new TbBoard
            {
                units =
                {
                    new TbUnit { id = "hero", team = "ally" },
                    new TbUnit { id = "orc", team = "enemy", x = 1 },
                },
            });
        }

        static EventTrigger Boss(string name = null) => new EventTrigger
        {
            eventType = typeof(TgBossKilled).FullName,
            filter = name == null ? null : new FieldFilter { path = nameof(TgBossKilled.Boss), op = FieldOp.Equal, value = name },
        };

        static ConditionTrigger GoldAtLeast(double amount) => new ConditionTrigger
        {
            condition = new PlayerDataCondition { key = "gold", filter = new FieldFilter { path = "", op = FieldOp.AtLeast, value = amount.ToString() } },
        };

        void Kill(string boss, int damage) => _session.Events.Publish(new TgBossKilled { Boss = boss, Damage = damage });

        [Test]
        public void 로그는_이벤트_받기_트리거와_로그에_기록_결과로_돈다()
        {
            var log = new StatDefinition { Id = "bossDamage", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss("dragon"), then = { new RecordOutcome { field = nameof(TgBossKilled.Damage), multiplier = 2 } } });
            _session.Stats.Define(log);
            Kill("dragon", 10);
            Kill("slime", 99);
            Kill("dragon", 5);
            Assert.AreEqual(30, _session.Stats.Get("bossDamage"), "dragon만, 피해 × 2");
        }

        [Test]
        public void 예전_짧은_규칙도_같은_길로_돈다()
        {
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Rules.Add(new StatRule { eventType = typeof(TgBossKilled).FullName });
            _session.Stats.Define(log);
            Kill("a", 1);
            Kill("b", 1);
            Assert.AreEqual(2, _session.Stats.Get("kills"));
        }

        [Test]
        public void AND는_이벤트를_받은_순간_조건도_참이어야_발동한다()
        {
            var achievement = new AchievementDefinition { Id = "richSlayer", When = new AllTrigger { triggers = { Boss("dragon"), GoldAtLeast(100) } } };
            achievement.Outcomes.Add(new TgGoldFromEvent());
            _session.Achievements.Define(achievement);

            Kill("dragon", 7);
            Assert.IsFalse(_session.Achievements.IsUnlocked("richSlayer"), "골드가 모자란 채 처치");
            _session.Data.Set("gold", 100.0);
            _session.Achievements.Flush();
            Assert.IsFalse(_session.Achievements.IsUnlocked("richSlayer"), "조건만 참이 된 순간은 이벤트가 없다");
            Kill("dragon", 7);
            Assert.IsTrue(_session.Achievements.IsUnlocked("richSlayer"));
            Assert.AreEqual(107, _session.Data.GetNumber("gold"), "결과가 트리거의 이벤트 값을 봤다");
        }

        [Test]
        public void OR는_이벤트나_조건_중_먼저_오는_쪽으로_발동한다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "either", When = new AnyTrigger { triggers = { Boss("dragon"), GoldAtLeast(50) } } });
            _session.Data.Set("gold", 60.0);
            _session.Achievements.Flush();
            Assert.IsTrue(_session.Achievements.IsUnlocked("either"), "조건 쪽");
            Assert.IsFalse(_session.Triggers.IsRegistered("either"), "한 번 발동하면 감시하지 않는다");
        }

        [Test]
        public void 반복_트리거의_조건은_거짓이_된_뒤에_다시_발동한다()
        {
            var fired = 0;
            _session.Triggers.Register("watch", GoldAtLeast(10), repeat: true, fire: _ => fired++);
            _session.Data.Set("gold", 20.0);
            _session.Triggers.Flush();
            _session.Data.Set("gold", 30.0);
            _session.Triggers.Flush();
            Assert.AreEqual(1, fired, "참인 동안 다시 발동하지 않는다");
            _session.Data.Set("gold", 0.0);
            _session.Triggers.Flush();
            _session.Data.Set("gold", 15.0);
            _session.Triggers.Flush();
            Assert.AreEqual(2, fired, "거짓이 된 뒤 다시 참");
        }

        [Test]
        public void 능력은_사용_트리거와_사용_조건을_AND로_보고_결과를_실행한다()
        {
            var strike = new AbilityDefinition { Id = "strike", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new TgUserAlive() } } } };
            strike.Outcomes.Add(new TargetedOutcome { effects = { new TbDamage { amount = 4 } } });
            strike.Outcomes.Add(new PlayerDataReward { key = "gold", amount = 3 });
            _session.Abilities.Define(strike);
            var game = _session.Game;

            game.Use("strike", game.Find<TbUnit>("hero"), game.Find<TbUnit>("orc"));
            Assert.AreEqual(26, game.Find<TbUnit>("orc").hp);
            Assert.AreEqual(3, _session.Data.GetNumber("gold"));

            game.Find<TbUnit>("hero").hp = 0;
            Assert.Throws<GameFailure>(() => game.Use("strike", game.Find<TbUnit>("hero"), game.Find<TbUnit>("orc")), "사용 조건 거짓");
            Assert.AreEqual(26, game.Find<TbUnit>("orc").hp, "실패한 사용은 아무것도 바꾸지 않는다");
        }

        [Test]
        public void 같은_이벤트면_로그가_먼저_오르고_조건이_있는_트리거가_그_값을_본다()
        {
            // 도전과제를 먼저 정의해도(등록 순서와 상관없이) 로그 기록이 먼저.
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "third",
                When = new AllTrigger { triggers = { Boss(), new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", value = 3 } } } },
            });
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome() } });
            _session.Stats.Define(log);
            Kill("a", 1);
            Kill("b", 1);
            Assert.IsFalse(_session.Achievements.IsUnlocked("third"));
            Kill("c", 1);
            Assert.IsTrue(_session.Achievements.IsUnlocked("third"), "세 번째 처치 순간에 처치 수가 이미 3");
        }

        // 결과가 자기 트리거의 이벤트를 다시 쏜다(잘못 만든 데이터).
        [Serializable, BlockInfo("보스 처치 다시 알리기")]
        public sealed class TgEcho : Reward
        {
            public override RewardEffect Effect => RewardEffect.Reversible;
            public override string Describe(INameResolver names) => "다시 알리기";
            public override void Grant(GameSession session) => session.Events.Publish(new TgBossKilled { Boss = "echo" });
        }

        [Test]
        public void 자기_이벤트를_다시_부르는_사슬은_한도에서_끊긴다()
        {
            var fired = 0;
            _session.Triggers.Register("loop", Boss(), repeat: true, fire: moment =>
            {
                fired++;
                _session.Triggers.RunOutcomes(new Outcome[] { new TgEcho() }, moment, "test");
            });
            Assert.DoesNotThrow(() => Kill("start", 1));
            Assert.AreEqual(TriggerSystem.MaxDepth, fired);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("트리거 사슬")));
        }

        [Test]
        public void CanUse는_사용_조건만_보고_아무것도_바꾸지_않는다()
        {
            var strike = new AbilityDefinition { Id = "strike", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new TgUserAlive() } } } };
            strike.Outcomes.Add(new PlayerDataReward { key = "gold", amount = 3 });
            _session.Abilities.Define(strike);
            var game = _session.Game;
            Assert.IsTrue(game.CanUse("strike", game.Find<TbUnit>("hero")));
            game.Find<TbUnit>("hero").hp = 0;
            Assert.IsFalse(game.CanUse("strike", "hero", out var why));
            StringAssert.Contains("사용 조건", why);
            Assert.IsFalse(game.CanUse("none", "hero", out why));
            Assert.AreEqual(0, _session.Data.GetNumber("gold"));
        }

        [Test]
        public void 진행도는_AND면_가장_덜_된_조건이다()
        {
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome() } });
            _session.Stats.Define(log);
            var damage = new StatDefinition { Id = "damage", Aggregate = StatAggregate.Sum };
            damage.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome { field = nameof(TgBossKilled.Damage) } } });
            _session.Stats.Define(damage);
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "both",
                When = new AllTrigger
                {
                    triggers =
                    {
                        new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", value = 10 } },
                        new ConditionTrigger { condition = new StatCompareCondition { stat = "damage", value = 100 } },
                    },
                },
            });
            Kill("a", 80);
            Assert.IsTrue(_session.Achievements.TryGetProgress("both", out var current, out var target));
            Assert.AreEqual(1, current, "처치 1/10이 피해 80/100보다 덜 됐다");
            Assert.AreEqual(10, target);
        }

        [Test]
        public void 트리거로_달성은_조건을_채우고_값을_맞춘_이벤트를_쏜다()
        {
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome() } });
            _session.Stats.Define(log);
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "dragonVeteran",
                When = new AllTrigger { triggers = { Boss("dragon"), new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", value = 3 } } } },
            });
            Assert.IsTrue(_session.TrySimulateAchievement("dragonVeteran", out var error), error);
            Assert.IsTrue(_session.Achievements.IsUnlocked("dragonVeteran"));
            Assert.IsTrue(_session.Undo(), "한 행동");
            Assert.IsTrue(_session.Achievements.IsUnlocked("dragonVeteran"), "달성은 유지");

            _session.Achievements.Define(new AchievementDefinition { Id = "used", When = new UseTrigger() });
            Assert.IsFalse(_session.TrySimulateAchievement("used", out error));
            StringAssert.Contains("사용", error);
        }

        // 보스 처치를 쏘는 행동(fail이면 쏜 뒤 실패한다 — 바꾼 것이 모두 취소된다).
        [Serializable]
        public sealed class TgKill : GameAction
        {
            public string boss = "dragon";
            public bool fail;
            protected override void Run()
            {
                Game.Events.Publish(new TgBossKilled { Boss = boss, Damage = 3 });
                if (fail) throw Game.Fail("fail.test", "일부러 실패");
            }
        }

        // 다음 보스 처치를 받으면 '드래곤을 처치한 뒤 실패하는 행동'을 하도록 예약한다.
        sealed class TgScheduleFailingKill : GameAction
        {
            protected override void Run() => Game.ScheduleOn<TgBossKilled>(1, new TgKill { fail = true });
        }

        void DefineSlayer()
        {
            var slayer = new AchievementDefinition { Id = "slayer", DisplayName = "slayer", When = Boss("dragon") };
            slayer.Outcomes.Add(new PlayerDataReward { key = "gold", amount = 10 });
            _session.Achievements.Define(slayer);
        }

        [Test]
        public void 미리보기와_CanDo는_도전과제_트리거를_쓰지_않는다()
        {
            DefineSlayer();
            var game = _session.Game;
            Assert.IsTrue(game.CanDo(new TgKill(), out _));
            game.Preview(new TgKill(), g => 0);
            Assert.IsFalse(_session.Achievements.IsUnlocked("slayer"), "미리보기는 달성하지 않는다");
            Assert.IsTrue(_session.Triggers.IsRegistered("slayer"), "미리보기가 끝나면 트리거가 그대로 감시한다");
            game.Do(new TgKill());
            Assert.IsTrue(_session.Achievements.IsUnlocked("slayer"), "실제로 하면 달성된다");
            Assert.AreEqual(10, _session.Data.GetNumber("gold"));
            Assert.IsFalse(_session.Triggers.IsRegistered("slayer"), "달성하면 감시를 그만둔다");
        }

        [Test]
        public void 실패한_행동_안에서_발동한_도전과제는_달성되지_않고_계속_감시한다()
        {
            DefineSlayer();
            var published = new List<string>();
            _session.Events.Subscribe<AchievementUnlocked>(e => published.Add(e.Id));
            var unlocked = 0;
            _session.Achievements.Unlocked += _ => unlocked++;
            Assert.Throws<GameFailure>(() => _session.Game.Do(new TgKill { fail = true }));
            Assert.IsFalse(_session.Achievements.IsUnlocked("slayer"), "실패한 행동은 되돌릴 수 없는 달성을 남기지 않는다");
            Assert.AreEqual(0, _session.Data.GetNumber("gold"), "보상도 없다");
            Assert.AreEqual(0, published.Count + unlocked, "달성 이벤트·Unlocked(Steam)도 없다");
            Assert.IsTrue(_session.Triggers.IsRegistered("slayer"), "트리거는 그대로 감시한다");

            _session.Game.Do(new TgKill());
            Assert.IsTrue(_session.Achievements.IsUnlocked("slayer"), "성공한 행동이 끝나면 달성된다");
            Assert.AreEqual(10, _session.Data.GetNumber("gold"));
            CollectionAssert.AreEqual(new[] { "slayer" }, published);
            Assert.AreEqual(1, unlocked);
            var entry = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Achievement);
            Assert.IsTrue(_session.Journal.CauseChain(entry.Id).Any(e => e.Kind == JournalKind.Event), "달성 줄은 발동한 이벤트를 원인으로 잇는다");
        }

        [Test]
        public void 성공한_행동_뒤에_받는_쪽의_미리보기나_실패한_행동이_그_행동의_달성을_버리지_않는다()
        {
            DefineSlayer();
            var game = _session.Game;
            // 화면 쪽: 행동이 끝난 뒤 버튼 상태를 미리 보고(CanDo·Preview — 해 보고 되돌린다), 실패하는 행동도 하나 보낸다(오류는 기록만).
            _session.Events.SubscribeAfterAction<TgBossKilled>(e =>
            {
                game.CanDo(new TgKill { boss = "slime" }, out _);
                game.Preview(new TgKill { boss = "slime" }, g => 0);
                game.Do(new TgKill { boss = "slime", fail = true });
            });
            game.Do(new TgKill());
            Assert.IsTrue(_session.Achievements.IsUnlocked("slayer"), "성공한 행동에서 발동한 달성은 그 뒤의 미리보기·실패한 행동이 지우지 않는다");
            Assert.AreEqual(10, _session.Data.GetNumber("gold"), "보상도 나온다");
        }

        [Test]
        public void 스냅샷으로_되감으면_이벤트로_발동한_도전과제의_결과를_그_이벤트로_다시_적용한다()
        {
            var session = new GameSession(new SessionOptions { History = HistoryMode.Snapshots, SnapshotInterval = 1, Seed = 1 });
            session.Data.Define("gold", DataZone.Meta, 0.0);
            var bounty = new AchievementDefinition { Id = "bounty", DisplayName = "bounty", When = Boss("dragon") };
            bounty.Outcomes.Add(new TgGoldFromEvent());
            session.Achievements.Define(bounty);
            session.EndFrame(0.5);                          // 스냅샷(달성 전)
            session.Game.Do(new TgKill());                   // 드래곤 처치(피해 3) → 달성, 골드 +3
            Assert.AreEqual(3, session.Data.GetNumber("gold"));
            session.EndFrame(0.2);
            Assert.IsTrue(session.Undo(), "달성 전 스냅샷으로 되감는다");
            Assert.IsTrue(session.Achievements.IsUnlocked("bounty"), "달성은 유지");
            Assert.AreEqual(3, session.Data.GetNumber("gold"), "결과도 발동한 이벤트(처치 피해 3)로 다시 적용한다");
        }

        [Test]
        public void 그만_받은_쪽은_같은_이벤트를_더_받지_않는다()
        {
            var calls = new List<string>();
            IDisposable rule = null, all = null, view = null;
            _session.Events.Subscribe<TgBossKilled>(_ => { calls.Add("규칙1"); rule.Dispose(); });
            rule = _session.Events.Subscribe<TgBossKilled>(_ => calls.Add("규칙2"));
            _session.Events.SubscribeAll(e => { if (e is TgBossKilled) { calls.Add("전체1"); all.Dispose(); } });
            all = _session.Events.SubscribeAll(e => { if (e is TgBossKilled) calls.Add("전체2"); });
            _session.Events.SubscribeAfterAction<TgBossKilled>(_ => { calls.Add("연출1"); view.Dispose(); });
            view = _session.Events.SubscribeAfterAction<TgBossKilled>(_ => calls.Add("연출2"));
            _session.Game.Do(new TgKill());
            CollectionAssert.AreEqual(new[] { "규칙1", "전체1", "연출1" }, calls, "앞의 받는 쪽이 그만 받게 한 쪽은 지금 이벤트도 받지 않는다");
        }

        [GameEvent("보스 현상금")]
        public sealed class TgBounty
        {
            public int Gold;
        }

        [Test]
        public void 행동_뒤에_받는_쪽은_받는_쪽이_안에서_쏜_이벤트보다_먼저_쏜_이벤트를_먼저_받는다()
        {
            // 규칙: 보스 처치를 받으면 그 자리에서 현상금 이벤트를 쏜다(트리거 결과 '이벤트 쏘기'·상태 효과의 반응과 같다).
            _session.Events.Subscribe<TgBossKilled>(e => _session.Events.Publish(new TgBounty { Gold = e.Damage }));
            var seen = new List<string>();
            _session.Events.SubscribeAfterAction<TgBossKilled>(_ => seen.Add("처치"));
            _session.Events.SubscribeAfterAction<TgBounty>(_ => seen.Add("현상금"));
            var published = new List<string>();
            _session.Events.Published += e => published.Add(e is TgBounty ? "현상금" : e is TgBossKilled ? "처치" : "");
            _session.Game.Do(new TgKill());
            CollectionAssert.AreEqual(new[] { "처치", "현상금" }, seen, "쏜 순서대로: 처치를 받은 쪽이 쏜 현상금은 처치 뒤");
            CollectionAssert.AreEqual(new[] { "처치", "현상금" }, published.Where(p => p.Length > 0).ToList(), "Published(멀티 서버가 모으는 차례)도 연출과 같은 차례");
        }

        [Test]
        public void 실패해_되돌린_이벤트_예약이_쏜_이벤트로는_달성되지_않는다()
        {
            DefineSlayer();
            _session.Game.Do(new TgScheduleFailingKill());
            // 슬라임 처치 → 예약 실행(드래곤 처치를 쏜 뒤 실패) → 그 예약이 바꾼 것·쏜 것만 되돌린다. 바깥 행동은 성공한다.
            _session.Game.Do(new TgKill { boss = "slime" });
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Message.Contains("그 예약이 바꾼 것만 되돌렸습니다")));
            Assert.IsFalse(_session.Achievements.IsUnlocked("slayer"), "되돌린 예약이 쏜 이벤트로는 달성되지 않는다");
            Assert.IsTrue(_session.Triggers.IsRegistered("slayer"), "트리거도 되살아난다");
            _session.Game.Do(new TgKill());
            Assert.IsTrue(_session.Achievements.IsUnlocked("slayer"));
        }

        // 의존 값 없이 1초마다 다시 보는 조건(평가 횟수를 센다).
        public sealed class TgPolled : SimpleCondition
        {
            public int evaluations;
            public override double PollInterval => 1;
            protected override bool Check()
            {
                evaluations++;
                return false;
            }
        }

        [Test]
        public void 시계를_되돌리면_조건_폴링도_새_시각부터_센다()
        {
            var polled = new TgPolled();
            _session.Triggers.Register("poll", new ConditionTrigger { condition = polled }, repeat: true, fire: _ => { });
            _session.EndFrame(100);
            var before = polled.evaluations;
            // 10초 시점의 판을 불러온다(시계가 뒤로 간다).
            var other = new GameSession(new SessionOptions { Seed = 1 });
            other.EndFrame(10);
            _session.ImportRun(other.ExportRun());
            Assert.AreEqual(10, _session.Clock.Time, 1e-9);
            _session.EndFrame(1.5);
            Assert.AreEqual(before + 1, polled.evaluations, "11.5초: 1초마다의 폴링이 돈다(예전 시각 101초까지 건너뛰지 않는다)");
        }

        [Serializable, PlayerData]
        public class TgBag
        {
            public Dictionary<string, int> counts = new Dictionary<string, int>();
            public Dictionary<string, string> names = new Dictionary<string, string>();
        }

        [Test]
        public void 딕셔너리에는_키와_값을_넣는다_숫자는_증감_나머지는_덮어쓰기()
        {
            _session.Data.Define("bag", DataZone.Session, new TgBag { counts = { ["potion"] = 2 }, names = { ["a"] = "old" } });
            new PlayerDataReward { key = "bag", path = "counts", keys = { "potion", "elixir" }, items = { "3", "1" } }.Grant(_session);
            new PlayerDataReward { key = "bag", path = "names", keys = { "a", "b" }, items = { "new", "fresh" } }.Grant(_session);
            var bag = _session.Data.Get<TgBag>("bag");
            Assert.AreEqual(5, bag.counts["potion"], "있던 키는 더한다");
            Assert.AreEqual(1, bag.counts["elixir"], "없던 키는 0에서");
            Assert.AreEqual("new", bag.names["a"], "숫자가 아니면 덮어쓴다");
            Assert.AreEqual("fresh", bag.names["b"]);
            Assert.AreEqual(GrantKind.Dictionary, PlayerDataGrants.KindOf(typeof(Dictionary<string, int>)));

            var problems = new List<string>();
            new PlayerDataReward { key = "bag", path = "counts", keys = { "x", "x" }, items = { "1", "abc" } }.Validate(typeof(TgBag), problems);
            Assert.IsTrue(problems.Any(p => p.Contains("두 번")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("숫자가 아닙니다")), string.Join("\n", problems));
        }

        [Test]
        public void 트리거와_결과는_문장으로_읽힌다()
        {
            var rule = new TriggerRule { when = new AllTrigger { triggers = { Boss("dragon"), GoldAtLeast(5) } }, then = { new RecordOutcome { field = "Damage" } } };
            var text = rule.Natural(_session);
            StringAssert.Contains("보스 처치 받기", text);
            StringAssert.Contains("그리고", text);
            StringAssert.Contains("Damage", text);
            Assert.AreEqual(TriggerKinds.Event | TriggerKinds.Condition, rule.when.Kinds());
            CollectionAssert.AreEqual(new[] { typeof(TgBossKilled) }, rule.when.EventTypes().ToArray());
        }

        [Test]
        public void 기록_글을_만들다_값이_던져도_이벤트는_쏘인다()
        {
            var got = 0;
            _session.Events.Subscribe<TgThrowingValue>(_ => got++);
            Assert.DoesNotThrow(() => _session.Events.Publish(new TgThrowingValue()));
            Assert.AreEqual(1, got, "받는 쪽에 간다");
            var line = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Event).Message;
            StringAssert.Contains("amount=3", line);
            StringAssert.Contains("Label=?", line);
        }
    }
}
