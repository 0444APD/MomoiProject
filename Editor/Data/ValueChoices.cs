using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;

namespace GBible.EditorTools
{
    // 값 칸의 "목록에서 고르기" 규칙 한 곳. 데이터베이스 편집 칸·탐색기·이벤트 호출가 같은 규칙을 쓴다.
    // ID 참조([DataRef]·[StatRef]·[AchievementRef]·[PlayerDataRef]), 이벤트 타입, 저장 형식, enum이면 목록을 돌려주고,
    // 자유 입력 칸이면 null.
    public static class ValueChoices
    {
        public sealed class Choice
        {
            public string Label;
            public string Caption;
            public string Group;
            public object Value;
        }

        public static IReadOnlyList<Choice> For(MemberInfo member, Type valueType)
        {
            var db = EditorDatabase.Instance;
            var type = Nullable.GetUnderlyingType(valueType ?? typeof(object)) ?? valueType;
            if (member != null)
            {
                var refType = EditorDatabase.RefTypeOf(member as FieldInfo) ??
                              (member.IsDefined(typeof(PlayerDataRefAttribute), false) ? typeof(PlayerDataEntry) : null);
                if (refType != null)
                    return (member is FieldInfo field && EditorDatabase.RefTypeOf(field) != null ? db.RefCandidates(field) : db.OfType(refType))
                        .OrderBy(d => db.GroupLabel(d) ?? "").ThenBy(d => d.Id).Select(d => new Choice { Label = d.Id, Group = db.GroupLabel(d), Value = d.Id }).ToList();
                if (member.IsDefined(typeof(EventTypeRefAttribute), false))
                    return ProjectCode.Events().OrderBy(EventBus.DisplayNameOf)
                        .Select(t => new Choice { Label = EventBus.DisplayNameOf(t), Caption = t.Name, Value = t.FullName }).ToList();
                if (member.IsDefined(typeof(IntentTypeRefAttribute), false))
                    return ProjectCode.Intents().OrderBy(Intents.DisplayNameOf)
                        .Select(t => new Choice { Label = Intents.DisplayNameOf(t), Caption = t.Name, Value = t.FullName }).ToList();
                if (member.IsDefined(typeof(DataTypeRefAttribute), false))
                    return StorableTypes.All().Select(t => new Choice { Label = StorableTypes.Label(t), Caption = t.Namespace, Group = StorableTypes.GroupOf(t), Value = t.FullName }).ToList();
            }
            if (type != null && type.IsEnum)
                return Enum.GetValues(type).Cast<object>().Select(v => new Choice { Label = v.ToString(), Value = v }).ToList();
            return null;
        }

        // 표시 글자: ID 참조면 이름으로.
        public static string Display(MemberInfo member, object value)
        {
            if (value is string id && !string.IsNullOrEmpty(id) && member is FieldInfo field)
            {
                var refType = EditorDatabase.RefTypeOf(field) ?? (field.IsDefined(typeof(PlayerDataRefAttribute), false) ? typeof(PlayerDataEntry) : null);
                if (refType != null)
                {
                    var target = EditorDatabase.Instance.FindById(id);
                    return target != null ? target.DisplayName : $"없는 항목: {id}";
                }
                if (field.IsDefined(typeof(EventTypeRefAttribute), false))
                {
                    var type = TypeResolver.Find(id);
                    return type != null ? EventBus.DisplayNameOf(type) : $"없는 이벤트: {id}";
                }
            }
            return PlayerDataStore.Format(value);
        }
    }
}
