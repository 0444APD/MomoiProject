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

        // 비교 코드가 없는 객체(에셋 참조처럼)는 참조 그대로: 복사·되돌리기에서 같은 인스턴스를 잇는다.
        public sealed class MtAssetLike
        {
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
