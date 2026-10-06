using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;

namespace GBible.EditorTools
{
    // 값 칸의 "목록에서 고르기" 규칙 한 곳. 데이터베이스 편집 칸·탐색기·이벤트 호출가 같은 규칙을 쓴다.
    // ID 참조([DataRef]·[StatRef]·[AchievementRef]·[PlayerDataRef]), 이벤트 타입, 저장 형식, 키워드([Keyword]), enum이면 목록을 돌려주고,
    // 자유 입력 칸이면 null. enum은 DB 편집 칸과 같은 보이는 이름([InspectorName])으로 보이고, 값은 enum 그대로다.
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
                {
                    // 형식을 고르면 그 형식의 인스턴스를 만드는 칸(InstanceField)은 클래스만 — 기본 형식·enum은 [SerializeReference]에 넣을 수 없다.
                    var forInstance = !string.IsNullOrEmpty(member.GetCustomAttribute<DataTypeRefAttribute>()?.InstanceField);
                    return StorableTypes.All().Where(t => !forInstance || (!t.IsValueType && t != typeof(string)))
                        .Select(t => new Choice { Label = StorableTypes.Label(t), Caption = t.Namespace, Group = StorableTypes.GroupOf(t), Value = t.FullName }).ToList();
                }
                // 키워드 칸(또는 키워드 목록의 요소): 그 그룹의 키워드. 비우기도 고를 수 있다.
                var group = type == typeof(string) ? KeywordTools.GroupOf(member as FieldInfo) : null;
                if (group != null)
                {
                    var keywords = new List<Choice> { new Choice { Label = "(비움)", Value = "" } };
                    keywords.AddRange(KeywordTools.KeywordsOf(group).Select(k => new Choice { Label = k.Id, Caption = k.Name, Value = k.Id }));
                    return keywords;
                }
            }
            if (type != null && type.IsEnum)
                return Enum.GetValues(type).Cast<object>().Select(v => new Choice { Label = EnumLabel(v), Caption = v.ToString(), Value = v }).ToList();
            return null;
        }

        // 표시 글자: ID 참조면 이름으로, enum이면 보이는 이름으로.
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
            if (value is Enum) return EnumLabel(value);
            return PlayerDataStore.Format(value);
        }

        // enum 값의 보이는 이름: [InspectorName]이 있으면 그것, 없으면 Unity 편집 칸과 같은 이름(enumDisplayNames 규칙). 플래그 조합은 코드 이름.
        public static string EnumLabel(object value)
        {
            if (!(value is Enum)) return PlayerDataStore.Format(value);
            var type = value.GetType();
            var name = Enum.GetName(type, value);
            if (name == null) return value.ToString();
            var shown = type.GetField(name)?.GetCustomAttribute<UnityEngine.InspectorNameAttribute>()?.displayName;
            return shown ?? UnityEditor.ObjectNames.NicifyVariableName(name);
        }

        // 글자로 적힌 enum 값(코드 이름)의 보이는 이름. 모르는 값이면 그대로.
        public static string EnumLabel(Type enumType, string code)
        {
            if (enumType == null || !enumType.IsEnum || string.IsNullOrEmpty(code) || !Enum.IsDefined(enumType, code)) return code;
            return EnumLabel(Enum.Parse(enumType, code));
        }
    }
}
