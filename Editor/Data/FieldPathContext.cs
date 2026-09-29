using System;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using GameSystems.EditorTools.UI;
using UnityEditor;

namespace GameSystems.EditorTools
{
    // 편집 중인 필드 경로 칸의 "뿌리 타입"을 찾는다.
    // 같은 객체나 위쪽 객체에서 [FieldPathRoot]가 붙은 칸(이벤트 타입·상태 키)을 찾아 그 값으로 타입을 정한다.
    // 예: 수치 규칙의 조건(filters[i].path) → 규칙의 이벤트 타입 / 상태 값 검사의 path → 고른 상태 키의 값 형식
    public static class FieldPathContext
    {
        public static Type RootType(SerializedProperty property, out string rootLabel)
        {
            rootLabel = null;
            var so = property.serializedObject;
            var path = property.propertyPath;
            while (true)
            {
                path = Parent(path);
                if (path == null) return null;
                var holder = path.Length == 0 ? null : so.FindProperty(path);
                if (holder != null && holder.isArray && holder.propertyType == SerializedPropertyType.Generic) continue;
                foreach (var child in path.Length == 0 ? TopLevel(so) : PropertyUtil.Children(holder))
                {
                    var field = PropertyUtil.GetField(child, out _);
                    var root = field?.GetCustomAttribute<FieldPathRootAttribute>();
                    if (root == null || child.propertyType != SerializedPropertyType.String) continue;
                    return Resolve(root.Kind, child.stringValue, out rootLabel);
                }
            }
        }

        public static Type Resolve(FieldPathRootKind kind, string value, out string label)
        {
            label = null;
            if (string.IsNullOrEmpty(value)) return null;
            if (kind == FieldPathRootKind.EventType)
            {
                var type = TypeResolver.Find(value);
                label = type != null ? EventBus.DisplayNameOf(type) : value;
                return type;
            }
            var key = EditorDatabase.Instance.FindById(value) as StateKeyData;
            label = key?.DisplayName ?? value;
            return key?.ValueType;
        }

        // 경로 칸 옆 형제 칸을 기준으로 경로 끝의 타입과 멤버.
        public static Type LeafType(SerializedProperty pathProperty, out MemberInfo member)
        {
            member = null;
            var root = RootType(pathProperty, out _);
            return root == null ? null : FieldPaths.TypeAt(root, pathProperty.stringValue, out member, out _);
        }

        static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var index = path.LastIndexOf('.');
            if (index < 0) return "";
            var parent = path.Substring(0, index);
            // "list.Array.data[3]"의 부모는 "list.Array" 가 아니라 "list"
            if (parent.EndsWith(".Array", StringComparison.Ordinal)) parent = parent.Substring(0, parent.Length - 6);
            return parent;
        }

        static System.Collections.Generic.IEnumerable<SerializedProperty> TopLevel(SerializedObject so)
        {
            var it = so.GetIterator();
            if (!it.NextVisible(true)) yield break;
            do yield return it.Copy();
            while (it.NextVisible(false));
        }

        public static string Sibling(string propertyPath, string name)
        {
            var index = propertyPath.LastIndexOf('.');
            return index < 0 ? name : propertyPath.Substring(0, index + 1) + name;
        }

        public static string DescribePath(SerializedProperty property)
        {
            var root = RootType(property, out var rootLabel);
            if (root == null) return string.IsNullOrEmpty(property.stringValue) ? "필드 선택" : property.stringValue;
            return GsDataExplorer.PathLabel(root, property.stringValue);
        }
    }
}
