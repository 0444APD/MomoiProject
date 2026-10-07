using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Serialization;

namespace GBible.Tests
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

    // [Serializable]이 없는 클래스: 예전 방식(리플렉션 비교).
    public sealed class TestPlainItem
    {
        public int level;
    }

    public sealed class TestPlainBag
    {
        public int gold;
        public List<TestPlainItem> items = new List<TestPlainItem>();
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

    // 같은 객체를 두 곳(목록·필드)에서 가리키는 데이터.
    [Serializable]
    public sealed class TestGraph
    {
        public List<TestNode> nodes = new List<TestNode>();
        public TestNode pinned;
    }

    // [Serializable]이 없는 자기 참조 클래스: 예전 방식(리플렉션 비교).
    public sealed class TestPlainNode
    {
        public int value;
        public TestPlainNode next;
    }

    // 빈 칸(null)이 섞인 고정 칸 배열: 예전 방식.
    public sealed class TestPlainSlots
    {
        public TestPlainItem[] slots = new TestPlainItem[3];
    }

    // 처음 값에 요소가 든 데이터(새 판의 시작 덱 같은 것).
    [Serializable]
    public sealed class TestDeck
    {
        public List<TestItem> cards = new List<TestItem> { new TestItem { name = "타격", level = 1 }, new TestItem { name = "수비", level = 1 } };
    }

    // 인터페이스 형식 컬렉션 칸: 담을 수 없다(불러올 때 실제 형식을 정할 수 없고 비교 코드도 안쪽을 놓친다).
    [Serializable]
    public sealed class TestInterfaceBag
    {
        public IList<int> items = new List<int>();
    }

    // 불러올 때 깨진 키·형식을 견디는지 보는 데이터.
    [Serializable]
    public sealed class TestKeyed
    {
        public Dictionary<TestElement, int> byElement = new Dictionary<TestElement, int>();
        public Dictionary<Guid, int> byGuid = new Dictionary<Guid, int>();
        public Dictionary<TimeSpan, string> byTime = new Dictionary<TimeSpan, string>();
        public ITestEffect effect;
        public TestElement flags;
        public int after = 3;
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

    [Serializable] public sealed class SchemaV1 { public string name; public int level; public TestElement element; }
    [Serializable] public sealed class SchemaAdded { public string name; public int level; public TestElement element; public int rarity; }
    [Serializable] public sealed class SchemaRemoved { public string name; public TestElement element; }
    [Serializable] public sealed class SchemaRetyped { public string name; public string level; public TestElement element; }

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
            _inventory = _session.Data.Define("s:inventory", DataZone.Session, new TestInventory(), "인벤토리");
            _session.Actions.Register<DataAction>(_ => _session.Data.Modify("s:inventory", _onAction));
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

            var id = _session.Data.Identity.IdOf(_inventory.items[0]);
            var entry = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Data);
            StringAssert.Contains($"items #{id} › level", entry.Message, "기본은 사람이 읽는 경로");
            StringAssert.Contains("1 → 4", entry.Message);
            StringAssert.Contains($"items[#{id}].level", entry.Code, "코드 형태(Alt)는 실제 경로");
        }

        [Test]
        public void Undo하면_같은_인스턴스가_제자리로_돌아온다()
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
        public void 제거를_Undo하면_빠졌던_인스턴스가_같은_ID로_돌아온다()
        {
            Do(inv =>
            {
                inv.items.Add(Sword());
                inv.items.Add(new TestItem { name = "방패" });
            });
            var sword = _inventory.items[0];
            var id = _session.Data.Identity.IdOf(sword);

            Do(inv => inv.items.RemoveAt(0));
            Assert.AreEqual(1, _inventory.items.Count);
            _session.Undo();

            Assert.AreEqual(2, _inventory.items.Count);
            Assert.AreSame(sword, _inventory.items[0]);
            Assert.AreEqual(id, _session.Data.Identity.IdOf(_inventory.items[0]));
        }

        [Test]
        public void 행동이_실패하면_데이터도_되돌린다()
        {
            _session.Actions.Register<FailingAction>(_ =>
            {
                _session.Data.Modify<TestInventory>("s:inventory", inv => inv.gold = 50);
                throw new InvalidOperationException("의도된 실패");
            });
            Assert.Throws<InvalidOperationException>(() => _session.Do(new FailingAction()));
            Assert.AreEqual(0, _inventory.gold);
            Assert.AreEqual(0, _session.Data.HistoryCount);
        }

        [Test]
        public void Modify_중_예외가_나면_바꾸기_전으로_돌아간다()
        {
            Assert.Throws<InvalidOperationException>(() => _session.Data.Modify<TestInventory>("s:inventory", inv =>
            {
                inv.gold = 10;
                throw new InvalidOperationException("중간 실패");
            }));
            Assert.AreEqual(0, _inventory.gold);
        }

        [Test]
        public void 직접_고친_값은_다음_Sync에_기록된다()
        {
            _inventory.gold = 77;
            Assert.AreEqual(1, _session.Data.Sync());
            var entry = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Data);
            StringAssert.Contains("gold", entry.Code);
            Assert.AreEqual(0, _session.Data.Sync(), "한 번 기록한 뒤에는 다시 기록하지 않는다");
            Assert.IsFalse(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning), "직접 고쳐도 경고하지 않는다");
        }

        [Test]
        public void 행동_안의_직접_수정도_그_행동의_Undo에_들어간다()
        {
            _session.Actions.Register<TestKillAction>(_ => _inventory.gold = 5);
            _session.Do(new TestKillAction());
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Data && e.Code != null && e.Code.Contains("gold")));
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, _inventory.gold);
        }

        [Test]
        public void 값이_바뀌면_Changed와_DataChanged가_알린다()
        {
            var keys = new List<string>();
            IReadOnlyList<DataChange> last = null;
            _session.Data.Changed += keys.Add;
            _session.Data.DataChanged += (_, changes) => last = changes;

            Do(inv => inv.counts["포션"] = 3);

            CollectionAssert.Contains(keys, "s:inventory");
            Assert.AreEqual(DataChangeKind.Insert, last.Single().Kind);
            Assert.AreEqual("counts[포션]", last.Single().Path);
        }

        [Test]
        public void 저장하고_불러오면_구조_ID_다형성_enum이_보존된다()
        {
            var meta = new GameSession();
            var inventory = meta.Data.Define("m:inventory", DataZone.Meta, new TestInventory());
            meta.Data.Modify<TestInventory>("m:inventory", inv =>
            {
                inv.gold = 1500;
                inv.items.Add(new TestItem { name = "지팡이", level = 3, tags = { "마법", "보스 드롭" }, effects = { new TestBurn { damage = 7, element = TestElement.Ice }, new TestLifesteal { ratio = 0.25 } } });
                inv.counts["열쇠"] = 2;
            });
            var id = meta.Data.Identity.IdOf(inventory.items[0]);
            var json = meta.ExportMeta();

            var loaded = new GameSession();
            loaded.ImportMeta(json);
            var restored = loaded.Data.Define("m:inventory", DataZone.Meta, new TestInventory());

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
            Assert.AreEqual(id, loaded.Data.Identity.IdOf(staff), "요소 ID가 저장을 거쳐도 이어진다");
            StringAssert.Contains("\"Ice\"", json, "enum은 이름으로 저장한다(순서를 바꿔도 안전)");
        }

        [Test]
        public void 이름이_바뀐_필드는_FormerlySerializedAs로_따라간다()
        {
            var session = new GameSession();
            session.ImportMeta("{\"version\":1,\"state\":{\"m:hero\":{\"hp\":42,\"removed\":1}}}");
            var hero = session.Data.Define("m:hero", DataZone.Meta, new TestRenamed());
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

            var inventory = session.Data.Define("m:inventory", DataZone.Meta, new TestInventory());
            Assert.AreEqual(30, inventory.gold);
            Assert.AreEqual(2, session.Journal.Entries.Count(e => e.Message.StartsWith("저장 데이터 변환")));
        }

        [Test]
        public void 리스트_요소_안의_필드도_옮길_수_있다()
        {
            var session = new GameSession(new SessionOptions { SaveVersion = 2 });
            session.Migrations.MoveField(2, "m:inventory", "items[].title", "items[].name");
            session.ImportMeta("{\"version\":1,\"state\":{\"m:inventory\":{\"items\":[{\"title\":\"검\"},{\"title\":\"활\"}]}}}");
            var inventory = session.Data.Define("m:inventory", DataZone.Meta, new TestInventory());
            CollectionAssert.AreEqual(new[] { "검", "활" }, inventory.items.Select(i => i.name));
        }

        [Test]
        public void 저장_버전은_등록한_변환을_따라간다()
        {
            var session = new GameSession();
            session.Migrations.RenameKey(2, "m:money", "m:inventory");
            session.Migrations.MoveField(3, "m:inventory", "coins", "gold");
            Assert.AreEqual(3, session.SaveVersion, "SaveVersion을 따로 올리지 않아도 가장 높은 변환 버전");
            session.ImportMeta("{\"version\":1,\"state\":{\"m:money\":{\"coins\":30}}}");
            var inventory = session.Data.Define("m:inventory", DataZone.Meta, new TestInventory());
            Assert.AreEqual(30, inventory.gold);
            StringAssert.Contains("\"version\":3", session.ExportMeta());
        }

        [Test]
        public void 불러올_때마다_EveryLoad_변환을_적용한다()
        {
            var session = new GameSession();
            session.Migrations.EveryLoad = state => state["m:gold"] = 7.0;
            session.ImportMeta("{\"version\":1,\"state\":{\"m:gold\":1}}");
            session.Data.Define("m:gold", DataZone.Meta, 0);
            Assert.AreEqual(7, session.Data.Get<int>("m:gold"));
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
            var e = Assert.Throws<ArgumentException>(() => _session.Data.Define("s:bad", DataZone.Session, new TestBadData()));
            StringAssert.Contains("onChanged", e.Message);
        }

        [Test]
        public void Permanent_안에서도_행동은_이어지고_그_변경만_Undo에서_빠진다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var inv = session.Data.Define("s:inv", DataZone.Session, new TestInventory());
            session.Data.Define("s:flag", DataZone.Session, 0.0);
            bool inside = false;
            session.Actions.Register<DataAction>(_ =>
            {
                session.Data.Modify<TestInventory>("s:inv", i => i.gold = 10);
                using (session.Data.Permanent())
                {
                    inside = session.Data.InTransaction;
                    session.Data.Set("s:flag", 1.0);
                    session.Data.Modify<TestInventory>("s:inv", i => i.counts["kept"] = 1);
                }
            });
            session.Do(new DataAction());
            Assert.IsTrue(inside, "Permanent 안에서도 행동은 진행 중이다");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(0, inv.gold, "행동의 변경은 되돌린다");
            Assert.AreEqual(1.0, session.Data.Get<double>("s:flag"), "Permanent 안의 변경은 남는다");
            Assert.AreEqual(1, inv.counts["kept"]);
        }

        [Test]
        public void Permanent_안의_변경도_행동이_실패하거나_미리보기면_취소된다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var inv = session.Data.Define("s:inv", DataZone.Session, new TestInventory());
            session.Data.Define("s:flag", DataZone.Session, 0.0);
            var fail = false;
            session.Actions.Register<DataAction>(_ =>
            {
                using (session.Data.Permanent())
                {
                    session.Data.Set("s:flag", 1.0);
                    session.Data.Modify<TestInventory>("s:inv", i => i.counts["kept"] = 1);
                }
                if (fail) throw new InvalidOperationException("의도된 실패");
            });
            var seen = session.Game.Preview(new DataAction(), g => g.Session.Data.Get<double>("s:flag"));
            Assert.AreEqual(1.0, seen, "미리보기 안에서는 보인다");
            Assert.AreEqual(0.0, session.Data.Get<double>("s:flag"), "미리보기 뒤에는 남지 않는다");
            Assert.IsFalse(inv.counts.ContainsKey("kept"));
            fail = true;
            Assert.Throws<InvalidOperationException>(() => session.Do(new DataAction()));
            Assert.AreEqual(0.0, session.Data.Get<double>("s:flag"), "실패한 행동의 Permanent 변경도 없던 일");
            Assert.IsFalse(inv.counts.ContainsKey("kept"));
            fail = false;
            session.Do(new DataAction());
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(1.0, session.Data.Get<double>("s:flag"), "성공한 행동을 Undo해도 Permanent 변경은 남는다");
            Assert.AreEqual(1, inv.counts["kept"]);
        }

        [Test]
        public void 행동_밖_Permanent에서_시작한_행동은_이력에_없지만_실패하면_취소된다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var inv = session.Data.Define("s:inv", DataZone.Session, new TestInventory());
            bool fail = false;
            session.Actions.Register<DataAction>(_ =>
            {
                session.Data.Modify<TestInventory>("s:inv", i => i.gold += 5);
                if (fail) throw new InvalidOperationException("의도된 실패");
            });
            using (session.Data.Permanent()) session.Do(new DataAction());
            Assert.AreEqual(5, inv.gold);
            Assert.AreEqual(0, session.Data.HistoryCount, "되돌리지 않을 행동이라 이력에 없다");
            fail = true;
            using (session.Data.Permanent()) Assert.Throws<InvalidOperationException>(() => session.Do(new DataAction()));
            Assert.AreEqual(5, inv.gold, "실패한 행동은 통째로 취소된다");
        }

        [Test]
        public void 저장점에서_Sync해도_꺼낸_예전_방식_데이터의_변경은_행동에_남는다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.Data.Define("s:bag", DataZone.Session, new TestPlainBag());
            session.Actions.Register<DataAction>(_ =>
            {
                var bag = session.Data.Get<TestPlainBag>("s:bag");
                bag.gold = 7;
                var point = session.Data.Mark();
                bag.gold = 9;
                session.Data.RollbackTo(point);
            });
            session.Do(new DataAction());
            var after = session.Data.Get<TestPlainBag>("s:bag");
            Assert.AreEqual(7, after.gold, "저장점 뒤만 취소된다");
            Assert.IsFalse(session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("Modify를 거치지 않고")), "행동 안에서 꺼낸 값을 고친 것은 경고하지 않는다");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(0, after.gold, "저장점 앞의 변경도 Undo된다");
        }

        [Test]
        public void 나중에_정의한_단순_값도_저장_형식대로_읽는다()
        {
            var when = new DateTime(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);
            var source = new GameSession(new SessionOptions { Seed = 1 });
            source.Data.Define("s:when", DataZone.Session, when);
            var json = source.ExportRun();
            var loaded = new GameSession(new SessionOptions { Seed = 1 });
            loaded.ImportRun(json);
            loaded.Data.Define("s:when", DataZone.Session, default(DateTime));
            Assert.AreEqual(when, loaded.Data.Get<DateTime>("s:when"));
        }

        [Test]
        public void 인터페이스_컬렉션_칸은_등록할_때_막는다()
        {
            var session = new GameSession();
            var e = Assert.Throws<ArgumentException>(() => session.Data.Define("s:ibag", DataZone.Session, new TestInterfaceBag()));
            StringAssert.Contains("List<T>", e.Message);
        }

        [Test]
        public void 서로_가리키는_객체도_담고_되돌리고_저장한다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var a = new TestNode { value = 1 };
            var b = new TestNode { value = 2, next = a };
            a.next = b;
            var graph = session.Data.Define("s:graph", DataZone.Session, new TestGraph { nodes = { a, b }, pinned = a });
            session.Actions.Register<DataAction>(_ => session.Data.Modify<TestGraph>("s:graph", g => g.nodes[1].value = 20));
            session.Do(new DataAction());
            Assert.AreEqual(20, b.value);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(2, b.value);
            Assert.AreSame(a, graph.pinned);
            Assert.AreSame(a, b.next);

            var json = session.ExportRun();
            var loaded = new GameSession(new SessionOptions { Seed = 1 });
            var into = loaded.Data.Define("s:graph", DataZone.Session, new TestGraph());
            loaded.ImportRun(json);
            Assert.AreEqual(2, into.nodes.Count);
            Assert.AreSame(into.nodes[0], into.pinned, "두 곳에서 가리키던 객체는 불러온 뒤에도 같은 인스턴스");
            Assert.AreSame(into.nodes[1], into.nodes[0].next);
            Assert.AreSame(into.nodes[0], into.nodes[1].next, "서로 가리키던 객체도 그대로");
            Assert.AreEqual(2, into.nodes[1].value);
            Assert.DoesNotThrow(() => loaded.ExportRun(), "불러온 값도 다시 저장된다");
            Assert.DoesNotThrow(() => session.Data.Define("s:chain", DataZone.Session, new TestNode { next = new TestNode() }), "자기 타입을 필드로 갖는 것 자체도 괜찮다");
        }

        [Test]
        public void 예전_방식_데이터도_서로_가리키는_객체를_담는다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var a = new TestPlainNode { value = 1 };
            a.next = new TestPlainNode { value = 2, next = a };
            session.Data.Define("s:plain", DataZone.Session, a);
            session.Actions.Register<DataAction>(_ => session.Data.Modify<TestPlainNode>("s:plain", n => n.next.value = 5));
            session.Do(new DataAction());
            Assert.AreEqual(5, a.next.value);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(2, a.next.value);
            Assert.AreSame(a, a.next.next, "되돌린 뒤에도 서로 가리킨다");

            var json = session.ExportRun();
            var loaded = new GameSession(new SessionOptions { Seed = 1 });
            var into = loaded.Data.Define("s:plain", DataZone.Session, new TestPlainNode());
            loaded.ImportRun(json);
            Assert.AreSame(into, into.next.next);
            Assert.AreEqual(2, into.next.value);
        }

        [Test]
        public void 가리키는_곳을_먼저_읽어도_같은_인스턴스로_잇는다()
        {
            // 필드 순서가 바뀐 저장: 번호로 가리키는 곳($ref)이 처음 쓴 곳($n)보다 앞에 온다.
            var tree = MiniJson.Deserialize("{\"pinned\":{\"$ref\":1},\"nodes\":[{\"$n\":1,\"$type\":\"GBible.Tests.TestNode\",\"value\":7,\"next\":null}]}");
            var warnings = new List<string>();
            var graph = (TestGraph)DataModel.FromTree(tree, typeof(TestGraph), null, warnings);
            Assert.AreEqual(1, graph.nodes.Count);
            Assert.AreSame(graph.nodes[0], graph.pinned);
            Assert.AreEqual(7, graph.pinned.value);
            Assert.AreEqual(0, warnings.Count, string.Join(" / ", warnings));

            var missing = (TestGraph)DataModel.FromTree(MiniJson.Deserialize("{\"pinned\":{\"$ref\":9}}"), typeof(TestGraph), null, warnings);
            Assert.IsNull(missing.pinned, "가리키는 객체가 없으면 비워 둔다");
            Assert.IsTrue(warnings.Any(w => w.Contains("#9")));
        }

        [Test]
        public void 새_판은_처음_값을_건드리지_않는다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var deck = session.Data.Define("s:deck", DataZone.Session, new TestDeck());
            session.Actions.Register<DataAction>(_ => session.Data.Modify<TestDeck>("s:deck", d => d.cards[0].level = 9));
            for (int round = 0; round < 3; round++)
            {
                session.NewRun();
                Assert.AreEqual(1, deck.cards[0].level, $"{round + 1}번째 새 판: 지난 판의 강화가 남지 않는다");
                session.Do(new DataAction());
                Assert.AreEqual(9, deck.cards[0].level);
            }

            // 저장에 없는 키를 처음 값으로 되돌리는 불러오기도 같다.
            var other = new GameSession(new SessionOptions { Seed = 1 });
            other.Actions.Register<DataAction>(_ => other.Data.Modify<TestDeck>("s:deck", d => d.cards[0].level = 9));
            var otherDeck = other.Data.Define("s:deck", DataZone.Session, new TestDeck());
            var empty = new GameSession(new SessionOptions { Seed = 1 }).ExportRun();
            for (int round = 0; round < 3; round++)
            {
                other.ImportRun(empty);
                Assert.AreEqual(1, otherDeck.cards[0].level, $"{round + 1}번째 불러오기");
                other.Do(new DataAction());
            }
        }

        [Test]
        public void 빈_칸이_섞인_배열에서_옮긴_것도_기록한다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            var bag = session.Data.Define("s:slots", DataZone.Session, new TestPlainSlots());
            Action<TestPlainSlots> change = null;
            session.Actions.Register<DataAction>(_ => session.Data.Modify("s:slots", change));
            change = s => s.slots[0] = new TestPlainItem { level = 3 };
            session.Do(new DataAction());
            var item = bag.slots[0];
            change = s =>
            {
                s.slots[2] = s.slots[0];
                s.slots[0] = null;
            };
            session.Do(new DataAction());
            Assert.IsNull(bag.slots[0]);
            Assert.AreSame(item, bag.slots[2]);
            Assert.IsTrue(session.Undo());
            Assert.AreSame(item, bag.slots[0], "옮기기를 되돌리면 처음 칸으로");
            Assert.IsNull(bag.slots[2]);
        }

        [Test]
        public void 불러오기는_깨진_키와_형식을_그_자리만_버린다()
        {
            var id = Guid.NewGuid();
            var source = new TestKeyed { flags = TestElement.Ice, after = 9 };
            source.byElement[TestElement.Fire] = 1;
            source.byGuid[id] = 5;
            source.byTime[TimeSpan.FromSeconds(90)] = "90초";
            var tree = (Dictionary<string, object>)DataModel.ToTree(source, typeof(TestKeyed), null, new List<string>());
            var roundTrip = (TestKeyed)DataModel.FromTree(MiniJson.Deserialize(MiniJson.Serialize(tree)), typeof(TestKeyed), null, new List<string>());
            Assert.AreEqual(5, roundTrip.byGuid[id], "Guid 키도 되읽는다");
            Assert.AreEqual("90초", roundTrip.byTime[TimeSpan.FromSeconds(90)], "시간 키도 되읽는다");
            Assert.AreEqual(TestElement.Ice, roundTrip.flags);

            ((Dictionary<string, object>)tree["byElement"])["Gone"] = 2.0;
            tree["effect"] = new Dictionary<string, object> { ["$type"] = "GBible.Tests.NoSuchEffect", ["damage"] = 1.0 };
            var warnings = new List<string>();
            TestKeyed broken = null;
            Assert.DoesNotThrow(() => broken = (TestKeyed)DataModel.FromTree(tree, typeof(TestKeyed), null, warnings));
            Assert.AreEqual(1, broken.byElement.Count, "없어진 enum 키는 그 항목만 버린다");
            Assert.AreEqual(1, broken.byElement[TestElement.Fire]);
            Assert.IsNull(broken.effect, "만들 수 없는 형식은 비워 둔다");
            Assert.AreEqual(9, broken.after, "나머지는 그대로 읽는다");
            Assert.IsTrue(warnings.Any(w => w.Contains("Gone")));
        }

        [Test]
        public void 초기화는_등록할_때의_값으로_돌린다()
        {
            Do(inv =>
            {
                inv.gold = 9;
                inv.items.Add(Sword());
            });
            _session.Data.ResetZone(DataZone.Session);
            Assert.AreEqual(0, _inventory.gold);
            Assert.AreEqual(0, _inventory.items.Count);
        }

        [Test]
        public void 데이터_탭에는_요약으로_보이고_글자로_고칠_수_없다()
        {
            Do(inv => inv.items.Add(Sword()));
            var root = new InspectNode("플레이어 데이터");
            _session.Data.Inspect(root);
            var node = root.Children.SelectMany(g => g.Children).Single(n => n.Label == "인벤토리");
            Assert.IsNull(node.PlayerDataKey);
            Assert.AreEqual("TestInventory", node.Value);
        }
        [Test]
        public void 집합_큐_스택_연결리스트_nullable_날짜도_담고_되돌리고_저장한다()
        {
            var session = new GameSession(new SessionOptions());
            session.Actions.Register<DataAction>(_ => session.Data.Modify<TestCollections>("m:misc", c =>
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
            var misc = session.Data.Define("m:misc", DataZone.Meta, new TestCollections());
            misc.stack.Push("아래");

            session.Data.CheckUntrackedChanges();
            session.Do(new DataAction());
            var json = session.ExportMeta();

            var loaded = new GameSession();
            loaded.ImportMeta(json);
            var copy = loaded.Data.Define("m:misc", DataZone.Meta, new TestCollections());
            CollectionAssert.AreEqual(new[] { "보스" }, copy.flags);
            CollectionAssert.AreEqual(new[] { 3 }, copy.queue);
            CollectionAssert.AreEqual(new[] { "위", "아래" }, copy.stack, "스택은 쌓인 순서까지 보존된다");
            Assert.AreEqual("고리", copy.chain.First.Value.name);
            Assert.AreEqual(5, copy.optional);
            Assert.AreEqual(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), copy.when);
            Assert.AreEqual(TimeSpan.FromSeconds(90), copy.cooldown);
            Assert.AreEqual(new Guid("11111111-2222-3333-4444-555555555555"), copy.guid);

            var undo = new GameSession(new SessionOptions());
            undo.Actions.Register<DataAction>(_ => undo.Data.Modify<TestCollections>("s:misc", c =>
            {
                c.flags.Add("임시");
                c.optional = null;
            }));
            var value = undo.Data.Define("s:misc", DataZone.Session, new TestCollections { optional = 1, flags = { "처음" } });
            undo.Do(new DataAction());
            Assert.IsTrue(undo.Undo());
            CollectionAssert.AreEqual(new[] { "처음" }, value.flags);
            Assert.AreEqual(1, value.optional);
        }

        [Test]
        public void 타입이_붙은_키로_같은_문법을_쓴다()
        {
            var gold = new PlayerDataKey<int>("player:gold");
            var bag = new PlayerDataKey<TestInventory>("player:bag");
            var session = new GameSession();
            session.Data.Define(gold, DataZone.Meta, 100);
            var inventory = session.Data.Define(bag, DataZone.Meta);

            session.Data.Set(gold, 150);
            session.Data.Modify(bag, inv => inv.gold = 3);

            Assert.AreEqual(150, session.Data.Get(gold), "숫자는 선언한 타입으로 돌려받는다");
            Assert.AreEqual(3, session.Data.Get(bag).gold);
            Assert.AreSame(inventory, session.Data.Get(bag));
            Assert.Throws<ArgumentException>(() => session.Data.Set("player:gold", "글자"), "선언한 타입과 다른 값은 거부한다");
        }

        [Test]
        public void 한_행동_안의_여러_수정은_Undo_기록_하나로_합친다()
        {
            Do(inv =>
            {
                _session.Data.Modify<TestInventory>("s:inventory", i => i.gold = 1);
                _session.Data.Modify<TestInventory>("s:inventory", i => i.gold = 2);
                inv.gold = 3;
            });
            var tx = _session.Data.History.Last();
            Assert.AreEqual(1, tx.Mirrored.Count);
            Assert.AreEqual(3, tx.Mirrored[0].Count);
            _session.Undo();
            Assert.AreEqual(0, _inventory.gold);
        }

        [Test]
        public void 예전_방식의_너무_큰_값은_한_번_경고한다()
        {
            var session = new GameSession();
            var big = new TestPlainBag();
            for (int i = 0; i < PlayerDataStore.LargeValueNodes; i++) big.items.Add(new TestPlainItem());
            session.Data.Define("s:big", DataZone.Session, big);
            session.Data.Modify<TestPlainBag>("s:big", inv => inv.gold = 1);
            Assert.AreEqual(1, session.Journal.Entries.Count(e => e.Kind == JournalKind.Warning && e.Message.Contains("값이 큽니다")));
        }

        [Test]
        public void 이름이_바뀐_키는_예전_이름의_저장값을_이어받는다()
        {
            var session = new GameSession();
            session.Data.AddAlias("m:coins", "m:gold");
            session.ImportMeta("{\"version\":1,\"state\":{\"m:coins\":42}}");
            session.Data.Define("m:gold", DataZone.Meta, 0);
            Assert.AreEqual(42, session.Data.Get<int>("m:gold"));
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
            session.Data.Define("m:inventory", DataZone.Meta, inventory);
            session.Actions.Register<DataAction>(_ => session.Data.Modify<TestInventory>("m:inventory", inv => inv.items[7].level++));

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
            Assert.IsTrue(IdRules.IsValid("killsTotal", out _));
            Assert.IsFalse(IdRules.IsValid("kills_total", out _), "밑줄 불가(lowerCamelCase)");
            Assert.IsFalse(IdRules.IsValid("Player", out _), "대문자로 시작 불가");
            Assert.IsFalse(IdRules.IsValid("1st", out _), "숫자로 시작 불가");
            Assert.IsFalse(IdRules.IsValid("처치", out _), "영문·숫자만");
            Assert.IsFalse(IdRules.IsValid("a..b", out _));
            Assert.AreEqual("PlayerInventory", IdRules.ToIdentifier("player.inventory"));
            Assert.AreEqual("KillsTotal", IdRules.ToIdentifier("killsTotal"));
            Assert.AreEqual("sampleEquipment", IdRules.Normalize("SampleEquipment"));
            Assert.AreEqual("effectTags", IdRules.Normalize("effect_tags"));
            Assert.AreEqual("player", IdRules.GroupOf("player.inventory"));
            Assert.AreEqual("stat", IdRules.GroupOf("stat:kills"));
        }
        [Test]
        public void 판을_저장하고_이어하면_내용_난수_시간이_이어진다()
        {
            GameSession Make()
            {
                var s = new GameSession(new SessionOptions { Seed = 11 });
                s.Data.Define("run.hp", DataZone.Session, 10);
                s.Data.Define("run.bag", DataZone.Session, new TestInventory());
                s.Data.Define("meta.gold", DataZone.Meta, 0);
                return s;
            }

            var played = Make();
            played.Data.Set("run.hp", 4);
            played.Data.Modify<TestInventory>("run.bag", inv => inv.items.Add(Sword()));
            played.Data.Set("meta.gold", 99);
            played.Random.Get("loot").NextULong();
            played.EndFrame(2.5);
            var json = played.ExportRun();
            var expectedNext = played.Random.Get("loot").NextULong();

            var resumed = Make();
            resumed.ImportRun(json);
            Assert.AreEqual(4, resumed.Data.Get<int>("run.hp"));
            Assert.AreEqual("검", resumed.Data.Get<TestInventory>("run.bag").items.Single().name);
            Assert.AreEqual(0, resumed.Data.Get<int>("meta.gold"), "프로필(메타)은 판 저장에 들어가지 않는다");
            Assert.AreEqual(2.5, resumed.Clock.Time, 1e-9);
            Assert.AreEqual(expectedNext, resumed.Random.Get("loot").NextULong(), "난수가 저장 시점부터 그대로 이어진다");
            Assert.AreEqual(0, resumed.UndoCount, "불러온 시점부터 새로 기록한다");

            resumed.NewRun();
            Assert.AreEqual(10, resumed.Data.Get<int>("run.hp"));
            Assert.AreEqual(0, resumed.Data.Get<TestInventory>("run.bag").items.Count);
        }

        [Test]
        public void 판_저장은_나중에_등록되는_키에도_적용된다()
        {
            var played = new GameSession();
            played.Data.Define("run.turn", DataZone.Session, 1);
            played.Data.Set("run.turn", 7);
            var json = played.ExportRun();

            var resumed = new GameSession();
            resumed.ImportRun(json);
            resumed.Data.Define("run.turn", DataZone.Session, 1);
            Assert.AreEqual(7, resumed.Data.Get<int>("run.turn"));
        }
        static Dictionary<string, string> SchemaOf(Type type)
        {
            var schema = new Dictionary<string, string>();
            DataModel.Schema(type, "hero", schema);
            return schema;
        }

        [Test]
        public void 구조_호환_추가는_호환_삭제와_형식_변경은_비호환()
        {
            var saved = SchemaOf(typeof(SchemaV1));
            Assert.IsTrue(DataModel.IsCompatible(saved, SchemaOf(typeof(SchemaAdded)), out _), "필드 추가는 기본값으로 채우면 된다");
            Assert.IsFalse(DataModel.IsCompatible(saved, SchemaOf(typeof(SchemaRemoved)), out var removed));
            StringAssert.Contains("hero.level", removed);
            Assert.IsFalse(DataModel.IsCompatible(saved, SchemaOf(typeof(SchemaRetyped)), out var retyped));
            StringAssert.Contains("형식이 바뀌었습니다", retyped);
            Assert.IsTrue(saved.ContainsKey("hero.element=Fire"), "enum 값 이름도 구조에 들어간다(없어지면 비호환)");
        }

        [Test]
        public void 저장_파일에_구조가_남고_키가_사라지면_비호환()
        {
            var session = new GameSession();
            session.Data.Define("m:hero", DataZone.Meta, new SchemaV1());
            session.SaveSchema = SchemaOf(typeof(SchemaV1));
            var json = session.ExportMeta();

            Assert.IsTrue(GameSession.IsSaveCompatible(json, SchemaOf(typeof(SchemaAdded)), out _));
            var renamed = new Dictionary<string, string>();
            DataModel.Schema(typeof(SchemaV1), "champion", renamed);
            Assert.IsFalse(GameSession.IsSaveCompatible(json, renamed, out var reason), "키 이름이 바뀌면 비호환");
            StringAssert.Contains("hero", reason);
            Assert.IsTrue(GameSession.IsSaveCompatible("{\"version\":1,\"state\":{}}", renamed, out _), "구조 정보가 없는 예전 파일은 호환으로 본다");
        }
        [Test]
        public void 필수_필드가_비면_경고하고_채워지면_알린다()
        {
            var session = new GameSession();
            session.Data.Require(typeof(TestItem), nameof(TestItem.name));
            session.Data.Define("s:bag", DataZone.Session, new TestInventory());
            session.Data.Modify<TestInventory>("s:bag", inv => inv.items.Add(new TestItem { level = 1 }));
            Assert.AreEqual(1, session.Data.MissingRequired.Count());
            Assert.IsTrue(session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.StartsWith("필수 값이 비어 있습니다")));

            session.Data.Modify<TestInventory>("s:bag", inv => inv.items[0].name = "검");
            Assert.AreEqual(0, session.Data.MissingRequired.Count());
            Assert.IsTrue(session.Journal.Entries.Any(e => e.Message.StartsWith("필수 값이 채워졌습니다")));
        }
    }
}