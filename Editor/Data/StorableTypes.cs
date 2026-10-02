using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace GBible.EditorTools
{
    // 플레이어 데이터의 "값 형식" 목록: 기본 형식 + 프로젝트의 enum + 저장소에 담을 수 있는 데이터 클래스.
    // 클래스는 추가 코드 없이 목록에 나타난다(공개 클래스이고 담을 수 없는 필드가 없으면).
    public static class StorableTypes
    {
        static readonly (Type type, string label)[] Basics =
        {
            (typeof(double), "숫자 (double)"),
            (typeof(int), "정수 (int)"),
            (typeof(long), "큰 정수 (long)"),
            (typeof(float), "소수 (float)"),
            (typeof(bool), "참/거짓 (bool)"),
            (typeof(string), "글자 (string)"),
            (typeof(DateTime), "날짜·시각 (DateTime)"),
            (typeof(TimeSpan), "시간 길이 (TimeSpan)"),
        };

        static List<Type> _cache;

        public static IReadOnlyList<Type> All()
        {
            if (_cache != null) return _cache;
            // 내 코드만(ProjectCode): 플러그인·패키지·라이브러리 형식은 보이지 않는다.
            var enums = TypeCache.GetTypesDerivedFrom<Enum>()
                .Where(t => t.IsVisible && ProjectCode.IsProjectType(t))
                .OrderBy(t => t.Name);
            // 켜 둔 선택 기능이 내놓는 데이터 클래스(인벤토리 등)도 값 형식으로 고를 수 있다.
            var featureData = new HashSet<Type>(GameFeatures.DataClassTypes());
            var classes = TypeCache.GetTypesWithAttribute<SerializableAttribute>()
                .Where(t => !t.IsEnum && !t.IsAbstract && (ProjectCode.IsDataClass(t) || featureData.Contains(t)) &&
                            !typeof(UnityEngine.Object).IsAssignableFrom(t) &&
                            !typeof(GameAction).IsAssignableFrom(t) &&
                            DataModel.Of(t).Kind == DataKind.Object && DataModel.Validate(t).Count == 0)
                .OrderBy(t => t.Name);
            _cache = Basics.Select(b => b.type).Concat(enums).Concat(classes).ToList();
            return _cache;
        }

        public static string Label(Type type)
        {
            foreach (var basic in Basics)
                if (basic.type == type) return basic.label;
            if (type.IsEnum) return type.Name + " (enum)";
            return DataModel.FriendlyName(type);
        }

        public static string GroupOf(Type type) =>
            Basics.Any(b => b.type == type) ? "기본 형식" : type.IsEnum ? "enum" : "데이터 클래스";

        [InitializeOnLoadMethod]
        static void ResetOnReload()
        {
            _cache = null;
            ProjectCode.Changed += () => _cache = null;
        }
    }
}
