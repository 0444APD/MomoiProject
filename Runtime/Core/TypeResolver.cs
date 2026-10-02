using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

// 코어는 Unity 없이(.NET 서버)도 돌아야 하므로 Unity 전용 어셈블리 API 대신 AppDomain을 쓴다.
// 조회 결과는 이름별로 캐시하고, 에디터에서는 스크립트 리로드 때 ClearCache가 호출된다.
#pragma warning disable UAC0005

namespace GBible
{
    // 데이터에 문자열로 저장된 타입 이름·필드 경로를 실제 코드로 연결한다.
    // 이 연결은 에디터 검증기가 저장·빌드 시점에 확인한다(이름이 바뀌면 빨간 글씨로 표시).
    public static class TypeResolver
    {
        static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>(StringComparer.Ordinal);
        const string LegacyNamespace = "GameSystems.";

        public static Type Find(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(fullName, out var cached)) return cached;
                Type found = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    found = assembly.GetType(fullName, false);
                    if (found != null) break;
                }
                // 옛 이름(GameSystems)으로 저장된 형식 이름도 찾는다(이름을 바꾸기 전에 만든 데이터·저장 파일).
                if (found == null && fullName.StartsWith(LegacyNamespace, StringComparison.Ordinal))
                {
                    Cache[fullName] = null;
                    found = Find("GBible." + fullName.Substring(LegacyNamespace.Length));
                }
                Cache[fullName] = found;
                return found;
            }
        }

        public static void ClearCache()
        {
            lock (Cache) Cache.Clear();
        }

        public static IEnumerable<Type> GameEventTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types)
                    if (type.IsDefined(typeof(GameEventAttribute), false)) yield return type;
            }
        }

        public static bool IsNumeric(Type type)
        {
            if (type == null) return false;
            if (type.IsEnum) return false;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.Boolean:
                    return true;
            }
            return false;
        }

        // 이벤트 타입에서 숫자로 읽을 수 있는 공개 필드·프로퍼티 이름.
        public static IEnumerable<MemberInfo> NumericMembers(Type type)
        {
            if (type == null) yield break;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (IsNumeric(field.FieldType)) yield return field;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanRead && property.GetIndexParameters().Length == 0 && IsNumeric(property.PropertyType)) yield return property;
        }

        // "Amount" 또는 "Info.Amount" 같은 경로를 숫자 읽기 함수로 만든다. 실패하면 null과 이유를 돌려준다.
        public static Func<object, double> NumberGetter(Type type, string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "필드 경로가 비어 있습니다.";
                return null;
            }

            var chain = new List<Func<object, object>>();
            var current = type;
            foreach (var part in path.Split('.'))
            {
                var field = current.GetField(part, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    chain.Add(field.GetValue);
                    current = field.FieldType;
                    continue;
                }
                var property = current.GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanRead)
                {
                    chain.Add(property.GetValue);
                    current = property.PropertyType;
                    continue;
                }
                error = $"'{type.Name}'에 '{path}' 필드가 없습니다.";
                return null;
            }

            if (!IsNumeric(current))
            {
                error = $"'{type.Name}.{path}'는 숫자가 아닙니다 ({current.Name}).";
                return null;
            }

            return target =>
            {
                object value = target;
                foreach (var step in chain)
                {
                    if (value == null) return 0;
                    value = step(value);
                }
                return value == null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            };
        }
    }
}
