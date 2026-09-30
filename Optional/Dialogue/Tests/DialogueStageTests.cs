using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace GameSystems.Tests
{
    public sealed class DialogueStageTests
    {
        readonly List<Object> _made = new List<Object>();
        DialogueStage _stage;
        Sprite _a;
        Sprite _b;

        [SetUp]
        public void SetUp()
        {
            var cameraObject = Make(new GameObject("Camera"));
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            var stageObject = Make(new GameObject("Stage"));
            _stage = stageObject.AddComponent<DialogueStage>();
            _stage.stageCamera = camera;
            _stage.backgroundBack = new GameObject("Back").AddComponent<SpriteRenderer>();
            _stage.backgroundBack.transform.SetParent(stageObject.transform);
            _stage.backgroundFront = new GameObject("Front").AddComponent<SpriteRenderer>();
            _stage.backgroundFront.transform.SetParent(stageObject.transform);
            _stage.standingRoot = new GameObject("Standings").transform;
            _stage.standingRoot.SetParent(stageObject.transform);
            var texture = Make(new Texture2D(4, 8));
            _a = Make(Sprite.Create(texture, new Rect(0, 0, 4, 8), new Vector2(0.5f, 0f), 4));
            _b = Make(Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 4));
            var arin = Make(ScriptableObject.CreateInstance<StandingProfileData>());
            arin.EditorAssignId("arin");
            arin.expressions[0].sprite = _a;
            _stage.Characters = id => id == "arin" ? arin : null;
        }

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

        (DialogueRunner runner, DialogueData d, LineNode line) One(params EffectNode[] effects)
        {
            var d = Make(ScriptableObject.CreateInstance<DialogueData>());
            d.EditorAssignId("stage");
            var line = new LineNode { speaker = "arin", text = "..." };
            d.graph.Add(line);
            foreach (var effect in effects)
            {
                d.graph.Add(effect);
                d.graph.Connect(line.guid, LineNode.EnterPort, effect.guid, EffectNode.PlayPort);
            }
            return (new DialogueRunner(_ => d, _ => null) { Stage = _stage, ClickCooldown = 0 }, d, line);
        }

        [Test]
        public void 창_모드_스탠딩은_창_모양_안만_보인다()
        {
            var (runner, d, line) = One(new StandingEnterEffect { duration = 0, frame = _b });
            runner.StartAt(d, line.guid);
            var view = _stage.standingRoot.GetChild(0);
            var frame = view.Find("Frame");
            Assert.IsNotNull(frame);
            Assert.IsTrue(frame.gameObject.activeSelf);
            Assert.AreEqual(SpriteMaskInteraction.VisibleInsideMask, view.Find("Current").GetComponent<SpriteRenderer>().maskInteraction);
            Assert.AreEqual(_b, runner.State.Standing("arin").frame);

            var (off, d2, line2) = One(new StandingEnterEffect { duration = 0, frame = _b }, new StandingFrameEffect { duration = 0 });
            off.StartAt(d2, line2.guid);
            var view2 = _stage.standingRoot.GetChild(0);
            Assert.IsFalse(view2.Find("Frame").gameObject.activeSelf, "창 이미지를 비우면 창 모드 끄기");
            Assert.AreEqual(SpriteMaskInteraction.None, view2.Find("Current").GetComponent<SpriteRenderer>().maskInteraction);
        }

        [Test]
        public void 이미지_창은_자리마다_나타나고_지워진다()
        {
            var show = new ShowImageEffect { slot = "cg", sprite = _a, duration = 0.5f, ease = Ease.Linear };
            var hide = new HideImageEffect { slot = "cg", duration = 0.5f, delay = 1f, ease = Ease.Linear };
            var (runner, d, line) = One(show, hide);
            runner.StartAt(d, line.guid);
            var images = _stage.transform.Find("Images");
            Assert.IsNotNull(images, "이미지 부모를 만든다");
            Assert.AreEqual(1, images.childCount);
            var current = images.GetChild(0).Find("Current").GetComponent<SpriteRenderer>();
            runner.Tick(0.25f);
            Assert.AreEqual(0.5f, current.color.a, 0.02f, "나타나는 중");
            runner.Tick(0.3f);
            Assert.AreEqual(1f, current.color.a, 1e-3);
            Assert.IsNotNull(runner.State.Image("cg"));
            runner.Tick(1f);
            Assert.AreEqual(0, images.childCount, "지우기가 끝나면 없앤다");
            Assert.IsNull(runner.State.Image("cg"));
        }

        [Test]
        public void 검은_화면은_유지_시간_동안_바뀐_배경으로_기다린다()
        {
            var background = new BackgroundEffect { sprite = _a, viaBlack = true, duration = 2f, blackHold = 1f, ease = Ease.Linear };
            var (runner, d, line) = One(background);
            runner.StartAt(d, line.guid);
            runner.Tick(0.4f);
            Assert.IsNull(_stage.backgroundBack.sprite, "어두워지는 중(처음 0.5초)");
            runner.Tick(0.2f);
            Assert.AreEqual(_a, _stage.backgroundBack.sprite, "검은 화면 동안 바뀐다");
        }
    }
}
