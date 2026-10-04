using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 턴제 예제: 게임 코드는 이 정도만 쓴다(Modify·등록·의존성 선언 없음).
    [Serializable] public class TbUnit { public string id; public string team; public int x, y; public int hp = 30; }
    [Serializable] public class TbBoard { public List<TbUnit> units = new List<TbUnit>(); public int turn; }

    public static class TbBoundUse
    {
        public static object User;

        [BindTo("fn.use")]
        static void Use(TriggerContext c) => User = c.User;
    }

    public static class TbRules
    {
        public static int Distance(TbUnit a, TbUnit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        public static IEnumerable<TbUnit> Around(this GameApi game, TbUnit center, int n) =>
            game.All<TbUnit>().Where(u => u != center && Distance(center, u) <= n);
    }

    [Serializable]
    public class TbMove : GameAction
    {
        public TbUnit unit;
        public int x, y;
        protected override void Run() { unit.x = x; unit.y = y; }
    }

    // 참조 필드 여러 개·리스트(대상 수가 정해지지 않은 행동).
    [Serializable]
    public class TbSwap : GameAction
    {
        public TbUnit a;
        public TbUnit b;
        public List<TbUnit> others = new List<TbUnit>();
        protected override void Run()
        {
            (a.x, b.x) = (b.x, a.x);
            foreach (var other in others) other.hp -= 1;
        }
    }

    [Serializable]
    public class TbUseInside : GameAction
    {
        protected override void Run()
        {
            Game.Use("nested.self", "hero");
            Game.Do(new TbEndTurn());
        }
    }

    [Serializable]
    public class TbEndTurn : GameAction
    {
        protected override void Run() => Game.All<TbBoard>()[0].turn++;
    }

    [Serializable]
    public class TbBroken : GameAction
    {
        protected override void Run()
        {
            Game.Find<TbUnit>("hero").hp = 1;
            throw new InvalidOperationException("의도된 실패");
        }
    }

    [Serializable, BlockInfo("피해", Category = "전투")]
    public class TbDamage : Effect<TbUnit>
    {
        public int amount = 10;
        public int spread;
        protected override void Apply(TbUnit target) => target.hp -= amount + Game.Random("damage").Range(0, spread + 1);
    }

    [Serializable, BlockInfo("주변 n칸의 적 추가", Category = "대상")]
    public class TbAddEnemiesAround : TargetSelector<TbUnit>
    {
        public int range = 1;
        protected override IEnumerable<TbUnit> Select(IEnumerable<TbUnit> targets, TbUnit user)
        {
            var list = targets.ToList();
            return list.Concat(list.SelectMany(t => Game.Around(t, range))).Where(u => u.team != user.team).Distinct();
        }
    }

    [Serializable, BlockInfo("적 전멸", Category = "전투")]
    public class TbAllEnemiesDown : SimpleCondition
    {
        public string team = "enemy";
        protected override bool Check() => Game.All<TbUnit>().Where(u => u.team == team).All(u => u.hp <= 0);
    }

    public class GameApiTests
    {
        GameSession _session;
        GameApi Game => _session.Game;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 7 });
            _session.Data.Define("board", DataZone.Session, new TbBoard
            {
                units =
                {
                    new TbUnit { id = "hero", team = "ally", x = 0, y = 0 },
                    new TbUnit { id = "orc1", team = "enemy", x = 3, y = 0 },
                    new TbUnit { id = "orc2", team = "enemy", x = 4, y = 0 },
                    new TbUnit { id = "orc3", team = "enemy", x = 9, y = 9 },
                    new TbUnit { id = "ally2", team = "ally", x = 3, y = 1 },
                },
            });
            var splash = new AbilityDefinition { Id = "splash" };
            splash.Targets.Add(new TbAddEnemiesAround { range = 1 });
            splash.Effects.Add(new TbDamage { amount = 10 });
            _session.Abilities.Define(splash);
        }

        int Hp(string id) => Game.Find<TbUnit>(id).hp;

        [Test]
        public void 행동의_참조_필드는_실행_직전에_지금_객체로_바뀐다()
        {
            var hero = Game.Find<TbUnit>("hero");
            var old = new TbUnit { id = "hero" };   // Undo·불러오기 뒤의 옛 객체처럼 데이터 밖의 객체
            Game.Do(new TbMove { unit = old, x = 5, y = 1 });
            Assert.AreEqual(5, hero.x, "id로 지금 데이터의 객체를 찾아 고친다");
            Assert.AreEqual(0, old.x, "넣은 옛 객체는 건드리지 않는다");
        }

        [Test]
        public void 참조_필드_여러_개와_리스트를_모두_찾는다()
        {
            var all = Game.All<TbUnit>();
            var a = all[0];
            var b = all[1];
            var ax = a.x;
            var bx = b.x;
            var hp = all[2].hp;
            Game.Do(new TbSwap { a = a, b = b, others = { new TbUnit { id = all[2].id } } });
            Assert.AreEqual(bx, a.x);
            Assert.AreEqual(ax, b.x);
            Assert.AreEqual(hp - 1, all[2].hp);

            var list = new List<TbUnit> { new TbUnit { id = all[2].id } };
            var action = new TbSwap { a = a, b = b, others = list };
            Game.Do(action);
            Assert.IsFalse(ReferenceEquals(list[0], all[2]), "넣어 준 목록은 그대로 둔다");
            Assert.IsTrue(ReferenceEquals(all[2], action.others[0]), "행동은 새 목록에 지금 객체를 담는다");
        }

        [Test]
        public void 찾지_못한_참조는_행동_실패로_변경이_없다()
        {
            var hero = Game.Find<TbUnit>("hero");
            var x = hero.x;
            var e = Assert.Throws<InvalidOperationException>(() => Game.Do(new TbMove { unit = new TbUnit { id = "nobody" }, x = 9 }));
            StringAssert.Contains("nobody", e.Message);
            Assert.AreEqual(x, hero.x);
        }

        [Test]
        public void 행동은_등록_없이_실행되고_직접_고친_값도_Undo된다()
        {
            Game.Do(new TbMove { unit = Game.Find<TbUnit>("hero"), x = 2, y = 0 });
            Assert.AreEqual(2, Game.Find<TbUnit>("hero").x);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Data && e.Key == "board"), "바뀐 곳이 기록된다");
            Assert.IsFalse(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning), "추적된 변경은 경고가 없다");
            Game.Do(new TbEndTurn());
            Assert.AreEqual(1, Game.All<TbBoard>()[0].turn);

            Assert.IsTrue(_session.Undo());
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, Game.Find<TbUnit>("hero").x);
            Assert.AreEqual(0, Game.All<TbBoard>()[0].turn);
        }

        [Test]
        public void 능력은_대상을_이어_고르고_대상마다_효과를_적용한다()
        {
            var used = new List<AbilityUsed>();
            _session.Events.Subscribe<AbilityUsed>(used.Add);
            Game.Use("splash", Game.Find<TbUnit>("hero"), Game.Find<TbUnit>("orc1"));

            Assert.AreEqual(20, Hp("orc1"), "고른 대상");
            Assert.AreEqual(20, Hp("orc2"), "고른 대상 주변 1칸의 적");
            Assert.AreEqual(30, Hp("ally2"), "아군은 빠진다");
            Assert.AreEqual(30, Hp("orc3"), "멀리 있는 적은 빠진다");
            Assert.AreEqual(1, used.Count);
            CollectionAssert.AreEquivalent(new[] { "orc1", "orc2" }, used[0].Targets);

            Assert.IsTrue(_session.Undo(), "능력 사용 전체가 행동 하나");
            Assert.AreEqual(30, Hp("orc1"));
            Assert.AreEqual(30, Hp("orc2"));
        }

        [Test]
        public void 함수로_연결한_결과가_사용_결과가_된다()
        {
            _session.Abilities.Define(new AbilityDefinition { Id = "fn.use" });
            Game.Use("fn.use", "hero");
            Assert.AreSame(Game.Find("hero"), TbBoundUse.User, "컨텍스트의 사용자");
        }

        [Test]
        public void 행동_안에서_부른_행동과_능력은_바깥_행동_하나로_묶인다()
        {
            var heal = new AbilityDefinition { Id = "nested.self" };
            heal.Targets.Add(new SelfTarget());
            heal.Effects.Add(new TbDamage { amount = -5 });
            _session.Abilities.Define(heal);
            var before = _session.UndoCount;
            Game.Do(new TbUseInside());
            Assert.AreEqual(35, Hp("hero"), "안에서 쓴 능력");
            Assert.AreEqual(1, Game.All<TbBoard>()[0].turn, "안에서 부른 행동");
            Assert.AreEqual(before + 1, _session.UndoCount, "Undo 단위는 하나");
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hp("hero"));
            Assert.AreEqual(0, Game.All<TbBoard>()[0].turn);
        }

        [Test]
        public void 사용자_자신에게_부여한다()

        {
            var heal = new AbilityDefinition { Id = "self" };
            heal.Targets.Add(new SelfTarget());
            heal.Effects.Add(new TbDamage { amount = -5 });
            _session.Abilities.Define(heal);
            Game.Use("self", "hero");
            Assert.AreEqual(35, Hp("hero"));
        }

        [Test]
        public void 조건은_읽은_값으로_자동_감시된다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "wipe", DisplayName = "전멸", Condition = new TbAllEnemiesDown() });
            var nuke = new AbilityDefinition { Id = "nuke" };
            nuke.Effects.Add(new TbDamage { amount = 100 });
            _session.Abilities.Define(nuke);

            Game.Use("nuke", "hero", "orc1", "orc2");
            Assert.IsFalse(_session.Achievements.IsUnlocked("wipe"));
            Game.Use("nuke", "hero", "orc3");
            Assert.IsTrue(_session.Achievements.IsUnlocked("wipe"), "CollectDependencies 없이도 board가 바뀌면 다시 평가된다");
        }

        [Test]
        public void 실패한_행동은_직접_고친_값까지_되돌린다()
        {
            Assert.Throws<InvalidOperationException>(() => Game.Do(new TbBroken()));
            Assert.AreEqual(30, Hp("hero"));
        }

        [Test]
        public void 행동_밖에서_고친_값은_행동_밖_변경으로_기록된다()
        {
            var held = Game.Find<TbUnit>("hero");
            held.hp = 1;
            Game.Do(new TbEndTurn());
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Data && e.TransactionId == 0 && e.Code != null && e.Code.Contains("hp")));
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(1, Hp("hero"), "행동 밖 변경은 행동의 Undo로 되돌리지 않는다");
        }

        [Test]
        public void 새_id는_순서대로_나온다()
        {
            Assert.AreEqual("unit1", Game.NewId("unit"));
            Assert.AreEqual("unit2", Game.NewId("unit"));
        }

        [Test]
        public void id가_없는_형식은_Find할_수_없다()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Game.Find<TbBoard>("x"));
            StringAssert.Contains("id", e.Message);
        }
    }
}
