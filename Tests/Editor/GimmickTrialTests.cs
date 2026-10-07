using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 4게임 기믹 시험(발라트로·와일드프로스트·매지크래프트·세피리아·장르 섞기)에서 나온 빈틈을 메운 부품:
    // 패시브 발동 여러 줄·일치 필드 목록·원본에서 읽기·레벨 0 꺼짐·침묵·보정 조건·줄마다 받을 대상·여럿일 때·주기,
    // 효과 배율, 상태 효과 걸기 의도의 최대 중첩·주기·건 쪽, 열 방향·기준 없음·플레이어마다 칸, 값 틀 확장, 이벤트 쏘기 틀, TryPay·비용 대상,
    // 숫자 참조가 보정을 읽음, 사용자 필드 검사, 빌려 쓰기와 가진 쪽 필드.
    public class GimmickTrialTests
    {
        [Serializable] public class TrPiece { public string id; public string card; public List<string> traits = new List<string>(); public int level = 1; public int mult; public int counter; }
        [Serializable] public class TrUnit { public string id; public int hp = 20; public int attack = 2; public int options = 3; }
        [Serializable] public class TrPlayer { public string id; public List<TrPiece> charms = new List<TrPiece>(); }
        [Serializable] public class TrCard { public string name = ""; public List<string> passives = new List<string>(); }
        [Serializable]
        public class TrRun
        {
            public List<TrPiece> pieces = new List<TrPiece>();
            public List<TrPiece> board = new List<TrPiece>();
            public List<TrPlayer> players = new List<TrPlayer>();
            public List<TrUnit> party = new List<TrUnit> { new TrUnit { id = "ally1" }, new TrUnit { id = "ally2" } };
            public TrUnit hero = new TrUnit { id = "hero" };
            public Dictionary<string, int> relics = new Dictionary<string, int>();
            public List<string> log = new List<string>();
            public int score;
            public int gold = 10;
            public int mana = 5;
        }
        [Serializable, GameEvent("점수")] public class TrScored { public string pieceId; public int amount = 1; }
        [Serializable, GameEvent("버림")] public class TrDiscarded { }
        [Serializable, GameEvent("기록")] public class TrNoted { public string text; public int number; }

        [Serializable]
        public sealed class Log : Outcome
        {
            public string text = "";
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context)
            {
                var who = context.User == null ? "-" : GameApi.HasId(context.User.GetType()) ? GameApi.IdOf(context.User) : "run";
                context.Game.Get<TrRun>("run").log.Add($"{text}:{who}:{context.Power:0.##}");
            }
            public override string Describe(INameResolver names) => text;
        }

        [Serializable]
        public sealed class AddScore : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<TrRun>("run").score += ((TrPiece)context.User).mult;
            public override string Describe(INameResolver names) => "자기 배수 더하기";
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;
        GameApi Game => _session.Game;
        TrRun Run => _session.Data.Get<TrRun>("run");

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 5 });
            _session.Data.Define("run", DataZone.Session, new TrRun(), "판");
        }

        void Do(Action<GameApi> run) => Game.Do(new Edit { run = run });
        void Publish(object evt) => Do(g => g.Session.Events.PublishObject(evt));

        static TargetedOutcome Self(Effect effect) => new TargetedOutcome { targets = { new SelfTarget() }, effects = { effect } };

        PassiveDefinition Piece(string id, Trigger when, params Outcome[] outcomes)
        {
            var d = new PassiveDefinition { Id = id, OwnerKey = "run", OwnerPath = "pieces", MatchField = "card", PerElement = true, LevelField = "level", When = when };
            d.Outcomes.AddRange(outcomes);
            return d;
        }

        static EventTrigger On<T>() => new EventTrigger { eventType = typeof(T).FullName };

        [Test]
        public void 패시브_하나가_순간마다_다른_결과를_낸다_Green_Joker()
        {
            var green = Piece("green", On<TrScored>(), Self(new ChangeFieldEffect { field = "mult", value = "1" }), new AddScore());
            var discard = new PassiveRule { When = On<TrDiscarded>() };
            discard.Outcomes.Add(Self(new ChangeFieldEffect { field = "mult", value = "-1", clampZero = true }));
            green.Rules.Add(discard);
            _session.Passives.Define(green);
            Do(g => g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "p1", card = "green" }));
            Publish(new TrScored());
            Publish(new TrScored());
            Publish(new TrDiscarded());
            Assert.AreEqual(1, Run.pieces[0].mult, "+1 +1 −1");
            Assert.AreEqual(1 + 2, Run.score);
        }

        [Test]
        public void 일치_필드가_글자_목록이거나_원본에서_읽으면_조각_하나에_패시브가_여럿이다()
        {
            var cards = new Dictionary<string, object> { ["card.foxee"] = new TrCard { name = "Foxee", passives = { "wild", "teeth" } } };
            _session.Instances = id => cards.TryGetValue(id, out var v) ? v : null;
            foreach (var id in new[] { "wild", "teeth", "spark" })
            {
                var viaCard = new PassiveDefinition { Id = id, OwnerKey = "run", OwnerPath = "pieces", MatchField = "card", MatchVia = "passives", PerElement = true, When = On<TrScored>() };
                viaCard.Outcomes.Add(new Log { text = id });
                _session.Passives.Define(viaCard);
            }
            var charm = new PassiveDefinition { Id = "lamb", OwnerKey = "run", OwnerPath = "pieces", MatchField = "traits", PerElement = true, When = On<TrScored>() };
            charm.Outcomes.Add(new Log { text = "lamb" });
            _session.Passives.Define(charm);
            Do(g => g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "u1", card = "card.foxee", traits = { "lamb" } }));
            Publish(new TrScored());
            CollectionAssert.AreEquivalent(new[] { "wild:u1:1", "teeth:u1:1", "lamb:u1:1" }, Run.log, "원본의 패시브 둘 + 조각에 붙은 참 하나, spark는 없음");
        }

        [Test]
        public void 레벨_0과_침묵이면_발동도_보정도_없다()
        {
            _session.Statuses.Define(new StatusDefinition { Id = "ink", Silences = true });
            var amp = Piece("amp", On<TrScored>(), new Log { text = "amp" });
            amp.ModifierTarget = "hero";
            amp.Modifiers.Add(new PassiveModifier { attribute = "attack", value = 3 });
            _session.Passives.Define(amp);
            Do(g => g.Get<TrRun>("run").pieces.AddRange(new[] { new TrPiece { id = "a", card = "amp", level = 0 }, new TrPiece { id = "b", card = "amp", level = 2 } }));
            Publish(new TrScored());
            CollectionAssert.AreEqual(new[] { "amp:b:1" }, Run.log, "레벨 0인 a는 꺼짐");
            Assert.AreEqual(2 + 3, Game.Attribute(Run.hero, "attack"));
            Do(g => g.ApplyStatus("b", "ink"));
            Publish(new TrScored());
            Assert.AreEqual(1, Run.log.Count, "침묵한 b도 발동하지 않는다");
            Assert.AreEqual(2, Game.Attribute(Run.hero, "attack"), "보정도 빠진다");
            Assert.AreEqual(0, Game.Owned("amp"));
        }

        [Test]
        public void 보정_조건과_줄마다_받을_대상과_여럿일_때()
        {
            // Fury: 같은 줄에 혼자면 그 조각의 attack +3(줄 길이 3).
            var fury = new PassiveDefinition
            {
                Id = "fury", OwnerKey = "run", OwnerPath = "board", MatchField = "card", PerElement = true,
                ModifierWhen = new ConditionTrigger { condition = new PlaceCondition { list = new PlayerDataListRef { key = "run", path = "board" }, columns = 3, spot = PlaceSpot.RowAlone } },
            };
            fury.Modifiers.Add(new PassiveModifier { attribute = "mult", value = 3 });
            _session.Passives.Define(fury);
            Do(g => g.Get<TrRun>("run").board.AddRange(new[] { new TrPiece { id = "f1", card = "fury" }, null, null, new TrPiece { id = "f2", card = "fury" }, new TrPiece { id = "x", card = "none" }, null }));
            Assert.AreEqual(3, Game.Attribute("f1", "mult"), "첫 줄에 혼자");
            Assert.AreEqual(0, Game.Attribute("f2", "mult"), "둘째 줄에 x가 있다");

            // 같은 유물 둘(레벨 2·3, 레벨별 값 3/6/10): 레벨 합(5 → 10) · 값 합(6 + 10) · 가장 높은 하나(10). 줄마다 받을 대상.
            foreach (var (id, stacking) in new[] { ("sum", PassiveStacking.SumLevels), ("each", PassiveStacking.SumValues), ("top", PassiveStacking.Highest) })
            {
                var relic = new PassiveDefinition { Id = id, OwnerKey = "run", OwnerPath = "pieces", MatchField = "card", PerElement = true, LevelField = "level", Stacking = stacking };
                relic.Modifiers.Add(new PassiveModifier { attribute = id == "sum" ? "attack" : id == "each" ? "hp" : "options", byLevel = { 3, 6, 10 }, target = "hero" });
                _session.Passives.Define(relic);
                Do(g => g.Get<TrRun>("run").pieces.AddRange(new[] { new TrPiece { id = id + "1", card = id, level = 2 }, new TrPiece { id = id + "2", card = id, level = 3 } }));
            }
            Assert.AreEqual(2 + 10, Game.Attribute(Run.hero, "attack"), "레벨 5 → 마지막 값 10");
            Assert.AreEqual(20 + 6 + 10, Game.Attribute(Run.hero, "hp"), "사본마다 값");
            Assert.AreEqual(3 + 10, Game.Attribute(Run.hero, "options"), "가장 높은 레벨");

            // 모든 동료: 받을 대상 경로에 [] → 그 모두가 받는다.
            var banner = new PassiveDefinition { Id = "banner", OwnerKey = "run", OwnerPath = "relics", ModifierTarget = "party[]" };
            banner.Modifiers.Add(new PassiveModifier { attribute = "attack", value = 2 });
            _session.Passives.Define(banner);
            Do(g => g.Get<TrRun>("run").relics["banner"] = 1);
            Assert.AreEqual(4, Game.Attribute("ally1", "attack"));
            Assert.AreEqual(4, Game.Attribute("ally2", "attack"));
            Assert.AreEqual(2 + 10, Game.Attribute(Run.hero, "attack"), "주인공은 그대로");
        }

        [Test]
        public void 주기_패시브는_N초마다_가진_쪽마다_발동한다()
        {
            var ember = Piece("ember", null, new Log { text = "ember" });
            ember.PulseByLevel.AddRange(new[] { 2.0, 1.0 });
            _session.Passives.Define(ember);
            Do(g => g.Get<TrRun>("run").pieces.AddRange(new[] { new TrPiece { id = "e1", card = "ember", level = 1 }, new TrPiece { id = "e2", card = "ember", level = 2 } }));
            _session.EndFrame(0.1);   // 기준 시각
            for (int i = 0; i < 8; i++) _session.EndFrame(0.5);
            Assert.AreEqual(2, Run.log.Count(l => l.StartsWith("ember:e1")), "레벨 1: 2초마다");
            Assert.AreEqual(4, Run.log.Count(l => l.StartsWith("ember:e2")), "레벨 2: 1초마다");
        }

        [Test]
        public void 주기_패시브는_판을_불러오거나_시계를_되돌려도_몰아서_내거나_건너뛰지_않는다()
        {
            var ember = Piece("ember", null, new Log { text = "ember" });
            ember.Pulse = 1;
            _session.Passives.Define(ember);
            Do(g => g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "e1", card = "ember" }));
            _session.EndFrame(0.25);                       // 기준 시각
            _session.EndFrame(1);                          // 1.25: 경계 1
            Assert.AreEqual(1, Run.log.Count);
            Do(g => g.Get<TrRun>("run").score++);          // 1.25초의 행동
            _session.EndFrame(1);                          // 2.25: 경계 2(행동 '패시브 주기')
            Assert.AreEqual(2, Run.log.Count);

            // 주기(시간 규칙)와 앞의 행동을 함께 되돌리고 시계도 1.25로 — 시간이 다시 흐르면 경계 2에서 다시 낸다(건너뛰지 않는다).
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(1.25, _session.Clock.Time, 1e-9);
            Assert.AreEqual(1, Run.log.Count);
            _session.EndFrame(1);
            Assert.AreEqual(2, Run.log.Count, "되돌린 경계를 다시 지나면 다시 낸다");

            // 게임 시각이 훨씬 앞선 판을 불러오면 그 사이의 주기를 몰아서 내지 않는다(그 판은 그 시각까지 이미 냈다).
            var other = new GameSession(new SessionOptions { Seed = 5 });
            other.Data.Define("run", DataZone.Session, new TrRun { pieces = { new TrPiece { id = "e1", card = "ember" } } }, "판");
            other.EndFrame(100);
            _session.ImportRun(other.ExportRun());
            _session.EndFrame(0.5);
            Assert.AreEqual(0, Run.log.Count, "100 → 100.5: 경계가 없다(밀린 8번을 몰아서 내지 않는다)");
            _session.EndFrame(0.75);
            Assert.AreEqual(1, Run.log.Count, "101을 넘으면 한 번");
        }

        // 결과: 가진 쪽이 p1이면 p2를 판에서 뺀다(같은 순간에 뒤 가진 쪽이 사라지는 경우).
        [Serializable]
        public sealed class RemoveP2 : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context)
            {
                if (GameApi.IdOf(context.User) == "p1") context.Game.Get<TrRun>("run").players.RemoveAll(p => p.id == "p2");
            }
            public override string Describe(INameResolver names) => "p2 빼기";
        }

        [Test]
        public void 앞_가진_쪽의_결과가_없앤_가진_쪽의_패시브는_발동하지_않는다()
        {
            // 가진 쪽 = 칸(charms)을 가진 플레이어마다. p1의 결과가 p2를 판에서 뺀다.
            var oath = new PassiveDefinition { Id = "oath", OwnerKey = "run", OwnerPath = "players[].charms", MatchField = "card", When = On<TrScored>() };
            oath.Outcomes.Add(new RemoveP2());
            oath.Outcomes.Add(new Log { text = "oath" });
            _session.Passives.Define(oath);
            Do(g => g.Get<TrRun>("run").players.AddRange(new[]
            {
                new TrPlayer { id = "p1", charms = { new TrPiece { id = "c1", card = "oath" } } },
                new TrPlayer { id = "p2", charms = { new TrPiece { id = "c2", card = "oath" } } },
            }));
            Publish(new TrScored());
            CollectionAssert.AreEqual(new[] { "oath:p1:1" }, Run.log, "p1이 뺀 p2는 그 순간에 발동하지 않는다");
        }

        [Test]
        public void 효과_배율은_능력과_패시브의_수치를_곱한다_Lumin()
        {
            _session.Statuses.Define(new StatusDefinition { Id = "poison", MaxStacks = 0 });
            // p1이 가진 수치만 2배: 효과 배율 의도를 사용자 id로 거른다.
            var doubler = new PassiveDefinition { Id = "luminDoubler", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(PowerIntent).FullName, filter = new FieldFilter { path = "userId", op = FieldOp.Equal, value = "p1" } } };
            doubler.Outcomes.Add(new ModifyIntentOutcome { path = "multiplier", op = IntentOp.Multiply, value = "2" });
            _session.Passives.Define(doubler);
            var venom = Piece("venom", On<TrScored>(), new TargetedOutcome { targets = { new SelfTarget() }, effects = { new ApplyStatusEffect2 { status = "poison", stacks = 3 } } }, new Log { text = "v" });
            _session.Passives.Define(venom);
            Do(g => { var r = g.Get<TrRun>("run"); r.relics["luminDoubler"] = 1; r.pieces.AddRange(new[] { new TrPiece { id = "p1", card = "venom" }, new TrPiece { id = "p2", card = "venom" } }); });
            Publish(new TrScored());
            Assert.AreEqual(6, Game.StatusStacks("p1", "poison"), "p1은 수치 2배");
            Assert.AreEqual(3, Game.StatusStacks("p2", "poison"));
            CollectionAssert.AreEqual(new[] { "v:p1:2", "v:p2:1" }, Run.log);
        }

        // Core 테스트에는 Unity 쪽 '상태 효과 걸기' Block이 없어 같은 동작(중첩 × 배율, 건 개체 = 사용자)을 여기서.
        [Serializable]
        public sealed class ApplyStatusEffect2 : Effect
        {
            public string status;
            public int stacks = 1;
            protected override void Apply(object target) =>
                Game.ApplyStatus(target, status, Math.Max(1, (int)Math.Round(stacks * (Use?.Context?.Power ?? 1))), Use?.Context?.Owner, null, Use?.User);
        }

        [Test]
        public void 상태_효과_걸기_의도로_최대_중첩과_주기를_바꾸고_건_쪽을_남긴다()
        {
            _session.Statuses.Define(new StatusDefinition { Id = "burn", MaxStacks = 3, Pulse = 1, Kind = StatusKind.Debuff });
            var magma = new PassiveDefinition { Id = "magma", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName } };
            magma.Outcomes.Add(new ModifyIntentOutcome { path = "maxStacks", op = IntentOp.Add, value = "2" });
            magma.Outcomes.Add(new ModifyIntentOutcome { path = "pulse", op = IntentOp.Multiply, value = "0.5" });
            _session.Passives.Define(magma);
            Do(g => { g.Get<TrRun>("run").relics["magma"] = 1; g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "enemy" }); });
            Do(g => g.ApplyStatus("enemy", "burn", 4, by: "hero"));
            var active = Game.Statuses("enemy").Single();
            Assert.AreEqual(4, active.stacks, "최대 중첩 3 + 2");
            Assert.AreEqual(0.5, active.pulse, "주기 1초 → 0.5초");
            Assert.AreEqual("hero", active.by);
            Do(g => g.ApplyStatus("enemy", "burn", 9, by: "p9"));
            Assert.AreEqual(5, Game.Statuses("enemy").Single().stacks);
            Assert.AreEqual("p9", Game.Statuses("enemy").Single().by, "다시 걸면 마지막에 건 쪽");
            Do(g => g.Get<TrRun>("run").relics.Remove("magma"));
            Do(g => g.ApplyStatus("enemy", "burn", 1));
            Assert.AreEqual(0, Game.Statuses("enemy").Single().pulse, "구슬을 잃고 다시 걸면 정의의 주기로(0 = 정의의 것)");

            // 주기가 없던 상태 효과에 다시 걸 때 주기가 생기면 지금부터 센다.
            _session.Statuses.Define(new StatusDefinition { Id = "mark" });
            Do(g => g.ApplyStatus("enemy", "mark"));
            Assert.AreEqual(0, Game.Statuses("enemy").Single(s => s.status == "mark").nextPulse);
            var quick = new PassiveDefinition { Id = "quick", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName, filter = new FieldFilter { path = "statusId", op = FieldOp.Equal, value = "mark" } } };
            quick.Outcomes.Add(new ModifyIntentOutcome { path = "pulse", op = IntentOp.Set, value = "2" });
            _session.Passives.Define(quick);
            Do(g => g.Get<TrRun>("run").relics["quick"] = 1);
            Do(g => g.ApplyStatus("enemy", "mark"));
            var mark = Game.Statuses("enemy").Single(s => s.status == "mark");
            Assert.AreEqual(2, mark.pulse);
            Assert.IsTrue(mark.nextPulse > 0, "주기가 생겼다");
        }

        [Test]
        public void 열_방향과_기준_없음과_플레이어마다_칸()
        {
            // 열 우선 보드(줄 길이 2: 0·1이 맨 앞 열의 위·아래). 같은 열 맨 앞 = 열에서 차 있는 첫 칸.
            var board = new List<string> { null, "a", "b", "c" };
            CollectionAssert.AreEqual(new[] { 1 }, Placement.Indices(3, 4, 2, PlaceDirection.ColumnFirst, 1, board).ToArray(), "x = 1 열: 1(a)·3(c) 중 처음");
            CollectionAssert.AreEqual(new[] { 3 }, Placement.Indices(-1, 4, 2, PlaceDirection.Last, 1, board).ToArray(), "기준 없이 목록 맨 끝");
            // 열 우선으로 담은 보드(와일드프로스트: 앞 칸 위·아래 → 다음 칸): 원작의 '같은 줄' = 여기의 '같은 열'.
            var lanes = new List<string> { "u1", null, "u2", "u3", null, null };
            Assert.IsTrue(Placement.Is(lanes, 0, 2, PlaceSpot.ColumnFirst), "위 줄(x = 0)의 맨 앞");
            Assert.IsFalse(Placement.Is(lanes, 2, 2, PlaceSpot.ColumnFirst));
            Assert.IsTrue(Placement.Is(lanes, 3, 2, PlaceSpot.ColumnAlone), "아래 줄(x = 1)에 u3 혼자");
            Assert.IsFalse(Placement.Is(lanes, 0, 2, PlaceSpot.ColumnAlone), "위 줄에 u1·u2");

            Do(g =>
            {
                var r = g.Get<TrRun>("run");
                r.players.Add(new TrPlayer { id = "P1", charms = { new TrPiece { id = "c1" }, new TrPiece { id = "c2" } } });
                r.players.Add(new TrPlayer { id = "P2", charms = { new TrPiece { id = "d1" }, new TrPiece { id = "d2" } } });
            });
            var right = new PlaceTarget { list = new PlayerDataListRef { key = "run", path = "players[].charms" }, direction = PlaceDirection.Right };
            var mark = new AbilityDefinition { Id = "mark" };
            mark.Outcomes.Add(new TargetedOutcome { targets = { right }, effects = { new BumpMult() } });
            _session.Abilities.Define(mark);
            Game.Use("mark", "d1");
            Assert.AreEqual(1, Run.players[1].charms[1].mult, "2P 줄에서 d1의 오른쪽 = d2");
            Assert.AreEqual(0, Run.players[0].charms[1].mult);

            var last = new PlaceTarget { origin = PlaceOrigin.None, list = new PlayerDataListRef { key = "run", path = "pieces" }, direction = PlaceDirection.Last };
            Do(g => g.Get<TrRun>("run").pieces.AddRange(new[] { new TrPiece { id = "h1" }, new TrPiece { id = "h2" } }));
            var critical = new AbilityDefinition { Id = "critical" };
            critical.Outcomes.Add(new TargetedOutcome { targets = { last }, effects = { new BumpMult() } });
            _session.Abilities.Define(critical);
            Game.Use("critical", "hero");
            Assert.AreEqual(1, Run.pieces[1].mult, "손패 맨 오른쪽");
        }

        [Serializable]
        public sealed class BumpMult : Effect<TrPiece>
        {
            protected override void Apply(TrPiece target) => target.mult++;
        }

        [Test]
        public void 값_틀이_사용자_이벤트_데이터_개수를_읽고_이벤트_쏘기에도_쓰인다()
        {
            Do(g => g.Get<TrRun>("run").pieces.AddRange(new[] { new TrPiece { id = "q1", mult = 7 }, new TrPiece { id = "q2" }, null }));
            var context = new TriggerContext { Session = _session, User = Run.pieces[0], Event = new TrScored { amount = 4 } };
            Assert.AreEqual("7", ModifyIntentOutcome.Expand("{user:mult}", context));
            Assert.AreEqual("4", ModifyIntentOutcome.Expand("{event:amount}", context));
            Assert.AreEqual("10", ModifyIntentOutcome.Expand("{data:run.gold}", context));
            Assert.AreEqual("2", ModifyIntentOutcome.Expand("{count:run.pieces}", context), "빈 칸은 세지 않는다");

            var noted = new List<TrNoted>();
            _session.Events.Subscribe<TrNoted>(e => noted.Add(e));
            var publish = new PublishEventOutcome { eventType = typeof(TrNoted).FullName, values = { new EventFieldValue { path = "text", value = "{user}" }, new EventFieldValue { path = "number", value = "{count:run.pieces}" } } };
            var shout = Piece("shout", On<TrScored>(), publish);
            shout.LevelField = "";
            _session.Passives.Define(shout);
            Do(g => g.Get<TrRun>("run").pieces[0].card = "shout");
            Publish(new TrScored());
            Assert.AreEqual("q1", noted.Single().text);
            Assert.AreEqual(2, noted.Single().number);
        }

        [Test]
        public void TryPay는_모자라면_거짓이고_비용_의도에_무엇을_사는지가_있다()
        {
            bool paid = true;
            Do(g => paid = g.TryPay("run", "mana", 8));
            Assert.IsFalse(paid);
            Assert.AreEqual(5, Run.mana, "바뀌지 않음");
            var planetFree = new PassiveDefinition { Id = "astronomer", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(CostIntent).FullName, filter = new FieldFilter { path = "forId", op = FieldOp.Equal, value = "planet" } } };
            planetFree.Outcomes.Add(new ModifyIntentOutcome { op = IntentOp.Cancel });
            _session.Passives.Define(planetFree);
            Do(g => g.Get<TrRun>("run").relics["astronomer"] = 1);
            Do(g => g.Pay("run", "gold", 6, forId: "planet"));
            Assert.AreEqual(10, Run.gold, "행성은 공짜");
            Do(g => g.Pay("run", "gold", 6, forId: "joker"));
            Assert.AreEqual(4, Run.gold);
        }

        [Test]
        public void 숫자_참조는_보정을_읽고_사용자_필드_검사는_조각의_값을_본다()
        {
            var bunny = new PassiveDefinition { Id = "bunny", OwnerKey = "run", OwnerPath = "relics", ModifierTarget = "hero" };
            bunny.Modifiers.Add(new PassiveModifier { attribute = "options", value = 1 });
            _session.Passives.Define(bunny);
            Do(g => g.Get<TrRun>("run").relics["bunny"] = 1);
            Assert.AreEqual(4, new PlayerDataNumberRef { key = "run", path = "hero.options" }.Read(_session), "3 + 유물 1");

            var loyalty = Piece("loyalty", On<TrScored>(), Self(new ChangeFieldEffect { field = "counter", value = "1" }));
            var payout = new PassiveRule { When = new AllTrigger { triggers = { On<TrScored>(), new ConditionTrigger { condition = new UserFieldCondition { path = "counter", op = FieldOp.AtLeast, value = "3" } } } } };
            payout.Outcomes.Add(new Log { text = "x4" });
            payout.Outcomes.Add(Self(new ChangeFieldEffect { field = "counter", change = FieldChange.Set, value = "0" }));
            loyalty.Rules.Add(payout);
            _session.Passives.Define(loyalty);
            Do(g => g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "l1", card = "loyalty" }));
            for (int i = 0; i < 6; i++) Publish(new TrScored());
            Assert.AreEqual(2, Run.log.Count(l => l.StartsWith("x4")), "3번째·6번째");
        }

        [Test]
        public void 패시브_트리거의_조건은_검사하는_순간에도_패시브_ID와_이_조각의_레벨을_본다()
        {
            // 값 틀 {owner}·{level}은 결과뿐 아니라 트리거를 검사하는 순간에도 그 패시브(등록 키 'passive:…'가 아니라)와 가진 조각의 레벨.
            var named = Piece("named", new AllTrigger { triggers = { On<TrScored>(), new ConditionTrigger { condition = new UserFieldCondition { path = "card", op = FieldOp.Equal, value = "{owner}" } } } }, new Log { text = "owner" });
            var leveled = Piece("leveled", new AllTrigger { triggers = { On<TrScored>(), new ConditionTrigger { condition = new UserFieldCondition { path = "counter", op = FieldOp.Equal, value = "{level}" } } } }, new Log { text = "level" });
            _session.Passives.Define(named);
            _session.Passives.Define(leveled);
            Do(g =>
            {
                g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "n1", card = "named" });
                g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "l1", card = "leveled", level = 3, counter = 3 });
            });
            Publish(new TrScored());
            Assert.AreEqual(1, Run.log.Count(l => l.StartsWith("owner")), "{owner} = 패시브 ID");
            Assert.AreEqual(1, Run.log.Count(l => l.StartsWith("level")), "{level} = 이 조각의 레벨 3");
        }

        [Test]
        public void 상태_효과_트리거의_조건은_검사하는_순간에도_그_상태_효과의_중첩을_본다()
        {
            // {stacks}는 트리거를 검사하는 순간에도 이 상태 효과(등록 키 'status:…'가 아니라)가 가진 쪽에 걸린 중첩.
            var focus = new StatusDefinition { Id = "focus", MaxStacks = 0, When = new AllTrigger { triggers = { On<TrScored>(), new ConditionTrigger { condition = new UserFieldCondition { path = "counter", op = FieldOp.Equal, value = "{stacks}" } } } } };
            focus.Outcomes.Add(new Log { text = "focus" });
            _session.Statuses.Define(focus);
            Do(g => g.Get<TrRun>("run").pieces.Add(new TrPiece { id = "s1", card = "none", counter = 2 }));
            Do(g => g.ApplyStatus("s1", "focus", 2));
            Publish(new TrScored());
            Assert.AreEqual(1, Run.log.Count(l => l.StartsWith("focus")), "{stacks} = 2");
        }

        [Test]
        public void 빌려_쓰기는_가진_쪽_필드를_빌리는_쪽으로_본다()
        {
            // 메인 룰이 조커마다 차례 이벤트(pieceId)를 쏘고, 패시브는 가진 쪽 필드 pieceId로 "내 차례"만 받는다.
            var plus = Piece("plus", On<TrScored>(), new Log { text = "plus" });
            plus.HolderField = "pieceId";
            var blueprint = Piece("blueprint", On<TrScored>(), new BorrowOutcome { targets = { new PlaceTarget { list = new PlayerDataListRef { key = "run", path = "pieces" }, direction = PlaceDirection.Right } } });
            blueprint.HolderField = "pieceId";
            _session.Passives.Define(plus);
            _session.Passives.Define(blueprint);
            Do(g => g.Get<TrRun>("run").pieces.AddRange(new[] { new TrPiece { id = "j1", card = "blueprint" }, new TrPiece { id = "j2", card = "plus" } }));
            Publish(new TrScored { pieceId = "j1" });
            CollectionAssert.AreEqual(new[] { "plus:j2:1" }, Run.log, "Blueprint 차례에 오른쪽 plus를 빌림(사용자 = 원본)");
            Publish(new TrScored { pieceId = "j2" });
            Assert.AreEqual(2, Run.log.Count);
        }
    }
}
