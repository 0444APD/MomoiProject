using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 에디터에서 GameData 에셋 전체를 다루는 창구: 조회, 생성, ID 부여, 이름 조회, GameDatabase 동기화.
    public sealed class EditorDatabase : INameResolver
    {
        public const string DataRoot = "Assets/MomoiProjectData";
        public const string DatabaseAssetPath = DataRoot + "/Resources/MomoiProject/GameDatabase.asset";

        static EditorDatabase _instance;
        public static EditorDatabase Instance => _instance ??= new EditorDatabase();

        List<GameData> _all;
        List<GameData> _view;
        public static event Action Changed;

        // DB에서 모아 둔(아직 적용하지 않은) 변경. 저장(Ctrl+S)할 때 적용하거나 되돌린다.
        public static CodexStaging Staging { get; } = new CodexStaging();

        // DB가 보는 모습: 실제 에셋 + 적용 대기 중인 변경(추가·수정·삭제). 목록·드롭다운·검증이 이것을 쓴다.
        public IReadOnlyList<GameData> All => Staging.IsEmpty ? Committed : _view ??= Staging.Overlay(Committed);

        // 실제 에셋만(적용된 것). 생성 코드·런타임 목록 동기화처럼 Unity에 쓰는 쪽이 쓴다.
        public IReadOnlyList<GameData> Committed
        {
            get
            {
                if (_all != null) return _all;
                // 추상 베이스 이름 검색에 기대지 않고 구체 타입마다 찾는다.
                _all = DataTypes.All
                    .SelectMany(info => AssetDatabase.FindAssets("t:" + info.Type.Name))
                    .Distinct()
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameData>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(d => d != null && DataTypes.IsRegistered(d.GetType()))
                    .Distinct()
                    .OrderBy(d => d.Id, StringComparer.Ordinal)
                    .ToList();
                return _all;
            }
        }

        public IEnumerable<GameData> OfType(Type type) => All.Where(d => d.GetType() == type);
        public IEnumerable<T> OfType<T>() where T : GameData => All.OfType<T>();

        public GameData FindById(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(d => d.Id == id);

        // 드롭다운 그룹 머리글(지금은 없음).
        public string GroupLabel(GameData data) => null;

        public static void MarkDirty(bool notify = true)
        {
            if (_instance != null)
            {
                _instance._all = null;
                _instance._view = null;
            }
            _rules = null;
            if (notify) Changed?.Invoke();
        }

        // 적용 대기 중인 변경이 바뀌었다(에셋은 그대로, DB가 보는 모습만 다시 만든다).
        public static void StagingChanged()
        {
            if (_instance != null) _instance._view = null;
            _rules = null;
            Changed?.Invoke();
        }

        // ── 이름 조회(조건·보상 문장용) ──────────────────────────

        public string StatName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(수치 없음)";
            return OfType<StatData>().FirstOrDefault(s => s.Id == id)?.DisplayName ?? $"(없는 수치: {id})";
        }

        public string AchievementName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(도전과제 없음)";
            return OfType<AchievementData>().FirstOrDefault(a => a.Id == id)?.DisplayName ?? $"(없는 도전과제: {id})";
        }

        // ── 생성과 ID ─────────────────────────────────────────

        // id를 주지 않으면 접두어+연번(예: keyword3)으로 제안한 ID를 쓴다. DB는 만들기 전에 ID를 묻는다.
        public GameData Create(Type type, string id = null)
        {
            var info = DataTypes.Find(type) ?? throw new ArgumentException($"{type.Name}은 데이터베이스 타입이 아닙니다.");
            id = string.IsNullOrEmpty(id) ? NextId(info) : id;
            if (!CanUseId(id, null, out var error)) throw new ArgumentException("[MomoiProject] " + error);
            var data = (GameData)ScriptableObject.CreateInstance(type);
            data.EditorAssignId(id);
            CreateAsset(data);
            AssetDatabase.SaveAssets();
            MarkDirty();
            SyncDatabaseAsset();
            return data;
        }

        // 메모리의 항목을 에셋으로 저장한다(종류 이름 폴더, 파일 이름 = ID). DB의 '적용'이 새 항목에 쓴다.
        public void CreateAsset(GameData data)
        {
            var info = DataTypes.Find(data.GetType()) ?? throw new ArgumentException($"{data.GetType().Name}은 데이터베이스 타입이 아닙니다.");
            var folder = $"{DataRoot}/{Sanitize(info.DisplayName)}";
            EnsureFolder(folder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{data.Id}.asset");
            AssetDatabase.CreateAsset(data, path);
            Undo.RegisterCreatedObjectUndo(data, $"{info.DisplayName} 추가");
        }

        // 새 항목 ID 제안: 접두어 + 연번(예: keyword3). 만들기 창에서 바로 고쳐 쓰는 자리 표시용.
        public string NextId(DataTypeInfo info)
        {
            var pattern = new Regex("^" + Regex.Escape(info.IdPrefix) + "(\\d+)$");
            var max = 0;
            foreach (var data in All)
            {
                var match = pattern.Match(data.Id ?? "");
                if (match.Success) max = Math.Max(max, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            return info.IdPrefix + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ── ID 규칙·이름 바꾸기·참조 찾기 ─────────────────────────

        // 형식이 맞고 다른 항목이 쓰지 않는 ID인지. self는 이름을 바꾸는 항목 자신(자기 ID는 중복으로 치지 않는다).
        public bool CanUseId(string id, GameData self, out string error)
        {
            if (!IdRules.IsValid(id, out error)) return false;
            // 대소문자만 다른 ID도 막는다(에셋 파일 이름이 겹치고, 읽을 때 헷갈린다).
            var owner = All.FirstOrDefault(d => d != self && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (owner == null) return true;
            error = $"'{id}'는 이미 '{owner.DisplayName}'({DataTypes.Find(owner.GetType())?.DisplayName})이(가) 쓰고 있습니다.";
            return false;
        }

        public static bool IsReleased => AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath)?.IsReleased ?? false;

        internal const string ReleasedMenu = "MomoiProject/출시됨 (저장 호환 유지)";

        [MenuItem(ReleasedMenu, priority = 40)]
        static void ToggleReleased()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (database == null)
            {
                Debug.LogWarning("[MomoiProject] GameDatabase가 아직 없습니다. DB 창을 한 번 여세요.");
                return;
            }
            var next = !database.IsReleased;
            var message = next
                ? "출시 상태로 바꿉니다. 앞으로 ID 이름을 바꾸거나 데이터 구조를 바꿔도 기존 저장을 지우지 않고 이어받습니다(ID 변경 이력 기록, 구조 변경은 저장 버전 변환 필요)."
                : "개발 중 상태로 바꿉니다. 데이터 구조가 호환되지 않게 바뀌면 저장을 변환하지 않고 지웁니다.";
            if (!EditorUtility.DisplayDialog("출시 상태", message, "바꾸기", "취소")) return;
            Undo.RecordObject(database, "출시 상태 변경");
            database.EditorSetReleased(next);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        [MenuItem(ReleasedMenu, true)]
        static bool ToggleReleasedValidate()
        {
            Menu.SetChecked(ReleasedMenu, IsReleased);
            return true;
        }

        public sealed class IdUsage
        {
            public GameData Owner;
            public string PropertyPath;
            public string Label;
        }

        // 이 항목의 ID를 가리키는 모든 곳(조건·보상·키워드 칸 등 ID 참조 필드).
        public List<IdUsage> FindUsages(GameData target)
        {
            var result = new List<IdUsage>();
            if (target == null || string.IsNullOrEmpty(target.Id)) return result;
            foreach (var data in All)
            {
                ForEachReference(data, (property, refType) =>
                {
                    if (property.stringValue == target.Id && refType.IsInstanceOfType(target))
                        result.Add(new IdUsage { Owner = data, PropertyPath = property.propertyPath, Label = $"{data.DisplayName} · {property.displayName}" });
                });
            }
            return result;
        }

        // ID 참조 필드(수치·도전과제·[DataRef])를 모두 방문한다. 조건·보상 블록 안쪽까지 들어간다.
        public static void ForEachReference(GameData data, Action<SerializedProperty, Type> visit)
        {
            var so = new SerializedObject(data);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.String) continue;
                var refType = RefTypeOf(PropertyUtil.GetField(it, out _));
                if (refType != null) visit(it, refType);
            }
        }

        public static Type RefTypeOf(System.Reflection.FieldInfo field)
        {
            if (field == null) return null;
            if (field.IsDefined(typeof(StatRefAttribute), false)) return typeof(StatData);
            if (field.IsDefined(typeof(AchievementRefAttribute), false)) return typeof(AchievementData);
            var attribute = ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
            // [DataRef(typeof(Card))]처럼 GameData가 아닌 클래스: 종류는 인스턴스, 고를 수 있는 것은 RefClassOf로 거른다.
            if (attribute != null) return typeof(GameData).IsAssignableFrom(attribute) ? attribute : typeof(InstanceData);
            // 필드 규칙(DB에서 연결): 코드에 어트리뷰트가 없어도 같은 대우를 받는다.
            var rule = RuleFor(field);
            // 규칙의 값 출처는 글자 필드(또는 string 리스트)에만 적용한다(스프라이트 필드의 규칙은 규격 그룹용).
            if (rule == null || (field.FieldType != typeof(string) && FieldPaths.ElementTypeOf(field.FieldType) != typeof(string))) return null;
            switch (rule.source)
            {
                case FieldRuleSource.PlayerDataKey: return typeof(PlayerDataEntry);
                case FieldRuleSource.Data: return TypeResolver.Find(rule.dataType);
                default: return null;
            }
        }

        // 이 필드에 걸린 필드 규칙(없으면 null). 필드를 선언한 클래스와 그 부모 클래스 기준.
        public static FieldRuleData RuleFor(System.Reflection.FieldInfo field)
        {
            if (field == null) return null;
            if (_rules == null)
            {
                _rules = new Dictionary<string, FieldRuleData>(StringComparer.Ordinal);
                foreach (var rule in Instance.OfType<FieldRuleData>())
                    if (!string.IsNullOrEmpty(rule.targetType) && !string.IsNullOrEmpty(rule.field)) _rules[rule.targetType + "|" + rule.field] = rule;
            }
            for (var type = field.ReflectedType; type != null; type = type.BaseType)
                if (_rules.TryGetValue(type.FullName + "|" + field.Name, out var found)) return found;
            return null;
        }

        static Dictionary<string, FieldRuleData> _rules;

        // 이 필드에서 고를 수 있는 항목(타입 + 키워드 그룹으로 거른다. 그룹이 비어 있으면 모든 키워드).
        public IEnumerable<GameData> RefCandidates(System.Reflection.FieldInfo field)
        {
            var type = RefTypeOf(field);
            if (type == null) return Enumerable.Empty<GameData>();
            return OfType(type).Where(item => MatchesRef(field, item));
        }

        // [DataRef(typeof(Card))]처럼 인스턴스 값의 클래스로 거르는 필드면 그 클래스(아니면 null).
        public static Type RefClassOf(System.Reflection.FieldInfo field)
        {
            if (field == null) return null;
            var attribute = ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
            return attribute != null && !typeof(GameData).IsAssignableFrom(attribute) ? attribute : null;
        }

        // 그 항목이 이 필드에서 고를 수 있는 것인가(종류 + 인스턴스 클래스).
        public static bool MatchesRef(System.Reflection.FieldInfo field, GameData target)
        {
            if (target == null) return false;
            var type = RefTypeOf(field);
            if (type == null || !type.IsInstanceOfType(target)) return false;
            var cls = RefClassOf(field);
            return cls == null || target is InstanceData instance && instance.InstanceType != null && cls.IsAssignableFrom(instance.InstanceType);
        }

        // 화면에 보일 참조 종류 이름("카드 인스턴스", "유물" …).
        public static string RefKindLabel(System.Reflection.FieldInfo field)
        {
            var cls = RefClassOf(field);
            if (cls != null) return DataModel.FriendlyName(cls);
            var type = RefTypeOf(field);
            return type == null ? "" : DataTypes.Find(type)?.DisplayName ?? type.Name;
        }

        public static bool IsRequired(System.Reflection.FieldInfo field) => RuleFor(field)?.required ?? false;

        // ID를 바꾼다. 이 ID를 가리키는 모든 필드, 에셋 파일 이름, 생성 코드(GameIds)가 함께 바뀐다.
        // 출시 후: 예전 이름으로 저장된 플레이어 데이터를 런타임이 새 이름으로 이어받도록 변경 이력을 남긴다.
        // 개발 중: 이력을 남기지 않는다(호환되지 않는 저장은 런타임이 지우고 새로 시작).
        public int Rename(GameData data, string newId)
        {
            var oldId = data.Id;
            if (oldId == newId) return 0;
            if (!CanUseId(newId, data, out var error)) throw new ArgumentException("[MomoiProject] " + error);

            var usages = FindUsages(data);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            foreach (var usage in usages)
            {
                var so = new SerializedObject(usage.Owner);
                var property = so.FindProperty(usage.PropertyPath);
                if (property == null) continue;
                property.stringValue = newId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(usage.Owner);
            }
            Undo.RecordObject(data, "ID 바꾸기");
            data.EditorAssignId(newId);
            EditorUtility.SetDirty(data);
            Undo.CollapseUndoOperations(group);

            var path = AssetDatabase.GetAssetPath(data);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == oldId) AssetDatabase.RenameAsset(path, newId);

            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (database != null && database.IsReleased)
            {
                database.EditorAddRename(data.GetType().FullName, oldId, newId);
                EditorUtility.SetDirty(database);
            }
            AssetDatabase.SaveAssets();
            MarkDirty();
            return usages.Count;
        }

        // 프로젝트에서 프리팹 에셋을 지우면 그 프리팹을 가리키던 DB 항목도 지운다(연결만 남은 빈 항목이 생기지 않게).
        public int RemoveOrphanPrefabEntries()
        {
            int removed = 0;
            foreach (var entry in Committed.OfType<PrefabData>().ToList())
            {
                if (entry.prefab != null || string.IsNullOrEmpty(entry.PrefabGuid)) continue;
                if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.PrefabGuid))) continue;
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(entry));
                Debug.Log($"[MomoiProject] 프리팹이 지워져 DB 항목 '{entry.DisplayName}'을(를) 정리했습니다.");
                removed++;
            }
            if (removed > 0) MarkDirty();
            return removed;
        }

        // ID 없는 항목(Create 메뉴로 만든 것 등)에 ID를 부여하고, 런타임용 GameDatabase 목록을 맞춘다.
        public void Sync()
        {
            _all = null;
            foreach (var data in Committed.Where(d => string.IsNullOrEmpty(d.Id)).ToList())
            {
                var info = DataTypes.Find(data.GetType());
                data.EditorAssignId(NextId(info));
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                _all = null;
            }
            // 예전 저장 형식(로그의 반영 규칙 한 줄, 도전과제의 조건·보상, 능력의 대상·효과)을 트리거·결과로 옮긴다.
            foreach (var data in Committed.OfType<ILegacyData>().ToList())
            {
                if (!data.MigrateLegacy()) continue;
                var asset = (GameData)data;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                Debug.Log($"[MomoiProject] '{asset.DisplayName}'을(를) 트리거·결과 형식으로 옮겼습니다.");
                _all = null;
            }
            // 저장 전 작업본(옮기기 전에 만든 것)도 같은 형식으로(저장하면 원본에 그대로 들어간다).
            foreach (var data in All.OfType<ILegacyData>().Where(d => Staging.IsWorking((GameData)d)).ToList())
                data.MigrateLegacy();
            SyncDatabaseAsset();
            MarkDirty();
        }

        public void SyncDatabaseAsset()
        {
            var entries = Committed.ToList();
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (database == null)
            {
                if (entries.Count == 0) return;
                EnsureFolder(Path.GetDirectoryName(DatabaseAssetPath).Replace('\\', '/'));
                database = ScriptableObject.CreateInstance<GameDatabase>();
                AssetDatabase.CreateAsset(database, DatabaseAssetPath);
            }
            if (database.Entries.SequenceEqual(entries)) return;
            database.EditorSetEntries(entries);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static string Sanitize(string name) => Regex.Replace(name, "[\\\\/:*?\"<>|]", "_");
    }

    // GameData 에셋이 추가·삭제·이동되면 자동으로 동기화한다.
    sealed class EditorDatabasePostprocessor : AssetPostprocessor
    {
        static bool _scheduled;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool relevant = deleted.Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) ||
                            imported.Concat(moved).Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                                                            typeof(GameData).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(p)));
            if (!relevant || _scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                EditorDatabase.Instance.RemoveOrphanPrefabEntries();
                EditorDatabase.Instance.Sync();
            };
        }
    }
}
