using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameSystems.Data;
using UnityEditor;
using UnityEngine;

namespace GameSystems.EditorTools
{
    // 에디터에서 GameData 에셋 전체를 다루는 창구: 조회, 생성, ID 부여, 이름 조회, GameDatabase 동기화.
    public sealed class EditorDatabase : INameResolver
    {
        public const string DataRoot = "Assets/GameSystemsData";
        public const string DatabaseAssetPath = DataRoot + "/Resources/GameSystems/GameDatabase.asset";

        static EditorDatabase _instance;
        public static EditorDatabase Instance => _instance ??= new EditorDatabase();

        List<GameData> _all;
        public static event Action Changed;

        public IReadOnlyList<GameData> All
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

        public static void MarkDirty()
        {
            if (_instance != null) _instance._all = null;
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

        // id를 주지 않으면 접두어_0001 형식으로 제안한 ID를 쓴다. 데이터베이스 창은 만들기 전에 이름을 묻는다.
        public GameData Create(Type type, string displayName = null, string id = null)
        {
            var info = DataTypes.Find(type) ?? throw new ArgumentException($"{type.Name}은 데이터베이스 타입이 아닙니다.");
            id = string.IsNullOrEmpty(id) ? NextId(info) : id;
            if (!CanUseId(id, null, out var error)) throw new ArgumentException("[GameSystems] " + error);
            var folder = $"{DataRoot}/{Sanitize(info.DisplayName)}";
            EnsureFolder(folder);

            var data = (GameData)ScriptableObject.CreateInstance(type);
            data.EditorAssignId(id);
            data.EditorSetDisplayName(displayName ?? $"새 {info.DisplayName}");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{id}.asset");
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Undo.RegisterCreatedObjectUndo(data, $"{info.DisplayName} 추가");
            MarkDirty();
            SyncDatabaseAsset();
            return data;
        }

        // 형식: 접두어_0001 (자동 연번, 설계 문서 미결 사항 — 확정 시 이 함수만 바꾼다).
        public string NextId(DataTypeInfo info)
        {
            var pattern = new Regex("^" + Regex.Escape(info.IdPrefix) + "_(\\d+)$");
            var max = 0;
            foreach (var data in All)
            {
                var match = pattern.Match(data.Id ?? "");
                if (match.Success) max = Math.Max(max, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            return $"{info.IdPrefix}_{(max + 1).ToString("0000", CultureInfo.InvariantCulture)}";
        }

        public void AssignNewId(GameData data)
        {
            var info = DataTypes.Find(data.GetType());
            if (info == null) return;
            Undo.RecordObject(data, "ID 재발급");
            data.EditorAssignId(NextId(info));
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            MarkDirty();
        }

        // ── ID 규칙·이름 바꾸기·참조 찾기 ─────────────────────────

        // 형식이 맞고 다른 항목이 쓰지 않는 ID인지. self는 이름을 바꾸는 항목 자신(자기 ID는 중복으로 치지 않는다).
        public bool CanUseId(string id, GameData self, out string error)
        {
            if (!IdRules.IsValid(id, out error)) return false;
            var owner = All.FirstOrDefault(d => d != self && d.Id == id);
            if (owner == null) return true;
            error = $"'{id}'는 이미 '{owner.DisplayName}'({DataTypes.Find(owner.GetType())?.DisplayName})이(가) 쓰고 있습니다.";
            return false;
        }

        public static bool IsReleased => AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath)?.IsReleased ?? false;

        const string ReleasedMenu = "Game Systems/출시됨 (저장 호환 유지)";

        [MenuItem(ReleasedMenu, priority = 40)]
        static void ToggleReleased()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (database == null)
            {
                Debug.LogWarning("[GameSystems] GameDatabase가 아직 없습니다. 데이터베이스 창을 한 번 여세요.");
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

        // 이 항목의 ID를 가리키는 모든 곳(조건·보상·태그 칸 등 ID 참조 필드).
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
            return ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
        }

        // ID를 바꾼다. 이 ID를 가리키는 모든 필드, 에셋 파일 이름, 생성 코드(GameIds)가 함께 바뀐다.
        // 출시 후: 예전 이름으로 저장된 플레이어 데이터를 런타임이 새 이름으로 이어받도록 변경 이력을 남긴다.
        // 개발 중: 이력을 남기지 않는다(호환되지 않는 저장은 런타임이 지우고 새로 시작).
        public int Rename(GameData data, string newId)
        {
            var oldId = data.Id;
            if (oldId == newId) return 0;
            if (!CanUseId(newId, data, out var error)) throw new ArgumentException("[GameSystems] " + error);

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

        // 프로젝트에서 프리팹 에셋을 지우면 그 프리팹을 가리키던 도감 항목도 지운다(연결만 남은 빈 항목이 생기지 않게).
        public int RemoveOrphanPrefabEntries()
        {
            int removed = 0;
            foreach (var entry in OfType<PrefabData>().ToList())
            {
                if (entry.prefab != null || string.IsNullOrEmpty(entry.PrefabGuid)) continue;
                if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.PrefabGuid))) continue;
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(entry));
                Debug.Log($"[GameSystems] 프리팹이 지워져 도감 항목 '{entry.DisplayName}'을(를) 정리했습니다.");
                removed++;
            }
            if (removed > 0) MarkDirty();
            return removed;
        }

        // ID 없는 항목(Create 메뉴로 만든 것 등)에 ID를 부여하고, 런타임용 GameDatabase 목록을 맞춘다.
        public void Sync()
        {
            _all = null;
            foreach (var data in All.Where(d => string.IsNullOrEmpty(d.Id)).ToList())
            {
                var info = DataTypes.Find(data.GetType());
                data.EditorAssignId(NextId(info));
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                _all = null;
            }
            SyncDatabaseAsset();
            MarkDirty();
        }

        public void SyncDatabaseAsset()
        {
            var entries = All.ToList();
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
