using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace GBible.EditorTools.UI
{
    // 값 칸의 목록을 선택 모듈(다이얼로그 시스템 등)이 채우는 입구. 코어는 모듈을 모르고, 모듈이 이 인터페이스를 구현하면 자동으로 쓰인다.
    // 예: 다이얼로그 시스템의 '스탠딩 ID' 칸은 같은 노드의 프로필 칸을 읽어 그 프로필의 스탠딩 목록을 보여 준다.
    public interface IInlineChoiceProvider
    {
        // 이 필드의 목록을 맡는지(보통 필드의 어트리뷰트로 판단).
        bool Handles(FieldInfo field);

        // 목록. set으로 문자열 값을 넣는다(같은 오브젝트의 다른 칸은 property.serializedObject로 읽는다).
        IEnumerable<GsDropdownItem> Items(SerializedProperty property, FieldInfo field, Action<string> set);

        string Placeholder { get; }
    }

    public static class InlineChoices
    {
        static List<IInlineChoiceProvider> _providers;
        static readonly Dictionary<FieldInfo, IInlineChoiceProvider> Cache = new Dictionary<FieldInfo, IInlineChoiceProvider>();

        static List<IInlineChoiceProvider> Providers => _providers ??= TypeCache.GetTypesDerivedFrom<IInlineChoiceProvider>()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (IInlineChoiceProvider)Activator.CreateInstance(t))
            .ToList();

        public static IInlineChoiceProvider For(FieldInfo field)
        {
            if (field == null) return null;
            if (Cache.TryGetValue(field, out var cached)) return cached;
            var provider = Providers.FirstOrDefault(p => p.Handles(field));
            Cache[field] = provider;
            return provider;
        }
    }
}
