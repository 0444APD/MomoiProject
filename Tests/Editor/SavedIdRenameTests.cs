using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    // 출시 뒤 DB 항목 ID를 바꾸면 저장 안의 그 ID([DataRef] 필드)를 불러올 때 새 ID로 바꾼다.
    public class SavedIdRenameTests
    {
        [Serializable]
        public class RnCard
        {
            [DataRef(typeof(InstanceData))] public string card = "";
            public int level;
        }

        [Serializable]
        public class RnRun
        {
            public List<RnCard> deck = new List<RnCard>();
            [DataRef(typeof(InstanceData))] public List<string> unlocked = new List<string>();
            public string note = "";
        }

        readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void Clean()
        {
            foreach (var o in _created) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        GameDatabase Database(bool rename)
        {
            var entry = ScriptableObject.CreateInstance<PlayerDataEntry>();
            entry.EditorAssignId("run");
            entry.zone = DataZone.Session;
            entry.valueType = typeof(RnRun).FullName;
            var database = ScriptableObject.CreateInstance<GameDatabase>();
            database.EditorSetEntries(new List<GameData> { entry });
            if (rename) database.EditorAddRename(typeof(InstanceData).FullName, "strike", "slash");
            _created.Add(entry);
            _created.Add(database);
            return database;
        }

        [Test]
        public void 바꾼_ID는_DataRef_필드에서만_새_ID로_바뀐다()
        {
            var everyLoad = SavedIdRenames.Build(Database(true));
            Assert.IsNotNull(everyLoad);
            var session = new GameSession();
            session.Migrations.EveryLoad = everyLoad;
            session.ImportRun("{\"version\":1,\"kind\":\"run\",\"state\":{\"run\":{\"deck\":[{\"card\":\"strike\",\"level\":1},{\"card\":\"defend\"}],\"unlocked\":[\"strike\"],\"note\":\"strike\"}}}");
            var run = session.Data.Define("run", DataZone.Session, new RnRun());

            Assert.AreEqual("slash", run.deck[0].card);
            Assert.AreEqual("defend", run.deck[1].card);
            CollectionAssert.AreEqual(new[] { "slash" }, run.unlocked);
            Assert.AreEqual("strike", run.note, "[DataRef]가 없는 글자는 그대로");
        }

        [Test]
        public void 이름_변경_이력이_없으면_아무것도_하지_않는다()
        {
            Assert.IsNull(SavedIdRenames.Build(Database(false)));
        }

        [Test]
        public void 이력의_예전_ID를_지금_다른_항목이_쓰면_그_줄은_쓰지_않는다()
        {
            // strike → slash 뒤에 새 항목이 strike를 다시 쓰면, 그 항목으로 저장한 값이 불러올 때마다 slash로 바뀌면 안 된다.
            var database = Database(true);
            var reused = ScriptableObject.CreateInstance<InstanceData>();
            reused.EditorAssignId("strike");
            _created.Add(reused);
            database.EditorSetEntries(new List<GameData>(database.Entries) { reused });
            Assert.IsNull(SavedIdRenames.Build(database));
        }

        [Test]
        public void 이력은_다른_항목의_줄을_지우지_않고_제자리로_돌아온_줄만_지운다()
        {
            var database = ScriptableObject.CreateInstance<GameDatabase>();
            _created.Add(database);
            var type = typeof(InstanceData).FullName;
            database.EditorAddRename(type, "potion", "potionSmall");
            database.EditorAddRename(type, "elixir", "potion");
            Assert.IsTrue(database.Renames.Any(r => r.oldId == "potion" && r.newId == "potionSmall"), "비운 ID를 다른 항목이 써도 예전 항목의 이력은 남는다");

            database.EditorAddRename(type, "a", "b");
            database.EditorAddRename(type, "b", "a");
            Assert.IsFalse(database.Renames.Any(r => r.oldId == r.newId), "같은 항목이 예전 이름으로 돌아온 줄(a → a)은 지운다");
            Assert.IsTrue(database.Renames.Any(r => r.oldId == "b" && r.newId == "a"));
        }
    }
}
