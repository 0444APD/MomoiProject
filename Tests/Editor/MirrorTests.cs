using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace GBible.Tests
{
    public enum MtMood : byte
    {
        Calm,
        Angry,
    }

    [Serializable]
    public struct MtPoint
    {
        public int x;
        public float y;
        public string tag;
    }

    [Serializable]
    public abstract class MtShape
    {
        public string name;
    }

    [Serializable]
    public class MtCircle : MtShape
    {
        public float radius;
    }

    [Serializable]
    public class MtBox : MtShape
    {
        public int w, h;
    }

    [Serializable]
    public class MtBase
    {
        public int level = 1;
        [UnityEngine.SerializeField] int _secret = 7;

        public int Secret
        {
            get => _secret;
            set => _secret = value;
        }
    }

    [Serializable]
    public class MtItem : MtBase
    {
        public string id;
        public MtMood mood;
        public long big;
        public double precise;
        public char letter = 'a';
        public bool broken;
        public MtPoint at;
        public List<MtPoint> path = new List<MtPoint>();
    }

    [Serializable]
    public class MtWorld
    {
        public int turn;
        public List<MtItem> items = new List<MtItem>();
        public MtItem[] slots = new MtItem[3];
        public int[] scores = { 1, 2, 3 };
        public Dictionary<string, MtItem> byName = new Dictionary<string, MtItem>();
        public Dictionary<MtMood, int> moods = new Dictionary<MtMood, int>();
        public MtShape shape = new MtCircle { name = "c", radius = 2 };
        public List<MtShape> shapes = new List<MtShape>();
        public object anything;
        [NonSerialized] public int notData;
    }

    [GameEvent("테스트 피격")]
    public sealed class MtHit
    {
        public readonly int Damage;
        public int Double => Damage * 2;

        public MtHit(int damage)
        {
            Damage = damage;
        }
    }

    // [BindTo]: 코드로 쓴 결과를 DB 항목에 연결한다(그 항목의 결과 칸 대신).
    [BindTo("bound.achievement")]
    public sealed class MtBoundReward : Reward
    {
        public static int Granted;
        public static string Owner;

        public override void Grant(GameSession session)
        {
            Granted++;
            Owner = Context?.Owner;
        }

        public override RewardEffect Effect => RewardEffect.Reversible;
        public override string Describe(INameResolver names) => "테스트 코드 결과";
    }

    [BindTo("bound.stat")]
    public sealed class MtBoundRecord : Reward
    {
        public override void Grant(GameSession session) => session.Stats.Report(Context.Owner, 5);
        public override RewardEffect Effect => RewardEffect.Reversible;
        public override string Describe(INameResolver names) => "5씩 기록";
    }

    [Serializable]
    public class MtOptional
    {
        public int? maybe;
        public MtMood? mood;
        public List<float?> many = new List<float?>();
    }

    [Serializable]
    public class MtLoop
    {
        public string name;
        public MtLoop next;
    }

    [Serializable]
    public class MtFloats
    {
        public float field;
        public float? maybe = 0f;
        public List<float> list = new List<float> { 0f };
        public double[] array = { 0.0 };
        public Dictionary<string, float> map = new Dictionary<string, float> { ["a"] = 0f };
    }

    // 컬렉션 클래스를 상속한 데이터(class Deck : List<Card>) — 요소는 상속한 클래스의 private 칸이다.
    [Serializable]
    public class MtDeck : List<string>
    {
    }

    [Serializable]
    public class MtDeckHolder
    {
        public string name = "덱";
        public MtDeck deck = new MtDeck();
    }

    // 요소가 자기를 가진 객체를 가리킨다(부모 참조).
    [Serializable]
    public class MtTreeNode
    {
        public string name;
        public MtTreeNode parent;
        public List<MtTreeNode> children = new List<MtTreeNode>();
    }

    // 새로 만든 객체가 이미 있던 객체를 가리킬 때(앞쪽 필드에서 먼저 만난다): 그 객체의 변경이 기록에서 빠지지 않는지.
    [Serializable]
    public class MtUnit
    {
        public int hp = 10;
    }

    [Serializable]
    public class MtHitLog
    {
        public MtUnit target;
        public List<MtUnit> also = new List<MtUnit>();
    }

    [Serializable]
    public class MtBattle
    {
        public MtHitLog last;
        public List<MtHitLog> log = new List<MtHitLog>();
        public Dictionary<string, MtHitLog> byKey = new Dictionary<string, MtHitLog>();
        public List<MtUnit> units = new List<MtUnit>();
    }

    // 안쪽에 컬렉션이 있는 구조체: 값 복사로는 안쪽 변경을 따라가지 못한다 → 예전 비교 방식.
    [Serializable]
    public struct MtBag
    {
        public int count;
        public List<int> items;
    }

    [Serializable]
    public class MtHolder
    {
        public MtBag bag = new MtBag { items = new List<int>() };
    }

    [Serializable]
    public class MtTwoLists
    {
        public List<MtUnit> all = new List<MtUnit>();
        public List<MtUnit> chosen = new List<MtUnit>();
    }

    // [BindTo] 함수: 매개변수 형식으로 받을 이벤트를 정한다.
    public sealed class MtOtherEvent
    {
    }

    public static class MtBoundFunctions
    {
        public static int Calls;
        public static int LastCount;
        public static string LastOwner;
        public static int Wrong;

        [BindTo("fn.achievement")]
        static void OnKill(TestKill e, TriggerContext c)
        {
            Calls++;
            LastCount = e.Count;
            LastOwner = c.Owner;
        }

        [BindTo("fn.wrong")]
        static void OnOther(MtOtherEvent e) => Wrong++;
    }

    public class MtBadBind
    {
        [BindTo("fn.bad")]
        public void NotStatic()
        {
        }
    }

    public class BindToTests
    {
        [Test]
        public void 함수로_연결하면_이벤트를_형식_그대로_받는다()
        {
            var session = new GameSession();
            MtBoundFunctions.Calls = 0;
            MtBoundFunctions.Wrong = 0;
            session.Achievements.Define(new AchievementDefinition { Id = "fn.achievement", When = new EventTrigger { eventType = typeof(TestKill).FullName } });
            session.Achievements.Define(new AchievementDefinition { Id = "fn.wrong", When = new EventTrigger { eventType = typeof(TestKill).FullName } });

            session.Events.Publish(new TestKill { Count = 7 });
            Assert.AreEqual(1, MtBoundFunctions.Calls);
            Assert.AreEqual(7, MtBoundFunctions.LastCount, "이벤트 필드를 형 변환 없이");
            Assert.AreEqual("fn.achievement", MtBoundFunctions.LastOwner);
            Assert.AreEqual(0, MtBoundFunctions.Wrong, "받는 이벤트 형식이 다르면 부르지 않는다");
            Assert.IsTrue(session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("MtOtherEvent")), "대신 경고");
            Assert.IsTrue(BoundOutcomes.Problems.Any(p => p.Contains("NotStatic")), "static이 아닌 함수는 연결하지 않고 알린다");
            Assert.IsFalse(BoundOutcomes.Has("fn.bad"));
        }

        [Test]
        public void 연결된_코드_결과가_도전과제와_로그의_결과가_된다()
        {
            var session = new GameSession();
            MtBoundReward.Granted = 0;
            var definition = new AchievementDefinition { Id = "bound.achievement", When = new EventTrigger { eventType = typeof(TestKill).FullName } };
            definition.Outcomes.Add(new PlayerDataReward { key = "never", amount = 1 });
            session.Achievements.Define(definition);
            var stat = new StatDefinition { Id = "bound.stat" };
            stat.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count) });
            session.Stats.Define(stat);

            session.Events.Publish(new TestKill { Count = 1 });
            Assert.AreEqual(1, MtBoundReward.Granted, "에디터에서 고른 결과 대신 코드 결과");
            Assert.AreEqual("bound.achievement", MtBoundReward.Owner, "Context.Owner = 연결된 항목");
            Assert.AreEqual(5, session.Stats.Get("bound.stat"), "로그에 기록 대신 코드 결과");
            Assert.IsTrue(BoundOutcomes.Has("bound.stat"));
            Assert.IsFalse(BoundOutcomes.Has("nothing"));
        }
    }

    // Mirror: 생성 코드(사본·비교·쓰기·읽기·적용)를 여러 형식으로 확인한다.
    public class MirrorTests
    {
        static MtWorld World()
        {
            var w = new MtWorld { turn = 1 };
            for (var i = 0; i < 5; i++) w.items.Add(new MtItem { id = "i" + i, level = i, at = new MtPoint { x = i, tag = "p" } });
            w.slots[1] = w.items[1];
            w.byName["first"] = w.items[0];
            w.moods[MtMood.Calm] = 2;
            w.shapes.Add(new MtBox { name = "b", w = 2, h = 3 });
            return w;
        }

        static byte[] Bytes(object value)
        {
            var w = new MirrorWriter();
            Mirror.Write(w, value);
            return w.ToArray();
        }

        // 바뀐 것 → 받는 쪽에 적용 → 같다(두 곳에서 가리키는 객체도 같은 인스턴스) / 되돌리기 → 처음과 같다 / 사본도 맞다.
        static void Check(MtWorld live, Action<MtWorld> change)
        {
            var node = Mirror.Make(live);
            var receiver = new MirrorReceiver();
            var client = (MtWorld)receiver.Load(Mirror.Snapshot(node));
            var before = Bytes(live);
            CollectionAssert.AreEqual(before, Bytes(client), "처음 모습");
            change(live);
            var changes = new ChangeSet { Describe = true };
            Mirror.Sync(live, node, changes);
            receiver.Apply(client, changes);
            CollectionAssert.AreEqual(Bytes(live), Bytes(client), "받는 쪽이 같아진다");
            var after = Bytes(live);
            Mirror.Undo(live, changes);
            CollectionAssert.AreEqual(before, Bytes(live), "되돌리면 처음과 같다");
            var redo = new ChangeSet();
            Mirror.Sync(live, node, redo);
            receiver.Apply(client, redo);
            CollectionAssert.AreEqual(before, Bytes(client), "되돌린 것도 받는 쪽에 전해진다");
            Assert.AreNotEqual(Convert.ToBase64String(before), Convert.ToBase64String(after), "실제로 바뀌었다");
        }

        [Test]
        public void 모든_생성_코드가_실행_가능한_코드다()
        {
            Mirror.Initialize();
            var count = 0;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (name != "GBible.Core" && !assembly.GetReferencedAssemblies().Any(r => r.Name == "GBible.Core")) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types)
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.Name.StartsWith("__gb_")))
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    count++;
                }
            }
            Assert.IsTrue(count > 50, "생성된 메서드 " + count);
            Assert.IsNotNull(Mirror.Find(typeof(MtWorld)));
            Assert.IsTrue(Mirror.Supports(typeof(MtWorld), out var reason), reason);
        }

        [Test]
        public void 생성된_읽기_쓰기_코드로_멤버를_다룬다()
        {
            Mirror.Initialize();
            var item = new MtItem { id = "a", level = 3 };
            item.Secret = 9;
            var level = FieldAccess.Find(typeof(MtItem), "level");
            Assert.IsTrue(level.Generated, "부모 클래스 필드도 생성 코드");
            Assert.AreEqual(typeof(MtBase), level.Owner);
            Assert.AreEqual(3, level.Get(item));
            Assert.AreEqual(9, FieldAccess.Read(item, "_secret"), "private [SerializeField]");
            Assert.AreEqual(9, FieldAccess.Read(item, "Secret"), "공개 속성");
            FieldAccess.Find(typeof(MtItem), "mood").Set(item, MtMood.Angry);
            Assert.AreEqual(MtMood.Angry, item.mood);
            FieldAccess.Find(typeof(MtItem), "path").Set(item, null);
            Assert.IsNull(item.path);

            object point = new MtPoint { x = 1 };
            FieldAccess.Find(typeof(MtPoint), "x").Set(point, 5);
            Assert.AreEqual(5, ((MtPoint)point).x, "구조체(상자 안)");

            var name = FieldAccess.Find(typeof(MtCircle), "name");
            Assert.IsTrue(name.Generated, "추상 부모도 생성 코드");
            Assert.AreEqual("c", name.Get(new MtCircle { name = "c" }));

            var hit = FieldAccess.Find(typeof(MtHit), "Damage");
            Assert.IsTrue(hit.Generated, "[GameEvent]는 [Serializable]이 없어도 생성 코드");
            Assert.IsFalse(hit.CanWrite, "readonly");
            Assert.AreEqual(7, hit.Get(new MtHit(7)));
            Assert.AreEqual(14, FieldAccess.Read(new MtHit(7), "Double"), "계산 속성");

            var world = new MtWorld();
            world.items.Add(new MtItem { level = 2 });
            world.items.Add(new MtItem { level = 5 });
            CollectionAssert.AreEqual(new object[] { 2, 5 }, FieldPaths.Resolve(world, "items[].level").ToList());
            Assert.IsTrue(FieldOps.Test(world, "items[].level", FieldOp.AtLeast, "5"));
        }

        [Test]
        public void 쓰고_읽으면_같다_private_부모_구조체_다형성_포함()
        {
            var w = World();
            w.items[2].Secret = 42;
            w.items[2].mood = MtMood.Angry;
            w.items[2].big = long.MinValue;
            w.items[2].precise = Math.PI;
            w.items[2].letter = '가';
            w.items[2].path.Add(new MtPoint { x = -3, y = 1.5f, tag = null });
            var copy = Mirror.Clone(w);
            CollectionAssert.AreEqual(Bytes(w), Bytes(copy));
            Assert.AreEqual(42, copy.items[2].Secret, "private [SerializeField]");
            Assert.AreEqual(2, copy.items[2].level, "부모 클래스 필드");
            Assert.IsInstanceOf<MtCircle>(copy.shape, "선언은 추상, 실제 형식으로");
            Assert.IsInstanceOf<MtBox>(copy.shapes[0]);
            Assert.AreEqual(Math.PI, copy.items[2].precise);
            Assert.AreEqual('가', copy.items[2].letter);
        }

        [Test]
        public void 값_필드() => Check(World(), w =>
        {
            w.turn = 9;
            w.items[0].mood = MtMood.Angry;
            w.items[0].precise = 0.1;
            w.items[0].broken = true;
            w.items[3].Secret = 5;
            w.items[4].at = new MtPoint { x = 7, y = 2, tag = "q" };
        });

        [Test]
        public void 리스트_앞쪽_지우기와_끼워_넣기는_작게_기록된다()
        {
            var w = World();
            var node = Mirror.Make(w);
            w.items.RemoveAt(0);
            w.items.Insert(2, new MtItem { id = "new" });
            var changes = new ChangeSet();
            Mirror.Sync(w, node, changes);
            Assert.IsTrue(changes.Forward.Length < 80, "배치 구간 + 새 요소만: " + changes.Forward.Length);
            Check(World(), x =>
            {
                x.items.RemoveAt(0);
                x.items.Insert(2, new MtItem { id = "new" });
                x.items.Reverse();
            });
        }

        [Test]
        public void 객체_교체와_null() => Check(World(), w =>
        {
            w.shape = new MtBox { name = "box" };
            w.items[1] = new MtItem { id = "swap" };
            w.slots[0] = w.items[3];
            w.slots[1] = null;
            w.anything = new MtCircle { radius = 9 };
            w.items[2].path = null;
        });

        [Test]
        public void 배열_딕셔너리() => Check(World(), w =>
        {
            w.scores[1] = 20;
            w.scores = new[] { 5, 6, 7, 8 };
            w.byName["second"] = w.items[2];
            w.byName.Remove("first");
            w.moods[MtMood.Calm] = 3;
            w.moods[MtMood.Angry] = 1;
        });

        [Test]
        public void 안쪽_객체_필드는_경로로_기록된다()
        {
            var w = World();
            var node = Mirror.Make(w);
            w.items[3].level = 10;
            w.byName["first"].id = "renamed";
            var changes = new ChangeSet { Describe = true };
            Mirror.Sync(w, node, changes);
            var paths = changes.Changes.Select(c => c.Path).ToList();
            CollectionAssert.Contains(paths, "items[3].level");
            // byName["first"]은 items[0]과 같은 객체: 한 번만(먼저 만난 자리에서) 기록한다.
            CollectionAssert.Contains(paths, "items[0].id");
            Assert.AreEqual(1, paths.Count(p => p.EndsWith(".id")), "같은 객체의 변경은 한 번");
        }

        [Test]
        public void 두_곳에서_가리키는_객체는_받는_쪽에서도_같은_인스턴스다()
        {
            var w = World();
            var node = Mirror.Make(w);
            var receiver = new MirrorReceiver();
            var client = (MtWorld)receiver.Load(Mirror.Snapshot(node));
            Assert.AreSame(client.items[1], client.slots[1], "처음 모습에서");
            Assert.AreSame(client.items[0], client.byName["first"]);

            w.slots[2] = w.items[4];
            var moved = w.items[2];
            w.items.RemoveAt(2);
            w.byName["moved"] = moved;
            w.items[4 - 1].level = 77;
            var changes = new ChangeSet();
            Mirror.Sync(w, node, changes);
            receiver.Apply(client, changes);
            Assert.AreSame(client.items[3], client.slots[2], "새로 가리킨 곳");
            Assert.AreEqual(77, client.slots[2].level);
            Assert.AreEqual("i2", client.byName["moved"].id, "리스트에서 빠져 딕셔너리로 옮긴 객체");
            CollectionAssert.AreEqual(Bytes(w), Bytes(client));

            var copy = Mirror.Clone(w);
            Assert.AreSame(copy.items[0], copy.byName["first"], "복사본도 공유를 지킨다");
        }

        [Test]
        public void 비어_있을_수_있는_값_int_물음표()
        {
            Mirror.Initialize();
            Assert.IsTrue(Mirror.Supports(typeof(MtOptional), out var reason), reason);
            var live = new MtOptional();
            var node = Mirror.Make(live);
            var receiver = new MirrorReceiver();
            var client = (MtOptional)receiver.Load(Mirror.Snapshot(node));
            live.maybe = 3;
            live.mood = MtMood.Angry;
            live.many.Add(null);
            live.many.Add(1.5f);
            var changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            receiver.Apply(client, changes);
            Assert.AreEqual(3, client.maybe);
            Assert.AreEqual(MtMood.Angry, client.mood);
            CollectionAssert.AreEqual(new float?[] { null, 1.5f }, client.many);
            Mirror.Undo(live, changes);
            Assert.IsNull(live.maybe);
            Assert.AreEqual(0, live.many.Count);
        }

        [Test]
        public void 비어_있는_Nullable은_Sync마다_바뀐_것으로_기록하지_않는다()
        {
            Mirror.Initialize();
            var live = new MtOptional();
            var node = Mirror.Make(live);
            var changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            Assert.IsTrue(changes.IsEmpty, "아무것도 바꾸지 않았다(null → null)");
            live.maybe = 2;
            Mirror.Sync(live, node, changes = new ChangeSet());
            Assert.IsFalse(changes.IsEmpty);
            live.maybe = null;
            Mirror.Sync(live, node, changes = new ChangeSet());
            Assert.IsFalse(changes.IsEmpty, "값 → null은 바뀐 것");
            Mirror.Sync(live, node, changes = new ChangeSet());
            Assert.IsTrue(changes.IsEmpty);
        }

        [Test]
        public void 닿지_않게_된_고리는_나무에서_빠지고_다시_들어와도_늦게_온_받는_쪽이_읽는다()
        {
            Mirror.Initialize();
            var r = new MtLoop { name = "r" };
            var a = new MtLoop { name = "a" };
            var b = new MtLoop { name = "b", next = a };
            a.next = b;
            r.next = a;
            var node = Mirror.Make(r);
            r.next = null;
            Mirror.Sync(r, node, new ChangeSet());
            var late = new MirrorReceiver();
            var client = (MtLoop)late.Load(Mirror.Snapshot(node));
            Assert.IsNull(client.next);
            r.next = a;
            var changes = new ChangeSet();
            Mirror.Sync(r, node, changes);
            Assert.DoesNotThrow(() => late.Apply(client, changes), "다시 들어온 고리는 내용째 간다");
            Assert.AreEqual("a", client.next.name);
            Assert.AreSame(client.next, client.next.next.next, "고리 모양도 그대로");
        }

        [Test]
        public void 서로_가리키는_객체도_비교하고_복사한다()
        {
            var a = new MtLoop { name = "a" };
            var b = new MtLoop { name = "b", next = a };
            a.next = b;
            var node = Mirror.Make(a);
            b.name = "B";
            var changes = new ChangeSet { Describe = true };
            Mirror.Sync(a, node, changes);
            Assert.AreEqual(1, changes.Changes.Count);
            var copy = Mirror.Clone(a);
            Assert.AreSame(copy, copy.next.next, "순환도 그대로");
            var receiver = new MirrorReceiver();
            var client = (MtLoop)receiver.Load(Mirror.Snapshot(node));
            Assert.AreSame(client, client.next.next);
            var found = new List<MtLoop>();
            Mirror.Collect(node, found);
            Assert.AreEqual(2, found.Count, "찾기는 한 번씩");
        }

        [Test]
        public void 되돌리면_빠졌던_객체가_같은_인스턴스로_돌아온다()
        {
            var w = World();
            var node = Mirror.Make(w);
            var removed = w.items[1];
            var oldShape = w.shape;
            w.items.RemoveAt(1);
            w.shape = new MtBox();
            removed.level = 99;
            var changes = new ChangeSet();
            Mirror.Sync(w, node, changes);
            Mirror.Undo(w, changes);
            Assert.AreSame(removed, w.items[1]);
            Assert.AreEqual(1, removed.level, "값은 기록대로");
            Assert.AreSame(oldShape, w.shape);
        }

        static MtTreeNode Tree()
        {
            var root = new MtTreeNode { name = "root" };
            foreach (var name in new[] { "a", "b", "c" }) root.children.Add(new MtTreeNode { name = name, parent = root });
            return root;
        }

        // 뺀 요소가 그 리스트를 가진 객체를 가리키면(부모 참조) 뒤로 기록에 그 객체도 원래 객체째 들어간다. 되돌리며 그 객체를
        // 다시 채울 때 리스트 필드가 새 리스트로 바뀌어도, 배치를 짜던 리스트에 끝까지 짠다(요소가 겹치지 않고 같은 리스트 인스턴스).
        [Test]
        public void 부모를_가리키는_요소를_빼고_되돌려도_리스트가_겹치지_않는다()
        {
            var root = Tree();
            var children = root.children;
            var removed = root.children[1];
            var node = Mirror.Make(root);
            var receiver = new MirrorReceiver();
            var client = (MtTreeNode)receiver.Load(Mirror.Snapshot(node));
            var before = Bytes(root);
            root.children.RemoveAt(1);
            var changes = new ChangeSet();
            Mirror.Sync(root, node, changes);
            receiver.Apply(client, changes);
            Mirror.Undo(root, changes);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, root.children.Select(c => c.name).ToList(), "겹치지 않는다");
            Assert.AreSame(children, root.children, "같은 리스트 인스턴스");
            Assert.AreSame(removed, root.children[1], "빠졌던 객체도 같은 인스턴스");
            CollectionAssert.AreEqual(before, Bytes(root));
            var redo = new ChangeSet();
            Mirror.Sync(root, node, redo);
            receiver.Apply(client, redo);
            CollectionAssert.AreEqual(before, Bytes(client), "받는 쪽도 되돌린 모습");
        }

        sealed class MtCut : GameAction
        {
            public bool fail;
            protected override void Run()
            {
                Game.Get<MtTreeNode>("tree").children.RemoveAt(1);
                if (fail) throw Game.Fail("fail.test", "시험 실패");
            }
        }

        // 미리보기(CanDo)·실패한 행동·Undo는 같은 되돌리기(Mirror.Undo)를 쓴다: 부모 참조가 있어도 데이터가 그대로 돌아온다.
        [Test]
        public void 부모를_가리키는_요소를_빼는_행동의_미리보기와_실패와_Undo는_데이터를_깨지_않는다()
        {
            var session = new GameSession();
            var root = Tree();
            session.Data.Define("tree", DataZone.Session, root);
            string Names() => string.Join(",", session.Data.Get<MtTreeNode>("tree").children.Select(c => c.name));
            Assert.IsTrue(session.Game.CanDo(new MtCut(), out _));
            Assert.AreEqual("a,b,c", Names(), "미리보기 뒤");
            Assert.Throws<GameFailure>(() => session.Game.Do(new MtCut { fail = true }));
            Assert.AreEqual("a,b,c", Names(), "실패한 행동 뒤");
            session.Game.Do(new MtCut());
            Assert.AreEqual("a,c", Names());
            Assert.IsTrue(session.Undo());
            Assert.AreEqual("a,b,c", Names(), "Undo 뒤");
        }

        sealed class MtDraw : GameAction
        {
            protected override void Run() => Game.Get<MtDeckHolder>("deck").deck.Add("card");
        }

        // 컬렉션 클래스를 상속한 데이터: 생성 코드는 요소를 보지 못하므로 그 형식(과 그것을 담은 형식)을 생성 코드로 다루지 않고 이유를 밝힌다 —
        // 플레이어 데이터는 예전 비교 방식으로 요소의 변경도 기록·Undo하고, 네트워크로는 보내기 전에 막는다(예전에는 요소의 변경을 조용히 놓쳤다).
        [Test]
        public void 컬렉션_클래스를_상속한_데이터는_생성_코드로_다루지_않고_요소의_변경도_되돌린다()
        {
            Mirror.Initialize();
            Assert.IsFalse(Mirror.Supports(typeof(MtDeck), out var reason));
            StringAssert.Contains("컬렉션 클래스", reason);
            Assert.IsFalse(Mirror.Supports(typeof(MtDeckHolder), out reason), "그것을 담은 클래스도");
            StringAssert.Contains("컬렉션 클래스", reason);
            Assert.IsFalse(Mirror.CanSend(typeof(MtDeckHolder), out _), "네트워크로 보낼 때도 막는다");

            var session = new GameSession();
            session.Data.Define("deck", DataZone.Session, new MtDeckHolder());
            session.Game.Do(new MtDraw());
            Assert.AreEqual(1, session.Data.Get<MtDeckHolder>("deck").deck.Count);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(0, session.Data.Get<MtDeckHolder>("deck").deck.Count, "요소의 변경도 되돌린다");
        }

        // 비교 코드가 없는 객체(에셋 참조처럼)는 참조 그대로: 복사·되돌리기에서 같은 인스턴스를 잇는다.
        public sealed class MtAssetLike
        {
        }

        // 이어 붙인 기록(Append — 한 행동 안의 여러 Sync)은 원래 객체 번호를 기록마다 따로 센다: 떼어 내거나(Detach) 비우고(Clear) 다시 써도 맞는다.
        [Test]
        public void 이어_붙인_기록을_떼어_내거나_비우고_다시_써도_원래_객체로_되돌린다()
        {
            var w = World();
            var asset = new MtAssetLike();
            w.anything = asset;
            var shape = w.shape;
            var node = Mirror.Make(w);
            ChangeSet Step(Action change)
            {
                change();
                var changes = new ChangeSet();
                Mirror.Sync(w, node, changes);
                return changes;
            }
            var combined = new ChangeSet();
            combined.Append(Step(() => w.anything = new MtAssetLike()));
            combined.Append(Step(() => w.shape = new MtBox()));
            Mirror.Undo(w, combined.Detach());
            Assert.AreSame(asset, w.anything);
            Assert.AreSame(shape, w.shape, "떼어 낸 기록: 뒤 기록의 원래 객체 번호도 맞다");

            Mirror.Sync(w, node, new ChangeSet());
            combined.Append(Step(() => w.anything = new MtAssetLike()));
            combined.Append(Step(() => w.shape = new MtBox()));
            combined.Clear();
            var box = w.shape;
            combined.Append(Step(() =>
            {
                w.items[0].id = new string('x', 200); // 앞에 긴 기록 — 원래 객체를 가리키는 기록이 비우기 전 이어 붙인 자리보다 뒤에 온다
                w.shape = new MtCircle();
            }));
            Mirror.Undo(w, combined);
            Assert.AreSame(box, w.shape, "비운 기록: 비우기 전 이어 붙인 자리가 남지 않는다");
        }

        [Test]
        public void 에셋_같은_참조는_복사와_되돌리기에서_그대로다()
        {
            var w = World();
            var asset = new MtAssetLike();
            w.anything = asset;
            Assert.AreSame(asset, Mirror.Clone(w).anything, "복사본도 같은 참조");

            var node = Mirror.Make(w);
            w.anything = new MtAssetLike();
            var changes = new ChangeSet();
            Mirror.Sync(w, node, changes);
            Mirror.Undo(w, changes);
            Assert.AreSame(asset, w.anything, "되돌리면 원래 참조");
        }

        [Test]
        public void 바뀐_것이_없으면_기록도_할당도_없다()
        {
            var w = World();
            for (var i = 0; i < 200; i++) w.items.Add(new MtItem { id = "x" + i });
            var node = Mirror.Make(w);
            var changes = new ChangeSet();
            Mirror.Sync(w, node, changes);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++) Mirror.Sync(w, node, changes);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(changes.IsEmpty);
            Assert.AreEqual(0, allocated, "비교만 할 때는 할당이 없다");
        }

        // 바뀐 것 → 받는 쪽에 적용 → 같다 / 되돌리기 → 처음과 같다(MtBattle용).
        static void CheckBattle(Action<MtBattle> change)
        {
            var live = new MtBattle();
            live.units.Add(new MtUnit());
            live.units.Add(new MtUnit { hp = 20 });
            var node = Mirror.Make(live);
            var receiver = new MirrorReceiver();
            var client = (MtBattle)receiver.Load(Mirror.Snapshot(node));
            var before = Bytes(live);
            change(live);
            var changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            receiver.Apply(client, changes);
            CollectionAssert.AreEqual(Bytes(live), Bytes(client), "받는 쪽이 같아진다");
            Assert.AreSame(client.units[0], client.log.Count > 0 ? client.log[0].target : client.last?.target ?? client.byKey["k"].target, "같은 인스턴스");
            Mirror.Undo(live, changes);
            CollectionAssert.AreEqual(before, Bytes(live), "되돌리면 처음과 같다");
            Assert.AreEqual(10, live.units[0].hp, "되돌리면 hp도 돌아온다");
            var redo = new ChangeSet();
            Mirror.Sync(live, node, redo);
            receiver.Apply(client, redo);
            CollectionAssert.AreEqual(before, Bytes(client), "되돌린 것도 받는 쪽에 전해진다");
        }

        [Test]
        public void 새_객체가_가리키는_기존_객체의_변경도_기록된다_리스트() => CheckBattle(b =>
        {
            b.units[0].hp -= 5;
            b.log.Add(new MtHitLog { target = b.units[0] });
        });

        [Test]
        public void 새_객체가_가리키는_기존_객체의_변경도_기록된다_필드() => CheckBattle(b =>
        {
            b.units[0].hp -= 5;
            b.last = new MtHitLog { target = b.units[0] };
        });

        [Test]
        public void 새_객체가_가리키는_기존_객체의_변경도_기록된다_딕셔너리와_안쪽_리스트() => CheckBattle(b =>
        {
            b.units[0].hp -= 5;
            b.units[1].hp += 1;
            var hit = new MtHitLog { target = b.units[0] };
            hit.also.Add(b.units[1]);
            b.byKey["k"] = hit;
        });


        [Test]
        public void 안쪽에_컬렉션이_있는_구조체는_예전_비교_방식으로_다룬다()
        {
            Mirror.Initialize();
            Assert.IsFalse(Mirror.Supports(typeof(MtHolder), out var reason));
            Assert.IsTrue(reason.Contains("구조체"), reason);

            // 예전 방식: Modify로 바꾸면 안쪽 변경도 Undo된다(Modify 없이 바꾸면 경고).
            var session = new GameSession();
            var holder = new MtHolder();
            session.Data.Define("holder", DataZone.Session, holder);
            session.Game.Do(new MtEdit { run = () => session.Data.Modify<MtHolder>("holder", h => h.bag.items.Add(3)) });
            Assert.AreEqual(1, holder.bag.items.Count);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(0, session.Data.Get<MtHolder>("holder").bag.items.Count);
        }

        sealed class MtEdit : GameAction
        {
            public Action run;
            protected override void Run() => run();
        }


        // 같은 리스트 인스턴스를 두 필드가 가리키면 알린다(변경이 두 번 기록되어 Undo가 데이터를 깬다).
        [Test]
        public void 같은_컬렉션을_두_곳에서_가리키면_알린다()
        {
            var warnings = new List<string>();
            Action<string> listen = warnings.Add;
            Mirror.Warning += listen;
            try
            {
                Mirror.ResetWarnings();
                var live = new MtTwoLists();
                var node = Mirror.Make(live);
                Mirror.Sync(live, node, new ChangeSet());
                Assert.AreEqual(0, warnings.Count, "따로면 알리지 않는다");
                live.chosen = live.all;
                Mirror.Sync(live, node, new ChangeSet());
                Assert.AreEqual(1, warnings.Count, "같은 인스턴스를 가리키면 알린다");
                Assert.IsTrue(warnings[0].Contains("chosen"), warnings[0]);
                live.chosen = new List<MtUnit>();
                Mirror.ResetWarnings();
                Mirror.Sync(live, node, new ChangeSet());
                Mirror.Sync(live, node, new ChangeSet());
                Assert.AreEqual(1, warnings.Count, "다시 따로면 더 알리지 않는다");
            }
            finally
            {
                Mirror.Warning -= listen;
            }
        }


        // 받은 데이터(네트워크·리플레이)는 믿지 않는다: 끝없이 겹친 객체·남은 데이터보다 큰 개수는 거절(FormatException — 잡을 수 있는 예외).
        [Test]
        public void 깨진_데이터는_스택을_넘기거나_거대하게_할당하지_않고_거절한다()
        {
            var head = new MtLoop { name = "0" };
            var cur = head;
            for (var i = 1; i < 400; i++)
            {
                cur.next = new MtLoop { name = i.ToString() };
                cur = cur.next;
            }
            var w = new MirrorWriter();
            Mirror.Write(w, head);
            Assert.Throws<FormatException>(() => Mirror.Read(new MirrorReader(w.ToArray()), typeof(MtLoop)));

            // 그 뒤에도 보통 읽기는 그대로 된다(깊이 세기가 남지 않는다).
            var shallow = new MirrorWriter();
            Mirror.Write(shallow, new MtLoop { name = "a", next = new MtLoop { name = "b" } });
            Assert.AreEqual("b", ((MtLoop)Mirror.Read(new MirrorReader(shallow.ToArray()), typeof(MtLoop))).next.name);

            var huge = new MirrorWriter();
            huge.UVar(100_000_000);
            Assert.Throws<FormatException>(() => NetProtocol.ReadStrings(new MirrorReader(huge.ToArray())), "개수가 남은 데이터보다 많다");
            Assert.Throws<FormatException>(() => new MirrorReader(huge.ToArray()).Str(), "글자 길이가 남은 데이터보다 길다");
            var endless = new byte[16];
            for (var i = 0; i < endless.Length; i++) endless[i] = 0xFF;
            Assert.Throws<FormatException>(() => new MirrorReader(endless).UVar(), "끝나지 않는 숫자 인코딩");
            Assert.AreEqual("abc", NetProtocol.CleanName("\u0001 abc \n"));
            Assert.AreEqual(NetProtocol.MaxNameLength, NetProtocol.CleanName(new string('가', 100)).Length);
        }

        // 형식 번호표(입장할 때 받는 이름 목록)는 한도보다 많으면 배열을 잡기 전에 거절한다: 1바이트짜리 빈 이름 수천만 개를 담은 메시지가
        // 입장 확인 전에 서버 메모리를 메시지의 여러 배로 잡지 못하게.
        [Test]
        public void 형식_번호표는_한도보다_많은_이름을_잡기_전에_거절한다()
        {
            var w = new MirrorWriter();
            var count = NetProtocol.MaxTypeTable + 1;
            w.UVar((ulong)count);
            for (var i = 0; i < count; i++) w.Str(null);
            Assert.Throws<FormatException>(() => NetProtocol.ReadStrings(new MirrorReader(w.ToArray()), NetProtocol.MaxTypeTable));
            Assert.AreEqual(count, NetProtocol.ReadStrings(new MirrorReader(w.ToArray())).Length, "한도를 주지 않으면 예전처럼 다 읽는다");
            var ok = new MirrorWriter();
            NetProtocol.WriteStrings(ok, new[] { "a", "b" });
            CollectionAssert.AreEqual(new[] { "a", "b" }, NetProtocol.ReadStrings(new MirrorReader(ok.ToArray()), NetProtocol.MaxTypeTable));
        }

        // 리스트·배열·딕셔너리 값·Nullable의 float·double도 필드처럼 비트로 비교한다: 0 → -0도 바뀐 것이라 받는 쪽도 같은 값(-x·1/x가 같은 결과)이 된다.
        [Test]
        public void 컬렉션과_Nullable의_음의_영도_필드처럼_바뀐_것으로_본다()
        {
            Mirror.Initialize();
            var live = new MtFloats();
            var node = Mirror.Make(live);
            var receiver = new MirrorReceiver();
            var client = (MtFloats)receiver.Load(Mirror.Snapshot(node));
            live.field = -0f;
            live.maybe = -0f;
            live.list[0] = -0f;
            live.array[0] = -0.0;
            live.map["a"] = -0f;
            var changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            Assert.AreEqual(5, changes.Count, "필드와 같이 다섯 곳 모두");
            receiver.Apply(client, changes);
            CollectionAssert.AreEqual(Bytes(live), Bytes(client), "받는 쪽도 -0");
            Assert.IsTrue(float.IsNegativeInfinity(1 / client.list[0]) && float.IsNegativeInfinity(1 / client.map["a"]) && float.IsNegativeInfinity(1 / client.maybe.Value));
            Mirror.Sync(live, node, changes = new ChangeSet());
            Assert.IsTrue(changes.IsEmpty, "그대로면 기록하지 않는다");
            live.list[0] = float.NaN;
            Mirror.Sync(live, node, new ChangeSet());
            Mirror.Sync(live, node, changes = new ChangeSet());
            Assert.IsTrue(changes.IsEmpty, "NaN은 같은 값");
        }

        // 짝을 잃은 서러게이트(이모지를 글자 수로 자른 글 등)도 글자 그대로 오간다(UTF-8로만 쓰면 U+FFFD로 바뀌어 받는 쪽·Undo가 다른 글이 된다).
        // 짝이 맞는 글은 예전과 같은 바이트다.
        [Test]
        public void 짝을_잃은_서러게이트도_글자_그대로_오가고_되돌린다()
        {
            foreach (var text in new[] { "a\uD83D", "\uDE00b", "x\uDC00\uD800y", "😀가\uD83D", ((char)0xFFFD).ToString(), "한글", "" })
            {
                var w = new MirrorWriter();
                w.Str(text);
                Assert.IsTrue(text == new MirrorReader(w.ToArray()).Str(), "글: " + string.Join(" ", text.Select(c => ((int)c).ToString("X4"))));
            }
            var valid = new MirrorWriter();
            valid.Str("가😀a");
            var utf8 = System.Text.Encoding.UTF8.GetBytes("가😀a");
            CollectionAssert.AreEqual(new[] { (byte)(utf8.Length + 1) }.Concat(utf8).ToArray(), valid.ToArray(), "짝이 맞는 글은 UTF-8 그대로");

            var live = new MtLoop { name = "a" };
            var node = Mirror.Make(live);
            var receiver = new MirrorReceiver();
            var client = (MtLoop)receiver.Load(Mirror.Snapshot(node));
            live.name = "b\uD83D";
            var changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            receiver.Apply(client, changes);
            Assert.IsTrue(client.name == "b\uD83D", "받는 쪽");
            live.name = "c";
            changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            Mirror.Undo(live, changes);
            Assert.IsTrue(live.name == "b\uD83D", "되돌리기");
            Assert.IsTrue(Mirror.Clone(live).name == "b\uD83D", "복사");
        }

        // 받은 덩어리(시작·끝 — 메시지 안의 한 기록)의 끝을 넘어 읽지 않는다: 잘린 기록은 뒤에 이어진 바이트(다음 기록·지난 메시지의 찌꺼기)를
        // 값으로 읽어 적용하는 대신 거절한다.
        [Test]
        public void 받은_범위의_끝을_넘어_읽지_않고_거절한다()
        {
            var live = new MtUnit();
            var node = Mirror.Make(live);
            live.hp = 300;                                   // 2바이트 숫자
            var changes = new ChangeSet();
            Mirror.Sync(live, node, changes);
            var bytes = changes.Forward.ToArray();
            var target = new MtUnit();
            Assert.Throws<FormatException>(() => Mirror.Apply(target, bytes, 0, bytes.Length - 1), "숫자 중간에서 잘린 기록");
            Assert.AreEqual(10, target.hp, "잘린 값을 적용하지 않는다");
            Mirror.Apply(target, bytes, 0, bytes.Length);
            Assert.AreEqual(300, target.hp);

            var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            Assert.Throws<FormatException>(() => new MirrorReader(data, 0, 0).Byte());
            Assert.Throws<FormatException>(() => new MirrorReader(data, 0, 0).Bool());
            Assert.Throws<FormatException>(() => new MirrorReader(data, 0, 3).Float());
            Assert.Throws<FormatException>(() => new MirrorReader(data, 0, 7).Double());
            Assert.Throws<FormatException>(() => new MirrorReader(new byte[] { 0x80, 0x80 }).UVar(), "배열 끝에서 끝나는 숫자도");
        }

        // 받은 개수만큼 미리 잡지 않는다: 겹친 리스트마다 "남은 바이트만큼"을 잡게 한 작은 메시지가 메모리를 수천 배로 부풀리지 못한다.
        [Test]
        public void 겹친_리스트의_큰_개수로_메모리를_부풀리지_못한다()
        {
            Mirror.Initialize();
            var id = (ulong)Mirror.Find(typeof(MtTreeNode)).Id;
            var w = new MirrorWriter();
            for (var level = 0; level < 300; level++)
            {
                w.Byte(1);              // 객체(형식 번호 + 필드)
                w.UVar(id);
                w.Str("x");             // name
                w.Byte(0);              // parent = null
                w.UVar(60000 + 1);      // children: 60000개라고 한다(남은 바이트 안)
            }
            w.Bytes(new byte[64 * 1024], 0, 64 * 1024);
            var bytes = w.ToArray();
            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<FormatException>(() => new MirrorReceiver().Load(bytes));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(allocated < 16 * 1024 * 1024, $"메시지 {bytes.Length / 1024}KB에 {allocated / (1024 * 1024)}MB를 잡았다");
        }

        // 깊이 한도는 받은 데이터(네트워크·리플레이)를 막기 위한 것이다: 이 프로그램이 쓴 기록(되돌리기·복사)은 더 깊어도 읽는다.
        [Test]
        public void 이_프로그램이_쓴_깊은_기록은_한도와_상관없이_되돌리고_복사한다()
        {
            var head = new MtLoop { name = "0" };
            var tail = head;
            for (var i = 1; i < 300; i++)
            {
                tail.next = new MtLoop { name = i.ToString() };
                tail = tail.next;
            }
            var node = Mirror.Make(head);
            tail.name = "끝";
            var changes = new ChangeSet();
            Mirror.Sync(head, node, changes);
            Assert.DoesNotThrow(() => Mirror.Undo(head, changes), "깊은 곳의 변경");
            Assert.AreEqual("299", tail.name);
            Mirror.Sync(head, node, new ChangeSet());

            var second = head.next;
            head.next = null;
            changes = new ChangeSet();
            Mirror.Sync(head, node, changes);
            Assert.DoesNotThrow(() => Mirror.Undo(head, changes), "깊은 사슬을 떼었다가 되돌리기");
            Assert.AreSame(second, head.next);

            MtLoop copy = null;
            Assert.DoesNotThrow(() => copy = Mirror.Clone(head), "깊은 데이터의 복사");
            var count = 0;
            for (var at = copy; at != null; at = at.next) count++;
            Assert.AreEqual(300, count);

            // 받은 데이터는 그대로 막는다.
            Mirror.Sync(head, node, new ChangeSet());
            Assert.Throws<FormatException>(() => new MirrorReceiver().Load(Mirror.Snapshot(node)));
        }

        // 미리보기(받는 쪽에 보내지 않음)에서 빠졌다가 되돌려진 객체: 번호가 그대로라 받는 쪽에서 계속 같은 인스턴스로 이어진다.
        [Test]
        public void 보내지_않은_변경에서_빠졌다_돌아온_객체는_번호를_지킨다()
        {
            var b = new MtBattle();
            var u = new MtUnit();
            b.units.Add(u);
            b.last = new MtHitLog { target = u };
            var node = Mirror.Make(b);
            var receiver = new MirrorReceiver();
            var client = (MtBattle)receiver.Load(Mirror.Snapshot(node));

            Mirror.KeepIds(node, true);
            b.units.Clear();
            b.last = null;
            var quiet = new ChangeSet();
            Mirror.Sync(b, node, quiet);
            Mirror.Undo(b, quiet);
            Mirror.Sync(b, node, new ChangeSet());
            Mirror.KeepIds(node, false);

            b.log.Add(new MtHitLog { target = u });
            u.hp = 3;
            var changes = new ChangeSet();
            Mirror.Sync(b, node, changes);
            receiver.Apply(client, changes);
            Assert.AreSame(client.units[0], client.log[0].target, "새로 가리킨 곳도 같은 인스턴스");
            Assert.AreSame(client.units[0], client.last.target);
            u.hp = 1;
            changes = new ChangeSet();
            Mirror.Sync(b, node, changes);
            receiver.Apply(client, changes);
            Assert.AreEqual(1, client.log[0].target.hp);
            CollectionAssert.AreEqual(Bytes(b), Bytes(client));
        }
    }
}
