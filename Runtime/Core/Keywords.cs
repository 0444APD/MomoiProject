using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 키워드 그룹: 키워드(고르는 값) 모음을 코드로 정의한다. 코드가 원본이고(AI가 쓴다), DB › 키워드 탭은 이 코드를 읽기만 한다.
    //   [KeywordGroup("등급")]
    //   public static class Rarity
    //   {
    //       public const string Common = "common";
    //       public const string Rare = "rare";
    //   }
    // 필드 값에는 키워드 ID(글자, 예: "rare")가 저장된다. 게임 코드는 상수로 쓴다: if (item.rarity == Rarity.Rare)
    // 하위 그룹은 안쪽 static class에 [KeywordGroup]을 붙인다. 키워드 ID는 모든 그룹에서 겹치지 않는다.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class KeywordGroupAttribute : Attribute
    {
        public string Label { get; }

        public KeywordGroupAttribute(string label = null)
        {
            Label = label;
        }
    }

    // 필드가 어느 키워드 그룹에서 값을 고르는지. string 필드는 하나, string 리스트·배열은 여러 개.
    //   [Keyword(typeof(Rarity))] public string rarity;
    //   [Keyword(typeof(ItemTag))] public List<string> tags = new List<string>();
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class KeywordAttribute : Attribute
    {
        public Type Group { get; }

        public KeywordAttribute(Type group)
        {
            Group = group;
        }
    }

    public sealed class KeywordGroupInfo
    {
        public Type Type;
        // 그룹 ID = 형식 전체 이름(안쪽 클래스는 + 대신 .).
        public string Id;
        public string Name;
        public string Label;
        public KeywordGroupInfo Parent;
        public readonly List<KeywordGroupInfo> Children = new List<KeywordGroupInfo>();
        public readonly List<KeywordInfo> Keywords = new List<KeywordInfo>();

        public string DisplayName => string.IsNullOrEmpty(Label) ? Name : Label;
    }

    public sealed class KeywordInfo
    {
        // 저장되는 값(예: "rare").
        public string Id;
        // 코드의 상수 이름(예: Rare).
        public string Name;
        public KeywordGroupInfo Group;
    }

    // 코드에 정의된 키워드 그룹·키워드 목록. 처음 쓸 때(게임 시작·에디터 컴파일 뒤) 한 번 모은다(리플렉션은 여기서만).
    public static class Keywords
    {
        static List<KeywordGroupInfo> _groups;
        static Dictionary<string, KeywordInfo> _byId;
        static Dictionary<Type, KeywordGroupInfo> _byType;
        static readonly object Gate = new object();

        public static IReadOnlyList<KeywordGroupInfo> Groups
        {
            get
            {
                Load();
                return _groups;
            }
        }

        public static IEnumerable<KeywordInfo> All => Groups.SelectMany(g => g.Keywords);

        public static KeywordInfo Find(string id)
        {
            Load();
            return id != null && _byId.TryGetValue(id, out var found) ? found : null;
        }

        public static KeywordGroupInfo GroupOf(Type type)
        {
            Load();
            return type != null && _byType.TryGetValue(type, out var found) ? found : null;
        }

        public static KeywordGroupInfo GroupById(string id) => Groups.FirstOrDefault(g => g.Id == id);

        // 필드에 연결된 그룹([Keyword]). 없으면 null.
        public static KeywordGroupInfo GroupOf(FieldInfo field)
        {
            var attribute = field?.GetCustomAttribute<KeywordAttribute>(true);
            return attribute == null ? null : GroupOf(attribute.Group);
        }

        // 다시 모으게 한다(에디터에서 코드가 바뀐 뒤).
        public static void Reset()
        {
            lock (Gate)
            {
                _groups = null;
                _byId = null;
                _byType = null;
            }
        }

        static void Load()
        {
            if (_groups != null) return;
            lock (Gate)
            {
                if (_groups != null) return;
                var groups = new List<KeywordGroupInfo>();
                var byType = new Dictionary<Type, KeywordGroupInfo>();
                foreach (var assembly in GameAssemblies.UsingCore())
                {
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var type in types)
                    {
                        var attribute = type.GetCustomAttribute<KeywordGroupAttribute>(false);
                        if (attribute == null) continue;
                        var info = new KeywordGroupInfo { Type = type, Id = type.FullName.Replace('+', '.'), Name = type.Name, Label = attribute.Label };
                        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string id && id.Length > 0)
                                info.Keywords.Add(new KeywordInfo { Id = id, Name = field.Name, Group = info });
                        groups.Add(info);
                        byType[type] = info;
                    }
                }
                foreach (var group in groups)
                    if (group.Type.DeclaringType != null && byType.TryGetValue(group.Type.DeclaringType, out var parent))
                    {
                        group.Parent = parent;
                        parent.Children.Add(group);
                    }
                // 겹친 ID(코드 실수)는 그룹 ID 순으로 앞선 그룹의 것 — 어셈블리를 불러온 차례와 상관없이, DB 키워드 탭과 같게.
                var sorted = groups.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
                var byId = new Dictionary<string, KeywordInfo>(StringComparer.Ordinal);
                foreach (var keyword in sorted.SelectMany(g => g.Keywords))
                    if (!byId.ContainsKey(keyword.Id)) byId[keyword.Id] = keyword;
                _byType = byType;
                _byId = byId;
                _groups = sorted;
            }
        }
    }
}
