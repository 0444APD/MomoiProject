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
