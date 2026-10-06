using System;
using System.Linq;
using System.Reflection;
using GBible.Data;
using GBible.EditorTools.UI;
using UnityEditor;

namespace GBible.EditorTools
{
    // 편집 중인 필드 경로 칸의 "뿌리 타입"을 찾는다.
    // 같은 객체나 위쪽 객체에서 [FieldPathRoot]가 붙은 칸(이벤트 타입·플레이어 데이터)을 찾아 그 값으로 타입을 정한다.
    // 예: 수치 규칙의 조건(filters[i].path) → 규칙의 이벤트 타입 / 플레이어 데이터 검사의 path → 고른 플레이어 데이터의 값 형식
    public static class FieldPathContext
    {
        public static Type RootType(SerializedProperty property, out string rootLabel)
        {
            rootLabel = null;
            var so = property.serializedObject;
            var path = property.propertyPath;
            // 칸이 뿌리를 직접 정했으면 그 형제 칸(트리거면 받는 이벤트, 뿌리 칸이면 그 값). 그 칸이 비었으면 뿌리 없음 —
            // 위쪽의 다른 뿌리 칸으로 넘어가지 않는다(패시브의 '가진 쪽 필드'가 발동이 없을 때 가진 목록의 필드를 보이지 않게).
            var own = PropertyUtil.GetField(property, out _)?.GetCustomAttribute<FieldPathAttribute>();
            if (!string.IsNullOrEmpty(own?.Root))
            {
                var named = so.FindProperty(Sibling(path, own.Root));
                if (named != null)
                {
                    if (named.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        if (!(named.managedReferenceValue is Trigger chosen)) return null;
                        var events = chosen.EventTypes().ToList();
                        if (events.Count == 0) return null;
                        rootLabel = string.Join(" · ", events.Select(EventBus.DisplayNameOf));
                        return events[0];
                    }
                    var namedRoot = PropertyUtil.GetField(named, out _)?.GetCustomAttribute<FieldPathRootAttribute>();
                    return namedRoot != null && named.propertyType == SerializedPropertyType.String ? Resolve(namedRoot.Kind, named.stringValue, out rootLabel) : null;
                }
            }
            while (true)
            {
                path = Parent(path);
                if (path == null) return null;
                var holder = path.Length == 0 ? null : so.FindProperty(path);
                if (holder != null && holder.isArray && holder.propertyType == SerializedPropertyType.Generic) continue;
                foreach (var child in path.Length == 0 ? TopLevel(so) : PropertyUtil.Children(holder))
                {
                    var field = PropertyUtil.GetField(child, out _);
                    // 목록 항목 검사 안쪽: 뿌리는 그 목록의 요소 타입.
                    if (field != null && field.IsDefined(typeof(FieldPathScopeAttribute), false) &&
                        child.propertyPath != property.propertyPath && child.propertyType == SerializedPropertyType.String)
                    {
                        var outer = RootType(child, out rootLabel);
                        if (outer == null) return null;
                        rootLabel = GsDataExplorer.PathLabel(outer, child.stringValue) + "의 각 항목";
                        return ElementFilter.ElementRoot(outer, child.stringValue);
                    }
                    // 트리거·결과 규칙: 결과 칸(로그에 기록의 '반영할 값' 등)의 뿌리는 같은 규칙의 트리거가 보는 이벤트.
                    if (child.propertyType == SerializedPropertyType.ManagedReference && child.managedReferenceValue is Trigger trigger &&
                        !property.propertyPath.StartsWith(child.propertyPath + ".", StringComparison.Ordinal))
                    {
                        var events = trigger.EventTypes().ToList();
                        if (events.Count == 0) continue;
                        rootLabel = string.Join(" · ", events.Select(EventBus.DisplayNameOf));
                        return events[0];
                    }
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
            var key = EditorDatabase.Instance.FindById(value) as PlayerDataEntry;
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
