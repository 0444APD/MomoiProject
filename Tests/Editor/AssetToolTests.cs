using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GBible.Tests
{
    // 에셋 관리: 넣을 것은 GUID로 따라가고(옮김·확장자 바꿈), 정돈·안 쓰는 것은 개발자 폴더 안만 옮기며 기록을 남기고 되돌린다.
    // 프로젝트에 잠깐 시험 폴더(Assets/__GBibleAssetToolTests)를 만들고 끝나면 지운다. 넣을 것 목록·옮긴 기록은 시스템 임시 폴더에.
    public class AssetToolTests
    {
        const string Root = "Assets/__GBibleAssetToolTests";
        string _temp;
        string _oldStore;
        string _oldBackups;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "momoi-assettools-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(_temp);
            _oldStore = AssetRequests.StorePath;
            _oldBackups = AssetTools.BackupRoot;
            AssetRequests.StorePath = _temp + "/AssetRequests.json";
            AssetTools.BackupRoot = _temp + "/Backups";
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);   // 끊긴 지난 시험이 남긴 것
            AssetTools.EnsureFolder(Root);
        }

        [TearDown]
        public void TearDown()
        {
            AssetRequests.StorePath = _oldStore;
            AssetTools.BackupRoot = _oldBackups;
            AssetDatabase.DeleteAsset(Root);   // 이 시험이 만든 폴더만
            if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
        }

        static byte[] Image(Color color, int size, bool jpg)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            var bytes = jpg ? texture.EncodeToJPG() : texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return bytes;
        }

        // 프로젝트 안에 파일을 만든다(개발자가 넣은 것처럼). GUID를 돌려준다.
        static string MakeAsset(string path, byte[] bytes)
        {
            AssetTools.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.AssetPathToGUID(path);
        }

        string External(string name, byte[] bytes)
        {
            var path = _temp + "/" + name;
            File.WriteAllBytes(path, bytes);
            return path;
        }

        static AssetRequests.Item Only() => AssetRequests.Load().Single();

        // ── 넣을 것 ──────────────────────────────────────────────

        [Test]
        public void 옮긴_임시_에셋은_GUID로_따라가고_다시_돌아도_새로_만들지_않는다()
        {
            var requested = Root + "/Art/fireball.png";
            Assert.AreEqual(requested, AssetRequests.Sprite(requested, 4, 4, "파이어볼"));
            Assert.IsNotEmpty(Only().guid);
            Assert.AreEqual(AssetRequests.State.Placeholder, AssetRequests.StateOf(Only()));

            var moved = Root + "/Art/Fire/fireball.png";
            AssetTools.EnsureFolder(Root + "/Art/Fire");
            Assert.AreEqual("", AssetDatabase.MoveAsset(requested, moved));

            Assert.AreEqual(moved, AssetRequests.Sprite(requested, 4, 4, "파이어볼"), "반영 스크립트가 다시 돌아도 옮긴 자리");
            Assert.IsFalse(File.Exists(requested), "예전 자리에 임시 파일을 다시 만들지 않는다");
            Assert.AreEqual(1, AssetRequests.Load().Count);
            Assert.AreEqual(moved, AssetRequests.PathOf(Only()));
            Assert.AreEqual(AssetRequests.State.Placeholder, AssetRequests.StateOf(Only()));
        }

        [Test]
        public void 옮긴_자리에_넣은_파일은_같은_GUID에_들어가고_확장자가_바뀌어도_넣은_것이다()
        {
            var requested = Root + "/Art/hit.png";
            AssetRequests.Sprite(requested, 4, 4, "맞음");
            var guid = Only().guid;
            var moved = Root + "/Art/Sub/hit.png";
            AssetTools.EnsureFolder(Root + "/Art/Sub");
            Assert.AreEqual("", AssetDatabase.MoveAsset(requested, moved));

            // 창의 '파일 넣기'와 같은 길: 지금 자리(GUID로 찾음)에 내용을 넣는다.
            var result = AssetRequests.Put(Only(), External("real.jpg", Image(Color.red, 8, jpg: true)));
            Assert.AreEqual(Root + "/Art/Sub/hit.jpg", result);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(result), "GUID 그대로 — 쓰는 곳이 모두 새 것을 쓴다");
            Assert.IsFalse(File.Exists(requested), "요청한 자리에 따로 만들지 않는다");
            Assert.AreEqual(AssetRequests.State.Done, AssetRequests.StateOf(Only()));

            // 확장자가 바뀐 뒤(hit.wav → hit.ogg와 같은 경우) 반영 스크립트가 다시 돌아도 임시 파일·줄을 새로 만들지 않는다.
            Assert.AreEqual(result, AssetRequests.Sprite(requested, 4, 4, "맞음"));
            Assert.IsFalse(File.Exists(requested));
            Assert.AreEqual(1, AssetRequests.Load().Count);
            Assert.AreEqual(AssetRequests.State.Done, AssetRequests.StateOf(Only()));
        }

        [Test]
        public void 창_밖에서_내용을_덮어써도_임시_파일과_다르면_넣은_것이다()
        {
            var path = Root + "/Art/outside.png";
            AssetRequests.Sprite(path, 4, 4, "밖에서 바꿈");
            Assert.AreEqual(AssetRequests.State.Placeholder, AssetRequests.StateOf(Only()));
            File.WriteAllBytes(path, Image(Color.magenta, 16, jpg: false));   // 탐색기에서 덮어쓴 것처럼(.meta·GUID 그대로)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Assert.AreEqual(AssetRequests.State.Done, AssetRequests.StateOf(Only()));
        }

        [Test]
        public void 확장자만_다른_예전_줄을_찾고_임시_파일을_만들지_않는다()
        {
            // 예전 형식: GUID 없이, 바꾸기가 경로를 새 확장자로 고쳐 적은 줄.
            var legacy = Root + "/Art/old.jpg";
            var guid = MakeAsset(legacy, Image(Color.blue, 8, jpg: true));
            File.WriteAllText(AssetRequests.StorePath, "{\"items\":[{\"path\":\"" + legacy + "\",\"usage\":\"예전\",\"done\":true}]}");

            Assert.AreEqual(legacy, AssetRequests.Sprite(Root + "/Art/old.png", 4, 4, "예전"));
            Assert.IsFalse(File.Exists(Root + "/Art/old.png"));
            Assert.AreEqual(1, AssetRequests.Load().Count);
            Assert.AreEqual(guid, Only().guid, "찾은 에셋의 GUID를 적어 둔다(그 뒤로는 옮겨도 따라간다)");
            Assert.AreEqual(AssetRequests.State.Done, AssetRequests.StateOf(Only()));
        }

        [Test]
        public void 목록만_올린_줄은_파일이_생기면_넣은_것이다()
        {
            var requested = Root + "/Art/icon.png";
            Assert.AreEqual(requested, AssetRequests.Request(requested, "아이콘"));
            Assert.IsFalse(File.Exists(requested), "목록만 — 임시 파일을 만들지 않는다");
            Assert.AreEqual(AssetRequests.State.Missing, AssetRequests.StateOf(Only()));

            var guid = MakeAsset(requested, Image(Color.green, 4, jpg: false));
            var item = AssetRequests.Refresh().Single();
            Assert.AreEqual(guid, item.guid);
            Assert.IsTrue(item.done);
            Assert.AreEqual(AssetRequests.State.Done, AssetRequests.StateOf(item));
        }

        [Test]
        public void 지문이_없는_예전_줄은_넣음_표시를_따른다()
        {
            var path = Root + "/Art/legacy.png";
            var guid = MakeAsset(path, Image(Color.gray, 4, jpg: false));
            File.WriteAllText(AssetRequests.StorePath, "{\"items\":[{\"path\":\"" + path + "\",\"usage\":\"예전 임시\",\"done\":false}]}");
            Assert.AreEqual(AssetRequests.State.Placeholder, AssetRequests.StateOf(Only()), "예전 임시 파일 — 아직 넣지 않음");

            AssetRequests.MarkDone(path);
            Assert.AreEqual(AssetRequests.State.Done, AssetRequests.StateOf(Only()));
            Assert.AreEqual(guid, Only().guid);
        }

        // ── 폴더 정돈 ────────────────────────────────────────────

        [Test]
        public void 정돈은_개발자_폴더_안에서만_하위_모양을_지키며_옮긴다()
        {
            var paths = new[]
            {
                "Assets/Dev/Cards/Fire/a.png",                         // → Art/Cards/Fire
                "Assets/Dev/Audio/Icons/b.png",                        // 다른 종류 폴더 이름만 뺀다 → Art/Icons
                "Assets/Dev/Art/c.png",                                // 제자리
                "Assets/Dev/Art/hero.mat",                             // → Materials
                "Assets/Dev/Packs/Toon/Scripts/Toon.cs",               // 코드가 든 폴더 = 가져온 패키지
                "Assets/Dev/Packs/Toon/Textures/t.png",                // 그대로
                "Assets/Dev/Scenes/Level1.unity",                      // 제자리
                "Assets/Dev/Scenes/Level1/Lightmap-0_comp_light.exr",  // 씬 옆 굽는 폴더 — 그대로
                "Assets/Dev/Remote/r.png",                             // Addressables 폴더 항목 — 그대로
                "Assets/Dev/Things/Resources/res.png",                 // Resources — 그대로
                "Assets/Dev/_Unused/Dev/old.png",                      // _Unused — 그대로
                "Assets/Dev/Notes/readme.txt",                         // 기타 — 그대로
                "Assets/Polygon/Textures/p.png",                       // 에셋 스토어 폴더(개발자 폴더 밖) — 그대로
                "Assets/MomoiProject/UI/Screens/Hud.uss",              // 시스템 폴더 — 그대로
            };
            var moves = AssetTools.PlanTidy("Assets/Dev", paths, _ => false, new[] { "Assets/Dev/Remote" }).ToDictionary(m => m.path, m => m.target);

            CollectionAssert.AreEquivalent(new[] { "Assets/Dev/Cards/Fire/a.png", "Assets/Dev/Audio/Icons/b.png", "Assets/Dev/Art/hero.mat" }, moves.Keys);
            Assert.AreEqual("Assets/Dev/Art/Cards/Fire/a.png", moves["Assets/Dev/Cards/Fire/a.png"]);
            Assert.AreEqual("Assets/Dev/Art/Icons/b.png", moves["Assets/Dev/Audio/Icons/b.png"]);
            Assert.AreEqual("Assets/Dev/Materials/hero.mat", moves["Assets/Dev/Art/hero.mat"]);
        }

        [Test]
        public void 정돈은_기록을_남기고_기록대로_되돌린다()
        {
            var dev = Root + "/Dev";
            var original = dev + "/Cards/Fire/a.png";
            var guid = MakeAsset(original, Image(Color.yellow, 4, jpg: false));
            var moves = AssetTools.PlanTidy(dev, AssetDatabase.GetAllAssetPaths(), AssetDatabase.IsValidFolder, Array.Empty<string>());
            var target = dev + "/Art/Cards/Fire/a.png";
            Assert.AreEqual(1, moves.Count);
            Assert.AreEqual(target, moves[0].target);

            Assert.AreEqual(1, AssetTools.Tidy(dev, moves, out var log));
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(target), "GUID 그대로");
            Assert.IsFalse(File.Exists(original));
            Assert.IsTrue(File.Exists(log));
            StringAssert.Contains(original + "\t" + target, File.ReadAllText(log));
            Assert.AreEqual(Path.GetFullPath(log), Path.GetFullPath(AssetTools.LastMoveLog("tidy") ?? ""));

            Assert.AreEqual(1, AssetTools.UndoMoves(log));
            Assert.IsTrue(File.Exists(original));
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(original));
            Assert.IsNull(AssetTools.LastMoveLog("tidy"), "되돌린 기록은 다시 되돌리기로 보이지 않는다(지우지 않고 이름만 바꾼다)");
        }

        // ── 안 쓰는 것 ───────────────────────────────────────────

        [Test]
        public void 안_쓰는_것의_뿌리에_빌드_목록_밖의_개발자_씬과_DB_항목과_Addressables가_든다()
        {
            var paths = new[]
            {
                "Assets/Dev/Scenes/Level2.unity",          // 빌드 목록에 아직 없음
                "Assets/Dev/Levels/Boss.unity",            // Scenes 밖의 개발자 씬
                "Assets/Dev/_Unused/Dev/Old.unity",        // _Unused — 뿌리 아님
                "Assets/MomoiProject/Scenes/Test.unity",   // AI 씬(시스템 폴더)
                "Assets/Dev/Things/Resources/res.png",     // Resources
                "Assets/Dev/Data/boss.asset",              // 시스템 폴더 밖의 DB 항목
                "Assets/Dev/Data/notes.asset",             // DB 항목이 아닌 에셋
                "Assets/Dev/Remote/r.png",                 // Addressables 폴더 항목 안
                "Assets/Dev/Art/single.png",               // Addressables 에셋 항목
                "Assets/Polygon/Demo/Demo.unity",          // 개발자 폴더 밖의 씬 — 뿌리 아님
            };
            var roots = AssetTools.UnusedRoots(paths, new[] { "Assets/Dev/Scenes/Main.unity" },
                p => p.EndsWith("boss.asset", StringComparison.Ordinal) ? typeof(GBible.Data.InstanceData) : typeof(ScriptableObject),
                new[] { "Assets/Dev/Remote", "Assets/Dev/Art/single.png" }, p => p == "Assets/Dev/Remote", new[] { "Assets/Dev/Art/requested.png", null });

            foreach (var expected in new[]
                     {
                         "Assets/Dev/Scenes/Main.unity", "Assets/Dev/Scenes/Level2.unity", "Assets/Dev/Levels/Boss.unity", "Assets/MomoiProject/Scenes/Test.unity",
                         "Assets/Dev/Things/Resources/res.png", "Assets/Dev/Data/boss.asset", "Assets/Dev/Remote/r.png", "Assets/Dev/Art/single.png", "Assets/Dev/Art/requested.png",
                     })
                CollectionAssert.Contains(roots, expected);
            CollectionAssert.DoesNotContain(roots, "Assets/Dev/_Unused/Dev/Old.unity");
            CollectionAssert.DoesNotContain(roots, "Assets/Dev/Data/notes.asset");
            CollectionAssert.DoesNotContain(roots, "Assets/Polygon/Demo/Demo.unity");
        }

        [Test]
        public void 안_쓰는_것은_개발자_폴더_안에서_닿지_않는_것만()
        {
            var paths = new[]
            {
                "Assets/Dev/Art/used.png", "Assets/Dev/Art/orphan.png", "Assets/Dev/Packs/Toon/Scripts/Toon.cs", "Assets/Dev/Packs/Toon/Textures/demo.png",
                "Assets/Dev/Remote/r.png", "Assets/Dev/Notes/readme.txt", "Assets/Polygon/Textures/p.png", "Assets/Dev/_Unused/Dev/Art/old.png",
            };
            var unused = AssetTools.UnusedIn("Assets/Dev", paths, _ => false, new HashSet<string> { "Assets/Dev/Art/used.png" }, new[] { "Assets/Dev/Remote" });
            CollectionAssert.AreEqual(new[] { "Assets/Dev/Art/orphan.png" }, unused);
        }

        [Test]
        public void 안_쓰는_것은_개발자_폴더_안만_원래_경로_그대로_옮기고_되돌린다()
        {
            var dev = Root + "/Dev";
            var path = dev + "/Art/orphan.png";
            var guid = MakeAsset(path, Image(Color.cyan, 4, jpg: false));
            var outside = Root + "/Elsewhere/keep.png";
            MakeAsset(outside, Image(Color.white, 4, jpg: false));

            Assert.AreEqual(1, AssetTools.MoveToUnused(dev, new[] { path, outside }, out var log));
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(dev + "/_Unused" + path.Substring("Assets".Length)));
            Assert.IsTrue(File.Exists(outside), "개발자 폴더 밖은 옮기지 않는다");

            Assert.AreEqual(1, AssetTools.UndoMoves(log));
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
        }
    }
}
