using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 상태 효과와 예약.
    public class StatusScheduleTests
    {
        [Serializable] public class SsUnit { public string id; public double attack = 10; public int hp = 30; }
        [Serializable] public class SsParty { public List<SsUnit> units = new List<SsUnit>(); }
        [Serializable, GameEvent("턴 끝")] public class SsTurnEnded { public string unit; }
        [Serializable, Intent("피해")] public class SsDamage { public string target; public int amount; public bool cancelled; }

        // 결과: 가진 쪽에게 중첩 × amount 피해.
        [Serializable]
        public sealed class Hurt : Outcome
        {
            public int amount = 1;
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => ((SsUnit)context.User).hp -= amount * context.Game.StatusStacks(context.User, context.Owner);
            public override string Describe(INameResolver names) => "피해";
        }

        // 결과: 불린 순간을 남긴다.
        public sealed class Mark : Outcome
        {
            public readonly List<string> log = new List<string>();
            public string text;
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => log.Add($"{text}:{GameApi.IdOf(context.User)}");
            public override string Describe(INameResolver names) => text;
        }

        public sealed class Hit : GameAction
        {
            public SsUnit unit;
            public int amount;
            protected override void Run()
            {
                var hit = Game.Intercept(new SsDamage { target = unit.id, amount = amount });
                if (!hit.cancelled) unit.hp -= hit.amount;
            }
        }

        [Serializable]
        public sealed class Smite : GameAction
        {
            public SsUnit unit;
            public int amount;
            protected override void Run() => unit.hp -= amount;
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;
        GameApi Game => _session.Game;
        SsUnit Hero => Game.Find<SsUnit>("hero");
        SsUnit Foe => Game.Find<SsUnit>("foe");

        static GameSession NewSession()
        {
            var session = new GameSession(new SessionOptions { Seed = 5 });
            session.Data.Define("party", DataZone.Session, new SsParty { units = { new SsUnit { id = "hero" }, new SsUnit { id = "foe" } } }, "파티");
            return session;
        }

        [SetUp]
        public void SetUp() => _session = NewSession();

        void Do(Action<GameApi> run) => Game.Do(new Edit { run = run });
        void TurnEnd(string unit) => Do(g => g.Session.Events.Publish(new SsTurnEnded { unit = unit }));

        static StatusDefinition Poison(Mark expire = null)
        {
            var poison = new StatusDefinition
            {
                Id = "poison", MaxStacks = 5, Duration = StatusDuration.Events, Length = 3,
                TickEvent = typeof(SsTurnEnded).FullName, TickHolderField = "unit",
                When = new EventTrigger { eventType = typeof(SsTurnEnded).FullName }, HolderField = "unit",
            };
            poison.Outcomes.Add(new Hurt());
            if (expire != null) poison.OnExpire.Add(expire);
            return poison;
        }

        [Test]
        public void 중첩만큼_능력치_보정이_붙는다()
        {
            var rage = new StatusDefinition { Id = "rage", MaxStacks = 3, Reapply = StatusReapply.AddStacks };
            rage.Modifiers.Add(new StatusModifier { attribute = nameof(SsUnit.attack), kind = ModifierKind.Percent, valuePerStack = 0.1 });
            _session.Statuses.Define(rage);

            Do(g => g.ApplyStatus(Hero, "rage", 2));
            Do(g => g.ApplyStatus("hero", "rage", 2));
            Assert.AreEqual(3, Game.StatusStacks(Hero, "rage"), "최대 중첩에서 멈춘다");
            Assert.AreEqual(13, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);

            Do(g => g.RemoveStatus(Hero, "rage", 1));
            Assert.AreEqual(12, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);
            Do(g => g.RemoveStatus(Hero, "rage"));
            Assert.IsFalse(Game.HasStatus(Hero, "rage"));
            Assert.AreEqual(10, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);
        }

        [Test]
        public void 이벤트_지속은_결과가_먼저_돌고_끝난다()
        {
            var expire = new Mark { text = "expire" };
            _session.Statuses.Define(Poison(expire));
            Do(g => g.ApplyStatus(Hero, "poison", 2));

            TurnEnd("foe");
            Assert.AreEqual(30, Hero.hp, "다른 쪽의 턴 끝은 세지 않는다");
            TurnEnd("hero");
            TurnEnd("hero");
            TurnEnd("hero");
            Assert.AreEqual(24, Hero.hp, "2중첩 × 3번");
            Assert.IsFalse(Game.HasStatus(Hero, "poison"));
            CollectionAssert.AreEqual(new[] { "expire:hero" }, expire.log, "끝날 때 결과의 사용자는 가진 쪽");
            TurnEnd("hero");
            Assert.AreEqual(24, Hero.hp);
        }

        [Test]
        public void 다시_걸면_지속이_갱신된다()
        {
            var poison = Poison();
            poison.Reapply = StatusReapply.AddStacksAndRefresh;
            _session.Statuses.Define(poison);
            Do(g => g.ApplyStatus(Hero, "poison"));
            TurnEnd("hero");
            TurnEnd("hero");
            Do(g => g.ApplyStatus(Hero, "poison"));
            Assert.AreEqual(2, Game.StatusStacks(Hero, "poison"));
            Assert.AreEqual(3, Game.Statuses(Hero).Single().remaining, 1e-9);
        }

        [Test]
        public void 시간_지속은_프레임_끝에_끝나고_Undo된다()
        {
            var shield = new StatusDefinition { Id = "shield", Duration = StatusDuration.Seconds, Length = 2 };
            shield.Modifiers.Add(new StatusModifier { attribute = nameof(SsUnit.attack), kind = ModifierKind.Flat, valuePerStack = 5 });
            var expire = new Mark { text = "expire" };
            shield.OnExpire.Add(expire);
            _session.Statuses.Define(shield);

            Do(g => g.ApplyStatus(Hero, "shield"));
            _session.EndFrame(1);
            Assert.AreEqual(15, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);
            Do(g => Hero.hp -= 1);
            _session.EndFrame(1.5);
            Assert.IsFalse(Game.HasStatus(Hero, "shield"));
            Assert.AreEqual(10, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);
            Assert.AreEqual(1, expire.log.Count);

            // 만료(시간 규칙)는 앞의 행동과 함께 되돌리고 시계도 그 행동이 시작한 때(1초)로 돌린다 — 다음 프레임에 바로 다시 끝나지 않는다.
            Assert.IsTrue(_session.Undo());
            Assert.IsTrue(Game.HasStatus(Hero, "shield"), "만료가 되돌려진다");
            Assert.AreEqual(30, Hero.hp, "앞의 행동도 함께");
            Assert.AreEqual(1, _session.Clock.Time, 1e-9, "시계도 그 행동 전으로");
            _session.EndFrame(0.5);
            Assert.IsTrue(Game.HasStatus(Hero, "shield"), "시각이 되기 전에는 다시 끝나지 않는다");
            Assert.AreEqual(15, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);
            _session.EndFrame(1);
            Assert.IsFalse(Game.HasStatus(Hero, "shield"), "시각이 되면 다시 끝난다");
        }

        [Test]
        public void 가로채는_상태_효과는_가진_쪽만_지킨다()
        {
            var guard = new StatusDefinition
            {
                Id = "guard", When = new InterceptTrigger { intentType = typeof(SsDamage).FullName }, HolderField = "target",
            };
            guard.Outcomes.Add(new ModifyIntentOutcome { path = "amount", op = IntentOp.Multiply, value = "0.5" });
            _session.Statuses.Define(guard);
            Do(g => g.ApplyStatus(Hero, "guard"));

            Game.Do(new Hit { unit = Hero, amount = 10 });
            Game.Do(new Hit { unit = Foe, amount = 10 });
            Assert.AreEqual(25, Hero.hp);
            Assert.AreEqual(20, Foe.hp);
        }

        // 조건: 가진 쪽(컨텍스트의 사용자) 체력이 20 미만.
        public sealed class HolderLowHp : SimpleCondition
        {
            protected override bool Check() => Context?.User is SsUnit unit && unit.hp < 20;
        }

        [Test]
        public void 트리거_안의_조건은_가진_쪽마다_검사한다()
        {
            var rally = new StatusDefinition
            {
                Id = "rally",
                When = new AllTrigger { triggers = { new EventTrigger { eventType = typeof(SsTurnEnded).FullName }, new ConditionTrigger { condition = new HolderLowHp() } } },
            };
            rally.Outcomes.Add(new Hurt { amount = -5 });   // 회복 5
            _session.Statuses.Define(rally);
            Do(g => { g.ApplyStatus(Hero, "rally"); g.ApplyStatus(Foe, "rally"); Foe.hp = 10; });
            TurnEnd("hero");
            Assert.AreEqual(30, Hero.hp, "체력이 많은 쪽은 발동하지 않는다");
            Assert.AreEqual(15, Foe.hp, "체력이 적은 쪽만");
        }

        [Test]
        public void 풀면_끝날_때_결과가_돌지_않는다()
        {
            var marks = new Mark { text = "x" };
            var curse = new StatusDefinition { Id = "curse" };
            curse.OnApply.Add(marks);
            curse.OnExpire.Add(marks);
            curse.Modifiers.Add(new StatusModifier { attribute = nameof(SsUnit.attack), kind = ModifierKind.Flat, valuePerStack = -3 });
            _session.Statuses.Define(curse);

            Do(g => g.ApplyStatus(Hero, "curse"));
            Do(g => g.ApplyStatus(Hero, "curse"));
            Assert.AreEqual(1, marks.log.Count, "걸릴 때 결과는 처음 걸 때만");
            Do(g => g.RemoveStatus(Hero, "curse"));
            Assert.AreEqual(1, marks.log.Count);
            Assert.AreEqual(10, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9);
        }

        [Test]
        public void 가진_쪽이_사라지면_정리된다()
        {
            _session.Statuses.Define(Poison());
            Do(g => g.ApplyStatus(Foe, "poison"));
            Do(g => g.Session.Data.Modify<SsParty>("party", p => p.units.RemoveAll(u => u.id == "foe")));
            Assert.AreEqual(0, _session.Statuses.Of("foe").Count, "없앤 행동이 끝날 때 정리된다");
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(1, _session.Statuses.Of("foe").Count, "그 행동을 Undo하면 같이 돌아온다");
        }

        [Serializable]
        public sealed class Note : GameAction
        {
            public string text;
            public override string Describe() => text;
            protected override void Run() { }
        }

        [Test]
        public void 같은_시각의_시간_예약은_예약한_차례대로_돈다()
        {
            Do(g =>
            {
                for (var i = 1; i <= 12; i++) g.ScheduleAfter(1, new Note { text = "차례" + i });
            });
            _session.EndFrame(1.5);
            var order = _session.Journal.Entries.Where(e => e.Kind == JournalKind.Action && e.Message.StartsWith("차례", StringComparison.Ordinal)).Select(e => e.Message).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(1, 12).Select(i => "차례" + i).ToList(), order, "id 글자 순(10이 2보다 먼저)이 아니라 예약한 차례");
        }

        [Test]
        public void 시간_예약은_대상을_다시_찾아_실행한다()
        {
            var hero = Hero;
            Do(g => g.ScheduleAfter(1, new Smite { unit = hero, amount = 5 }));
            var stored = (Smite)_session.Schedules.All.Single().action;
            Assert.IsFalse(ReferenceEquals(hero, stored.unit), "대상은 id만 남겨 저장한다");
            Assert.AreEqual("hero", stored.unit.id);

            _session.EndFrame(0.5);
            Assert.AreEqual(30, Hero.hp);
            _session.EndFrame(0.6);
            Assert.AreEqual(25, Hero.hp);
            Assert.AreEqual(0, _session.Schedules.All.Count);
        }

        [Test]
        public void 대상이_없으면_예약을_건너뛴다()
        {
            var foe = Foe;
            Do(g => g.ScheduleAfter(1, new Smite { unit = foe, amount = 5 }));
            Do(g => g.Session.Data.Modify<SsParty>("party", p => p.units.RemoveAll(u => u.id == "foe")));
            Assert.DoesNotThrow(() => _session.EndFrame(2));
            Assert.AreEqual(0, _session.Schedules.All.Count);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Message.Contains("대상이 없어")));
        }

        [Test]
        public void 이벤트_예약은_그_행동_안에서_돌고_같이_Undo된다()
        {
            var hero = Hero;
            Do(g => g.ScheduleOn<SsTurnEnded>(2, new Smite { unit = hero, amount = 7 }));
            TurnEnd("hero");
            Assert.AreEqual(30, Hero.hp);
            TurnEnd("hero");
            Assert.AreEqual(23, Hero.hp);
            Assert.AreEqual(0, _session.Schedules.All.Count);

            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hero.hp);
            Assert.AreEqual(1, _session.Schedules.All.Single().remaining, "Undo하면 예약이 남은 횟수 1로 돌아온다");
        }

        // 체력을 깎은 뒤 실패하는 행동.
        [Serializable]
        public sealed class SmiteThenFail : GameAction
        {
            public SsUnit unit;
            protected override void Run()
            {
                unit.hp -= 9;
                throw new InvalidOperationException("일부러 실패");
            }
        }


        [Serializable, GameEvent("시험 외침")] public class SsShout { public string text; }

        // 연출 이벤트를 쏜 뒤 실패하는 행동.
        [Serializable]
        public sealed class ShoutThenFail : GameAction
        {
            protected override void Run()
            {
                Game.Session.Events.Publish(new SsShout { text = "inner" });
                throw new InvalidOperationException("일부러 실패");
            }
        }

        [Test]
        public void 이벤트_예약이_실패하면_그_예약이_쏜_연출도_빠진다()
        {
            var seen = new List<string>();
            _session.Events.SubscribeAfterAction<SsShout>(s => seen.Add(s.text));
            Do(g => g.ScheduleOn<SsTurnEnded>(1, new ShoutThenFail()));
            Do(g =>
            {
                g.Session.Events.Publish(new SsTurnEnded { unit = "hero" });
                g.Session.Events.Publish(new SsShout { text = "outer" });
            });
            CollectionAssert.AreEqual(new[] { "outer" }, seen, "실패한 예약이 쏜 연출은 재생하지 않는다");
        }

        [Test]
        public void 이벤트_예약이_실패하면_그_예약이_바꾼_것만_되돌린다()
        {
            var hero = Hero;
            Do(g => g.ScheduleOn<SsTurnEnded>(1, new SmiteThenFail { unit = hero }));
            Do(g =>
            {
                g.Find<SsUnit>("foe").hp -= 4;                              // 예약 앞의 변경
                g.Session.Events.Publish(new SsTurnEnded { unit = "hero" }); // 예약이 돌다가 실패
                g.Find<SsUnit>("foe").hp -= 1;                              // 예약 뒤의 변경
            });
            Assert.AreEqual(30, Hero.hp, "실패한 예약의 변경만 되돌아간다");
            Assert.AreEqual(25, Foe.hp, "이벤트를 쏜 행동의 다른 변경은 남는다");
            Assert.AreEqual(0, _session.Schedules.All.Count);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Message.Contains("그 예약이 바꾼 것만 되돌렸습니다")));
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Foe.hp, "그 행동을 Undo하면 나머지도 돌아간다");
            Assert.AreEqual(30, Hero.hp);
            Assert.AreEqual(1, _session.Schedules.All.Count, "예약도 돌아온다");
        }

        // 비교 코드가 없는(예전 방식) 데이터.
        public class SsLegacyBag { public int coins = 10; }

        public sealed class SpendThenFail : GameAction
        {
            protected override void Run()
            {
                Game.Session.Data.Modify<SsLegacyBag>("bag", b => b.coins -= 3);
                throw new InvalidOperationException("일부러 실패");
            }
        }

        [Test]
        public void 예전_방식_데이터도_저장점까지만_되돌린다()
        {
            _session.Data.Define("bag", DataZone.Session, new SsLegacyBag(), "주머니");
            Do(g => g.ScheduleOn<SsTurnEnded>(1, new SpendThenFail()));
            Do(g =>
            {
                g.Session.Data.Modify<SsLegacyBag>("bag", b => b.coins -= 1);   // 저장점 앞
                g.Session.Events.Publish(new SsTurnEnded { unit = "hero" });
            });
            Assert.AreEqual(9, _session.Data.Get<SsLegacyBag>("bag").coins);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(10, _session.Data.Get<SsLegacyBag>("bag").coins);
        }

        [Serializable] public class SsWallet { public int coins; }

        // 조건: 주인공 체력이 20 미만.
        public sealed class HeroHurt : SimpleCondition
        {
            protected override bool Check() => Game.Find<SsUnit>("hero").hp < 20;
        }

        // 결과: 가진 쪽이 주인공이면 적을 데이터에서 뺀다(같은 순간에 뒤 가진 쪽이 사라지는 경우).
        public sealed class RemoveFoe : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context)
            {
                if (GameApi.IdOf(context.User) == "hero") context.Game.Get<SsParty>("party").units.RemoveAll(u => u.id == "foe");
            }
            public override string Describe(INameResolver names) => "적 빼기";
        }

        // 결과: 그 상태 효과를 가진 쪽에서 푼다.
        public sealed class RemoveStatusOutcome : Outcome
        {
            public string status;
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.RemoveStatus(context.User, status);
            public override string Describe(INameResolver names) => "풀기";
        }

        int ErrorCount => _session.Journal.Entries.Count(e => e.Kind == JournalKind.Error);

        [Test]
        public void 주기_결과가_실패해도_다른_주기는_돌고_매_프레임_다시_실패하지_않는다()
        {
            _session.Data.Define("wallet", DataZone.Session, new SsWallet { coins = 1 }, "지갑");
            var drain = new StatusDefinition { Id = "drain", Pulse = 1 };
            drain.Outcomes.Add(new PayOutcome { key = "wallet", path = "coins", amount = 1 });
            var regen = new StatusDefinition { Id = "regen", Pulse = 1 };
            regen.Outcomes.Add(new Hurt { amount = -1 });
            _session.Statuses.Define(drain);
            _session.Statuses.Define(regen);
            Do(g => { g.ApplyStatus(Hero, "drain"); g.ApplyStatus(Foe, "regen"); });
            var ticked = 0;
            _session.Ticked += _ => ticked++;

            _session.EndFrame(1);
            Assert.AreEqual(0, _session.Data.Get<SsWallet>("wallet").coins, "1초: 1을 냈다");
            Assert.AreEqual(31, Foe.hp);

            Assert.DoesNotThrow(() => _session.EndFrame(1), "2초: 낼 돈이 없어 주기 결과가 실패한다");
            Assert.AreEqual(32, Foe.hp, "같은 프레임의 다른 상태 효과 주기는 돈다");
            Assert.AreEqual(2, ticked, "프레임 끝의 나머지 처리도 돈다");
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("주기 결과가 실패")));
            Assert.AreEqual(3, Game.Statuses(Hero).Single().nextPulse, 1e-9, "실패한 회차는 건너뛰고 다음 시각으로");

            var errors = ErrorCount;
            _session.EndFrame(0.5);
            Assert.AreEqual(errors, ErrorCount, "실패한 주기를 매 프레임 다시 내지 않는다");
            Assert.IsTrue(_session.Undo(), "주기 행동도 Undo된다");
        }

        [Test]
        public void 끝날_때_결과가_실패해도_상태_효과는_끝나고_다시_시도하지_않는다()
        {
            _session.Data.Define("wallet", DataZone.Session, new SsWallet(), "지갑");
            var bomb = new StatusDefinition { Id = "bomb", Duration = StatusDuration.Seconds, Length = 1 };
            bomb.OnExpire.Add(new PayOutcome { key = "wallet", path = "coins", amount = 5 });
            bomb.Modifiers.Add(new StatusModifier { attribute = nameof(SsUnit.attack), kind = ModifierKind.Flat, valuePerStack = 5 });
            var shield = new StatusDefinition { Id = "shield", Duration = StatusDuration.Seconds, Length = 1 };
            var expire = new Mark { text = "expire" };
            shield.OnExpire.Add(expire);
            _session.Statuses.Define(bomb);
            _session.Statuses.Define(shield);
            var ended = new List<string>();
            _session.Events.SubscribeAfterAction<StatusEnded>(e => ended.Add($"{e.status}:{e.holder}"));
            Do(g => { g.ApplyStatus(Hero, "bomb"); g.ApplyStatus(Foe, "shield"); });

            Assert.DoesNotThrow(() => _session.EndFrame(1.5));
            Assert.IsFalse(Game.HasStatus(Hero, "bomb"), "끝날 때 결과가 실패해도 끝난다");
            Assert.AreEqual(10, Game.Attribute(Hero, nameof(SsUnit.attack)), 1e-9, "보정도 빠진다");
            Assert.IsFalse(Game.HasStatus(Foe, "shield"), "같은 프레임에 끝나는 다른 상태 효과도 끝난다");
            CollectionAssert.AreEqual(new[] { "expire:foe" }, expire.log);
            CollectionAssert.AreEquivalent(new[] { "bomb:hero", "shield:foe" }, ended);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("끝날 때 결과가 실패")));
            var errors = ErrorCount;
            _session.EndFrame(0.5);
            Assert.AreEqual(errors, ErrorCount, "다시 시도하지 않는다");
        }

        [Test]
        public void 프레임_끝의_한_처리가_던져도_나머지는_돈다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "hurt", DisplayName = "hurt", Condition = new HeroHurt() });
            _session.Ticked += _ => throw new InvalidOperationException("화면 쪽 실수");
            Hero.hp = 10;   // 행동 밖에서 바꾼 값 — 프레임 끝의 Sync가 잡고 도전과제 조건을 평가한다
            Assert.DoesNotThrow(() => _session.EndFrame(0.1));
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Error && e.Message.Contains("Ticked")), "실패는 기록에 남긴다");
            Assert.IsTrue(_session.Achievements.IsUnlocked("hurt"), "그 뒤의 Sync·도전과제 평가가 돈다");
        }

        [Test]
        public void 미리보기에서도_그_이벤트를_받는_도중에_건_것은_그_이벤트를_세지_않는다()
        {
            var weak = new StatusDefinition { Id = "weak", Duration = StatusDuration.Events, Length = 2, TickEvent = typeof(SsTurnEnded).FullName };
            _session.Statuses.Define(weak);
            // 턴 끝마다 약화를 거는 상태 효과(턴 끝을 받는 도중에 건다).
            var curse = new StatusDefinition { Id = "curse", When = new EventTrigger { eventType = typeof(SsTurnEnded).FullName } };
            curse.Outcomes.Add(new TargetedOutcome { effects = { new ApplyWeak() } });
            _session.Statuses.Define(curse);
            Do(g => g.ApplyStatus(Hero, "curse"));
            var turnEnd = new Edit { run = g => g.Session.Events.Publish(new SsTurnEnded { unit = "hero" }) };

            var predicted = Game.Preview(turnEnd, g => g.Statuses("hero").Single(s => s.status == "weak").remaining);
            Game.Do(new Edit { run = g => g.Session.Events.Publish(new SsTurnEnded { unit = "hero" }) });
            var actual = Game.Statuses(Hero).Single(s => s.status == "weak").remaining;
            Assert.AreEqual(2, actual, 1e-9, "건 순간의 턴 끝은 세지 않는다");
            Assert.AreEqual(actual, predicted, 1e-9, "미리보기도 실제와 같다");
        }

        // 효과: 대상에게 약화를 건다.
        [Serializable]
        public sealed class ApplyWeak : Effect
        {
            protected override void Apply(object target) => Game.ApplyStatus(target, "weak");
            public override string Describe() => "약화 걸기";
        }

        [Test]
        public void 같은_순간에_끝나는_상태_효과를_앞의_끝날_때_결과가_풀면_두_번_끝나지_않는다()
        {
            var first = new StatusDefinition { Id = "first", Duration = StatusDuration.Events, Length = 1, TickEvent = typeof(SsTurnEnded).FullName };
            first.OnExpire.Add(new RemoveStatusOutcome { status = "second" });
            var second = new StatusDefinition { Id = "second", Duration = StatusDuration.Events, Length = 1, TickEvent = typeof(SsTurnEnded).FullName };
            var expire = new Mark { text = "second" };
            second.OnExpire.Add(expire);
            _session.Statuses.Define(first);
            _session.Statuses.Define(second);
            var ended = new List<string>();
            _session.Events.Subscribe<StatusEnded>(e => ended.Add($"{e.status}:{e.expired}"));
            Do(g => { g.ApplyStatus(Hero, "first"); g.ApplyStatus(Hero, "second"); });
            TurnEnd("hero");
            Assert.AreEqual(0, expire.log.Count, "풀린 것은 끝날 때 결과가 돌지 않는다");
            CollectionAssert.AreEqual(new[] { "second:False", "first:True" }, ended, "끝남은 한 번씩");
        }

        // 결과: 가진 쪽을 파티에서 뺀다(쓰러져 전투에서 빠짐).
        public sealed class RemoveHolderUnit : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<SsParty>("party").units.Remove((SsUnit)context.User);
            public override string Describe(INameResolver names) => "가진 쪽 빼기";
        }

        [Test]
        public void 밀린_주기를_내는_도중에_가진_쪽이_사라지면_남은_회차를_내지_않는다()
        {
            var rot = new StatusDefinition { Id = "rot", Pulse = 1 };
            var marks = new Mark { text = "rot" };
            rot.Outcomes.Add(new RemoveHolderUnit());
            rot.Outcomes.Add(marks);
            _session.Statuses.Define(rot);
            Do(g => g.ApplyStatus(Foe, "rot"));
            _session.EndFrame(3.5);   // 1·2·3초 주기가 한 프레임에 밀렸다
            CollectionAssert.AreEqual(new[] { "rot:foe" }, marks.log, "첫 회차가 뺀 가진 쪽에는 남은 회차가 없다");
            Assert.IsNull(Foe);
            Assert.AreEqual(0, _session.Statuses.Of("foe").Count, "사라진 가진 쪽의 상태 효과는 정리된다");
        }

        // 결과: 가진 쪽에게 그 상태 효과를 다시 건다.
        public sealed class ReapplyStatusOutcome : Outcome
        {
            public string status;
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.ApplyStatus(context.User, status);
            public override string Describe(INameResolver names) => "다시 걸기";
        }

        [Test]
        public void 같은_순간에_끝나는_상태_효과를_앞의_끝날_때_결과가_다시_걸면_그것은_끝나지_않는다()
        {
            // 이벤트 횟수: 같은 턴 끝에 둘 다 끝나는데, first가 끝날 때 second를 다시 걸면(지속 갱신) second는 그 턴 끝에 끝나지 않는다.
            var first = new StatusDefinition { Id = "first", Duration = StatusDuration.Events, Length = 1, TickEvent = typeof(SsTurnEnded).FullName };
            first.OnExpire.Add(new ReapplyStatusOutcome { status = "second" });
            var second = new StatusDefinition { Id = "second", Reapply = StatusReapply.Refresh, Duration = StatusDuration.Events, Length = 1, TickEvent = typeof(SsTurnEnded).FullName };
            var expire = new Mark { text = "second" };
            second.OnExpire.Add(expire);
            _session.Statuses.Define(first);
            _session.Statuses.Define(second);
            Do(g => { g.ApplyStatus(Hero, "first"); g.ApplyStatus(Hero, "second"); });
            TurnEnd("hero");
            Assert.IsFalse(Game.HasStatus(Hero, "first"));
            Assert.IsTrue(Game.HasStatus(Hero, "second"), "다시 걸어 갱신했으니(1턴) 아직 남는다");
            Assert.AreEqual(0, expire.log.Count);
            TurnEnd("hero");
            Assert.IsFalse(Game.HasStatus(Hero, "second"), "갱신한 1턴이 지나면 끝난다");
            Assert.AreEqual(1, expire.log.Count);

            // 게임 시간: 같은 프레임에 끝나는 것도 같다.
            var fuse = new StatusDefinition { Id = "fuse", Duration = StatusDuration.Seconds, Length = 1 };
            fuse.OnExpire.Add(new ReapplyStatusOutcome { status = "glow" });
            var glow = new StatusDefinition { Id = "glow", Reapply = StatusReapply.Refresh, Duration = StatusDuration.Seconds, Length = 1 };
            _session.Statuses.Define(fuse);
            _session.Statuses.Define(glow);
            Do(g => { g.ApplyStatus(Foe, "fuse"); g.ApplyStatus(Foe, "glow"); });
            _session.EndFrame(1.5);
            Assert.IsFalse(Game.HasStatus(Foe, "fuse"));
            Assert.IsTrue(Game.HasStatus(Foe, "glow"), "fuse가 끝날 때 다시 건 glow는 그 프레임에 끝나지 않는다");
            _session.EndFrame(1);
            Assert.IsFalse(Game.HasStatus(Foe, "glow"));
        }

        [Test]
        public void 앞_가진_쪽의_결과가_없앤_가진_쪽은_발동하지_않는다()
        {
            var aura = new StatusDefinition { Id = "aura", When = new EventTrigger { eventType = typeof(SsTurnEnded).FullName } };
            var marks = new Mark { text = "aura" };
            aura.Outcomes.Add(new RemoveFoe());
            aura.Outcomes.Add(marks);
            _session.Statuses.Define(aura);
            Do(g => { g.ApplyStatus(Hero, "aura"); g.ApplyStatus(Foe, "aura"); });
            TurnEnd("hero");
            CollectionAssert.AreEqual(new[] { "aura:hero" }, marks.log, "주인공의 결과가 뺀 적은 그 순간에 발동하지 않는다");
            Assert.IsNull(Foe);
            Assert.AreEqual(0, _session.Statuses.Of("foe").Count, "사라진 가진 쪽의 상태 효과는 행동 끝에 정리된다");
        }

        [Test]
        public void 이벤트_횟수와_줄이기는_0점5에서_올림한다()
        {
            var weak = new StatusDefinition { Id = "weak", Duration = StatusDuration.Events, Length = 2.5, TickEvent = typeof(SsTurnEnded).FullName };
            _session.Statuses.Define(weak);
            Do(g => g.ApplyStatus(Hero, "weak"));
            Assert.AreEqual(3, Game.Statuses(Hero).Single().remaining, 1e-9, "2.5번 → 3번(짝수 쪽 2가 아니다)");
            Do(g => g.ShortenStatus(Hero, "weak", 0.5));
            Assert.AreEqual(2, Game.Statuses(Hero).Single().remaining, 1e-9, "0.5 줄이기 → 1번 줄인다");
        }

        // 행동 하나의 평균 시간(ms): 유닛 units명 모두에 보정 있는 상태 효과를 걸어 두고, 아무도 사라지지 않는 작은 행동을 되풀이한다.
        static double PruneCost(int units)
        {
            var session = new GameSession(new SessionOptions { Seed = 5, DetectUntrackedChanges = false });
            var party = new SsParty();
            for (int i = 0; i < units; i++) party.units.Add(new SsUnit { id = "u" + i });
            session.Data.Define("party", DataZone.Session, party, "파티");
            var mark = new StatusDefinition { Id = "mark" };
            mark.Modifiers.Add(new StatusModifier { attribute = nameof(SsUnit.attack), kind = ModifierKind.Flat, valuePerStack = 1 });
            session.Statuses.Define(mark);
            session.Game.Do(new Edit { run = g => { foreach (var unit in g.Get<SsParty>("party").units) g.ApplyStatus(unit, "mark"); } });
            for (int i = 0; i < 5; i++) session.Game.Do(new Edit { run = g => g.Get<SsParty>("party").units[0].hp-- });
            const int runs = 40;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) session.Game.Do(new Edit { run = g => g.Get<SsParty>("party").units[0].hp-- });
            var ms = watch.Elapsed.TotalMilliseconds / runs;
            Assert.AreEqual(units, session.Statuses.Of("u" + (units - 1)).Count + units - 1, "아무도 사라지지 않았으니 정리하지 않는다");
            return ms;
        }

        [Test]
        public void 사라진_개체_정리는_대상_수만큼_데이터를_훑지_않는다()
        {
            PruneCost(20);
            var small = PruneCost(60);
            var large = PruneCost(600);
            Console.WriteLine($"상태 효과 걸린 유닛 60명 → 600명: 행동 한 번 평균 {small:0.000}ms → {large:0.000}ms (×{large / small:0.0})");
            // 대상마다 Find하면(데이터 전체 × 대상 수) 10배 많은 유닛에 100배 가까이 든다. 한 번 훑으면 10배 남짓.
            Assert.IsTrue(large / small < 30, $"×{large / small:0.0}");
        }

        [Test]
        public void 다시_걸_때_의도가_주기를_0으로_하면_주기가_멈춘다()
        {
            // 처음 걸 때 주기 0이면 돌지 않는 것과 같게, 이미 돌던 것도 다시 걸 때 주기 0이면 멈춘다.
            var burn = new StatusDefinition { Id = "burn", MaxStacks = 0, Pulse = 1 };
            burn.Outcomes.Add(new Hurt());
            _session.Statuses.Define(burn);
            var freeze = new StatusDefinition { Id = "freeze", When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName } };
            freeze.Outcomes.Add(new ModifyIntentOutcome { path = "pulse", op = IntentOp.Set, value = "0" });
            _session.Statuses.Define(freeze);
            Do(g => g.ApplyStatus(Hero, "burn"));
            _session.EndFrame(1.5);
            Assert.AreEqual(29, Hero.hp, "1초: 주기 1번");

            Do(g => { g.ApplyStatus(Foe, "freeze"); g.ApplyStatus(Hero, "burn"); });
            Assert.AreEqual(0, Game.Statuses(Hero).Single(s => s.status == "burn").nextPulse, 1e-9, "주기 없음");
            _session.EndFrame(3);
            Assert.AreEqual(29, Hero.hp, "다시 걸 때 주기 0 → 더 돌지 않는다");

            Do(g => { g.RemoveStatus(Foe, "freeze"); g.ApplyStatus(Hero, "burn"); });
            _session.EndFrame(1.25);
            Assert.AreEqual(29 - 3, Hero.hp, "가로채기가 빠지면 정의의 주기로 지금부터 다시 센다(3중첩 × 1)");
        }

        [Test]
        public void 예약과_상태_효과는_판_저장으로_이어진다()
        {
            var shield = new StatusDefinition { Id = "shield", Duration = StatusDuration.Seconds, Length = 10 };
            shield.Modifiers.Add(new StatusModifier { attribute = nameof(SsUnit.attack), kind = ModifierKind.Flat, valuePerStack = 5 });
            _session.Statuses.Define(shield);
            var hero = Hero;
            Do(g => g.ScheduleAfter(3, new Smite { unit = hero, amount = 4 }));
            Do(g => g.ApplyStatus(hero, "shield"));
            var json = _session.ExportRun();

            var loaded = NewSession();
            loaded.Statuses.Define(shield);
            loaded.ImportRun(json);
            Assert.AreEqual(15, loaded.Game.Attribute("hero", nameof(SsUnit.attack)), 1e-9, "불러온 직후에도 보정이 보인다");
            Assert.IsTrue(loaded.Game.HasStatus("hero", "shield"));
            loaded.EndFrame(3.5);
            Assert.AreEqual(26, loaded.Game.Find<SsUnit>("hero").hp);
        }
    }
}
