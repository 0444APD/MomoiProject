using System;
using System.Collections.Generic;
using GameSystems.Data;
using GameSystems.Dialogue;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameSystems.Tests
{
    // 프로젝트 전용 흐름 노드 예시: 흐름이 지나가면 표시만 하고 다음으로.
    [Serializable]
    public sealed class TestMarkFlowNode : CustomFlowNode
    {
        public static int Passed;

        public override string Run(DialogueRunner runner)
        {
            Passed++;
            return NextPort;
        }
    }

    // 프로젝트 전용 효과 예시: 그리기를 직접 만든다.
    [Serializable]
    public sealed class TestCustomEffect : EffectNode
    {
        public static readonly List<float> Seen = new List<float>();

        sealed class Player : IEffectPlayer
        {
            public void Evaluate(float t) => Seen.Add(t);
            public void Finish() => Seen.Add(-1f);
        }

        public override IEffectPlayer CreatePlayer(DialogueStage stage, EffectContext context, StageState before, StageState after) => new Player();
    }

    public sealed class DialogueExtensionTests
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

        [Test]
        public void 추가_글은_대사가_캐릭터보다_우선이고_문장_틀을_푼다()
        {
            var arin = Make(ScriptableObject.CreateInstance<StandingProfileData>());
            arin.EditorAssignId("arin");
            arin.extras.Add(new ExtraText { key = "affiliation", text = "왕립 기사단" });
            arin.extras.Add(new ExtraText { key = "title", text = "부단장" });
            var line = new LineNode { speaker = "arin" };
            line.extras.Add(new ExtraText { key = "title", text = "{rank}번째 단장" });

            var extras = DialogueStage.ResolveExtras(line, arin, TextArgs.From(("rank", 3)));
            Assert.AreEqual("왕립 기사단", extras["affiliation"]);
            Assert.AreEqual("3번째 단장", extras["title"], "대사의 같은 키가 우선");
        }

        [Test]
        public void 예외_스프라이트가_스탠딩_ID보다_우선한다()
        {
            var texture = Make(new Texture2D(2, 2));
            var normal = Make(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            var special = Make(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            var arin = Make(ScriptableObject.CreateInstance<StandingProfileData>());
            arin.EditorAssignId("arin");
            arin.expressions[0].id = "smile";
            arin.expressions[0].sprite = normal;

            var go = Make(new GameObject("View"));
            var view = go.AddComponent<SpriteStandingView>();
            view.Bind(arin);
            view.BeginExpression("smile");
            view.SetExpressionBlend(1f);
            Assert.AreEqual(normal, go.transform.Find("Current").GetComponent<SpriteRenderer>().sprite);
            view.BeginExpression("smile", special);
            view.SetExpressionBlend(1f);
            Assert.AreEqual(special, go.transform.Find("Current").GetComponent<SpriteRenderer>().sprite);

            var state = new StageState();
            var context = new EffectContext { Speaker = "arin" };
            new StandingEnterEffect { expression = "smile", sprite = special }.ApplyTo(state, context);
            Assert.AreEqual(special, state.Standing("arin").sprite);
            new ExpressionEffect { expression = "smile" }.ApplyTo(state, context);
            Assert.IsNull(state.Standing("arin").sprite, "ID로 바꾸면 예외 스프라이트가 풀린다");
        }

        [Test]
        public void 프로젝트_전용_흐름_노드와_효과가_끼어든다()
        {
            TestMarkFlowNode.Passed = 0;
            TestCustomEffect.Seen.Clear();
            var dialogue = Make(ScriptableObject.CreateInstance<DialogueData>());
            dialogue.EditorAssignId("custom");
            var g = dialogue.graph;
            var start = new StartNode();
            var mark = new TestMarkFlowNode();
            var line = new LineNode { text = "끝" };
            var effect = new TestCustomEffect { duration = 1f, ease = Ease.Linear };
            foreach (var node in new GraphNode[] { start, mark, line, effect }) g.Add(node);
            g.Connect(start.guid, StartNode.NextPort, mark.guid, GraphNode.FlowInPort);
            g.Connect(mark.guid, CustomFlowNode.NextPort, line.guid, GraphNode.FlowInPort);
            g.Connect(line.guid, LineNode.EnterPort, effect.guid, EffectNode.PlayPort);

            var cameraObject = Make(new GameObject("Camera"));
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            var stage = Make(new GameObject("Stage")).AddComponent<DialogueStage>();
            stage.stageCamera = camera;

            var runner = new DialogueRunner(_ => dialogue, _ => null) { Stage = stage };
            runner.Start(dialogue);
            Assert.AreEqual(1, TestMarkFlowNode.Passed);
            Assert.AreEqual(line, runner.Line);
            runner.Tick(0.5f);
            Assert.AreEqual(0.5f, TestCustomEffect.Seen[0], 1e-4, "효과가 만든 재생기로 그린다");
            runner.Tick(0.6f);
            Assert.AreEqual(-1f, TestCustomEffect.Seen[TestCustomEffect.Seen.Count - 1]);
        }

        [Test]
        public void 커스텀_상태_값은_복사와_함께_따라간다()
        {
            var state = new StageState();
            state.Custom("weather", create: true).text = "rain";
            var copy = state.Clone();
            copy.Custom("weather").text = "snow";
            Assert.AreEqual("rain", state.Custom("weather").text);
            Assert.AreEqual("snow", copy.Custom("weather").text);
        }
    }
}
