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

        public static List<object> Picked;

        [BindTo("fn.dash")]
        static void Dash(TriggerContext c) => Picked = c.Picked.ToList();
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

    // 첫 대상(orc1)에게 주는 도중 같은 액티브 효과를 다른 대상(orc2)에게 한 번 더 쓴다 — 같은 효과 Block 인스턴스가 안에서 다시 돈다(반격·연쇄).
    [Serializable, BlockInfo("연쇄 피해", Category = "전투")]
    public class TbChainHit : Effect<TbUnit>
    {
        public int amount = 5;
        protected override void Apply(TbUnit target)
        {
            if (target.id == "orc1") Game.Use("chain", User<TbUnit>(), "orc2");
            target.hp -= User<TbUnit>() == null ? 0 : amount;
        }
    }

    // 고르는 도중 같은 액티브 효과를 다른 대상(orc2)에게 한 번 더 쓴다 — 같은 대상 Block 인스턴스가 안에서 다시 돈다.
    [Serializable, BlockInfo("연쇄 대상", Category = "대상")]
    public class TbChainPick : TargetSelector<TbUnit>
    {
        protected override IEnumerable<TbUnit> Select(IEnumerable<TbUnit> targets, TbUnit user)
        {
            var list = targets.ToList();
            if (list.Any(t => t.id == "orc1")) Game.Use("chainPick", user, "orc2");
            return Use?.User == null ? new List<TbUnit>() : list;
        }
    }

    [Serializable, BlockInfo("적 전멸", Category = "전투")]
    public class TbAllEnemiesDown : SimpleCondition
    {
        public string team = "enemy";
        protected override bool Check() => Game.All<TbUnit>().Where(u => u.team == team).All(u => u.hp <= 0);
    }

    // 검사 도중 같은 조건 인스턴스가 한 번 더 검사된다(스탯의 보정 조건이 이 조건일 때처럼) — 끝난 뒤 바깥 검사의 Game이 남아야 한다.
    [Serializable, BlockInfo("다시 검사", Category = "시험")]
    public class TbReentrantCheck : SimpleCondition
    {
        [NonSerialized] int _depth;
        protected override bool Check()
        {
            if (_depth == 0)
            {
                _depth++;
                try { Evaluate(Game.Session); }
                finally { _depth--; }
            }
            return Game != null;
        }
    }

    // 결과를 내는 도중 같은 결과 인스턴스가 한 번 더 돈다(결과가 쏜 이벤트가 같은 항목을 다시 발동할 때처럼) — 끝난 뒤 바깥 순간이 남아야 한다.
    [Serializable, BlockInfo("다시 내기", Category = "시험")]
    public class TbReentrantReward : Reward
    {
        [NonSerialized] int _depth;
        [NonSerialized] public bool outerContext;
        public override RewardEffect Effect => RewardEffect.Reversible;
        public override string Describe(INameResolver names) => "다시 내기";
        public override void Grant(GameSession session)
        {
            if (_depth > 0) return;
            _depth++;
            try { Run(new TriggerContext { Session = session, Owner = "inner" }); }
            finally { _depth--; }
            outerContext = Context?.Owner == "outer";
        }
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
        public void InstancesOf는_그_클래스의_원본만_정의한_차례로_준다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.DefineInstance("orc", new TbUnit { id = "orc", hp = 20 });
            session.DefineInstance("board", new TbBoard());
            session.DefineInstance("elf", new TbUnit { id = "elf", hp = 12 });
            var units = session.Game.InstancesOf<TbUnit>().ToList();
            CollectionAssert.AreEqual(new[] { "orc", "elf" }, units.Select(u => u.id).ToArray());
            Assert.AreEqual(12, units[1].value.hp);
            Assert.AreEqual(20, ((TbUnit)session.CopyInstance("orc")).hp);
        }

        [Serializable] public class TbPotion { public string id; public Usable usable; }

        [Test]
        public void DefineInstance는_사용_기능이_붙은_원본을_Game_Use로_쓸_수_있게_한다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.DefineInstance("potion", new TbPotion { id = "potion", usable = new Usable() });
            session.DefineInstance("rock", new TbPotion { id = "rock" });   // 사용 기능 없음
            Assert.DoesNotThrow(() => session.Game.Use("potion", null));
            Assert.Throws<InvalidOperationException>(() => session.Game.Use("rock", null));
        }

        [Test]
        public void 찾지_못한_참조는_행동_실패로_변경이_없다()
        {
            // 보낸 사이에 사라진 대상(실시간 · 멀티의 엇갈림)은 플레이어용 실패 — 화면이 알림으로 보이고 콘솔 오류가 아니다.
            var hero = Game.Find<TbUnit>("hero");
            var x = hero.x;
            var e = Assert.Throws<GameFailure>(() => Game.Do(new TbMove { unit = new TbUnit { id = "nobody" }, x = 9 }));
            Assert.AreEqual(FailureKeys.TargetGone, e.Key);
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
        public void 액티브_효과는_대상을_이어_고르고_대상마다_효과를_적용한다()
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

            Assert.IsTrue(_session.Undo(), "액티브 효과 사용 전체가 행동 하나");
            Assert.AreEqual(30, Hp("orc1"));
            Assert.AreEqual(30, Hp("orc2"));
        }

        [Test]
        public void 칸도_액티브_효과의_대상으로_받는다()
        {
            _session.Abilities.Define(new AbilityDefinition { Id = "fn.dash" });
            Game.Use("fn.dash", "hero", Game.Find("hero"), new Cell(2, 3));
            Assert.AreSame(Game.Find("hero"), TbBoundUse.Picked[0], "개체 대상이 먼저");
            Assert.AreEqual(new Cell(2, 3), TbBoundUse.Picked[1], "칸은 Cell 값으로");
        }

        [Test]
        public void 함수로_연결한_결과가_사용_결과가_된다()
        {
            _session.Abilities.Define(new AbilityDefinition { Id = "fn.use" });
            Game.Use("fn.use", "hero");
            Assert.AreSame(Game.Find("hero"), TbBoundUse.User, "컨텍스트의 사용자");
        }

        [Test]
        public void 행동_안에서_부른_행동과_액티브_효과는_바깥_행동_하나로_묶인다()
        {
            var heal = new AbilityDefinition { Id = "nested.self" };
            heal.Targets.Add(new SelfTarget());
            heal.Effects.Add(new TbDamage { amount = -5 });
            _session.Abilities.Define(heal);
            var before = _session.UndoCount;
            Game.Do(new TbUseInside());
            Assert.AreEqual(35, Hp("hero"), "안에서 쓴 액티브 효과");
            Assert.AreEqual(1, Game.All<TbBoard>()[0].turn, "안에서 부른 행동");
            Assert.AreEqual(before + 1, _session.UndoCount, "Undo 단위는 하나");
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hp("hero"));
            Assert.AreEqual(0, Game.All<TbBoard>()[0].turn);
        }

        [Test]
        public void 효과_안에서_같은_액티브_효과를_다시_써도_바깥_효과의_사용자가_남는다()
        {
            var chain = new AbilityDefinition { Id = "chain" };
            chain.Effects.Add(new TbChainHit());
            _session.Abilities.Define(chain);
            Game.Use("chain", "hero", "orc1");
            Assert.AreEqual(25, Hp("orc2"), "안쪽 사용");
            Assert.AreEqual(25, Hp("orc1"), "안쪽 사용이 끝난 뒤에도 바깥 효과는 자기 사용(사용자·Game)을 본다");

            var pick = new AbilityDefinition { Id = "chainPick" };
            pick.Targets.Add(new TbChainPick());
            pick.Effects.Add(new TbDamage { amount = 10 });
            _session.Abilities.Define(pick);
            Game.Use("chainPick", "hero", "orc1");
            Assert.AreEqual(15, Hp("orc2"), "안쪽 사용");
            Assert.AreEqual(15, Hp("orc1"), "대상 Block도 안쪽 사용이 끝나면 바깥 사용을 본다");
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
        public void 숫자로_끝나는_접두어의_id는_다른_접두어의_id와_겹치지_않는다()
        {
            var ids = new List<string>();
            for (var i = 0; i < 11; i++) ids.Add(Game.NewId("u"));
            Assert.AreEqual("u11", ids[10], "숫자로 끝나지 않는 접두어는 예전과 같다");
            ids.Add(Game.NewId("u1"));
            Assert.AreEqual("u1_1", ids[11], "u의 11번(u11)과 겹치지 않게 사이에 _");
            ids.AddRange(Game.NewIds("u1", 2));
            ids.Add(Game.NewId("u1_"));
            CollectionAssert.AreEqual(new[] { "u1_2", "u1_3", "u1_4" }, ids.Skip(12).ToList(), "u1과 u1_은 같은 번호를 이어 쓴다");
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), string.Join(", ", ids));
        }

        [Test]
        public void 조금씩_찬_값도_같은_수로_보고_비교하고_낸다()
        {
            // 0.1을 열 번 더하면 0.9999999999999999 — 화면에는 1이고, 비교·비용도 1로 본다(같음과 이상·미만이 서로 어긋나지 않게).
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.Data.Define("mana", DataZone.Session, 0.0);
            var mana = 0.0;
            for (var i = 0; i < 10; i++) mana += 0.1;
            Assert.IsTrue(mana < 1.0, "이 시험의 전제(부동소수 오차)");
            session.Do(new DeveloperEditAction { Edit = () => session.Data.Set("mana", mana) });
            Assert.IsTrue(new FieldFilter { op = FieldOp.Equal, value = "1" }.Test(mana));
            Assert.IsTrue(new FieldFilter { op = FieldOp.AtLeast, value = "1" }.Test(mana), "1과 같으면 1 이상");
            Assert.IsFalse(new FieldFilter { op = FieldOp.Less, value = "1" }.Test(mana), "1과 같으면 1 미만이 아니다");
            Assert.IsTrue(new FieldFilter { op = FieldOp.Less, value = "1" }.Test(0.999), "진짜 작은 것은 그대로");
            Assert.IsTrue(CompareOps.Apply(CompareOp.AtLeast, mana, 1), "로그 비교도 같은 규칙");
            Assert.IsFalse(CompareOps.Apply(CompareOp.LessThan, mana, 1));
            Assert.IsTrue(CompareOps.Apply(CompareOp.Equal, mana, 1));
            Assert.IsTrue(session.Game.CanPay("mana", "", 1), "낼 수 있다고 보인다");
            session.Do(new DeveloperEditAction { Edit = () => session.Game.Pay("mana", "", 1) });
            Assert.AreEqual(0.0, session.Data.GetNumber("mana"), "하한(0)으로 — 아주 작은 음수가 남지 않는다");
            Assert.Throws<GameFailure>(() => session.Do(new DeveloperEditAction { Edit = () => session.Game.Pay("mana", "", 0.5) }), "진짜 모자라면 실패");
        }

        // 조사: 공백뿐인 이름도 던지지 않고 받침을 모르는 이름처럼 둘 다 적는다(그 이름을 쓰는 조건 문장·DB 화면이 멈추지 않게).
        [Test]
        public void 조사는_공백뿐인_이름에도_던지지_않는다()
        {
            Assert.AreEqual("   (이)가", Josa.IGa("   "));
            Assert.AreEqual("(이)라면", Josa.RaMyeon(" "));
            Assert.AreEqual(" (이)가 100 이상", CompareOps.Clause(" ", CompareOp.AtLeast, "100"));
            Assert.AreEqual("처치 수가 100 이상", CompareOps.Clause("처치 수", CompareOp.AtLeast, "100"), "보통 이름은 그대로");
            Assert.AreEqual("골드를", Josa.EulReul("골드"));
        }

        // 정수 칸에 담기지 않는 수는 자르지 않고 던지되, 무엇을 고칠지 알리는 글로(.NET의 "Value was either too large…" 대신).
        [Test]
        public void 정수_칸에_담기지_않는_수는_고칠_것을_알리며_던진다()
        {
            var big = Assert.Throws<OverflowException>(() => GameNumbers.ToType(3e9, typeof(int)));
            StringAssert.Contains("Int32 칸에 담기지 않습니다", big.Message);
            var nan = Assert.Throws<OverflowException>(() => GameNumbers.ToType(double.NaN, typeof(int)));
            StringAssert.Contains("NaN", nan.Message);
            Assert.AreEqual(3000000000L, GameNumbers.ToType(3e9, typeof(long)), "담기는 형식은 그대로");
            Assert.AreEqual(3, GameNumbers.ToType(2.5, typeof(int?)), "반올림 규칙도 그대로");
        }

        [Test]
        public void 조건이_검사_도중_다시_검사돼도_바깥_검사의_Game이_남는다()
        {
            Assert.IsTrue(new TbReentrantCheck().Evaluate(_session), "안쪽 검사가 끝나도 바깥 Check의 Game은 null이 아니다");
        }

        [Test]
        public void 결과가_내는_도중_다시_돌아도_바깥_순간이_남는다()
        {
            var reward = new TbReentrantReward();
            reward.Run(new TriggerContext { Session = _session, Owner = "outer" });
            Assert.IsTrue(reward.outerContext, "안쪽 Run이 끝나도 바깥 Grant의 Context는 바깥 순간");
        }

        [Test]
        public void id가_없는_형식은_Find할_수_없다()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Game.Find<TbBoard>("x"));
            StringAssert.Contains("id", e.Message);
        }
    }
}
