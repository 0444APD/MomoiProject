using System.IO;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;

namespace GBible.Tests
{
    // 선택 요소(상태 효과·패시브·능력)의 DB 종류는 라이브러리에 없고 프로젝트가 템플릿(Documentation~/AI/Templates/Elements)에서 들인다.
    // 템플릿은 컴파일되지 않는 글이라, 이 폴더에 같은 글(네임스페이스만 GBible.Tests)을 두어 테스트가 컴파일한다 — 템플릿이 깨지면 여기서 잡힌다.
    public class ElementTemplateTests
    {
        static string PackageRoot() =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GameData).Assembly)?.resolvedPath ?? Path.GetFullPath("Packages/com.momoi.gbible");

        static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

        [TestCase("StatusData")]
        [TestCase("PassiveData")]
        [TestCase("AbilityData")]
        public void 템플릿과_테스트_사본이_같다(string name)
        {
            var template = Path.Combine(PackageRoot(), "Documentation~", "AI", "Templates", "Elements", name + ".cs.txt");
            var copy = Path.Combine(PackageRoot(), "Tests", "Editor", "Elements", name + ".cs");
            Assert.IsTrue(File.Exists(template), template);
            Assert.IsTrue(File.Exists(copy), copy);
            var expected = Normalize(File.ReadAllText(template)).Replace("namespace MomoiProject\n", "namespace GBible.Tests\n");
            Assert.AreEqual(expected, Normalize(File.ReadAllText(copy)), $"{name}: 템플릿을 고쳤으면 Tests/Editor/Elements/{name}.cs도 같게(네임스페이스만 GBible.Tests)");
        }

        [Test]
        public void 템플릿은_규격을_붙인다()
        {
            Assert.IsTrue(typeof(IStatusData).IsAssignableFrom(typeof(StatusData)));
            Assert.IsTrue(typeof(IPassiveData).IsAssignableFrom(typeof(PassiveData)));
            Assert.IsTrue(typeof(IUsableData).IsAssignableFrom(typeof(AbilityData)));
            foreach (var face in new[] { typeof(IStatusData), typeof(IPassiveData), typeof(IUsableData) })
                Assert.IsTrue(typeof(IDataKind).IsAssignableFrom(face), $"{face.Name}: [DataRef(typeof({face.Name}))]로 고를 수 있게 IDataKind");
        }

        [Test]
        public void 정의로_바꾼다()
        {
            var status = UnityEngine.ScriptableObject.CreateInstance<StatusData>();
            status.maxStacks = 3;
            Assert.AreEqual(3, ((IStatusData)status).ToDefinition().MaxStacks);
            var passive = UnityEngine.ScriptableObject.CreateInstance<PassiveData>();
            passive.ownerPath = "jokers";
            Assert.AreEqual("jokers", ((IPassiveData)passive).ToDefinition().OwnerPath);
            var ability = UnityEngine.ScriptableObject.CreateInstance<AbilityData>();
            ability.failText = "fail.x";
            Assert.AreEqual("fail.x", ((IUsableData)ability).ToUsable().FailText);
            UnityEngine.Object.DestroyImmediate(status);
            UnityEngine.Object.DestroyImmediate(passive);
            UnityEngine.Object.DestroyImmediate(ability);
        }

        [Test]
        public void 요소에_딸린_Block은_표시가_있다()
        {
            foreach (var type in new[] { typeof(ApplyStatusEffect), typeof(ShortenStatusEffect), typeof(StatusCondition), typeof(RemoveStatusEffect), typeof(StatusFilterTarget) })
                Assert.AreEqual(Elements.Status, ((ElementAttribute)System.Attribute.GetCustomAttribute(type, typeof(ElementAttribute)))?.Name, type.Name);
            Assert.AreEqual(Elements.Passive, ((ElementAttribute)System.Attribute.GetCustomAttribute(typeof(BorrowOutcome), typeof(ElementAttribute)))?.Name);
            // 계약 검사(모든 Block)는 요소가 꺼져 있어도 요소의 Block까지 본다.
            Assert.IsTrue(BlockTypes.AllConcreteTypes(typeof(Effect)).Contains(typeof(ApplyStatusEffect)));
        }
    }
}
