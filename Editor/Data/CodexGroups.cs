using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEditor;

namespace GBible.EditorTools
{
    // 그룹과 그 아래 항목(키워드 그룹·키워드, 규격 그룹·스프라이트 규격)을 순서대로 읽고 만든다.
    // 순서는 IOrderedData.Order(10 간격), 소속은 IGroupedData.Group.
    public static class CodexGroups
    {
        public const int OrderStep = 10;

        static EditorDatabase Db => EditorDatabase.Instance;

        // DB 창이 열려 있으면 DB 안의 변경으로 모아 둔다(저장(Ctrl+S)할 때 적용). 아니면 바로 에셋으로 만든다.
        public static bool StageByDefault => EditorWindow.HasOpenInstances<DatabaseWindow>();

        public static int OrderOf(GameData data) => (data as IOrderedData)?.Order ?? 0;
        public static string GroupOf(GameData data) => (data as IGroupedData)?.Group;

        public static IEnumerable<GameData> Groups(Type groupType) =>
            Db.OfType(groupType).OrderBy(OrderOf).ThenBy(d => d.Id, StringComparer.Ordinal);

        public static IEnumerable<GameData> Items(Type itemType, string group) =>
            Db.OfType(itemType).Where(d => GroupOf(d) == group).OrderBy(OrderOf).ThenBy(d => d.Id, StringComparer.Ordinal);

        // 그룹(group == null) 또는 그룹 아래 항목을 맨 뒤 순서로 만든다.
        public static GameData Create(Type type, string id, string group = null, bool? staged = null, Action<GameData> init = null)
        {
            var siblings = group != null ? Items(type, group) : Groups(type);
            var order = siblings.Select(OrderOf).DefaultIfEmpty(0).Max() + OrderStep;
            if (staged ?? StageByDefault)
            {
                var added = EditorDatabase.Staging.Add(type, id);
                Setup(added);
                EditorDatabase.StagingChanged();
                return added;
            }
            var created = Db.Create(type, id);
            Setup(created);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssetIfDirty(created);
            EditorDatabase.MarkDirty();
            return created;

            void Setup(GameData data)
            {
                if (data is IOrderedData ordered) ordered.Order = order;
                if (group != null && data is IGroupedData grouped) grouped.Group = group;
                init?.Invoke(data);
            }
        }

        // 여러 ID를 한 번에: 줄바꿈·쉼표·띄어쓰기로 나눈다. 예: "common rare legendary"
        public static List<string> ParseIds(string text) =>
            (text ?? "").Split(new[] { '\n', '\r', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        // 여러 항목을 만들기 전에 검사: 형식·중복(서로 간, 기존 항목과, 대소문자 무시).
        public static string CheckNew(IEnumerable<string> ids)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (!seen.Add(id)) return $"'{id}'가 두 번 들어 있습니다.";
                if (!Db.CanUseId(id, null, out var error)) return $"'{id}': {error}";
            }
            return null;
        }
    }
}
