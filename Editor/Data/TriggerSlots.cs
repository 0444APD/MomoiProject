using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 트리거·결과 칸 찾기(검증·편집 화면 공용). 칸의 잠금은 필드의 [TriggerSlot]·[OutcomeSlot]이 정한다.
    //   Trigger 필드 / List<Outcome> 필드에 직접 붙이거나, List<TriggerRule> 필드에 붙이면 규칙마다의 when·then에 적용된다.
    //   기능 목록처럼 관리 참조 목록 안의 객체에 붙은 칸도 찾는다(아이템의 '사용' 기능 등).
    public static class TriggerSlots
    {
        public sealed class Slot
        {
            // 칸이 들어 있는 객체(같은 객체의 트리거 칸과 결과 칸을 짝짓는다).
            public object Owner;
            public string Label;
            public Trigger Trigger;
            public TriggerKinds TriggerAllowed = TriggerKinds.All;
            public List<Outcome> Outcomes;
            public OutcomeKinds OutcomeAllowed = OutcomeKinds.All;
            public bool OutcomeFixed;
        }

        public static IEnumerable<Slot> Of(object root) => Walk(root, "", 0);

        static IEnumerable<Slot> Walk(object target, string prefix, int depth)
        {
            if (target == null || depth > 4) yield break;
            foreach (var field in Fields(target.GetType()))
            {
                var trigger = field.GetCustomAttribute<TriggerSlotAttribute>();
                var outcome = field.GetCustomAttribute<OutcomeSlotAttribute>();
                var value = field.GetValue(target);
                var label = prefix + (field.GetCustomAttribute<FieldLabelAttribute>()?.Text ?? field.Name);
                if (trigger != null || outcome != null)
                {
                    var triggerAllowed = trigger?.Allowed ?? TriggerKinds.All;
                    var outcomeAllowed = outcome?.Allowed ?? OutcomeKinds.All;
                    var isFixed = outcome?.Fixed ?? false;
                    switch (value)
                    {
                        case Trigger found:
                            yield return new Slot { Owner = target, Label = label, Trigger = found, TriggerAllowed = triggerAllowed };
                            break;
                        case Condition condition:
                            yield return new Slot { Owner = target, Label = label, Trigger = new ConditionTrigger { condition = condition }, TriggerAllowed = triggerAllowed };
                            break;
                        case IEnumerable<TriggerRule> rules:
                            var i = 0;
                            foreach (var rule in rules)
                            {
                                i++;
                                if (rule == null) continue;
                                yield return new Slot
                                {
                                    Owner = target, Label = $"{label} {i}", Trigger = rule.when, TriggerAllowed = triggerAllowed,
                                    Outcomes = rule.then ?? new List<Outcome>(), OutcomeAllowed = outcomeAllowed, OutcomeFixed = isFixed,
                                };
                            }
                            break;
                        case IEnumerable<StatRule> shortRules:
                            var j = 0;
                            foreach (var rule in shortRules.Where(r => r != null).Select(r => r.ToTriggerRule()))
                                yield return new Slot
                                {
                                    Owner = target, Label = $"{label} {++j}", Trigger = rule.when, TriggerAllowed = triggerAllowed,
                                    Outcomes = rule.then, OutcomeAllowed = outcomeAllowed, OutcomeFixed = isFixed,
                                };
                            break;
                        case IEnumerable list when !(value is string):
                            yield return new Slot { Owner = target, Label = label, Outcomes = list.OfType<Outcome>().ToList(), OutcomeAllowed = outcomeAllowed, OutcomeFixed = isFixed };
                            break;
                    }
                    continue;
                }
                // 관리 참조 목록·객체 안쪽(기능 목록 등)도 본다.
                if (!field.IsDefined(typeof(SerializeReference), false)) continue;
                if (value is IList items)
                {
                    foreach (var item in items)
                        if (item != null && !(item is UnityEngine.Object))
                            foreach (var slot in Walk(item, $"{label} › {BlockTypes.DisplayName(item.GetType())} › ", depth + 1))
                                yield return slot;
                }
                else if (value != null && !(value is UnityEngine.Object) && !(value is Trigger) && !(value is Condition) && !(value is ValueFilter))
                {
                    foreach (var slot in Walk(value, label + " › ", depth + 1)) yield return slot;
                }
            }
        }

        static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var t = type; t != null && t != typeof(object) && t != typeof(ScriptableObject); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), false) || field.IsDefined(typeof(SerializeReference), false)))
                        yield return field;
        }

        public static string Text(TriggerKinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & TriggerKinds.Event) != 0) parts.Add("이벤트 받기");
            if ((kinds & TriggerKinds.Condition) != 0) parts.Add("조건 충족");
            if ((kinds & TriggerKinds.Use) != 0) parts.Add("사용");
            return parts.Count == 0 ? "없음" : string.Join(" · ", parts);
        }

        public static string Text(OutcomeKinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & OutcomeKinds.Record) != 0) parts.Add("로그에 기록");
            if ((kinds & OutcomeKinds.PlayerData) != 0) parts.Add("플레이어 데이터에 주기");
            if ((kinds & OutcomeKinds.Targeted) != 0) parts.Add("대상에게 효과");
            if ((kinds & OutcomeKinds.Coded) != 0) parts.Add("코딩한 결과");
            if ((kinds & OutcomeKinds.Event) != 0) parts.Add("이벤트 쏘기");
            return parts.Count == 0 ? "없음" : string.Join(" · ", parts);
        }

        // ── 편집 화면: 이 칸의 잠금 ────────────────────────────

        // 트리거·결과 칸(또는 그 안쪽)의 잠금. 칸 필드에 없으면 위로 올라가며 찾는다(규칙 목록의 when·then은 목록 필드의 표시를 따른다).
        public static TriggerSlotAttribute TriggerSlotOf(SerializedProperty property) => Find<TriggerSlotAttribute>(property);
        public static OutcomeSlotAttribute OutcomeSlotOf(SerializedProperty property) => Find<OutcomeSlotAttribute>(property);

        static T Find<T>(SerializedProperty property) where T : Attribute
        {
            // 칸 자신 → 규칙(목록 요소) → 규칙 목록까지만 본다(더 바깥의 다른 칸 표시를 따라가지 않게).
            var so = property.serializedObject;
            var path = property.propertyPath;
            for (int step = 0; step < 3 && !string.IsNullOrEmpty(path); step++, path = Parent(path))
            {
                var current = so.FindProperty(path);
                if (current == null) continue;
                var found = PropertyUtil.GetField(current, out _)?.GetCustomAttribute<T>();
                if (found != null) return found;
            }
            return null;
        }

        static string Parent(string path)
        {
            var index = path.LastIndexOf('.');
            if (index < 0) return null;
            var parent = path.Substring(0, index);
            if (parent.EndsWith(".Array", StringComparison.Ordinal)) parent = parent.Substring(0, parent.Length - 6);
            return parent;
        }
    }
}
