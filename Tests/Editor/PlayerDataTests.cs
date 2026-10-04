using System;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 플레이어 데이터 클래스: 평범한 [Serializable] 클래스(List·Dictionary 그대로). Mirror가 Sync할 때 바뀐 곳을 기록한다.
    // 이 테스트 어셈블리도 Unity와 같은 Mirror 비교 코드 생성을 거친다.
    [Serializable] public class GsWeapon { public int power = 5; }

    [Serializable]
    public class GsUnit
    {
        public string id;
        public int hp = 30;
        public GsWeapon weapon = new GsWeapon();
        public System.Collections.Generic.List<string> tags = new System.Collections.Generic.List<string>();
    }

    [Serializable]
    public class GsParty
    {
        public int gold;
        public System.Collections.Generic.List<GsUnit> units = new System.Collections.Generic.List<GsUnit>();
        public System.Collections.Generic.Dictionary<string, int> counts = new System.Collections.Generic.Dictionary<string, int>();
    }

    // [Serializable]이 없는 클래스: 비교 코드가 없어 예전 방식(리플렉션 비교)으로 다룬다.
    public class GsLoose
    {
        public int x;
    }

    [Serializable]
    public class GsHit : GameAction
    {
        public GsUnit unit;
        public int damage;
        protected override void Run()
        {
            unit.hp -= damage;
            unit.weapon.power += 1;
        }

        // 시험용: id만 든 객체(실행 직전에 id로 지금 객체를 찾는다).
        public static GsUnit Ref(string id) => new GsUnit { id = id };
    }

    [Serializable]
    public class GsShuffle : GameAction
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

    // 행동 중간에 Game.All(Sync가 일어남)을 끼운 행동.
    [Serializable]
    public class GsFindBetween : GameAction
    {
        protected override void Run()
        {
            var party = Game.All<GsParty>()[0];
            party.gold = 3;
            party.units.Add(new GsUnit { id = "mid" });
            party = Game.All<GsParty>()[0];
            party.gold = 0;
            party.units.RemoveAt(0);
        }
    }

    [Serializable]
    public class GsBroken : GameAction
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
        public void Serializable_클래스는_Mirror로_비교한다()
        {
            Assert.IsTrue(PlayerDataStore.IsMirrored(typeof(GsParty)), "List·Dictionary를 그대로 쓴 클래스");
            Assert.IsFalse(PlayerDataStore.IsMirrored(typeof(GsLoose)), "[Serializable]이 없으면 예전 방식");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 필드를_고치면_행동이_끝날_때_기록되고_Undo된다(bool detect)
        {
            Create(detect);
            _session.Do(new GsHit { unit = GsHit.Ref("hero"), damage = 7 });
            Assert.AreEqual(23, Hero.hp);
            Assert.AreEqual(6, Hero.weapon.power, "깊은 필드도");
            var entries = _session.Journal.Entries.Where(e => e.Kind == JournalKind.Data && e.Key == "party").ToList();
            Assert.IsTrue(entries.Any(e => e.Code != null && e.Code.Contains("units[#") && e.Code.Contains("].hp")), string.Join("\n", entries.Select(e => e.Code)));
            Assert.IsTrue(entries.Any(e => e.Code != null && e.Code.Contains("weapon.power")));
            Assert.IsFalse(HasWarning, "Mirror 데이터는 누락 경고가 없다");

            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hero.hp);
            Assert.AreEqual(5, Hero.weapon.power);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 리스트_딕셔너리_객체_교체도_기록되고_Undo된다(bool detect)
        {
            Create(detect);
            var hero = Hero;
            var oldWeapon = Hero.weapon;
            _session.Do(new GsShuffle());
            Assert.AreEqual(2, _party.units.Count);
            CollectionAssert.AreEqual(new[] { "fast" }, Hero.tags);
            Assert.AreEqual(3, _party.counts["kills"]);
            Assert.AreEqual(100, _party.gold);
            Assert.AreEqual(1, Hero.weapon.power);
            Assert.IsFalse(HasWarning);

            // 갈아 끼운 새 무기도 이어서 기록되고, 빠진 옛 무기를 고친 것은 데이터가 아니라 무시된다.
            _session.Do(new GsHit { unit = GsHit.Ref("hero"), damage = 0 });
            Assert.AreEqual(2, Hero.weapon.power);
            oldWeapon.power = 99;
            _session.EndFrame(0.016);
            Assert.IsFalse(_session.Journal.Entries.Any(e => e.Message.Contains("99")));

            Assert.IsTrue(_session.Undo());
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(1, _party.units.Count);
            Assert.AreSame(hero, Hero, "리스트에 남은 유닛은 같은 인스턴스");
            CollectionAssert.AreEqual(new[] { "brave", "fast" }, Hero.tags);
            Assert.IsFalse(_party.counts.ContainsKey("kills"));
            Assert.AreEqual(0, _party.gold);
            Assert.AreEqual(5, Hero.weapon.power, "옛 무기 값으로 돌아온다");
        }

        [Test]
        public void 행동_밖에서_고친_것은_다음_Sync에_행동_밖_변경으로_남는다()
        {
            Create(true);
            Hero.hp = 12;
            _session.EndFrame(0.016);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Data && e.Code != null && e.Code.Contains("hp")));
            _session.Do(new GsHit { unit = GsHit.Ref("hero"), damage = 2 });
            Assert.IsFalse(HasWarning);
            Assert.AreEqual(10, Hero.hp);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(12, Hero.hp, "행동 밖 변경은 그 행동의 Undo에 섞이지 않는다");
        }

        [Test]
        public void 저장소에_넣지_않은_객체는_기록하지_않는다()
        {
            Create(true);
            var before = _session.Journal.Entries.Count();
            var loose = new GsUnit { id = "x" };
            loose.hp = 1;
            loose.tags.Add("a");
            _session.EndFrame(0.016);
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
            _session.Do(new GsHit { unit = GsHit.Ref("hero"), damage = 5 });
            Assert.AreEqual(45, Hero.hp);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(50, Hero.hp);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Modify_안의_리플렉션_쓰기도_기록된다(bool detect)
        {
            Create(detect);
            _session.Do(new GsReflect());
            Assert.AreEqual(1, Hero.hp);
            Assert.IsFalse(HasWarning);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(30, Hero.hp);
            // 보상 블록도 같은 길(Modify + 리플렉션)로 쓴다.
            new PlayerDataReward { key = "party", path = "gold", amount = 7 }.Grant(_session);
            Assert.AreEqual(7, _party.gold);
        }

        [Test]
        public void 예전_방식_데이터도_그대로_동작한다()
        {
            _session = new GameSession(new SessionOptions { Seed = 3 });
            var loose = _session.Data.Define("loose", DataZone.Session, new GsLoose(), "느슨한 값");
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("예전 방식")));
            _session.Data.Modify<GsLoose>("loose", v => v.x = 4);
            Assert.AreEqual(4, loose.x);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 큰_데이터에서도_가볍다(bool detect)
        {
            _session = new GameSession(new SessionOptions { Seed = 3, DetectUntrackedChanges = detect });
            var party = new GsParty();
            for (int i = 0; i < 500; i++) party.units.Add(new GsUnit { id = "u" + i, tags = { "a", "b" } });
            _session.Data.Define("party", DataZone.Session, party, "파티");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            const int runs = 200;
            for (int i = 0; i < runs; i++) _session.Do(new GsHit { unit = GsHit.Ref("u" + (i % 500)), damage = 1 });
            var ms = watch.Elapsed.TotalMilliseconds / runs;
            Console.WriteLine($"Mirror 유닛 500개, 누락 검사 {(detect ? "켬" : "끔")}: 행동 한 번 평균 {ms:0.00}ms");
            Assert.IsTrue(_session.Undo());
        }

        [Test]
        public void 조건은_Sync로_다시_평가된다()
        {
            Create(false);
            _session.Achievements.Define(new AchievementDefinition { Id = "rich", DisplayName = "부자", Condition = new GsRich() });
            _session.Do(new GsShuffle());
            Assert.IsTrue(_session.Achievements.IsUnlocked("rich"));
        }

        [Test]
        public void ChangeSet을_받는_쪽에_적용하면_같아진다()
        {
            Create(false);
            var receiver = new MirrorReceiver();
            var copy = (GsParty)receiver.Load(_session.Data.Snapshot("party"));
            ChangeSet last = null;
            _session.Data.Synced += c => last = c;
            _session.Do(new GsShuffle());
            Assert.IsNotNull(last);
            receiver.Apply(copy, last);
            var a = new MirrorWriter();
            var b = new MirrorWriter();
            Mirror.Write(a, _party);
            Mirror.Write(b, copy);
            Assert.IsTrue(a.SameAs(b), "앞으로 기록만 보내도 같아진다");
            Assert.IsTrue(last.Forward.Length < 200, "변경분만 보낸다: " + last.Forward.Length);
        }

        [Test]
        public void 되돌리기가_행동_밖_변경을_덮으면_경고한다()
        {
            Create(false);
            _session.Do(new GsShuffle());
            _party.units[0].hp = 1;
            _session.EndFrame(0.016);
            Assert.IsTrue(_session.Undo());
            Assert.IsFalse(HasWarning, "행동이 건드리지 않은 곳(hp)은 덮지 않는다");

            _session.Do(new GsShuffle());
            _party.gold = 5;
            _session.EndFrame(0.016);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, _party.gold, "되돌리기는 행동 전 값으로");
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("gold")), "덮었다고 알린다");
        }

        [Test]
        public void 행동_중간의_Sync는_행동이_끝날_때_한꺼번에_알린다()
        {
            Create(false);
            var receiver = new MirrorReceiver();
            var copy = (GsParty)receiver.Load(_session.Data.Snapshot("party"));
            var received = new System.Collections.Generic.List<ChangeSet>();
            var midAction = 0;
            _session.Data.Synced += c =>
            {
                if (_session.Data.InTransaction) midAction++;
                received.Add(c);
            };
            _session.Do(new GsFindBetween());
            Assert.AreEqual(0, midAction, "행동 중에는 알리지 않는다");
            Assert.IsTrue(received.Count > 0);
            foreach (var changes in received) receiver.Apply(copy, changes);
            var a = new MirrorWriter();
            var b = new MirrorWriter();
            Mirror.Write(a, _party);
            Mirror.Write(b, copy);
            Assert.IsTrue(a.SameAs(b), "모아 둔 변경을 차례대로 적용하면 같아진다");
        }
    }

    // 생성 코드가 닿지 않는 쓰기(리플렉션)를 Modify 안에서.
    [Serializable]
    public class GsReflect : GameAction
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
