using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.Dialogue;
using GBible.EditorTools.Graph;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    public sealed class DialogueViewTests
    {
        [Test]
        public void 타이핑은_남은_글자를_투명하게_두고_색_태그를_뺀다()
        {
            const string text = "안녕<color=red>하세요</color>!";
            Assert.AreEqual("안녕<color=red>하<alpha=#00>세요!", TextReveal.Reveal(text, 3));
            Assert.AreEqual("<alpha=#00>안녕하세요!", TextReveal.Reveal(text, 0));
            Assert.AreEqual(text, TextReveal.Reveal(text, 6));
            Assert.AreEqual(text, TextReveal.Reveal(text, 99));
            Assert.AreEqual("<b>가<alpha=#00>나</b>", TextReveal.Reveal("<b>가나</b>", 1), "굵기 태그는 줄바꿈 위치를 위해 남긴다");
        }

        [Test]
        public void 모션_진행_방식()
        {
            var motion = ScriptableObject.CreateInstance<UiMotion>();
            try
            {
                motion.duration = 1f;
                Assert.AreEqual(1f, motion.Progress(3f), 1e-4, "한 번: 끝에서 멈춤");
                motion.loop = UiMotion.LoopMode.Loop;
                Assert.AreEqual(0.25f, motion.Progress(2.25f), 1e-4);
                motion.loop = UiMotion.LoopMode.PingPong;
                Assert.AreEqual(0.75f, motion.Progress(1.25f), 1e-4);

                var element = new VisualElement();
                motion.loop = UiMotion.LoopMode.Once;
                motion.opacity = true;
                motion.opacityCurve = AnimationCurve.Linear(0, 0, 1, 1);
                motion.restoreWhenDone = true;
                var runner = new UiMotionRunner();
                var done = false;
                runner.Play(element, motion, () => done = true);
                runner.Update(0.5f);
                Assert.AreEqual(0.5f, element.style.opacity.value, 1e-3);
                runner.Update(0.6f);
                Assert.IsTrue(done);
                Assert.AreEqual(StyleKeyword.Null, element.style.opacity.keyword, "끝나면 USS 값으로 돌아간다");
            }
            finally
            {
                Object.DestroyImmediate(motion);
            }
        }

        [Test]
        public void 화면이_스탠딩을_만들고_지우고_카메라를_확대한다()
        {
            var made = new List<Object>();
            var cameraObject = new GameObject("TestCamera");
            made.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;

            var stageObject = new GameObject("Stage");
            made.Add(stageObject);
            var stage = stageObject.AddComponent<DialogueStage>();
            stage.stageCamera = camera;
            stage.backgroundBack = new GameObject("Back").AddComponent<SpriteRenderer>();
            stage.backgroundBack.transform.SetParent(stageObject.transform);
            stage.backgroundFront = new GameObject("Front").AddComponent<SpriteRenderer>();
            stage.backgroundFront.transform.SetParent(stageObject.transform);
            var standings = new GameObject("Standings").transform;
            standings.SetParent(stageObject.transform);
            stage.standingRoot = standings;

            var texture = new Texture2D(4, 8);
            made.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 8), new Vector2(0.5f, 0f), 4);
            made.Add(sprite);
            var arin = ScriptableObject.CreateInstance<StandingProfileData>();
            made.Add(arin);
            arin.EditorAssignId("arin");
            arin.expressions[0].sprite = sprite;
            stage.Characters = id => id == "arin" ? arin : null;

            var dialogue = ScriptableObject.CreateInstance<DialogueData>();
            made.Add(dialogue);
            dialogue.EditorAssignId("stageTest");
            var g = dialogue.graph;
            var start = new StartNode();
            var a = new LineNode { speaker = "arin", text = "안녕" };
            var b = new LineNode { speaker = "arin", text = "잘 가" };
            var enter = new StandingEnterEffect { duration = 0.5f, slot = StandingSlot.Left };
            var zoom = new CameraEffect { duration = 0.5f, zoomPercent = 200f };
            var exit = new StandingExitEffect { duration = 0.5f };
            foreach (var node in new GraphNode[] { start, a, b, enter, zoom, exit }) g.Add(node);
            g.Connect(start.guid, StartNode.NextPort, a.guid, GraphNode.FlowInPort);
            g.Connect(a.guid, LineNode.NextPort, b.guid, GraphNode.FlowInPort);
            g.Connect(a.guid, LineNode.EnterPort, enter.guid, EffectNode.PlayPort);
            g.Connect(a.guid, LineNode.EnterPort, zoom.guid, EffectNode.PlayPort);
            g.Connect(b.guid, LineNode.EnterPort, exit.guid, EffectNode.PlayPort);

            try
            {
                var runner = new DialogueRunner(id => dialogue, _ => null) { Stage = stage, ClickCooldown = 0 };
                runner.Start(dialogue);
                Assert.AreEqual(1, standings.childCount, "등장하면 스탠딩이 생긴다");
                runner.Tick(0.25f);
                stage.Sync();
                Assert.Less(camera.orthographicSize, 5f, "확대 중");
                runner.Tick(0.3f);
                stage.Sync();
                Assert.AreEqual(2.5f, camera.orthographicSize, 1e-3, "200% = 크기 절반");
                var view = standings.GetChild(0);
                Assert.Less(view.localPosition.x, 0f, "왼쪽 자리");

                runner.FastForward();
                runner.Advance();
                Assert.AreEqual(b, runner.Line);
                runner.Tick(0.6f);
                Assert.AreEqual(0, standings.childCount, "퇴장이 끝나면 지운다");

                runner.FastForward();
                runner.Advance();
                Assert.AreEqual(DialoguePhase.Ended, runner.Phase);
                Assert.AreEqual(5f, camera.orthographicSize, 1e-3, "끝나면 카메라를 되돌린다");
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void 재생기는_노드에서_시작하고_시간을_이동한다()
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueData>();
            dialogue.EditorAssignId("previewTest");
            var line = new LineNode { text = "미리보기" };
            var dim = new DimEffect { duration = 3f, amount = 1f, ease = Ease.Linear };
            dialogue.graph.Add(line);
            dialogue.graph.Add(dim);
            dialogue.graph.Connect(line.guid, LineNode.EnterPort, dim.guid, EffectNode.PlayPort);
            var go = new GameObject("Player");
            try
            {
                var player = go.AddComponent<DialoguePlayer>();
                Assert.IsTrue(player.PlayFrom(dialogue, line.guid, 1.5f));
                Assert.AreEqual(1.5f, player.Runner.Time, 1e-3);
                Assert.AreEqual(1f, player.Runner.State.dim, 1e-4, "상태는 시작할 때 끝 모습");
                Assert.IsTrue(player.Runner.InputBlocked, "3초 효과가 아직 진행 중");
                Assert.IsTrue(player.Seek(0.5f));
                Assert.AreEqual(0.5f, player.Runner.Time, 1e-3, "뒤로도 이동");
                Assert.IsTrue(player.Seek(3.5f));
                Assert.IsFalse(player.Runner.InputBlocked);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(dialogue);
            }
        }

        [Test]
        public void 템플릿은_이름_규격을_모두_갖춘_프리팹을_만든다()
        {
            const string folder = EditorTools.EditorDatabase.DataRoot + "/__DialogueTemplateTest";
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            try
            {
                var prefab = DialogueTemplate.Create(folder, registerAsDefault: false);
                Assert.IsNotNull(prefab);
                var player = prefab.GetComponent<DialoguePlayer>();
                Assert.IsNotNull(player);
                Assert.IsNotNull(player.stage);
                Assert.IsNotNull(player.stage.ui);
                Assert.IsNotNull(player.stage.dim);
                var document = player.stage.ui.GetComponent<UIDocument>();
                Assert.IsNotNull(document.panelSettings);
                Assert.IsNotNull(document.panelSettings.themeStyleSheet, "런타임 테마 연결");
                Assert.IsNotNull(document.visualTreeAsset);
                Assert.IsNotNull(player.stage.ui.choiceTemplate);
                Assert.AreEqual(4, player.stage.ui.motions.Count(m => m.motion != null));
                var check = DialogueTemplate.CheckNames(document.visualTreeAsset, player.stage.ui.names);
                Assert.IsTrue(check.All(c => c.found), "없는 요소: " + string.Join(", ", check.Where(c => !c.found).Select(c => c.name)));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
