using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace GBible.Tests
{
    // ── 부모 필드를 잃는 형식 ──

    [Serializable]
    public class MbPiece<T>
    {
        public string id;
        public T data;
    }

    // 제네릭 부모의 필드(id·data)는 생성 코드가 다루지 못한다 → Supports가 거짓(예전 비교 방식 — 부모 필드도 다룬다).
    [Serializable]
    public class MbJoker : MbPiece<int>
    {
        public int bonus;
    }

    [Serializable]
    public class MbJokerBox
    {
        public MbJoker joker = new MbJoker();
    }

    // 같은 어셈블리의 제네릭이 아닌 [Serializable] 부모는 그대로 생성 코드로 다룬다.
    [Serializable]
    public class MbBasePiece
    {
        public string id;
        public int level;
    }

    [Serializable]
    public class MbPlainJoker : MbBasePiece
    {
        public int bonus;
    }

    // [Serializable]이 없는 중간 부모(필드 없음): 그 위의 저장 대상 부모(MbBasePiece)의 필드는 그 부모의 생성 코드로 다룬다.
    public abstract class MbPlainMiddle : MbBasePiece
    {
    }

    [Serializable]
    public class MbMiddleJoker : MbPlainMiddle
    {
        public int bonus;
    }

    // 행동도 같다: 중간 부모에 [Serializable]이 없어도 GameAction의 필드(actorId …)를 쓴다.
    public abstract class MbUnitAction : GameAction
    {
    }

    [Serializable]
    public class MbMove : MbUnitAction
    {
        public int x;

        protected override void Run()
        {
        }
    }

    // 중간 부모에 [Serializable]이 없고 필드가 있으면 그 필드는 생성 코드가 쓰지 않는다 → 보내기 전에 실패(받는 쪽에 기본값으로 가지 않게).
    public abstract class MbTargetAction : GameAction
    {
        public string target;
    }

    [Serializable]
    public class MbAim : MbTargetAction
    {
        public int power;

        protected override void Run()
        {
        }
    }

    // 제네릭 [Serializable] 부모의 필드: 생성 코드가 다루지 못하는 부모(LostBase) — 보낼 수 없는 이유도 그것이다.
    [Serializable]
    public abstract class MbBaseAction<T> : GameAction
    {
        public T value;

        protected override void Run()
        {
        }
    }

    [Serializable]
    public class MbIntAction : MbBaseAction<int>
    {
    }

#if UNITY_5_3_OR_NEWER
    // GBible.Core를 참조하지 않는 어셈블리(UnityEngine.CoreModule)의 [Serializable] 부모: 그 저장 필드(m_PersistentCalls)를 잃는다.
    [Serializable]
    public class MbUnityEvent : UnityEngine.Events.UnityEvent
    {
        public int extra;
    }
#endif

    // ── 깨진 IL이 되던 형식: ref struct 속성 · volatile 필드 · [Serializable] ref struct ──

    [Serializable]
    public class MbUnit
    {
        public int hp = 10;
    }

    [Serializable]
    public class MbSpanHolder
    {
        public int[] values = { 1, 2, 3 };
        public volatile int count;
        public volatile MbUnit target;
        public ReadOnlySpan<int> Span => values;
        public int First => values[0];
    }

    [Serializable]
    public ref struct MbRefStruct
    {
        public int x;
    }

    // 등록이 실패하는 형식(손으로 쓴 등록 메서드 — 시험에서만 실패시킨다).
    public static class MbBrokenRegister
    {
        public static bool Fail;

        static void __gb_register()
        {
            if (Fail) throw new InvalidOperationException("시험용 등록 실패");
        }
    }

    // ── 네트워크로 보낼 수 없는 행동·이벤트 ──

    [Serializable]
    public class MbGridAction : GameAction
    {
        public List<List<int>> grid = new List<List<int>>();

        protected override void Run()
        {
        }
    }

    [Serializable]
    public class MbPairAction : GameAction
    {
        public KeyValuePair<int, int> pair;

        protected override void Run()
        {
        }
    }

    [Serializable]
    public class MbSetAction : GameAction
    {
        public HashSet<int> picked = new HashSet<int>();

        protected override void Run()
        {
        }
    }

    [Serializable]
    public class MbReadonlyAction : GameAction
    {
        public readonly int amount = 3;

        protected override void Run()
        {
        }
    }

    [Serializable]
    public class MbAutoAction : GameAction
    {
        public int Amount { get; set; }

        protected override void Run()
        {
        }
    }

    // object 칸: 실제 값이 생성 코드가 있는 객체면 가고, 상자에 든 숫자·글자는 보내기 전에 막는다.
    [Serializable]
    public class MbPayloadAction : GameAction
    {
        public object payload;
        public int amount;

        protected override void Run() => Game.Session.Data.Set("got", (double)(amount + ((payload as MbUnit)?.hp ?? 0)));
    }

    // id가 있는 객체(행동의 참조 필드)는 id만 가므로 그 형식에 빠지는 칸이 있어도 보낼 수 있다.
    [Serializable]
    public class MbCard
    {
        public string id;
        public List<List<int>> history = new List<List<int>>();
    }

    [Serializable]
    public class MbPlayCard : GameAction
    {
        public MbCard card;

        protected override void Run()
        {
        }
    }

    // 참조 필드라도 id가 잃은 부모 쪽에 있으면 id도 가지 않는다.
    [Serializable]
    public class MbJokerAction : GameAction
    {
        public MbJoker joker;

        protected override void Run()
        {
        }
    }

    // 안쪽에 컬렉션이 있는 구조체: 변경 추적은 못 하지만 쓰고 읽기는 온전하다(네트워크로 보낼 수 있다).
    [Serializable]
    public struct MbBag
    {
        public int count;
        public List<int> items;
    }

    // 쓰고 읽을 수 없는 구조체(KeyValuePair·[Serializable]이 없는 구조체)를 품은 구조체: 그 구조체도 쓰고 읽을 수 없다(쓰다가 멈추지 않게 미리 거절).
    public struct MbPlainPoint
    {
        public int x;
    }

    [Serializable]
    public struct MbPairSlot
    {
        public int count;
        public KeyValuePair<int, int> pair;
    }

    [Serializable]
    public struct MbPointSlot
    {
        public int count;
        public MbPlainPoint point;
    }

    [Serializable]
    public class MbPairBox
    {
        public MbPairSlot slot;
    }

    [Serializable]
    public class MbPointBox
    {
        public List<MbPointSlot> slots = new List<MbPointSlot>();
    }

    [Serializable]
    public class MbSlotAction : GameAction
    {
        public MbPairSlot slot;

        protected override void Run()
        {
        }
    }

    [GameEvent("시험 격자")]
    public class MbGridEvent
    {
        public List<List<int>> grid = new List<List<int>> { new List<int> { 1 } };
    }

    [GameEvent("시험 상자 값")]
    public class MbBoxedEvent
    {
        public object payload;
    }

    [GameEvent("시험 가방")]
    public class MbBagEvent
    {
        public MbBag bag;
    }

    [GameEvent("시험 알림")]
    public class MbNotice
    {
        public int n;
    }

    [Serializable]
    public class MbEmit : GameAction
    {
        protected override void Run()
        {
            var events = Game.Session.Events;
            events.Publish(new MbGridEvent());
            events.Publish(new MbBoxedEvent { payload = 3 });
            events.Publish(new MbBagEvent { bag = new MbBag { count = 2, items = new List<int> { 4, 5 } } });
            events.Publish(new MbNotice { n = 7 });
            Game.Session.Data.Set("gold", 9.0);
        }
    }

    // 빌드·멀티에서만 드러나던 문제: 코드 줄이기 보호([Preserve]·link.xml) · 보낼 수 없는 행동·이벤트 · 부모 필드를 잃는 형식 ·
    // 깨진 IL이 되던 형식 · 형식 하나의 등록 실패.
    public class MirrorBuildTests
    {
        sealed class MbEdit : GameAction
        {
            public Action run;
            protected override void Run() => run();
        }

        // 서버 하나 + 클라이언트 하나(메모리 전송, 시간은 손으로 흘린다).
        sealed class Rig
        {
            public readonly MemoryNetwork Net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            public readonly GameSession ServerSession = new GameSession();
            public readonly NetServer Server;
            public readonly NetClient Client;

            public Rig()
            {
                ServerSession.Data.Define("gold", DataZone.Session, 5.0);
                ServerSession.Data.Define("got", DataZone.Session, 0.0);
                Server = new NetServer(ServerSession, Net.Server());
                Server.Start();
                Client = new NetClient(new GameSession(), Net.Client(), "철수");
                Client.Connect();
                Pump();
            }

            public void Pump(int steps = 20)
            {
                for (var i = 0; i < steps; i++)
                {
                    Net.Advance(0.01);
                    Server.Update(0.01);
                    Client.Update(0.01);
                }
            }
        }

        static MethodInfo RegisterOf(Type type) =>
            type.GetMethod("__gb_register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        // Unity 링커는 이름이 PreserveAttribute인 어트리뷰트를 이름공간과 상관없이 [Preserve]로 본다.
        static bool HasPreserve(MethodInfo method) => method != null && method.CustomAttributes.Any(a => a.AttributeType.Name == "PreserveAttribute");

        [Test]
        public void 등록_메서드에는_코드_줄이기를_막는_Preserve가_붙는다()
        {
            Assert.IsTrue(HasPreserve(RegisterOf(typeof(MbUnit))), "게임(테스트) 어셈블리의 클래스");
            Assert.IsTrue(HasPreserve(RegisterOf(typeof(MbBag))), "구조체");
            Assert.IsTrue(HasPreserve(RegisterOf(typeof(GameAction))), "추상 클래스(멤버 읽기만 등록)");
            Assert.IsTrue(HasPreserve(RegisterOf(typeof(ChoiceList))), "코어 어셈블리");
            var unityMirror = Type.GetType("GBible.UnityMirror, GBible.Unity");
            if (unityMirror != null) Assert.IsTrue(HasPreserve(RegisterOf(unityMirror)), "손으로 쓴 Unity 구조체 등록");
            // 다른 생성 메서드는 등록 메서드가 가리키므로(ldftn) 따로 붙이지 않는다.
            Assert.IsFalse(HasPreserve(typeof(MbUnit).GetMethod("__gb_write", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)));
        }

        [Test]
        public void 제네릭_부모의_필드를_잃는_형식은_예전_비교_방식으로_다룬다()
        {
            Mirror.Initialize();
            Assert.IsFalse(Mirror.Supports(typeof(MbJoker), out var reason), "부모 필드를 잃으므로 생성 코드로 다루지 않는다");
            StringAssert.Contains("MbPiece", reason);
            StringAssert.Contains("data", reason);
            Assert.IsFalse(PlayerDataStore.IsMirrored(typeof(MbJokerBox)), "그 형식을 담은 형식도");
            Assert.IsTrue(Mirror.Supports(typeof(MbPlainJoker), out reason), "같은 어셈블리의 제네릭이 아닌 부모는 그대로: " + reason);

            // 예전 비교 방식(리플렉션)은 부모 필드도 다룬다: 되돌리면 부모 필드도 돌아온다.
            var session = new GameSession();
            session.Data.Define("box", DataZone.Session, new MbJokerBox { joker = new MbJoker { id = "j", data = 3, bonus = 1 } });
            session.Do(new MbEdit { run = () => session.Data.Modify<MbJokerBox>("box", b => { b.joker.data = 9; b.joker.bonus = 2; }) });
            Assert.AreEqual(9, session.Data.Get<MbJokerBox>("box").joker.data);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(3, session.Data.Get<MbJokerBox>("box").joker.data, "부모 필드도 되돌린다");
            Assert.AreEqual(1, session.Data.Get<MbJokerBox>("box").joker.bonus);
        }

        [Test]
        public void Serializable이_없는_중간_부모를_건너_그_위_저장_대상_부모의_필드도_다룬다()
        {
            Mirror.Initialize();
            Assert.IsTrue(Mirror.Supports(typeof(MbMiddleJoker), out var reason), reason);
            CollectionAssert.AreEqual(new[] { "id", "level", "bonus" }, Mirror.Find(typeof(MbMiddleJoker)).Names, "위 부모의 필드가 먼저");
            var joker = new MbMiddleJoker { id = "j", level = 2, bonus = 1 };
            var node = Mirror.Make(joker);
            joker.level = 5;
            var changes = new ChangeSet();
            Mirror.Sync(joker, node, changes);
            Assert.IsFalse(changes.IsEmpty, "위 부모 필드의 변경도 기록한다");
            Mirror.Undo(joker, changes);
            Assert.AreEqual(2, joker.level, "되돌린다");
            var copy = Mirror.Clone(new MbMiddleJoker { id = "c", level = 3, bonus = 4 });
            Assert.AreEqual("c", copy.id, "복사본에도");
            Assert.AreEqual(3, copy.level);
            Assert.AreEqual(4, copy.bonus);

            var w = new MirrorWriter();
            Mirror.Write(w, new MbMove { actorId = 3, tick = 9, x = 1 });
            var sent = (MbMove)Mirror.Read(new MirrorReader(w.ToArray()), typeof(MbMove));
            Assert.AreEqual(3, sent.actorId, "행동의 GameAction 필드도 쓰고 읽는다");
            Assert.AreEqual(9L, sent.tick);
            Assert.AreEqual(1, sent.x);
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void GBible_Core를_참조하지_않는_어셈블리의_부모_필드를_잃는_형식도_예전_비교_방식으로_다룬다()
        {
            Assert.IsFalse(Mirror.Supports(typeof(MbUnityEvent), out var reason));
            StringAssert.Contains("UnityEvent", reason);
            StringAssert.Contains("m_PersistentCalls", reason);
        }
#endif

        [Test]
        public void ref_struct_속성과_volatile_필드도_생성_코드가_깨지지_않는다()
        {
            Mirror.Initialize();
            foreach (var method in typeof(MbSpanHolder).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.Name.StartsWith("__gb_", StringComparison.Ordinal)))
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
            Assert.IsNull(RegisterOf(typeof(MbRefStruct)), "ref struct는 생성하지 않는다(상자·제네릭 인자로 쓸 수 없다)");

            var holder = new MbSpanHolder { count = 5, target = new MbUnit { hp = 4 } };
            Assert.IsTrue(FieldAccess.Find(typeof(MbSpanHolder), "count").Generated, "volatile 필드도 생성 코드로 읽는다");
            Assert.AreEqual(5, FieldAccess.Read(holder, "count"));
            Assert.AreEqual(1, FieldAccess.Read(holder, "First"), "ref struct 속성을 빼도 다른 속성은 그대로");
            Assert.IsTrue(Mirror.Supports(typeof(MbSpanHolder), out var reason), reason);
            var copy = Mirror.Clone(holder);
            Assert.AreEqual(5, copy.count);
            Assert.AreEqual(4, copy.target.hp);

            var node = Mirror.Make(holder);
            holder.count = 8;
            var changes = new ChangeSet();
            Mirror.Sync(holder, node, changes);
            Assert.IsFalse(changes.IsEmpty, "volatile 필드의 변경도 기록한다");
            Mirror.Undo(holder, changes);
            Assert.AreEqual(5, holder.count, "되돌린다");
        }

        [Test]
        public void 등록이_실패한_형식이_있어도_게임_시작은_계속되고_이유를_알린다()
        {
            Mirror.Initialize();
            var register = typeof(Mirror).GetMethod("RegisterType", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(register, "형식 하나씩 등록하는 곳(Mirror.Scan)");
            var warnings = new List<string>();
            Action<string> listen = warnings.Add;
            Mirror.Warning += listen;
            MbBrokenRegister.Fail = true;
            try
            {
                Mirror.ResetWarnings();
                Assert.DoesNotThrow(() => register.Invoke(null, new object[] { typeof(MbBrokenRegister) }), "한 형식의 실패가 밖으로 나가지 않는다");
                Assert.IsTrue(warnings.Any(w => w.Contains(nameof(MbBrokenRegister)) && w.Contains("시험용 등록 실패")), string.Join(" / ", warnings));
                Assert.IsNotNull(Mirror.Find(typeof(MbUnit)), "다른 형식은 그대로 등록되어 있다");
            }
            finally
            {
                MbBrokenRegister.Fail = false;
                Mirror.Warning -= listen;
            }
        }

        [Test]
        public void 보낼_수_있는지는_형식마다_먼저_본다()
        {
            Assert.IsTrue(Mirror.CanSend(typeof(MbPayloadAction), out var reason), reason);
            Assert.IsTrue(Mirror.CanSend(typeof(MbPlayCard), out reason), "참조 필드는 id만 가므로 그 형식에 빠지는 칸이 있어도 된다: " + reason);
            Assert.IsTrue(Mirror.CanSendValue(new MbCard { id = "c" }, out reason), "id만 든 객체(참조 필드의 값)도: " + reason);
            Assert.IsTrue(Mirror.CanSend(typeof(MbBagEvent), out reason), "안쪽에 컬렉션이 있는 구조체는 보낼 수 있다: " + reason);
            Assert.IsFalse(Mirror.Supports(typeof(MbBagEvent), out _), "(변경 추적은 못 하므로 데이터로는 예전 비교 방식)");
            Assert.IsFalse(Mirror.CanSend(typeof(MbGridEvent), out reason));
            StringAssert.Contains("MbGridEvent.grid", reason);
            Assert.IsFalse(Mirror.CanSend(typeof(MbJokerAction), out reason), "id가 잃은 부모 쪽에 있으면 참조 필드라도 보낼 수 없다");
            StringAssert.Contains("MbPiece", reason);
            Assert.IsFalse(Mirror.CanSendValue(5, out reason));
            StringAssert.Contains("Int32", reason);
            Assert.IsTrue(Mirror.CanSendValue(new MbUnit(), out reason), reason);
            Assert.IsTrue(Mirror.CanSend(typeof(MbMove), out reason), "필드 없는 중간 부모는 상관없다: " + reason);
            Assert.IsFalse(Mirror.CanSend(typeof(MbAim), out reason), "[Serializable]이 없는 중간 부모의 필드는 쓰이지 않는다");
            StringAssert.Contains("MbAim.target", reason);
            Assert.IsFalse(Mirror.CanSend(typeof(MbIntAction), out reason));
            StringAssert.Contains("제네릭", reason);
        }

        [Test]
        public void 쓸_수_없는_구조체를_품은_구조체는_비교_코드로_다루지_않고_보내기_전에_거절한다()
        {
            Mirror.Initialize();
            Assert.IsFalse(Mirror.Supports(typeof(MbPairBox), out var reason), "KeyValuePair를 품은 구조체");
            StringAssert.Contains("KeyValuePair", reason);
            Assert.IsFalse(Mirror.Supports(typeof(MbPointBox), out reason), "[Serializable]이 없는 구조체를 품은 구조체의 리스트");
            StringAssert.Contains("MbPlainPoint", reason);
            Assert.IsFalse(Mirror.CanSend(typeof(MbSlotAction), out reason), "보내다가 멈추지 않게 미리 거절");
            StringAssert.Contains("KeyValuePair", reason);

            // 예전 비교 방식으로 다루므로 바꿔도 멈추지 않는다.
            var session = new GameSession();
            session.Data.Define("box", DataZone.Session, new MbPairBox());
            Assert.DoesNotThrow(() => session.Do(new MbEdit { run = () => session.Data.Modify<MbPairBox>("box", b => b.slot.count = 2) }));
            Assert.AreEqual(2, session.Data.Get<MbPairBox>("box").slot.count);
        }

        [Test]
        public void 생성_코드가_다_쓰지_못하는_행동은_필드를_밝히며_보내기_전에_실패한다()
        {
            var rig = new Rig();
            Assert.IsTrue(rig.Client.IsJoined);
            void Refused(GameAction action, string field, bool firstOfType = true)
            {
                var error = Assert.Throws<InvalidOperationException>(() => rig.Client.Session.Do(action));
                StringAssert.Contains(field, error.Message);
                if (firstOfType)
                    Assert.IsTrue(rig.Client.Session.Journal.Entries.Any(e => e.Kind == JournalKind.Error && e.Message.Contains(field)), "기록에도 오류: " + field);
            }
            Refused(new MbGridAction(), "grid");                        // 중첩 컬렉션(받는 쪽에 빈 칸)
            Refused(new MbPairAction(), "pair");                        // 쓸 수 없는 구조체(전에는 NullReferenceException)
            Refused(new MbSetAction(), "picked");                       // 생성 코드가 없는 컬렉션(받는 쪽에 null)
            Refused(new MbReadonlyAction(), "amount");                  // readonly 필드(받는 쪽에 기본값)
            Refused(new MbAutoAction { Amount = 2 }, "Amount");         // 자동 속성(받는 쪽에 기본값)
            Refused(new MbPayloadAction { payload = 5 }, "Int32");      // object 칸의 상자에 든 숫자(받는 쪽에 null)
            Refused(new MbPayloadAction { payload = "글자" }, "String", false);
            Assert.AreEqual(0, rig.Client.PendingActions, "아무것도 보내지 않았다");
            rig.Pump();
            Assert.AreEqual(0.0, rig.ServerSession.Data.GetNumber("got"));

            // 생성 코드가 있는 객체를 담은 object 칸은 그대로 간다.
            rig.Client.Session.Do(new MbPayloadAction { payload = new MbUnit { hp = 4 }, amount = 1 });
            rig.Pump();
            Assert.AreEqual(5.0, rig.ServerSession.Data.GetNumber("got"));
        }

        [Test]
        public void 보낼_수_없는_이벤트는_빼고_오류를_남기며_나머지는_보낸다()
        {
            var rig = new Rig();
            var notices = new List<MbNotice>();
            var bags = new List<MbBagEvent>();
            var grids = new List<MbGridEvent>();
            var boxed = new List<MbBoxedEvent>();
            var events = rig.Client.Session.Events;
            events.SubscribeAfterAction<MbNotice>(notices.Add);
            events.SubscribeAfterAction<MbBagEvent>(bags.Add);
            events.SubscribeAfterAction<MbGridEvent>(grids.Add);
            events.SubscribeAfterAction<MbBoxedEvent>(boxed.Add);

            rig.ServerSession.Do(new MbEmit());
            rig.Pump();
            Assert.AreEqual(9.0, rig.Client.Session.Data.GetNumber("gold"), "행동은 그대로 됐다");
            Assert.AreEqual(1, notices.Count, "보낼 수 있는 이벤트는 간다");
            Assert.AreEqual(7, notices[0].n);
            Assert.AreEqual(1, bags.Count, "안쪽에 컬렉션이 있는 구조체도 보낸다(쓰기·읽기는 온전)");
            CollectionAssert.AreEqual(new[] { 4, 5 }, bags[0].bag.items);
            Assert.AreEqual(0, grids.Count, "중첩 컬렉션은 빈 칸으로 보내지 않고 뺀다");
            Assert.AreEqual(0, boxed.Count, "object 칸의 숫자는 null로 보내지 않고 뺀다");
            var errors = rig.ServerSession.Journal.Entries.Where(e => e.Kind == JournalKind.Error).Select(e => e.Message).ToList();
            Assert.IsTrue(errors.Any(m => m.Contains(nameof(MbGridEvent)) && m.Contains("grid")), string.Join(" / ", errors));
            Assert.IsTrue(errors.Any(m => m.Contains(nameof(MbBoxedEvent)) && m.Contains("Int32")), string.Join(" / ", errors));
        }

#if UNITY_EDITOR
        [Test]
        public void link_xml은_게임_어셈블리를_통째로_남기고_빌드_검사가_빠진_것을_알린다()
        {
            var xml = EditorTools.StartSetup.LinkXmlText();
            var document = new System.Xml.XmlDocument();
            document.LoadXml(xml);
            var preserved = document.GetElementsByTagName("assembly").Cast<System.Xml.XmlNode>()
                .Where(n => n.Attributes?["preserve"]?.Value == "all").Select(n => n.Attributes["fullname"].Value).ToList();
            CollectionAssert.Contains(preserved, "MomoiProject.Rules");
            CollectionAssert.Contains(preserved, "MomoiProject.Client");
            CollectionAssert.Contains(preserved, "Assembly-CSharp");

            Assert.IsFalse(EditorTools.LinkXmlBuildGuard.StripsGameCode(UnityEditor.ManagedStrippingLevel.Disabled));
            Assert.IsFalse(EditorTools.LinkXmlBuildGuard.StripsGameCode(UnityEditor.ManagedStrippingLevel.Minimal));
            Assert.IsTrue(EditorTools.LinkXmlBuildGuard.StripsGameCode(UnityEditor.ManagedStrippingLevel.Low));
            Assert.IsTrue(EditorTools.LinkXmlBuildGuard.StripsGameCode(UnityEditor.ManagedStrippingLevel.Medium));
            Assert.IsTrue(EditorTools.LinkXmlBuildGuard.StripsGameCode(UnityEditor.ManagedStrippingLevel.High));

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "momoi-link-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                StringAssert.Contains("없어", EditorTools.LinkXmlBuildGuard.Check(path, new[] { "MomoiProject.Rules" }));
                System.IO.File.WriteAllText(path, xml);
                Assert.IsNull(EditorTools.LinkXmlBuildGuard.Check(path, new[] { "MomoiProject.Rules", "MomoiProject.Client" }));
                StringAssert.Contains("MomoiProject.Server", EditorTools.LinkXmlBuildGuard.Check(path, new[] { "MomoiProject.Rules", "MomoiProject.Server" }));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
#endif
    }
}
