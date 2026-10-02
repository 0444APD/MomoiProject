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

    // [BindTo]: 코드로 쓴 결과를 도감 항목에 연결한다(그 항목의 결과 칸 대신).
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

    public class BindToTests
    {
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

        // 바뀐 것 → 받는 쪽에 적용 → 같다 / 되돌리기 → 처음과 같다 / 사본도 맞다.
        static void Check(MtWorld live, Action<MtWorld> change)
        {
            var node = Mirror.Make(live);
            var client = Mirror.Clone(live);
            var before = Bytes(live);
            change(live);
            var changes = new ChangeSet { Describe = true };
            Mirror.Sync(live, node, changes);
            Mirror.Apply(client, changes);
            CollectionAssert.AreEqual(Bytes(live), Bytes(client), "받는 쪽이 같아진다");
            var after = Bytes(live);
            Mirror.Undo(live, changes);
            CollectionAssert.AreEqual(before, Bytes(live), "되돌리면 처음과 같다");
            var redo = new ChangeSet();
            Mirror.Sync(live, node, redo);
            Mirror.Apply(client, redo);
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
            CollectionAssert.Contains(changes.Changes.Select(c => c.Path).ToList(), "items[3].level");
            CollectionAssert.Contains(changes.Changes.Select(c => c.Path).ToList(), "byName[first].id");
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
    }
}
