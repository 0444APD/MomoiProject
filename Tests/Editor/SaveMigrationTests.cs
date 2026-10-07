using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 저장 변환(SaveMigrations): 예전 경로를 찾지 못하면 경고 · 키의 값이 리스트면 [] · 경로 모양 미리 보기(CheckMove).
    // 코어만 쓴다(Unity 없이 도는 코어 시험 묶음에도 넣을 수 있다).
    public class SaveMigrationTests
    {
        // 변환을 거친 저장 트리(EveryLoad는 버전 변환 뒤에 같은 트리로 돈다).
        static Dictionary<string, object> Migrate(GameSession session, string json)
        {
            Dictionary<string, object> seen = null;
            session.Migrations.EveryLoad = state => seen = state;
            session.ImportMeta(json);
            return seen;
        }

        static List<JournalEntry> Warnings(GameSession session) =>
            session.Journal.Entries.Where(e => e.Kind == JournalKind.Warning && e.System == "Save").ToList();

        static string Text(GameSession session) => string.Join("\n", Warnings(session).Select(w => w.Message));

        [Test]
        public void 예전_경로를_찾지_못하면_변환_이름과_경로를_경고로_남긴다()
        {
            var session = new GameSession();
            session.Migrations.MoveField(2, "m:inventory", "coinz", "gold", "migration1 · 골드 옮김");
            var state = Migrate(session, "{\"version\":1,\"state\":{\"m:inventory\":{\"coins\":30}}}");

            var warnings = Warnings(session);
            Assert.AreEqual(1, warnings.Count, Text(session));
            StringAssert.Contains("migration1", warnings[0].Message);
            StringAssert.Contains("coinz", warnings[0].Message);
            Assert.IsFalse(((Dictionary<string, object>)state["m:inventory"]).ContainsKey("gold"));
        }

        [Test]
        public void 리스트_요소마다가_아니라_불러오기마다_한_번만_경고한다()
        {
            var session = new GameSession();
            session.Migrations.MoveField(2, "m:inventory", "items[].titel", "items[].name");
            Migrate(session, "{\"version\":1,\"state\":{\"m:inventory\":{\"items\":[{\"title\":\"검\"},{\"title\":\"활\"}]}}}");
            Assert.AreEqual(1, Warnings(session).Count, Text(session));
        }

        [Test]
        public void 다른_파일의_키_빈_리스트_null_값은_경고하지_않는다()
        {
            var session = new GameSession();
            session.Migrations.MoveField(2, "run", "hero.gold", "gold");                       // 판 키 — 프로필 파일에는 없다
            session.Migrations.MoveField(2, "m:inventory", "items[].title", "items[].name");  // 빈 리스트
            session.Migrations.MoveField(2, "m:hero", "pet.name", "petName");                  // null 값
            Migrate(session, "{\"version\":1,\"state\":{\"m:inventory\":{\"items\":[]},\"m:hero\":{\"pet\":null}}}");
            Assert.AreEqual(0, Warnings(session).Count, Text(session));
        }

        [Test]
        public void 일부_요소에만_있는_칸은_옮기고_경고하지_않는다()
        {
            var session = new GameSession();
            session.Migrations.MoveField(2, "m:inventory", "items[].title", "items[].name");
            var state = Migrate(session, "{\"version\":1,\"state\":{\"m:inventory\":{\"items\":[{\"title\":\"검\"},{\"level\":2}]}}}");
            Assert.AreEqual(0, Warnings(session).Count, Text(session));
            var items = (List<object>)((Dictionary<string, object>)state["m:inventory"])["items"];
            Assert.AreEqual("검", ((Dictionary<string, object>)items[0])["name"]);
        }

        [Test]
        public void 키의_값이_리스트면_대괄호로_시작하는_경로로_옮긴다()
        {
            var session = new GameSession();
            session.Migrations.MoveField(2, "m:items", "[].title", "[].name");
            var state = Migrate(session, "{\"version\":1,\"state\":{\"m:items\":[{\"title\":\"검\"},{\"title\":\"활\"}]}}");
            var items = (List<object>)state["m:items"];
            CollectionAssert.AreEqual(new[] { "검", "활" }, items.Select(i => ((Dictionary<string, object>)i)["name"]));
            Assert.AreEqual(0, Warnings(session).Count, Text(session));
        }

        [Test]
        public void 키의_값이_리스트인데_대괄호_없이_적으면_고칠_길을_알려_준다()
        {
            var session = new GameSession();
            session.Migrations.MoveField(2, "m:items", "title", "name");
            Migrate(session, "{\"version\":1,\"state\":{\"m:items\":[{\"title\":\"검\"}]}}");
            var warnings = Warnings(session);
            Assert.AreEqual(1, warnings.Count, Text(session));
            StringAssert.Contains("[].title", warnings[0].Message);
        }

        [Test]
        public void 변환_이름이_기록에_남는다()
        {
            var session = new GameSession();
            session.Migrations.RenameKey(2, "m:money", "m:wallet", "migration7 · 키 이름 변경");
            Migrate(session, "{\"version\":1,\"state\":{\"m:money\":5}}");
            Assert.IsTrue(session.Journal.Entries.Any(e => e.Kind == JournalKind.System && e.Message.Contains("migration7")));
        }

        [Test]
        public void 경로_모양을_미리_본다()
        {
            Assert.IsNull(SaveMigrations.CheckMove("coins", "wallet.gold"));
            Assert.IsNull(SaveMigrations.CheckMove("items[].title", "items[].name"));
            Assert.IsNull(SaveMigrations.CheckMove("items[].title", "items[].meta.name"));
            Assert.IsNull(SaveMigrations.CheckMove("hero.gold", "gold"));
            Assert.IsNull(SaveMigrations.CheckMove("[].title", "[].name"));
            StringAssert.Contains("[0]", SaveMigrations.CheckMove("items[0].title", "items[].name"));
            StringAssert.Contains("빈 칸", SaveMigrations.CheckMove("hero..gold", "gold"));
            StringAssert.Contains("공백", SaveMigrations.CheckMove("hero. gold", "gold"));
            StringAssert.Contains("마지막 칸", SaveMigrations.CheckMove("items[].title", "items[]"));
            StringAssert.Contains("같습니다", SaveMigrations.CheckMove("gold", "gold"));
            StringAssert.Contains("옮길 수 없습니다", SaveMigrations.CheckMove("items[].title", "bag[].title"));
            StringAssert.Contains("옮길 수 없습니다", SaveMigrations.CheckMove("[].title", "name"));
            StringAssert.Contains("적으세요", SaveMigrations.CheckMove("", "gold"));
        }
    }
}
