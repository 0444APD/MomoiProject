using System;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // [PlayerData] 클래스: 필드에 쓰는 순간 기록된다(위빙). 이 테스트 어셈블리도 Unity와 같은 위빙을 거친다.
    [Serializable, PlayerData] public class GsWeapon { public int power = 5; }

    [Serializable, PlayerData]
    public class GsUnit
    {
        public string id;
        public int hp = 30;
        public GsWeapon weapon = new GsWeapon();
        public GameList<string> tags = new GameList<string>();
    }

    [Serializable, PlayerData]
    public class GsParty
    {
        public int gold;
        public GameList<GsUnit> units = new GameList<GsUnit>();
        public GameDictionary<string, int> counts = new GameDictionary<string, int>();
    }

    [Serializable]
    public class GsHit : GameAction<GsUnit>
    {
        public int damage;
        protected override void Run(GsUnit u)
        {
            u.hp -= damage;
            u.weapon.power += 1;
        }
    }

    [Serializable]
    public class GsShuffle : SimpleAction
    {
        protected override void Run()
        {
            var party = Game.All<GsParty>()[0];
            party.units.Add(new GsUnit { id = "rookie", hp = 10 });
            party.units[0].tags.Remove("brave");
            party.counts["kills"] = 3;
            party.gold += 100;
            party.units[0].weapon = new GsWeapon { power = 1 };
        }
    }

    [Serializable]
    public class GsBroken : SimpleAction
    {
        protected override void Run()
        {
            Game.Find<GsUnit>("hero").hp = 1;
            throw new InvalidOperationException("의도된 실패");
        }
    }

    public class PlayerDataTests
    {
        GameSession _session;
        GsParty _party;

        void Create(bool detect)
        {
            _session = new GameSession(new SessionOptions { Seed = 3, DetectUntrackedChanges = detect });
            _party = _session.Data.Define("party", DataZone.Session, new GsParty
            {
                units = { new GsUnit { id = "hero", tags = { "brave", "fast" } } },
            }, "파티");
        }

        GsUnit Hero => _session.Game.Find<GsUnit>("hero");

        bool HasWarning => _session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning);

        [Test]
        public void 플레이어_데이터_클래스만_쓰는_순간_기록한다()
        {
            Assert.IsTrue(PlayerDataStore.IsTrackedType(typeof(GsParty)));
            Assert.IsFalse(PlayerDataStore.IsTrackedType(typeof(TestInventory)), "일반 List·[PlayerData] 없는 클래스는 예전처럼 비교");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 필드에_쓰면_그_순간_기록되고_Undo된다(bool detect)
        {
            Create(detect);
            _session.Do(new GsHit { target = "hero", damage = 7 });
            Assert.AreEqual(23, Hero.hp);
            Assert.AreEqual(6, Hero.weapon.power, "깊은 필드도");
            var entries = _session.Journal.Entries.Where(e => e.Kind == JournalKind.Data && e.Key == "party").ToList();
            Assert.IsTrue(entries.Any(e => e.Code != null && e.Code.Contains("hp")), string.Join("\n", entries.Select(e => e.Code)));
            Assert.IsTrue(entries.Any(e => e.Code != null && e.Code.Contains("weapon.power")));
            Assert.IsFalse(HasWarning, "기록된 쓰기는 누락 경고가 없다");

            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hero.hp);
            Assert.AreEqual(5, Hero.weapon.power);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 리스트_딕셔너리_객체_교체도_기록되고_Undo된다(bool detect)
        {
            Create(detect);
            var oldWeapon = Hero.weapon;
            _session.Do(new GsShuffle());
            Assert.AreEqual(2, _party.units.Count);
            CollectionAssert.AreEqual(new[] { "fast" }, Hero.tags);
            Assert.AreEqual(3, _party.counts["kills"]);
            Assert.AreEqual(100, _party.gold);
            Assert.AreEqual(1, Hero.weapon.power);
            Assert.IsFalse(HasWarning);

            // 갈아 끼운 새 무기도 이어서 기록되고, 빠진 옛 무기의 쓰기는 무시된다.
            _session.Do(new GsHit { target = "hero", damage = 0 });
            Assert.AreEqual(2, Hero.weapon.power);
            oldWeapon.power = 99;
            Assert.IsFalse(_session.Journal.Entries.Any(e => e.Message.Contains("99")));

            Assert.IsTrue(_session.Undo());
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(1, _party.units.Count);
            CollectionAssert.AreEqual(new[] { "brave", "fast" }, Hero.tags);
            Assert.IsFalse(_party.counts.ContainsKey("kills"));
            Assert.AreEqual(0, _party.gold);
            Assert.AreSame(oldWeapon, Hero.weapon, "같은 무기 인스턴스로 돌아온다");
        }

        [Test]
        public void 행동_밖에서_써도_기록된다()
        {
            Create(true);
            Hero.hp = 12;
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Data && e.Code != null && e.Code.Contains("hp")));
            _session.Do(new GsHit { target = "hero", damage = 2 });
            Assert.IsFalse(HasWarning, "기록된 쓰기는 누락이 아니다");
            Assert.AreEqual(10, Hero.hp);
        }

        [Test]
        public void 저장소에_넣지_않은_객체는_기록하지_않는다()
        {
            Create(true);
            var before = _session.Journal.Entries.Count();
            var loose = new GsUnit { id = "x" };
            loose.hp = 1;
            loose.tags.Add("a");
            Assert.AreEqual(before, _session.Journal.Entries.Count());
        }

        [Test]
        public void 실패한_행동은_쓴_것까지_되돌린다()
        {
            Create(true);
            Assert.Throws<InvalidOperationException>(() => _session.Do(new GsBroken()));
            Assert.AreEqual(30, Hero.hp);
        }

        [Test]
        public void 값_전체를_바꾼_뒤에도_이어서_기록된다()
        {
            Create(false);
            _session.Data.Set("party", new GsParty { units = { new GsUnit { id = "hero", hp = 50 } } });
            Assert.AreEqual(50, Hero.hp);
            _session.Do(new GsHit { target = "hero", damage = 5 });
            Assert.AreEqual(45, Hero.hp);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(50, Hero.hp);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Modify_안의_리플렉션_쓰기도_비교로_기록된다(bool detect)
        {
            Create(detect);
            var field = typeof(GsUnit).GetField("hp");
            _session.Do(new GsReflect());
            Assert.AreEqual(1, Hero.hp);
            Assert.IsFalse(HasWarning);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hero.hp);
            // 보상 블록도 같은 길(Modify + 리플렉션)로 쓴다.
            new PlayerDataReward { key = "party", path = "gold", amount = 7 }.Grant(_session);
            Assert.AreEqual(7, _party.gold);
            Assert.IsNotNull(field);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 큰_데이터에서도_쓴_만큼만_일한다(bool detect)
        {
            _session = new GameSession(new SessionOptions { Seed = 3, DetectUntrackedChanges = detect });
            var party = new GsParty();
            for (int i = 0; i < 500; i++) party.units.Add(new GsUnit { id = "u" + i, tags = { "a", "b" } });
            _session.Data.Define("party", DataZone.Session, party, "파티");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            const int runs = 200;
            for (int i = 0; i < runs; i++) _session.Do(new GsHit { target = "u" + (i % 500), damage = 1 });
            var ms = watch.Elapsed.TotalMilliseconds / runs;
            Console.WriteLine($"플레이어 데이터 클래스 유닛 500개, 누락 검사 {(detect ? "켬" : "끔")}: 행동 한 번 평균 {ms:0.00}ms");
            Assert.IsTrue(_session.Undo());
        }

        [Test]
        public void 조건은_쓰기로_다시_평가된다()
        {
            Create(false);
            _session.Achievements.Define(new AchievementDefinition { Id = "rich", DisplayName = "부자", Condition = new GsRich() });
            _session.Do(new GsShuffle());
            Assert.IsTrue(_session.Achievements.IsUnlocked("rich"));
        }
    }

    // 위빙이 닿지 않는 쓰기(리플렉션)를 Modify 안에서.
    [Serializable]
    public class GsReflect : SimpleAction
    {
        protected override void Run() =>
            Game.Session.Data.Modify<GsParty>("party", party => typeof(GsUnit).GetField("hp").SetValue(party.units[0], 1));
    }

    [Serializable, BlockInfo("골드 100 이상")]
    public class GsRich : SimpleCondition
    {
        protected override bool Check() => Game.All<GsParty>()[0].gold >= 100;
    }
}
