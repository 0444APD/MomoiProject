using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Serialization;

namespace GameSystems.Tests
{
    public enum TestElement
    {
        None,
        Fire,
        Ice,
    }

    public interface ITestEffect
    {
    }

    [Serializable]
    public sealed class TestLifesteal : ITestEffect
    {
        public double ratio = 0.1;
    }

    [Serializable]
    public sealed class TestBurn : ITestEffect
    {
        public int damage = 3;
        public TestElement element = TestElement.Fire;
    }

    [Serializable]
    public sealed class TestItem
    {
        public string name;
        public int level;
        public List<string> tags = new List<string>();
        public List<ITestEffect> effects = new List<ITestEffect>();
    }

    [Serializable]
    public sealed class TestInventory
    {
        public int gold;
        public List<TestItem> items = new List<TestItem>();
        public Dictionary<string, int> counts = new Dictionary<string, int>();
    }

    [Serializable]
    public sealed class TestBadData
    {
        public int value;
        public Action onChanged;
    }

    [Serializable]
    public sealed class TestRenamed
    {
        [FormerlySerializedAs("hp")] public int health;
    }

    [Serializable]
    public sealed class TestNode
    {
        public int value;
        public TestNode next;
    }

    [Serializable]
    public sealed class TestCollections
    {
        public HashSet<string> flags = new HashSet<string>();
        public Queue<int> queue = new Queue<int>();
        public Stack<string> stack = new Stack<string>();
        public LinkedList<TestItem> chain = new LinkedList<TestItem>();
        public int? optional;
        public DateTime when;
        public TimeSpan cooldown;
        public Guid guid;
    }

    [Serializable]
    public sealed class DataAction : GameAction
    {
        public override string Describe() => "데이터 행동";
    }

    public class DataStoreTests
    {
        GameSession _session;
        TestInventory _inventory;
        Action<TestInventory> _onAction;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 7 });
            _inventory = _session.State.Define("s:inventory", StateZone.Session, new TestInventory(), "인벤토리");
            _session.Actions.Register<DataAction>(_ => _session.State.Modify("s:inventory", _onAction));
        }

        void Do(Action<TestInventory> change)
        {
            _onAction = change;
            _session.Do(new DataAction());
        }

        static TestItem Sword() => new TestItem { name = "검", level = 1, tags = { "근접" }, effects = { new TestLifesteal() } };

        [Test]
        public void 바뀐_곳을_경로로_기록한다()
        {
            Do(inv => inv.items.Add(Sword()));
            Do(inv => inv.items[0].level = 4);

            var id = _session.State.Identity.IdOf(_inventory.items[0]);
            var entry = _session.Journal.Entries.Last(e => e.Kind == JournalKind.State);
            StringAssert.Contains($"items[#{id}].level", entry.Message);
            StringAssert.Contains("1 → 4", entry.Message);
        }

        [Test]
        public void 무르면_같은_인스턴스가_제자리로_돌아온다()
        {
            Do(inv => inv.items.Add(Sword()));
            var sword = _inventory.items[0];

            Do(inv =>
            {
                inv.items[0].level = 9;
                inv.items[0].tags.Add("전설");
                inv.gold = 100;
            });
            Assert.IsTrue(_session.Undo());

            Assert.AreSame(sword, _inventory.items[0], "게임 코드가 쥔 인스턴스가 그대로 유지된다");
            Assert.AreEqual(1, sword.level);
            CollectionAssert.AreEqual(new[] { "근접" }, sword.tags);
            Assert.AreEqual(0, _inventory.gold);
        }

        [Test]
        public void 제거를_무르면_빠졌던_인스턴스가_같은_ID로_돌아온다()
        {
            Do(inv =>
            {
                inv.items.Add(Sword());
                inv.items.Add(new TestItem { name = "방패" });
            });
            var sword = _inventory.items[0];
            var id = _session.State.Identity.IdOf(sword);

            Do(inv => inv.items.RemoveAt(0));
            Assert.AreEqual(1, _inventory.items.Count);
            _session.Undo();

            Assert.AreEqual(2, _inventory.items.Count);
            Assert.AreSame(sword, _inventory.items[0]);
            Assert.AreEqual(id, _session.State.Identity.IdOf(_inventory.items[0]));
        }

        [Test]
        public void 행동이_실패하면_데이터도_되돌린다()
        {
            _session.Actions.Register<FailingAction>(_ =>
            {
                _session.State.Modify<TestInventory>("s:inventory", inv => inv.gold = 50);
                throw new InvalidOperationException("의도된 실패");
            });
            Assert.Throws<InvalidOperationException>(() => _session.Do(new FailingAction()));
            Assert.AreEqual(0, _inventory.gold);
            Assert.AreEqual(0, _session.State.HistoryCount);
        }

        [Test]
        public void Modify_중_예외가_나면_바꾸기_전으로_돌아간다()
        {
            Assert.Throws<InvalidOperationException>(() => _session.State.Modify<TestInventory>("s:inventory", inv =>
            {
                inv.gold = 10;
                throw new InvalidOperationException("중간 실패");
            }));
            Assert.AreEqual(0, _inventory.gold);
        }

        [Test]
        public void Modify를_거치지_않은_수정은_경고하고_받아들인다()
        {
            _inventory.gold = 77;
            Assert.AreEqual(1, _session.State.CheckUntrackedChanges());
            var warning = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Warning);
            StringAssert.Contains("Modify를 거치지 않고", warning.Message);
            StringAssert.Contains("gold", warning.Message);
            Assert.AreEqual(0, _session.State.CheckUntrackedChanges(), "한 번 받아들인 뒤에는 다시 경고하지 않는다");
        }

        [Test]
        public void 행동_안의_직접_수정도_행동_직후_잡아낸다()
        {
            _session.Actions.Register<TestKillAction>(_ => _inventory.gold = 5);
            _session.Do(new TestKillAction());
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("gold")));
        }

        [Test]
        public void 값이_바뀌면_Changed와_DataChanged가_알린다()
        {
            var keys = new List<string>();
            IReadOnlyList<DataChange> last = null;
            _session.State.Changed += keys.Add;
            _session.State.DataChanged += (_, changes) => last = changes;

            Do(inv => inv.counts["포션"] = 3);

            CollectionAssert.Contains(keys, "s:inventory");
            Assert.AreEqual(DataChangeKind.Insert, last.Single().Kind);
            Assert.AreEqual("counts[포션]", last.Single().Path);
        }

        [Test]
        public void 저장하고_불러오면_구조_ID_다형성_enum이_보존된다()
        {
            var meta = new GameSession();
            var inventory = meta.State.Define("m:inventory", StateZone.Meta, new TestInventory());
            meta.State.Modify<TestInventory>("m:inventory", inv =>
            {
                inv.gold = 1500;
                inv.items.Add(new TestItem { name = "지팡이", level = 3, tags = { "마법", "보스 드롭" }, effects = { new TestBurn { damage = 7, element = TestElement.Ice }, new TestLifesteal { ratio = 0.25 } } });
                inv.counts["열쇠"] = 2;
            });
            var id = meta.State.Identity.IdOf(inventory.items[0]);
            var json = meta.ExportMeta();

            var loaded = new GameSession();
            loaded.ImportMeta(json);
            var restored = loaded.State.Define("m:inventory", StateZone.Meta, new TestInventory());

            Assert.AreEqual(1500, restored.gold);
            var staff = restored.items.Single();
            Assert.AreEqual("지팡이", staff.name);
            Assert.AreEqual(3, staff.level);
            CollectionAssert.AreEqual(new[] { "마법", "보스 드롭" }, staff.tags);
            var burn = (TestBurn)staff.effects[0];
            Assert.AreEqual(7, burn.damage);
            Assert.AreEqual(TestElement.Ice, burn.element);
            Assert.AreEqual(0.25, ((TestLifesteal)staff.effects[1]).ratio);
            Assert.AreEqual(2, restored.counts["열쇠"]);
            Assert.AreEqual(id, loaded.State.Identity.IdOf(staff), "요소 ID가 저장을 거쳐도 이어진다");
            StringAssert.Contains("\"Ice\"", json, "enum은 이름으로 저장한다(순서를 바꿔도 안전)");
        }

        [Test]
        public void 이름이_바뀐_필드는_FormerlySerializedAs로_따라간다()
        {
            var session = new GameSession();
            session.ImportMeta("{\"version\":1,\"state\":{\"m:hero\":{\"hp\":42,\"removed\":1}}}");
            var hero = session.State.Define("m:hero", StateZone.Meta, new TestRenamed());
            Assert.AreEqual(42, hero.health);
            Assert.IsTrue(session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("removed")), "모르는 필드는 경고로 남긴다");
        }

        [Test]
        public void 저장_버전이_낮으면_변환을_거쳐_불러온다()
        {
            var session = new GameSession(new SessionOptions { SaveVersion = 3 });
            session.Migrations.RenameKey(2, "m:money", "m:inventory");
            session.Migrations.MoveField(3, "m:inventory", "coins", "gold");
            session.ImportMeta("{\"version\":1,\"state\":{\"m:money\":{\"coins\":30}}}");

            var inventory = session.State.Define("m:inventory", StateZone.Meta, new TestInventory());
            Assert.AreEqual(30, inventory.gold);
            Assert.AreEqual(2, session.Journal.Entries.Count(e => e.Message.StartsWith("저장 데이터 변환")));
        }

        [Test]
        public void 리스트_요소_안의_필드도_옮길_수_있다()
        {
            var session = new GameSession(new SessionOptions { SaveVersion = 2 });
            session.Migrations.MoveField(2, "m:inventory", "items[].title", "items[].name");
            session.ImportMeta("{\"version\":1,\"state\":{\"m:inventory\":{\"items\":[{\"title\":\"검\"},{\"title\":\"활\"}]}}}");
            var inventory = session.State.Define("m:inventory", StateZone.Meta, new TestInventory());
            CollectionAssert.AreEqual(new[] { "검", "활" }, inventory.items.Select(i => i.name));
        }

        [Test]
        public void 게임보다_새_버전의_저장은_거부한다()
        {
            var session = new GameSession();
            Assert.Throws<FormatException>(() => session.ImportMeta("{\"version\":5,\"state\":{}}"));
        }

        [Test]
        public void 담을_수_없는_필드는_등록할_때_알려준다()
        {
            var e = Assert.Throws<ArgumentException>(() => _session.State.Define("s:bad", StateZone.Session, new TestBadData()));
            StringAssert.Contains("onChanged", e.Message);
        }

        [Test]
        public void 순환_참조는_알기_쉬운_오류로_막는다()
        {
            var session = new GameSession();
            var node = new TestNode();
            node.next = node;
            var e = Assert.Throws<InvalidOperationException>(() => session.State.Define("s:node", StateZone.Session, node));
            StringAssert.Contains("순환 참조", e.Message);
            Assert.DoesNotThrow(() => session.State.Define("s:chain", StateZone.Session, new TestNode { next = new TestNode() }), "자기 타입을 필드로 갖는 것 자체는 괜찮다");
        }

        [Test]
        public void 초기화는_등록할_때의_값으로_돌린다()
        {
            Do(inv =>
            {
                inv.gold = 9;
                inv.items.Add(Sword());
            });
            _session.State.ResetZone(StateZone.Session);
            Assert.AreEqual(0, _inventory.gold);
            Assert.AreEqual(0, _inventory.items.Count);
        }

        [Test]
        public void 상태_탭에는_요약으로_보이고_글자로_고칠_수_없다()
        {
            Do(inv => inv.items.Add(Sword()));
            var root = new InspectNode("상태");
            _session.State.Inspect(root);
            var node = root.Children.SelectMany(g => g.Children).Single(n => n.Label == "인벤토리");
            Assert.IsNull(node.StateKey);
            Assert.AreEqual("TestInventory", node.Value);
        }
        [Test]
        public void 집합_큐_스택_연결리스트_nullable_날짜도_담고_되돌리고_저장한다()
        {
            var session = new GameSession(new SessionOptions());
            session.Actions.Register<DataAction>(_ => session.State.Modify<TestCollections>("m:misc", c =>
            {
                c.flags.Add("보스");
                c.queue.Enqueue(3);
                c.stack.Push("위");
                c.chain.AddLast(new TestItem { name = "고리" });
                c.optional = 5;
                c.when = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
                c.cooldown = TimeSpan.FromSeconds(90);
                c.guid = new Guid("11111111-2222-3333-4444-555555555555");
            }));
            var misc = session.State.Define("m:misc", StateZone.Meta, new TestCollections());
            misc.stack.Push("아래");

            session.State.CheckUntrackedChanges();
            session.Do(new DataAction());
            var json = session.ExportMeta();

            var loaded = new GameSession();
            loaded.ImportMeta(json);
            var copy = loaded.State.Define("m:misc", StateZone.Meta, new TestCollections());
            CollectionAssert.AreEqual(new[] { "보스" }, copy.flags);
            CollectionAssert.AreEqual(new[] { 3 }, copy.queue);
            CollectionAssert.AreEqual(new[] { "위", "아래" }, copy.stack, "스택은 쌓인 순서까지 보존된다");
            Assert.AreEqual("고리", copy.chain.First.Value.name);
            Assert.AreEqual(5, copy.optional);
            Assert.AreEqual(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), copy.when);
            Assert.AreEqual(TimeSpan.FromSeconds(90), copy.cooldown);
            Assert.AreEqual(new Guid("11111111-2222-3333-4444-555555555555"), copy.guid);

            var undo = new GameSession(new SessionOptions());
            undo.Actions.Register<DataAction>(_ => undo.State.Modify<TestCollections>("s:misc", c =>
            {
                c.flags.Add("임시");
                c.optional = null;
            }));
            var value = undo.State.Define("s:misc", StateZone.Session, new TestCollections { optional = 1, flags = { "처음" } });
            undo.Do(new DataAction());
            Assert.IsTrue(undo.Undo());
            CollectionAssert.AreEqual(new[] { "처음" }, value.flags);
            Assert.AreEqual(1, value.optional);
        }

        [Test]
        public void 타입이_붙은_키로_같은_문법을_쓴다()
        {
            var gold = new StateKey<int>("player:gold");
            var bag = new StateKey<TestInventory>("player:bag");
            var session = new GameSession();
            session.State.Define(gold, StateZone.Meta, 100);
            var inventory = session.State.Define(bag, StateZone.Meta);

            session.State.Set(gold, 150);
            session.State.Modify(bag, inv => inv.gold = 3);

            Assert.AreEqual(150, session.State.Get(gold), "숫자는 선언한 타입으로 돌려받는다");
            Assert.AreEqual(3, session.State.Get(bag).gold);
            Assert.AreSame(inventory, session.State.Get(bag));
            Assert.Throws<ArgumentException>(() => session.State.Set("player:gold", "글자"), "선언한 타입과 다른 값은 거부한다");
        }

        [Test]
        public void 한_행동_안의_여러_수정은_무르기_기록_하나로_합친다()
        {
            Do(inv =>
            {
                _session.State.Modify<TestInventory>("s:inventory", i => i.gold = 1);
                _session.State.Modify<TestInventory>("s:inventory", i => i.gold = 2);
                inv.gold = 3;
            });
            var tx = _session.State.History.Last();
            Assert.AreEqual(1, tx.DataChanges.Count);
            Assert.AreEqual(3, tx.DataChanges[0].Changes.Count);
            _session.Undo();
            Assert.AreEqual(0, _inventory.gold);
        }

        [Test]
        public void 너무_큰_값은_한_번_경고한다()
        {
            var session = new GameSession();
            var big = new TestInventory();
            for (int i = 0; i < StateStore.LargeValueNodes; i++) big.items.Add(new TestItem());
            session.State.Define("s:big", StateZone.Session, big);
            session.State.Modify<TestInventory>("s:big", inv => inv.gold = 1);
            Assert.AreEqual(1, session.Journal.Entries.Count(e => e.Kind == JournalKind.Warning && e.Message.Contains("값이 큽니다")));
        }

        [Test]
        public void 이름이_바뀐_키는_예전_이름의_저장값을_이어받는다()
        {
            var session = new GameSession();
            session.State.AddAlias("m:coins", "m:gold");
            session.ImportMeta("{\"version\":1,\"state\":{\"m:coins\":42}}");
            session.State.Define("m:gold", StateZone.Meta, 0);
            Assert.AreEqual(42, session.State.Get<int>("m:gold"));
        }

        [Test]
        public void 수정_비용_측정_장비_500개()
        {
            Assert.IsTrue(MeasureAction(true) < 50);
            Assert.IsTrue(MeasureAction(false) < 50);
        }

        static double MeasureAction(bool detect)
        {
            var session = new GameSession(new SessionOptions { DetectUntrackedChanges = detect });
            var inventory = new TestInventory();
            for (int i = 0; i < 500; i++)
                inventory.items.Add(new TestItem { name = "장비" + i, level = i % 10, tags = { "근접", "희귀" }, effects = { new TestLifesteal(), new TestBurn() } });
            session.State.Define("m:inventory", StateZone.Meta, inventory);
            session.Actions.Register<DataAction>(_ => session.State.Modify<TestInventory>("m:inventory", inv => inv.items[7].level++));

            session.Do(new DataAction());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            const int runs = 50;
            for (int i = 0; i < runs; i++) session.Do(new DataAction());
            watch.Stop();
            var perAction = watch.Elapsed.TotalMilliseconds / runs;
            Console.WriteLine($"장비 500개 인벤토리, 누락 검사 {(detect ? "켬(개발 빌드)" : "끔(출시 빌드)")}: 행동 한 번 평균 {perAction:0.00}ms");
            return perAction;
        }
        [Test]
        public void 식별자_규칙()
        {
            Assert.IsTrue(IdRules.IsValid("player.inventory", out _));
            Assert.IsTrue(IdRules.IsValid("kills_total", out _));
            Assert.IsFalse(IdRules.IsValid("Player", out _), "대문자 불가");
            Assert.IsFalse(IdRules.IsValid("1st", out _), "숫자로 시작 불가");
            Assert.IsFalse(IdRules.IsValid("처치", out _), "한글은 표시 이름에");
            Assert.IsFalse(IdRules.IsValid("a..b", out _));
            Assert.AreEqual("PlayerInventory", IdRules.ToIdentifier("player.inventory"));
            Assert.AreEqual("KillsTotal", IdRules.ToIdentifier("kills_total"));
            Assert.AreEqual("player", IdRules.GroupOf("player.inventory"));
            Assert.AreEqual("stat", IdRules.GroupOf("stat:kills"));
        }
    }
}