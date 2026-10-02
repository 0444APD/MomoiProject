using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 조건식 편집기: AND·OR·NOT 트리를 한 줄 식(블럭 · 연산자 · 괄호)으로 펼치고, 다시 같은 뜻의 트리로 짠다.
    public sealed class ExpressionEditorTests
    {
        AchievementData _data;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<AchievementData>();
            _data.hideFlags = HideFlags.DontSave;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_data);

        static StatCompareCondition Stat(string id, double value) => new StatCompareCondition { stat = id, op = CompareOp.AtLeast, value = value };

        GsExpressionEditor Editor(out SerializedProperty property)
        {
            var so = new SerializedObject(_data);
            property = so.FindProperty(nameof(AchievementData.condition));
            return new GsExpressionEditor(property, typeof(Condition), null, null);
        }

        [Test]
        public void OR_안의_AND는_괄호_없이_이어_쓴다()
        {
            // (a AND b) OR NOT c
            _data.condition = new AnyOfCondition
            {
                conditions =
                {
                    new AllOfCondition { conditions = { Stat("a", 1), Stat("b", 2) } },
                    new NotCondition { condition = Stat("c", 3) },
                },
            };
            var editor = Editor(out var property);
            var sequence = editor.Parse(property);
            Assert.AreEqual(3, sequence.Terms.Count);
            CollectionAssert.AreEqual(new[] { false, true }, sequence.Or, "a AND b OR (NOT c)");
            Assert.IsTrue(sequence.Terms.All(t => t.Group == null), "괄호 없이");
            Assert.IsTrue(sequence.Terms[2].Not);
            Assert.IsFalse(sequence.Terms[0].Not);

            // 다시 짜면 같은 뜻의 트리.
            var rebuilt = (Condition)editor.Build(sequence, asGroup: false);
            Assert.AreEqual(_data.condition.Describe(EditorDatabase.Instance), rebuilt.Describe(EditorDatabase.Instance));
            Assert.IsInstanceOf<AnyOfCondition>(rebuilt);
        }

        [Test]
        public void AND_안의_OR는_괄호로_보인다()
        {
            // a AND (b OR c)
            _data.condition = new AllOfCondition
            {
                conditions = { Stat("a", 1), new AnyOfCondition { conditions = { Stat("b", 2), Stat("c", 3) } } },
            };
            var editor = Editor(out var property);
            var sequence = editor.Parse(property);
            Assert.AreEqual(2, sequence.Terms.Count);
            Assert.IsFalse(sequence.Or[0]);
            Assert.IsNotNull(sequence.Terms[1].Group, "괄호");
            Assert.AreEqual(2, sequence.Terms[1].Group.Terms.Count);
            Assert.IsTrue(sequence.Terms[1].Group.Or[0]);
            var rebuilt = (Condition)editor.Build(sequence, asGroup: false);
            Assert.AreEqual(_data.condition.Describe(EditorDatabase.Instance), rebuilt.Describe(EditorDatabase.Instance));
        }

        [Test]
        public void 연산자를_바꾸면_AND가_먼저_묶인다()
        {
            // a AND b AND c 에서 두 번째 연산자를 OR로: (a AND b) OR c
            _data.condition = new AllOfCondition { conditions = { Stat("a", 1), Stat("b", 2), Stat("c", 3) } };
            var editor = Editor(out var property);
            var sequence = editor.Parse(property);
            sequence.Or[1] = true;
            var rebuilt = editor.Build(sequence, asGroup: false);
            Assert.IsInstanceOf<AnyOfCondition>(rebuilt);
            var any = (AnyOfCondition)rebuilt;
            Assert.AreEqual(2, any.conditions.Count);
            Assert.IsInstanceOf<AllOfCondition>(any.conditions[0]);
            Assert.AreEqual(2, ((AllOfCondition)any.conditions[0]).conditions.Count);
            Assert.IsInstanceOf<StatCompareCondition>(any.conditions[1]);
        }

        [Test]
        public void 블럭_하나는_그대로_빈칸과_빈_괄호는_남는다()
        {
            _data.condition = Stat("a", 1);
            var editor = Editor(out var property);
            var sequence = editor.Parse(property);
            Assert.AreEqual(1, sequence.Terms.Count);
            Assert.IsInstanceOf<StatCompareCondition>(editor.Build(sequence, asGroup: false), "블럭 하나는 감싸지 않는다");

            // 연산자만 먼저 더한 상태: a AND (빈칸)
            sequence.Add(new GsExpressionEditor.Term());
            var withSlot = (AllOfCondition)editor.Build(sequence, asGroup: false);
            Assert.AreEqual(2, withSlot.conditions.Count);
            Assert.IsNull(withSlot.conditions[1], "빈칸은 비어 있는 자리로 남는다");

            // 빈 괄호: a AND (빈칸) AND ( )
            sequence.Add(new GsExpressionEditor.Term { Group = new GsExpressionEditor.Sequence() });
            var withParen = (AllOfCondition)editor.Build(sequence, asGroup: false);
            Assert.IsInstanceOf<AllOfCondition>(withParen.conditions[2], "빈 괄호는 빈 AND 블럭");

            // 식이 비면 조건 없음.
            Assert.IsNull(editor.Build(new GsExpressionEditor.Sequence(), asGroup: false));
        }

        [Test]
        public void 빈칸과_빈_괄호를_다시_읽는다()
        {
            _data.condition = new AllOfCondition { conditions = { Stat("a", 1), null, new AllOfCondition() } };
            var editor = Editor(out var property);
            var sequence = editor.Parse(property);
            Assert.AreEqual(3, sequence.Terms.Count);
            Assert.IsTrue(sequence.Terms[1].IsSlot);
            Assert.IsNotNull(sequence.Terms[2].Group);
            Assert.AreEqual(0, sequence.Terms[2].Group.Terms.Count);
            // 화면: 블럭 · 연산자 둘 · 빈칸 · 괄호, 그리고 아래의 더하기 버튼 셋.
            Assert.AreEqual(2, editor.Query<Label>(className: "gs-expr__op").ToList().Count);
            Assert.AreEqual(1, editor.Query(className: "gs-expr__slot").ToList().Count);
            Assert.AreEqual(2, editor.Query<Label>(className: "gs-expr__paren").ToList().Count);
            Assert.AreEqual(3, editor.Q(className: "gs-expr__bar").childCount);
        }

        static GsExpressionEditor.Term Block(string id) => new GsExpressionEditor.Term { Block = Stat(id, 1) };

        static string Ids(GsExpressionEditor.Sequence sequence) =>
            string.Join(" ", sequence.Terms.Select((t, i) =>
                (i > 0 ? (sequence.Or[i - 1] ? "OR " : "AND ") : "") + (t.Group != null ? "(" + Ids(t.Group) + ")" : ((StatCompareCondition)t.Block).stat)));

        [Test]
        public void 항을_끌어_순서를_바꾼다()
        {
            var sequence = new GsExpressionEditor.Sequence();
            sequence.Add(Block("a"));
            sequence.Add(Block("b"));
            sequence.Add(Block("c"), or: true);
            Assert.AreEqual("a AND b OR c", Ids(sequence));

            Assert.IsFalse(GsExpressionEditor.MoveTerm(sequence, 1, sequence, 1), "제자리");
            Assert.IsFalse(GsExpressionEditor.MoveTerm(sequence, 1, sequence, 2), "제자리(바로 뒤)");

            // c를 맨 앞으로: 앞에 있던 연산자(OR)를 가지고 간다.
            Assert.IsTrue(GsExpressionEditor.MoveTerm(sequence, 2, sequence, 0));
            Assert.AreEqual("c OR a AND b", Ids(sequence));
            // c를 맨 뒤로.
            Assert.IsTrue(GsExpressionEditor.MoveTerm(sequence, 0, sequence, 3));
            Assert.AreEqual("a AND b OR c", Ids(sequence));
            Assert.AreEqual(sequence.Terms.Count - 1, sequence.Or.Count, "연산자는 항 사이마다 하나");
        }

        [Test]
        public void 항을_괄호_안으로_넣고_밖으로_뺀다()
        {
            var sequence = new GsExpressionEditor.Sequence();
            sequence.Add(Block("a"));
            sequence.Add(Block("b"));
            sequence.Add(Block("c"));
            var paren = new GsExpressionEditor.Term { Group = new GsExpressionEditor.Sequence() };
            sequence.Add(paren);
            Assert.AreEqual("a AND b AND c AND ()", Ids(sequence));

            // b와 c를 빈 괄호 안으로.
            Assert.IsTrue(GsExpressionEditor.MoveTerm(sequence, 1, paren.Group, 0));
            Assert.IsTrue(GsExpressionEditor.MoveTerm(sequence, 1, paren.Group, 1));
            Assert.AreEqual("a AND (b AND c)", Ids(sequence));
            Assert.AreEqual(1, sequence.Or.Count);
            Assert.AreEqual(1, paren.Group.Or.Count);

            // 괄호를 자기 안으로는 못 넣는다.
            Assert.IsFalse(GsExpressionEditor.MoveTerm(sequence, 1, paren.Group, 0));

            // c를 괄호 밖 맨 앞으로.
            Assert.IsTrue(GsExpressionEditor.MoveTerm(paren.Group, 1, sequence, 0));
            Assert.AreEqual("c AND a AND (b)", Ids(sequence));
            Assert.AreEqual(0, paren.Group.Or.Count);
        }

        [Test]
        public void 값_조건도_같은_편집기를_쓴다()
        {
            Assert.IsTrue(GsExpressionEditor.Handles(typeof(Condition)));
            Assert.IsTrue(GsExpressionEditor.Handles(typeof(ValueFilter)));
            Assert.IsFalse(GsExpressionEditor.Handles(typeof(Reward)));
            Assert.IsFalse(GsExpressionEditor.Handles(null));
        }
    }
}
