using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 조건식 편집기. 식을 읽는 그대로 한 줄로 보여 주고, 세 가지 요소만으로 만든다:
    //   ① 조건 블럭(가장 작은 조건 하나)   ② 논리 연산자(AND · OR · NOT)   ③ 괄호
    //   예:  [처치 수 >= 10]  AND  ( [등급 == rare]  OR  NOT [태그 contains boss] )
    // 아래 버튼으로 블럭·연산자·괄호를 더한다. 연산자를 누르면 AND ↔ OR가 바뀌고, NOT을 누르면 떼어진다.
    // 항(블럭·괄호) 왼쪽의 손잡이(⠿)를 끌어 다른 자리에 놓는다: 순서를 바꾸거나, 괄호 안으로 넣거나, 괄호 밖으로 뺀다.
    // 괄호로 묶으려면 '+ 괄호'로 빈 괄호를 만들고 블럭들을 그 안으로 끌어 넣는다.
    // 연산자만 먼저 더하면 뒤에 빈칸('조건 선택')이 생기고, 빈칸을 눌러 블럭을 고른다.
    // AND가 OR보다 먼저 묶인다(A AND B OR C = (A AND B) OR C). 다르게 묶으려면 괄호를 쓴다.
    //
    // 저장 형식은 그대로다(AND·OR·NOT 블럭의 트리). 이 편집기는 트리를 한 줄 식으로 펼쳐 보이고,
    // 고칠 때마다 식 전체를 트리로 다시 짜서 넣는다. 조건(Condition)과 값 조건(ValueFilter)에 함께 쓴다.
    public sealed class GsExpressionEditor : VisualElement
    {
        // 한 종류의 조건식에서 AND·OR·NOT에 해당하는 블럭 형식과 그 안의 필드 이름.
        sealed class Logic
        {
            public Type Base, And, Or, Not;
            public string List, Child;
        }

        static readonly Logic[] Logics =
        {
            new Logic { Base = typeof(Condition), And = typeof(AllOfCondition), Or = typeof(AnyOfCondition), Not = typeof(NotCondition), List = "conditions", Child = "condition" },
            new Logic { Base = typeof(ValueFilter), And = typeof(AllFilter), Or = typeof(AnyFilter), Not = typeof(NotFilter), List = "filters", Child = "filter" },
            // 트리거 칸: 블럭은 이벤트 받기 · 조건 블럭(조건 충족으로 감싸 넣는다) · 사용. 칸의 잠금([TriggerSlot])에 없는 종류는 목록에 나오지 않는다.
            new Logic { Base = typeof(Trigger), And = typeof(AllTrigger), Or = typeof(AnyTrigger), Not = typeof(NotTrigger), List = "triggers", Child = "trigger" },
        };

        static Logic LogicFor(Type fieldType) => Logics.FirstOrDefault(l => l.Base == fieldType);

        // 이 형식의 필드를 조건식 편집기로 그리는지.
        public static bool Handles(Type fieldType) => fieldType != null && LogicFor(fieldType) != null;

        // 식의 항 하나: 조건 블럭(또는 빈칸)이거나 괄호. 앞에 NOT이 붙을 수 있다.
        internal sealed class Term
        {
            public bool Not;
            // 블럭: 트리 안의 자리(이미 있는 블럭)와 값. 새로 더한 블럭은 자리가 없다.
            public SerializedProperty Property;
            public object Block;
            // 괄호면 그 안의 식.
            public Sequence Group;
            public bool IsSlot => Group == null && Block == null;
        }

        // 항들과 그 사이의 연산자. Or[i]는 Terms[i]와 Terms[i + 1] 사이(참이면 OR, 거짓이면 AND).
        internal sealed class Sequence
        {
            public readonly List<Term> Terms = new List<Term>();
            public readonly List<bool> Or = new List<bool>();

            public void Add(Term term, bool or = false)
            {
                if (Terms.Count > 0) Or.Add(or);
                Terms.Add(term);
            }

            public void RemoveAt(int index)
            {
                Terms.RemoveAt(index);
                if (Or.Count == 0) return;
                // 지운 항의 앞 연산자를 함께 지운다(첫 항이면 뒤 연산자).
                Or.RemoveAt(Math.Max(0, index - 1));
            }

            // index 자리에 끼워 넣는다. 연산자는 앞 항과의 사이에(맨 앞이면 뒤 항과의 사이에) 생긴다.
            public void Insert(int index, Term term, bool or = false)
            {
                index = Math.Max(0, Math.Min(index, Terms.Count));
                if (Terms.Count > 0) Or.Insert(index == 0 ? 0 : index - 1, or);
                Terms.Insert(index, term);
            }

            // 이 식이나 그 안쪽 괄호 어딘가에 target이 있는지.
            public bool Contains(Sequence target) =>
                this == target || Terms.Any(t => t.Group != null && t.Group.Contains(target));
        }

        // 항을 끌어 옮긴다. 옮긴 항은 원래 앞에 있던 연산자를 가지고 간다(없으면 AND).
        // index는 옮기기 전 target의 자리(0 = 맨 앞, Terms.Count = 맨 뒤). 바뀐 것이 없으면 false.
        internal static bool MoveTerm(Sequence owner, int from, Sequence target, int index)
        {
            if (from < 0 || from >= owner.Terms.Count) return false;
            var term = owner.Terms[from];
            // 괄호를 자기 안으로는 넣을 수 없다.
            if (term.Group != null && term.Group.Contains(target)) return false;
            if (owner == target && (index == from || index == from + 1)) return false;
            var or = owner.Or.Count > 0 && owner.Or[Math.Max(0, from - 1)];
            owner.RemoveAt(from);
            if (owner == target && index > from) index--;
            target.Insert(index, term, or);
            return true;
        }

        readonly SerializedProperty _property;
        readonly Logic _logic;
        readonly Action _onValue;
        readonly Action _onStructure;
        Sequence _root;
        bool IsTrigger => _logic.Base == typeof(Trigger);
        string Noun => IsTrigger ? "트리거" : "조건";

        // 끌어 놓을 수 있는 자리: 식 하나(맨 바깥 줄 또는 괄호 안)와 그 안에 그려진 항들. 바깥 → 안쪽 순서로 쌓인다.
        sealed class Zone
        {
            public VisualElement Container;
            public Sequence Sequence;
            public readonly List<VisualElement> Terms = new List<VisualElement>();
        }

        readonly List<Zone> _zones = new List<Zone>();
        const float DragThreshold = 5f;
        Sequence _dragOwner;
        int _dragIndex = -1;
        VisualElement _dragSource;
        UnityEngine.Vector2 _dragStart;
        bool _dragging;
        VisualElement _ghost;
        VisualElement _marker;
        Zone _dropZone;
        int _dropIndex;

        public GsExpressionEditor(SerializedProperty property, Type fieldType, Action onValue, Action onStructure)
        {
            _property = property.Copy();
            _logic = LogicFor(fieldType);
            _onValue = onValue;
            _onStructure = onStructure;
            AddToClassList("gs-expr-editor");
            Rebuild();
        }

        // ── 트리 → 한 줄 식 ────────────────────────────────────

        bool IsAnd(object value) => value != null && value.GetType() == _logic.And;
        bool IsOr(object value) => value != null && value.GetType() == _logic.Or;
        bool IsNot(object value) => value != null && value.GetType() == _logic.Not;

        static IEnumerable<SerializedProperty> Elements(SerializedProperty node, string list)
        {
            var array = node.FindPropertyRelative(list);
            if (array == null) yield break;
            for (int i = 0; i < array.arraySize; i++) yield return array.GetArrayElementAtIndex(i);
        }

        internal Sequence Parse(SerializedProperty property)
        {
            var sequence = new Sequence();
            var value = property.managedReferenceValue;
            if (value == null) return sequence;
            if (IsAnd(value))
            {
                foreach (var child in Elements(property, _logic.List)) sequence.Add(ParseTerm(child));
            }
            else if (IsOr(value))
            {
                foreach (var child in Elements(property, _logic.List))
                {
                    var inner = child.managedReferenceValue;
                    var parts = IsAnd(inner) ? Elements(child, _logic.List).ToList() : null;
                    // OR 안의 AND(항이 둘 이상)는 괄호 없이 이어 쓴다: AND가 먼저 묶이므로 뜻이 같다.
                    if (parts != null && parts.Count >= 2)
                    {
                        for (int i = 0; i < parts.Count; i++) sequence.Add(ParseTerm(parts[i]), or: i == 0);
                    }
                    else sequence.Add(ParseTerm(child), or: true);
                }
            }
            else sequence.Add(ParseTerm(property));
            return sequence;
        }

        Term ParseTerm(SerializedProperty property)
        {
            var value = property.managedReferenceValue;
            if (IsNot(value))
            {
                var term = ParseTerm(property.FindPropertyRelative(_logic.Child));
                term.Not = !term.Not;
                return term;
            }
            if (IsAnd(value) || IsOr(value)) return new Term { Group = Parse(property) };
            return new Term { Property = property.Copy(), Block = value };
        }

        // ── 한 줄 식 → 트리 ────────────────────────────────────

        object Group(Type type, IEnumerable<object> children)
        {
            var node = Activator.CreateInstance(type);
            var list = (IList)type.GetField(_logic.List).GetValue(node);
            foreach (var child in children) list.Add(child);
            return node;
        }

        // 괄호 안의 식은 항이 하나뿐이거나 비어 있어도 괄호(AND 블럭)로 남긴다.
        internal object Build(Sequence sequence, bool asGroup)
        {
            // OR로 나뉜 덩어리마다 AND로 묶는다(AND가 먼저).
            var runs = new List<List<object>>();
            for (int i = 0; i < sequence.Terms.Count; i++)
            {
                if (i == 0 || sequence.Or[i - 1]) runs.Add(new List<object>());
                runs[runs.Count - 1].Add(BuildTerm(sequence.Terms[i]));
            }
            if (runs.Count == 0) return asGroup ? Group(_logic.And, Enumerable.Empty<object>()) : null;
            if (runs.Count == 1)
                return runs[0].Count == 1 && !asGroup ? runs[0][0] : Group(_logic.And, runs[0]);
            return Group(_logic.Or, runs.Select(run => run.Count == 1 ? run[0] : Group(_logic.And, run)));
        }

        object BuildTerm(Term term)
        {
            object node;
            if (term.Group != null) node = Build(term.Group, asGroup: true);
            else
            {
                // 이미 있던 블럭은 지금 값을 다시 읽어 사본으로 넣는다(같은 객체를 두 곳에서 가리키지 않게).
                var block = term.Property != null ? term.Property.managedReferenceValue : term.Block;
                node = block == null ? null : term.Property != null ? DataModel.Clone(block, null) : block;
            }
            if (!term.Not) return node;
            var not = Activator.CreateInstance(_logic.Not);
            _logic.Not.GetField(_logic.Child).SetValue(not, node);
            return not;
        }

        // 고친 식을 트리로 짜서 넣고 다시 그린다.
        void Commit()
        {
            _property.serializedObject.Update();
            var tree = Build(_root, asGroup: false);
            _property.managedReferenceValue = tree;
            _property.serializedObject.ApplyModifiedProperties();
            if (_onStructure != null) _onStructure();
            else
            {
                Rebuild();
                _onValue?.Invoke();
            }
        }

        // ── 그리기 ─────────────────────────────────────────────

        void Rebuild()
        {
            Clear();
            _zones.Clear();
            _dragging = false;
            _dragOwner = null;
            _property.serializedObject.Update();
            _root = Parse(_property);

            var line = Gs.Row("gs-expr");
            DrawSequence(line, _root);
            if (_root.Terms.Count == 0) line.Add(Gs.Label(Locked ? $"{Noun} 없음" : $"{Noun} 없음 — 아래에서 블럭을 더하세요", "gs-caption", "gs-expr__empty"));
            Add(line);

            // 구조 잠금(EditorPolicy): 블럭·연산자·괄호를 더하고 옮기는 것은 AI가 한다. 블럭 안의 값은 그대로 고친다.
            if (!Locked)
            {
                var bar = Gs.Row("gs-expr__bar");
                AddButtons(bar, _root);
                Add(bar);
            }

            var formula = Formula(_property.managedReferenceValue);
            if (!string.IsNullOrEmpty(formula) && _root.Terms.Count > 1)
            {
                var caption = Gs.Label(formula, "gs-caption", "gs-expr__formula");
                caption.tooltip = "지금 식(코드 형태). AND가 OR보다 먼저 묶입니다.";
                Add(caption);
            }
        }

        static string Formula(object value)
        {
            try
            {
                return value is Condition condition ? condition.Describe(EditorDatabase.Instance) : value is ValueFilter filter ? filter.Describe()
                    : value is Trigger trigger ? trigger.Describe(EditorDatabase.Instance) : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        static bool Locked => !EditorPolicy.CanEditStructure;

        void DrawSequence(VisualElement host, Sequence sequence)
        {
            var zone = new Zone { Container = host, Sequence = sequence };
            _zones.Add(zone);
            for (int i = 0; i < sequence.Terms.Count; i++)
            {
                if (i > 0)
                {
                    var index = i - 1;
                    var op = Gs.Label(sequence.Or[index] ? "OR" : "AND", "gs-expr__op");
                    op.tooltip = (sequence.Or[index] ? "또는" : "그리고") + (Locked ? "" : " · 눌러서 " + (sequence.Or[index] ? "AND" : "OR") + "로 바꾸기");
                    if (!Locked)
                        op.RegisterCallback<ClickEvent>(e =>
                        {
                            e.StopPropagation();
                            sequence.Or[index] = !sequence.Or[index];
                            Commit();
                        });
                    host.Add(op);
                }
                var element = DrawTerm(sequence, i);
                zone.Terms.Add(element);
                host.Add(element);
            }
        }

        // ── 끌어 놓기 ───────────────────────────────────────────

        // 항 왼쪽의 손잡이. 끌면 옮기기가 시작된다.
        VisualElement Grip(Sequence owner, int index, VisualElement source)
        {
            var grip = Gs.Label("⠿", "gs-expr__grip");
            grip.tooltip = "끌어서 옮기기(순서 바꾸기 · 괄호 안으로 넣기 · 밖으로 빼기)";
            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                _dragOwner = owner;
                _dragIndex = index;
                _dragSource = source;
                _dragStart = e.position;
                _dragging = false;
                grip.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragSource != source || !grip.HasPointerCapture(e.pointerId)) return;
                var position = (UnityEngine.Vector2)e.position;
                if (!_dragging)
                {
                    if ((position - _dragStart).magnitude < DragThreshold) return;
                    BeginDrag();
                }
                MoveDrag(position);
            });
            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragSource != source || !grip.HasPointerCapture(e.pointerId)) return;
                grip.ReleasePointer(e.pointerId);
                var moved = _dragging && _dropZone != null && MoveTerm(_dragOwner, _dragIndex, _dropZone.Sequence, _dropIndex);
                EndDrag();
                if (moved) Commit();
            });
            grip.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_dragSource == source) EndDrag();
            });
            return grip;
        }

        void BeginDrag()
        {
            _dragging = true;
            _dragSource.AddToClassList("gs-expr__term--dragging");
            _ghost = Gs.Box("gs-expr__ghost");
            _ghost.pickingMode = PickingMode.Ignore;
            _ghost.style.position = Position.Absolute;
            var term = _dragOwner.Terms[_dragIndex];
            _ghost.Add(Gs.Label(term.Group != null ? "( … )" : term.IsSlot ? "빈칸" : Formula(term.Property != null ? term.Property.managedReferenceValue : term.Block)));
            Add(_ghost);
            _marker = Gs.Box("gs-expr__marker");
            _marker.pickingMode = PickingMode.Ignore;
            _marker.style.position = Position.Absolute;
            _marker.style.display = DisplayStyle.None;
            Add(_marker);
        }

        void MoveDrag(UnityEngine.Vector2 position)
        {
            var local = this.WorldToLocal(position);
            _ghost.style.left = local.x + 12;
            _ghost.style.top = local.y + 10;

            _dropZone = null;
            _marker.style.display = DisplayStyle.None;
            var dragged = _dragOwner.Terms[_dragIndex];
            // 가장 안쪽의(나중에 쌓인) 자리부터 본다. 끌고 있는 괄호의 안쪽은 놓을 수 없는 자리다.
            for (int z = _zones.Count - 1; z >= 0; z--)
            {
                var zone = _zones[z];
                if (!zone.Container.worldBound.Contains(position)) continue;
                if (dragged.Group != null && dragged.Group.Contains(zone.Sequence)) continue;
                // 읽는 순서(왼쪽 → 오른쪽, 위 → 아래)로 포인터보다 뒤에 있는 첫 항 앞에 놓는다.
                var index = zone.Terms.Count;
                for (int i = 0; i < zone.Terms.Count; i++)
                {
                    var r = zone.Terms[i].worldBound;
                    if (position.y < r.yMin || (position.y <= r.yMax && position.x < r.center.x))
                    {
                        index = i;
                        break;
                    }
                }
                _dropZone = zone;
                _dropIndex = index;
                UnityEngine.Rect at;
                if (index < zone.Terms.Count)
                {
                    var r = zone.Terms[index].worldBound;
                    at = new UnityEngine.Rect(r.xMin - 4, r.yMin, 2, r.height);
                }
                else if (zone.Terms.Count > 0)
                {
                    var r = zone.Terms[zone.Terms.Count - 1].worldBound;
                    at = new UnityEngine.Rect(r.xMax + 2, r.yMin, 2, r.height);
                }
                else
                {
                    var r = zone.Container.worldBound;
                    at = new UnityEngine.Rect(r.xMin + 2, r.yMin + 2, 2, Math.Max(16f, r.height - 4));
                }
                var origin = this.WorldToLocal(at.position);
                _marker.style.display = DisplayStyle.Flex;
                _marker.style.left = origin.x;
                _marker.style.top = origin.y;
                _marker.style.width = at.width;
                _marker.style.height = at.height;
                return;
            }
        }

        void EndDrag()
        {
            _dragSource?.RemoveFromClassList("gs-expr__term--dragging");
            _ghost?.RemoveFromHierarchy();
            _marker?.RemoveFromHierarchy();
            _ghost = null;
            _marker = null;
            _dragging = false;
            _dragSource = null;
        }

        VisualElement DrawTerm(Sequence owner, int index)
        {
            var term = owner.Terms[index];
            var box = Gs.Row("gs-expr__term");
            if (!Locked) box.Add(Grip(owner, index, box));
            if (term.Not)
            {
                var not = Gs.Label("NOT", "gs-expr__op", "gs-expr__not");
                not.tooltip = Locked ? "아님" : "아님 · 눌러서 떼기";
                if (!Locked)
                    not.RegisterCallback<ClickEvent>(e =>
                    {
                        e.StopPropagation();
                        term.Not = false;
                        Commit();
                    });
                box.Add(not);
            }

            if (term.Group != null)
            {
                box.AddToClassList("gs-expr__term--group");
                box.Add(Gs.Label("(", "gs-expr__paren"));
                var inner = Gs.Row("gs-expr__group");
                DrawSequence(inner, term.Group);
                if (term.Group.Terms.Count == 0) inner.Add(Gs.Label(Locked ? "(빈 괄호)" : "블럭을 여기로 끌어 놓기", "gs-caption", "gs-expr__drop-hint"));
                if (!Locked)
                {
                    GsButton add = null;
                    add = new GsButton("+", () => GsDropdown.Show(add, AddItems(add, term.Group), searchable: false), GsButtonKind.Ghost);
                    add.AddToClassList("gs-expr__add");
                    add.tooltip = "괄호 안에 더하기";
                    inner.Add(add);
                }
                box.Add(inner);
                box.Add(Gs.Label(")", "gs-expr__paren"));
            }
            else if (term.IsSlot && Locked)
            {
                var empty = Gs.Label("(빈칸)", "gs-caption");
                empty.tooltip = EditorPolicy.LockedTip;
                box.Add(empty);
            }
            else if (term.IsSlot)
            {
                GsButton slot = null;
                slot = new GsButton($"{Noun} 선택", () => PickBlock(slot, block =>
                {
                    term.Property = null;
                    term.Block = block;
                    Commit();
                }), GsButtonKind.Ghost);
                slot.AddToClassList("gs-expr__slot");
                slot.tooltip = $"빈칸 · 눌러서 {Noun} 블럭 고르기";
                box.Add(slot);
            }
            else box.Add(DrawBlock(term));

            if (Locked) return box;
            GsButton more = null;
            more = new GsButton("⋯", () => GsDropdown.Show(more, TermMenu(more, owner, index), searchable: false), GsButtonKind.Ghost);
            more.AddToClassList("gs-expr__tool");
            more.tooltip = "NOT · 순서 · 괄호 · 삭제";
            box.Add(more);
            return box;
        }

        // 조건 블럭 하나: 문장의 단어마다 눌러서 바꾼다. 블럭 안에 또 조건식이 있으면(항목 조건 등) 그 아래에 같은 편집기로.
        // 트리거 칸의 "조건 충족"은 감싼 조건 블럭을 그대로 그린다(트리거 식 안에서 조건이 한 항으로 보인다).
        VisualElement DrawBlock(Term term)
        {
            var property = term.Property;
            if (term.Block is ConditionTrigger wrapped && property != null)
            {
                var inner = property.FindPropertyRelative(nameof(ConditionTrigger.condition));
                var condition = wrapped.condition;
                if (inner == null || condition == null) return Gs.Label("(조건 없음)", "gs-caption");
                if (condition is AllOfCondition || condition is AnyOfCondition || condition is NotCondition)
                    return new GsExpressionEditor(inner, typeof(Condition), _onValue, _onStructure);
                return DrawBlock(new Term { Property = inner, Block = condition });
            }
            var type = term.Block.GetType();
            var block = Gs.Col("gs-expr__block");
            block.tooltip = Explain(term.Block);
            var used = new HashSet<string>();
            var sentence = BlockTypes.Sentence(type);
            // 블럭 안의 [ShowIf] 칸(상태 효과 검사의 이벤트 필드 …)은 단어·칸을 바꾸면 바로 보이고 숨는다.
            var conditional = new ShowIfViews();
            var onValue = conditional.Wrap(_onValue);
            if (!string.IsNullOrEmpty(sentence) && sentence.Contains("{"))
            {
                block.Add(GsPropertyEditor.Sentence(property, sentence, used, onValue));
            }
            else
            {
                // 문장 틀이 없는 블럭: 이름 + 단순 값 칸들.
                var row = Gs.Row("gs-sentence");
                row.Add(Gs.Label(BlockTypes.DisplayName(type), "gs-sentence__word"));
                foreach (var child in PropertyUtil.Children(property))
                {
                    if (child.isArray && child.propertyType != SerializedPropertyType.String) continue;
                    if (child.propertyType == SerializedPropertyType.ManagedReference || child.hasVisibleChildren) continue;
                    used.Add(child.name);
                    var token = new GsInlineProperty(child);
                    token.AddToClassList("gs-token");
                    var tokenField = PropertyUtil.GetField(child, out _);
                    token.tooltip = GsPropertyEditor.HintOf(tokenField) ?? GsPropertyEditor.LabelOf(child, tokenField);
                    token.Changed += () => onValue();
                    conditional.Track(token, child);
                    row.Add(token);
                }
                block.Add(row);
            }
            foreach (var child in PropertyUtil.Children(property))
            {
                if (used.Contains(child.name)) continue;
                var field = PropertyUtil.GetField(child, out _);
                VisualElement view;
                if (child.propertyType == SerializedPropertyType.ManagedReference && field != null && Handles(field.FieldType))
                {
                    // 블럭 안의 조건식(예: 배열·리스트의 각 항목에 적용할 조건).
                    var nested = Gs.Col("gs-expr__nested");
                    nested.Add(Gs.Label(GsPropertyEditor.HintOf(field) ?? field.Name, "gs-caption"));
                    nested.Add(new GsExpressionEditor(child, field.FieldType, onValue, _onStructure));
                    view = nested;
                }
                else view = GsPropertyEditor.BuildProperty(child, onValue, _onStructure);
                conditional.Track(view, child);
                block.Add(view);
            }
            return block;
        }

        static string Explain(object value)
        {
            try
            {
                return value is Condition condition ? condition.Explain(EditorDatabase.Instance) : value is ValueFilter filter ? filter.Explain()
                    : value is Trigger trigger ? trigger.Explain(EditorDatabase.Instance) : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // ── 더하기 ─────────────────────────────────────────────

        // 아래 줄의 세 버튼: 블럭 · 논리 연산자 · 괄호.
        void AddButtons(VisualElement bar, Sequence target)
        {
            GsButton block = null;
            block = new GsButton("+ 블럭", () => PickBlock(block, picked => AddBlock(target, picked)), GsButtonKind.Ghost);
            block.tooltip = IsTrigger
                ? $"트리거 블럭({TriggerSlots.Text(AllowedTriggers)})을 식 끝에 더합니다. 앞의 항과는 AND로 이어지고, 연산자를 눌러 OR로 바꿀 수 있습니다."
                : "조건 블럭(가장 작은 조건 하나)을 식 끝에 더합니다. 앞의 항과는 AND로 이어지고, 연산자를 눌러 OR로 바꿀 수 있습니다.";
            bar.Add(block);
            GsButton op = null;
            op = new GsButton("+ 논리 연산자", () => GsDropdown.Show(op, OperatorItems(target), searchable: false), GsButtonKind.Ghost);
            op.tooltip = "AND · OR · NOT을 식 끝에 더합니다. 뒤에 빈칸이 생기고, 빈칸을 눌러 블럭을 고릅니다.";
            bar.Add(op);
            var paren = new GsButton("+ 괄호", () => AddParen(target), GsButtonKind.Ghost);
            paren.tooltip = "빈 괄호를 식 끝에 더합니다. 블럭의 손잡이(⠿)를 끌어 괄호 안에 넣거나, 괄호 안의 +로 채웁니다.";
            bar.Add(paren);
        }

        // 괄호 안 + 버튼의 목록: 같은 세 가지.
        List<GsDropdownItem> AddItems(VisualElement anchor, Sequence target)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("블럭", () => PickBlock(anchor, picked => AddBlock(target, picked))) { Detail = $"{Noun} 블럭 하나" },
            };
            items.AddRange(OperatorItems(target));
            items.Add(new GsDropdownItem("괄호", () => AddParen(target)) { Detail = "괄호 안에 또 괄호" });
            return items;
        }

        List<GsDropdownItem> OperatorItems(Sequence target) => new List<GsDropdownItem>
        {
            new GsDropdownItem("AND", () => AddOperator(target, or: false)) { Group = "논리 연산자", Detail = "그리고 — 앞과 뒤를 모두 만족" },
            new GsDropdownItem("OR", () => AddOperator(target, or: true)) { Group = "논리 연산자", Detail = "또는 — 앞과 뒤 중 하나만 만족" },
            new GsDropdownItem("NOT", () => AddNot(target)) { Group = "논리 연산자", Detail = "아님 — 바로 뒤의 조건을 뒤집는다" },
        };

        // 트리거 칸의 잠금(고를 수 있는 종류). 표시가 없으면 전부.
        TriggerKinds AllowedTriggers => TriggerSlots.TriggerSlotOf(_property)?.Allowed ?? TriggerKinds.All;

        // 논리 블럭(AND·OR·NOT)을 뺀, 고를 수 있는 블럭. 고르면 새 블럭 인스턴스를 넘긴다.
        void PickBlock(VisualElement anchor, Action<object> onPick)
        {
            List<GsDropdownItem> items;
            if (IsTrigger)
            {
                var allowed = AllowedTriggers;
                items = new List<GsDropdownItem>();
                if ((allowed & TriggerKinds.Event) != 0)
                    items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(EventTrigger)), () => onPick(new EventTrigger())) { Group = "트리거", Detail = BlockTypes.Description(typeof(EventTrigger)) });
                if ((allowed & TriggerKinds.Use) != 0)
                    items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(UseTrigger)), () => onPick(new UseTrigger())) { Group = "트리거", Detail = BlockTypes.Description(typeof(UseTrigger)) });
                if ((allowed & TriggerKinds.Intercept) != 0)
                    items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(InterceptTrigger)), () => onPick(new InterceptTrigger())) { Group = "트리거", Detail = BlockTypes.Description(typeof(InterceptTrigger)) });
                if ((allowed & TriggerKinds.Condition) != 0)
                    foreach (var type in BlockTypes.ConcreteTypes(typeof(Condition)).Where(t => t != typeof(AllOfCondition) && t != typeof(AnyOfCondition) && t != typeof(NotCondition)))
                    {
                        var picked = type;
                        items.Add(new GsDropdownItem(BlockTypes.DisplayName(type), () => onPick(new ConditionTrigger { condition = (Condition)Activator.CreateInstance(picked) }))
                        {
                            Group = "조건 충족 · " + BlockTypes.Category(type),
                            Detail = BlockTypes.Description(type),
                        });
                    }
            }
            else
            {
                items = BlockTypes.ConcreteTypes(_logic.Base)
                    .Where(t => t != _logic.And && t != _logic.Or && t != _logic.Not)
                    .Select(type =>
                    {
                        var picked = type;
                        return new GsDropdownItem(BlockTypes.DisplayName(type), () => onPick(Activator.CreateInstance(picked))) { Group = BlockTypes.Category(type), Detail = BlockTypes.Description(type) };
                    }).ToList();
            }
            if (items.Count == 0) items.Add(new GsDropdownItem("사용할 수 있는 블럭이 없습니다") { Disabled = true });
            GsDropdown.Show(anchor, items, placeholder: $"{Noun} 블럭 검색");
        }

        static Term LastSlot(Sequence target) =>
            target.Terms.Count > 0 && target.Terms[target.Terms.Count - 1].IsSlot ? target.Terms[target.Terms.Count - 1] : null;

        // 끝이 빈칸이면 그 빈칸을 채우고, 아니면 AND로 이어 붙인다.
        void AddBlock(Sequence target, object block)
        {
            var slot = LastSlot(target);
            if (slot != null) slot.Block = block;
            else target.Add(new Term { Block = block });
            Commit();
        }

        // 연산자 + 빈칸. 끝이 이미 빈칸이면 그 앞의 연산자만 바꾼다. 식이 비어 있으면 빈칸만 생긴다.
        void AddOperator(Sequence target, bool or)
        {
            if (LastSlot(target) != null && target.Or.Count > 0) target.Or[target.Or.Count - 1] = or;
            else target.Add(new Term(), or);
            Commit();
        }

        // 끝이 빈칸이면 그 빈칸에 NOT을 붙이고, 아니면 'AND NOT 빈칸'을 더한다.
        void AddNot(Sequence target)
        {
            var slot = LastSlot(target);
            if (slot != null) slot.Not = !slot.Not;
            else target.Add(new Term { Not = true });
            Commit();
        }

        void AddParen(Sequence target)
        {
            var slot = LastSlot(target);
            if (slot != null) slot.Group = new Sequence();
            else target.Add(new Term { Group = new Sequence() });
            Commit();
        }

        // ── 항 메뉴(⋯) ─────────────────────────────────────────

        List<GsDropdownItem> TermMenu(VisualElement anchor, Sequence owner, int index)
        {
            var term = owner.Terms[index];
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem(term.Not ? "NOT 떼기" : "NOT 붙이기", () =>
                {
                    term.Not = !term.Not;
                    Commit();
                }) { Detail = "이 항을 뒤집는다(아님)" },
            };
            if (index > 0) items.Add(new GsDropdownItem("앞으로", () => Swap(owner, index, index - 1)));
            if (index < owner.Terms.Count - 1) items.Add(new GsDropdownItem("뒤로", () => Swap(owner, index, index + 1)));
            if (term.Group == null)
            {
                items.Add(new GsDropdownItem("괄호로 감싸기", () =>
                {
                    var inner = new Sequence();
                    inner.Add(new Term { Property = term.Property, Block = term.Block, Not = term.Not });
                    term.Property = null;
                    term.Block = null;
                    term.Not = false;
                    term.Group = inner;
                    Commit();
                }) { Detail = "이 항을 괄호 안에 넣는다(괄호 안에 더 이어 쓸 수 있다)" });
                if (!term.IsSlot)
                    items.Add(new GsDropdownItem("다른 블럭으로 바꾸기", () => PickBlock(anchor, block =>
                    {
                        term.Property = null;
                        term.Block = block;
                        Commit();
                    })));
            }
            else
            {
                items.Add(new GsDropdownItem("괄호 풀기", () =>
                {
                    // 괄호 안의 항들을 이 자리에 그대로 펼친다.
                    var inner = term.Group;
                    owner.Terms.RemoveAt(index);
                    owner.Terms.InsertRange(index, inner.Terms);
                    owner.Or.InsertRange(Math.Min(index, owner.Or.Count), inner.Or);
                    if (inner.Terms.Count == 0 && owner.Or.Count > 0) owner.Or.RemoveAt(Math.Max(0, Math.Min(index, owner.Or.Count) - 1));
                    Commit();
                }) { Detail = "괄호만 없애고 안의 내용은 남긴다" });
            }
            items.Add(new GsDropdownItem("삭제", () =>
            {
                owner.RemoveAt(index);
                Commit();
            }) { Detail = term.Group != null ? "괄호와 그 안의 내용을 모두 지운다" : null });
            return items;
        }

        void Swap(Sequence owner, int a, int b)
        {
            var term = owner.Terms[a];
            owner.Terms[a] = owner.Terms[b];
            owner.Terms[b] = term;
            Commit();
        }
    }
}
