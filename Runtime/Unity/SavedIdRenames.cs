using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;

namespace GBible
{
    // 출시 뒤 DB 항목(인스턴스·유물 …)의 ID를 바꾸면, 저장 파일 안에 글자로 들어간 그 ID를 불러올 때 새 ID로 바꾼다.
    // 대상은 [DataRef(종류)]가 붙은 string 필드와 string 리스트·배열(덱 카드의 원본 ID, 가방 칸의 아이템 ID, 가진 유물 ID …).
    // 어느 필드를 볼지는 게임이 켜질 때 한 번 정해 두고(형식 정보는 DataModel의 캐시), 불러올 때는 저장 트리만 훑는다.
    static class SavedIdRenames
    {
        // ID 이름 변경 이력이 없거나 [DataRef] 필드가 없으면 null.
        public static Action<Dictionary<string, object>> Build(GameDatabase database)
        {
            var maps = new List<(Type type, Dictionary<string, string> map)>();
            foreach (var group in database.Renames.GroupBy(r => r.dataType))
            {
                var type = TypeResolver.Find(group.Key);
                // 플레이어 데이터·로그·도전과제의 ID는 저장 키라서 따로(별칭) 이어받는다.
                if (type == null || type == typeof(PlayerDataEntry) || type == typeof(StatData) || type == typeof(AchievementData)) continue;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var rename in group)
                    if (!string.IsNullOrEmpty(rename.oldId) && !string.IsNullOrEmpty(rename.newId)) map[rename.oldId] = rename.newId;
                if (map.Count > 0) maps.Add((type, map));
            }
            if (maps.Count == 0) return null;

            var planner = new Planner(maps);
            var plans = new Dictionary<string, Plan>(StringComparer.Ordinal);
            foreach (var entry in database.All<PlayerDataEntry>())
                if (entry.ValueType != null && planner.For(entry.ValueType) is Plan plan) plans[entry.Id] = plan;
            if (plans.Count == 0) return null;

            return state =>
            {
                foreach (var pair in plans)
                    if (state.TryGetValue(pair.Key, out var value)) state[pair.Key] = pair.Value.Apply(value);
            };
        }

        abstract class Plan
        {
            public abstract object Apply(object tree);
        }

        sealed class IdPlan : Plan
        {
            public Dictionary<string, string> Map;
            public override object Apply(object tree) => tree is string id && Map.TryGetValue(id, out var renamed) ? renamed : tree;
        }

        sealed class ListPlan : Plan
        {
            public Plan Element;
            public override object Apply(object tree)
            {
                if (tree is List<object> list)
                    for (int i = 0; i < list.Count; i++) list[i] = Element.Apply(list[i]);
                return tree;
            }
        }

        sealed class DictionaryPlan : Plan
        {
            public Plan Element;
            public override object Apply(object tree)
            {
                if (tree is Dictionary<string, object> map)
                    foreach (var key in map.Keys.ToList()) map[key] = Element.Apply(map[key]);
                return tree;
            }
        }

        sealed class ObjectPlan : Plan
        {
            public Planner Owner;
            public Type Declared;
            // 필드 → 그 필드의 계획(예전 이름으로 저장된 경우를 위해 예전 이름들도).
            public readonly List<(string[] names, Plan plan)> Fields = new List<(string[] names, Plan plan)>();

            public override object Apply(object tree)
            {
                if (!(tree is Dictionary<string, object> map)) return tree;
                // 선언과 다른 형식으로 저장된 값("$type")은 그 형식의 계획을 쓴다.
                if (map.TryGetValue("$type", out var typeName) && typeName is string name && name != Declared.FullName)
                {
                    var actual = TypeResolver.Find(name);
                    if (actual != null && actual != Declared)
                    {
                        if (Owner.For(actual) is ObjectPlan other) other.ApplyFields(map);
                        return tree;
                    }
                }
                ApplyFields(map);
                return tree;
            }

            void ApplyFields(Dictionary<string, object> map)
            {
                foreach (var (names, plan) in Fields)
                    foreach (var name in names)
                        if (map.TryGetValue(name, out var value))
                        {
                            map[name] = plan.Apply(value);
                            break;
                        }
            }
        }

        sealed class Planner
        {
            readonly List<(Type type, Dictionary<string, string> map)> _maps;
            readonly Dictionary<Type, Plan> _done = new Dictionary<Type, Plan>();
            readonly HashSet<Type> _visiting = new HashSet<Type>();

            public Planner(List<(Type type, Dictionary<string, string> map)> maps)
            {
                _maps = maps;
            }

            // 이 형식의 값 안에 바꿀 ID가 있을 수 있으면 계획, 없으면 null.
            public Plan For(Type type)
            {
                if (type == null) return null;
                if (_done.TryGetValue(type, out var known)) return known;
                if (!_visiting.Add(type)) return null; // 자기 자신을 담는 형식: 바깥 계획이 이미 훑는다
                Plan plan = null;
                var info = DataModel.Of(type);
                switch (info.Kind)
                {
                    case DataKind.Array:
                    case DataKind.List:
                    case DataKind.Collection:
                        if (For(info.ElementType) is Plan element) plan = new ListPlan { Element = element };
                        break;
                    case DataKind.Dictionary:
                        if (For(info.ElementType) is Plan value) plan = new DictionaryPlan { Element = value };
                        break;
                    case DataKind.Object:
                    {
                        var objectPlan = new ObjectPlan { Owner = this, Declared = type };
                        foreach (var field in info.Fields)
                        {
                            var fieldPlan = field.Field != null && field.Field.GetCustomAttributes(typeof(DataRefAttribute), true).FirstOrDefault() is DataRefAttribute reference
                                ? IdsOf(field.Type, reference.DataType)
                                : For(field.Type);
                            if (fieldPlan != null) objectPlan.Fields.Add((new[] { field.Name }.Concat(field.FormerNames).ToArray(), fieldPlan));
                        }
                        if (objectPlan.Fields.Count > 0) plan = objectPlan;
                        break;
                    }
                }
                _visiting.Remove(type);
                _done[type] = plan;
                return plan;
            }

            // [DataRef(종류)] 필드: string이면 그 값, string 리스트·배열이면 각 요소.
            Plan IdsOf(Type fieldType, Type dataType)
            {
                var map = MapFor(dataType);
                if (map == null) return null;
                var id = new IdPlan { Map = map };
                if (fieldType == typeof(string)) return id;
                var info = DataModel.Of(fieldType);
                return (info.Kind == DataKind.Array || info.Kind == DataKind.List || info.Kind == DataKind.Collection) && info.ElementType == typeof(string)
                    ? new ListPlan { Element = id }
                    : null;
            }

            // 그 종류(와 그 하위 종류)의 ID 이름 변경을 합친 표.
            Dictionary<string, string> MapFor(Type dataType)
            {
                Dictionary<string, string> merged = null;
                foreach (var (type, map) in _maps)
                {
                    if (dataType != null && !dataType.IsAssignableFrom(type) && !type.IsAssignableFrom(dataType)) continue;
                    merged ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in map) merged[pair.Key] = pair.Value;
                }
                return merged;
            }
        }
    }
}
