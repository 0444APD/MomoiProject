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
            if (string.IsNullOrEmpty(id)) return "(통계 없음)";
            return OfType<StatData>().FirstOrDefault(s => s.Id == id)?.DisplayName ?? $"(없는 통계: {id})";
        }

        public string AchievementName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(도전과제 없음)";
            return OfType<AchievementData>().FirstOrDefault(a => a.Id == id)?.DisplayName ?? $"(없는 도전과제: {id})";
        }

        // ── 생성과 ID ─────────────────────────────────────────

        public GameData Create(Type type, string displayName = null)
        {
            var info = DataTypes.Find(type) ?? throw new ArgumentException($"{type.Name}은 데이터베이스 타입이 아닙니다.");
            var id = NextId(info);
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
            bool relevant = deleted.Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) ||
                            imported.Concat(moved).Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                                                            typeof(GameData).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(p)));
            if (!relevant || _scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                EditorDatabase.Instance.Sync();
            };
        }
    }
}
