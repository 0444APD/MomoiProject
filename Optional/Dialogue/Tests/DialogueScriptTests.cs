using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    public sealed class DialogueScriptTests
    {
        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        T Make<T>(T o) where T : Object
        {
            _made.Add(o);
            return o;
        }

        LocationData Location(string id, Sprite sprite = null)
        {
            var location = Make(ScriptableObject.CreateInstance<LocationData>());
            location.EditorAssignId(id);
            location.background = sprite;
            return location;
        }

        [Test]
        public void 대본은_재생_순서대로_장소와_등장인물을_함께_펼친다()
        {
            var d = Make(ScriptableObject.CreateInstance<DialogueData>());
            d.EditorAssignId("script");
            var g = d.graph;
            var start = new StartNode();
            var castle = new BackgroundEffect { location = "castle" };
            var a = new LineNode { speaker = "arin", text = "왔구나" };
            var enter = new StandingEnterEffect();
            var ask = new LineNode { speaker = "arin", text = "어디로 갈래?", options = { new ChoiceOption { text = "숲" }, new ChoiceOption { text = "성에 남기" } } };
            var forest = new BackgroundEffect { location = "forest" };
            var b = new LineNode { speakerName = "???", text = "숲이다" };
            var c = new LineNode { thought = true, speaker = "arin", text = "(남았다)" };
            foreach (var node in new GraphNode[] { start, castle, a, enter, ask, forest, b, c }) g.Add(node);
            g.Connect(start.guid, StartNode.StartedPort, castle.guid, EffectNode.PlayPort);
            g.Connect(start.guid, StartNode.NextPort, a.guid, GraphNode.FlowInPort);
            g.Connect(a.guid, LineNode.EnterPort, enter.guid, EffectNode.PlayPort);
            g.Connect(a.guid, LineNode.NextPort, ask.guid, GraphNode.FlowInPort);
            g.Connect(ask.guid, LineNode.OptionPrefix + ask.options[0].id, b.guid, GraphNode.FlowInPort);
            g.Connect(ask.guid, LineNode.OptionPrefix + ask.options[1].id, c.guid, GraphNode.FlowInPort);
            g.Connect(b.guid, LineNode.EnterPort, forest.guid, EffectNode.PlayPort);

            var rows = DialogueScript.Walk(d, _ => null, _ => null);
            CollectionAssert.AreEqual(new[] { "왔구나", "어디로 갈래?", "숲", "성에 남기", "숲이다", "(남았다)" }, rows.Select(r => r.Source).ToArray());
            Assert.AreEqual("castle", rows[0].Location);
            CollectionAssert.AreEqual(new[] { "arin" }, rows[0].Present.ToArray(), "등장한 뒤의 대사");
            Assert.IsTrue(rows[2].IsChoice);
            Assert.AreEqual("forest", rows[4].Location, "숲 가지");
            Assert.AreEqual("castle", rows[5].Location, "다른 가지는 영향 없음");
            Assert.AreEqual(rows[2].Option.lineId, rows[2].Key);
        }

        [Test]
        public void 장소를_고르면_그_배경이_들어가고_예외_이미지가_우선한다()
        {
            var texture = Make(new Texture2D(2, 2));
            var hall = Make(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            var special = Make(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            var castle = Location("castle", hall);
            var context = new EffectContext { Locations = id => id == "castle" ? castle : null };
            var state = new StageState();
            new BackgroundEffect { location = "castle" }.ApplyTo(state, context);
            Assert.AreEqual("castle", state.location);
            Assert.AreEqual(hall, state.background);
            new BackgroundEffect { location = "castle", sprite = special }.ApplyTo(state, context);
            Assert.AreEqual(special, state.background);
        }

        [Test]
        public void 화자가_나와_있으면_화자만_밝고_아니면_모두_원래대로()
        {
            var d = Make(ScriptableObject.CreateInstance<DialogueData>());
            d.EditorAssignId("highlight");
            var g = d.graph;
            var start = new StartNode();
            var enterA = new StandingEnterEffect { character = "arin", duration = 0 };
            var enterB = new StandingEnterEffect { character = "bob", duration = 0 };
            var a = new LineNode { speaker = "arin", text = "나야" };
            var n = new LineNode { text = "해설" };
            var keep = new LineNode { speaker = "bob", text = "그대로", highlight = SpeakerHighlight.Keep };
            foreach (var node in new GraphNode[] { start, enterA, enterB, a, n, keep }) g.Add(node);
            g.Connect(start.guid, StartNode.StartedPort, enterA.guid, EffectNode.PlayPort);
            g.Connect(start.guid, StartNode.StartedPort, enterB.guid, EffectNode.PlayPort);
            g.Connect(start.guid, StartNode.NextPort, a.guid, GraphNode.FlowInPort);
            g.Connect(a.guid, LineNode.NextPort, n.guid, GraphNode.FlowInPort);
            g.Connect(n.guid, LineNode.NextPort, keep.guid, GraphNode.FlowInPort);

            var runner = new DialogueRunner(_ => d, _ => null) { ClickCooldown = 0 };
            runner.Start(d);
            runner.FastForward();
            Assert.IsTrue(runner.State.Standing("arin").highlighted);
            Assert.IsFalse(runner.State.Standing("bob").highlighted, "화자가 아닌 캐릭터는 어둡게");
            runner.Advance();
            runner.FastForward();
            Assert.AreEqual(n, runner.Line);
            Assert.IsTrue(runner.State.standings.All(s => s.highlighted), "해설이면 모두 원래대로");
            runner.Advance();
            runner.FastForward();
            Assert.IsTrue(runner.State.standings.All(s => s.highlighted), "'그대로'는 건드리지 않는다");
        }

        [Test]
        public void 로그는_말하는_이_얼굴_대사와_내_선택을_남긴다()
        {
            var texture = Make(new Texture2D(2, 2));
            var smile = Make(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            var icon = Make(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            var arin = Make(ScriptableObject.CreateInstance<StandingProfileData>());
            arin.EditorAssignId("arin");
            arin.expressions[0].id = "smile";
            arin.expressions[0].sprite = smile;
            var bob = Make(ScriptableObject.CreateInstance<StandingProfileData>());
            bob.EditorAssignId("bob");
            bob.logIcon = icon;

            var d = Make(ScriptableObject.CreateInstance<DialogueData>());
            d.EditorAssignId("log");
            var g = d.graph;
            var start = new StartNode();
            var a = new LineNode { speaker = "arin", text = "안녕" };
            var ask = new LineNode { speaker = "bob", text = "어때?", options = { new ChoiceOption { text = "좋아" } } };
            var n = new LineNode { text = "해설" };
            foreach (var node in new GraphNode[] { start, a, ask, n }) g.Add(node);
            g.Connect(start.guid, StartNode.NextPort, a.guid, GraphNode.FlowInPort);
            g.Connect(a.guid, LineNode.NextPort, ask.guid, GraphNode.FlowInPort);
            g.Connect(ask.guid, LineNode.OptionPrefix + ask.options[0].id, n.guid, GraphNode.FlowInPort);

            var runner = new DialogueRunner(_ => d, _ => null) { ClickCooldown = 0, Profiles = id => id == "arin" ? arin : id == "bob" ? bob : null };
            runner.Start(d);
            runner.FastForward();
            runner.Advance();
            runner.FastForward();
            runner.Choose(0);
            runner.FastForward();
            var log = runner.Log;
            Assert.AreEqual(4, log.Count);
            Assert.AreEqual("arin", log[0].SpeakerName);
            Assert.AreEqual(smile, log[0].Portrait, "나와 있지 않으면 기본 스탠딩 그림");
            Assert.AreEqual(icon, log[1].Portrait, "로그 얼굴이 있으면 그것");
            Assert.IsTrue(log[2].Choice);
            Assert.AreEqual("좋아", log[2].Text);
            Assert.AreEqual("", log[3].SpeakerName, "해설");
            Assert.IsNull(log[3].Portrait);
        }

        [Test]
        public void 화자_이름은_다른_이름_프로필_해설_순서()
        {
            var runner = new DialogueRunner(_ => null, _ => null);
            Assert.AreEqual("???", runner.SpeakerNameOf(new LineNode { speaker = "arin", speakerName = "???" }));
            Assert.AreEqual("arin", runner.SpeakerNameOf(new LineNode { speaker = "arin" }), "번역 표에 이름이 없으면 ID");
            Assert.AreEqual("", runner.SpeakerNameOf(new LineNode()));
        }
    }
}
