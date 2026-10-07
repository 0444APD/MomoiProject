using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    // DB › 저장 변환 항목: 편집기 검사가 경로 모양을 미리 잡고(불러올 때 조용히 버려지기 전에), 등록할 때 항목 ID가 변환 이름에 붙는다.
    public class SaveMigrationDataTests
    {
        sealed class Collect : IValidationContext
        {
            public readonly List<string> Errors = new List<string>();
            public bool StatExists(string id) => true;
            public bool AchievementExists(string id) => true;
            public StatDefinitionInfo? FindStat(string id) => null;

            public void Report(ValidationSeverity severity, string message)
            {
                if (severity == ValidationSeverity.Error) Errors.Add(message);
            }
        }

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Clean()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        SaveMigrationData Make(SaveMigrationKind kind, string key, string from = "", string to = "", string newKey = "")
        {
            var data = ScriptableObject.CreateInstance<SaveMigrationData>();
            data.EditorAssignId("migration1");
            data.version = 2;
            data.kind = kind;
            data.key = key;
            data.fromPath = from;
            data.toPath = to;
            data.newKey = newKey;
            _created.Add(data);
            return data;
        }

        static string Errors(GameData data)
        {
            var context = new Collect();
            data.ValidateData(context);
            return string.Join("\n", context.Errors);
        }

        [Test]
        public void 틀린_경로와_키를_오류로_알린다()
        {
            StringAssert.Contains("[0]", Errors(Make(SaveMigrationKind.MoveField, "run", "items[0].title", "items[].name")));
            StringAssert.Contains("옮길 수 없습니다", Errors(Make(SaveMigrationKind.MoveField, "run", "items[].title", "bag[].title")));
            StringAssert.Contains("공백", Errors(Make(SaveMigrationKind.MoveField, "run ", "gold", "wallet.gold")));
            StringAssert.Contains("같습니다", Errors(Make(SaveMigrationKind.RenameKey, "run", newKey: "run")));
            Assert.AreEqual("", Errors(Make(SaveMigrationKind.MoveField, "run", "hero.gold", "gold")));
        }

        [Test]
        public void 등록하면_항목_ID가_변환_이름에_붙는다()
        {
            var migrations = new SaveMigrations();
            Make(SaveMigrationKind.MoveField, "run", "hero.gold", "gold").Register(migrations);
            StringAssert.Contains("migration1", migrations.Steps.Single().label);
        }
    }
}
