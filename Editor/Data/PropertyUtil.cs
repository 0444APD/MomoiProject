using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // SerializedProperty ↔ 실제 C# 필드 정보 연결.
    public static class PropertyUtil
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // 필드 정보와 실제 값의 타입(관리 참조면 구체 타입)을 찾는다.
        public static FieldInfo GetField(SerializedProperty property, out Type valueType)
        {
            valueType = null;
            var so = property.serializedObject;
            if (so.targetObject == null) return null;

            Type type = so.targetObject.GetType();
            FieldInfo field = null;
            var tokens = property.propertyPath.Split('.');
            var pathSoFar = "";

            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token == "Array" && i + 1 < tokens.Length && tokens[i + 1].StartsWith("data[", StringComparison.Ordinal))
                {
                    pathSoFar += "." + token + "." + tokens[i + 1];
                    i++;
                    type = ElementType(type);
                    type = ResolveManagedType(so, pathSoFar, type);
                    continue;
                }

                field = FindFieldInHierarchy(type, token);
                if (field == null) return null;
                pathSoFar = pathSoFar.Length == 0 ? token : pathSoFar + "." + token;
                type = ResolveManagedType(so, pathSoFar, field.FieldType);
            }

            valueType = type;
            return field;
        }

        public static FieldInfo FindFieldInHierarchy(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var field = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        public static Type ElementType(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return type;
        }

        static Type ResolveManagedType(SerializedObject so, string path, Type declared)
        {
            var sp = so.FindProperty(path);
            if (sp != null && sp.propertyType == SerializedPropertyType.ManagedReference && sp.managedReferenceValue != null)
                return sp.managedReferenceValue.GetType();
            return declared;
        }

        public static bool IsManagedReferenceList(FieldInfo field) =>
            field != null && field.IsDefined(typeof(SerializeReference), true) &&
            (field.FieldType.IsArray || (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>)));

        // 직계 자식 속성들(보이는 것만).
        public static IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var iterator = property.Copy();
            var end = property.GetEndProperty();
            if (!iterator.NextVisible(true)) yield break;
            while (!SerializedProperty.EqualContents(iterator, end))
            {
                yield return iterator.Copy();
                if (!iterator.NextVisible(false)) yield break;
            }
        }

        // 오브젝트의 최상위 속성들(m_Script 제외).
        public static IEnumerable<SerializedProperty> TopLevel(SerializedObject so)
        {
            var iterator = so.GetIterator();
            if (!iterator.NextVisible(true)) yield break;
            do
            {
                if (iterator.propertyPath == "m_Script") continue;
                yield return iterator.Copy();
            }
            while (iterator.NextVisible(false));
        }

        public static string Nicify(string name) => ObjectNames.NicifyVariableName(name);

        public static string FormatNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // 표시용 요약 문자열.
        public static string Summary(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String: return property.stringValue;
                case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float: return FormatNumber(property.type == "double" ? property.doubleValue : property.floatValue);
                case SerializedPropertyType.Boolean: return property.boolValue ? "예" : "아니오";
                case SerializedPropertyType.Enum:
                    var names = property.enumDisplayNames;
                    return property.enumValueIndex >= 0 && property.enumValueIndex < names.Length ? names[property.enumValueIndex] : "?";
                case SerializedPropertyType.ObjectReference: return property.objectReferenceValue != null ? property.objectReferenceValue.name : "";
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(property.colorValue);
                case SerializedPropertyType.ManagedReference:
                    // 조건식은 자연어(기본) / 코드(Alt) 두 가지로.
                    switch (property.managedReferenceValue)
                    {
                        case null: return "";
                        case Condition condition: return GBible.EditorTools.UI.Gs.Dual(condition.Natural(EditorDatabase.Instance), condition.Describe(EditorDatabase.Instance));
                        case ValueFilter filter: return GBible.EditorTools.UI.Gs.Dual(filter.Natural(), filter.Describe());
                        case Reward reward: return reward.Describe(EditorDatabase.Instance);
                        default: return BlockTypes.DisplayName(property.managedReferenceValue.GetType());
                    }
            }
            if (property.isArray && property.propertyType == SerializedPropertyType.Generic) return $"{property.arraySize}개";
            return property.hasVisibleChildren ? "…" : property.type;
        }

        public static object GetBoxed(SerializedProperty property)
        {
            try { return property.boxedValue; }
            catch { return null; }
        }

        public static IList AsList(object value) => value as IList;
    }
}
