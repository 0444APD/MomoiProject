using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 텍스트 시트에 행을 더하는 곳(선택 모듈: 다이얼로그 시스템의 대사 등). 원문은 모듈 쪽이 기준이라 가져올 때 원문 칸은 건너뛴다.
    public interface ITextSheetSource
    {
        // sourceText: 원문 표에서 키 찾기(없으면 null). 문맥 열에 이름을 보일 때 쓴다.
        IEnumerable<TextSheetRow> Rows(Func<string, string> sourceText);
    }

    public sealed class TextSheetRow
    {
        public string Key;
        public string Source;
        // 문맥 열 이름 → 값(예: dialogue, location, speaker). 읽기 전용, 가져올 때 무시.
        public Dictionary<string, string> Context = new Dictionary<string, string>();
        public string Note;
    }

    // 다국어 설정·텍스트 표 만들기, 시트(CSV) 내보내기·가져오기, 번역 검사.
    public static class TextSheetTools
    {
        public const string SettingsPath = EditorDatabase.DataRoot + "/Resources/MomoiProject/LocalizationSettings.asset";
        public const string TableFolder = EditorDatabase.DataRoot + "/Texts";
        const string Menu = "MomoiProject/다국어/";

        // 설정이 없으면 원문 언어 표와 함께 만든다.
        public static LocalizationSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (settings != null) return settings;
            EditorDatabase.EnsureFolder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            foreach (var language in settings.languages) language.table = EnsureTable(language.code);
            Save(settings);
            return settings;
        }

        public static TextTable EnsureTable(string code)
        {
            var path = $"{TableFolder}/Texts_{code}.asset";
            var table = AssetDatabase.LoadAssetAtPath<TextTable>(path);
            if (table != null) return table;
            EditorDatabase.EnsureFolder(TableFolder);
            table = ScriptableObject.CreateInstance<TextTable>();
            table.language = code;
            AssetDatabase.CreateAsset(table, path);
            return table;
        }

        static void Save(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        [MenuItem(Menu + "설정 열기", priority = 40)]
        static void OpenSettings()
        {
            Selection.activeObject = EnsureSettings();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        // 설정의 모든 언어에 표가 있게 한다(새로 추가한 언어).
        static void FillTables(LocalizationSettings settings)
        {
            var changed = false;
            foreach (var language in settings.languages.Where(l => l != null && !string.IsNullOrEmpty(l.code) && l.table == null))
            {
                language.table = EnsureTable(language.code);
                changed = true;
            }
            if (changed) Save(settings);
        }

        [MenuItem(Menu + "시트 내보내기 (CSV)", priority = 41)]
        static void ExportMenu()
        {
            var settings = EnsureSettings();
            FillTables(settings);
            var path = EditorUtility.SaveFilePanel("텍스트 시트 내보내기", "", "texts.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, Export(settings), new UTF8Encoding(true));
            Debug.Log($"[다국어] 텍스트 시트를 내보냈습니다: {path}");
        }

        public static string Export(LocalizationSettings settings)
        {
            // 원문 언어를 첫 언어 열로.
            var codes = settings.Codes.OrderBy(c => string.Equals(c, settings.sourceLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
            var tables = codes.ToDictionary(c => c, c => settings.Find(c)?.table?.ToDictionary() ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            var sourceTable = tables.TryGetValue(settings.sourceLanguage, out var st) ? st : new Dictionary<string, string>();
            var order = settings.Find(settings.sourceLanguage)?.table?.entries.Select(e => e.key).ToList() ?? new List<string>();
            // 순서 목록에 이미 있는 키(대사가 수만 줄이면 List.Contains는 줄 수의 제곱만큼 걸린다).
            var ordered = new HashSet<string>(order, StringComparer.Ordinal);

            // 모듈이 더하는 행(다이얼로그 시스템의 대사 등): 원문은 모듈 쪽(그래프)이 기준이다.
            var rows = SourceRows(sourceTable).ToList();
            var notes = new Dictionary<string, string>(StringComparer.Ordinal);
            var context = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var columns = new List<string>();
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Key)) continue;
                sourceTable[row.Key] = row.Source ?? "";
                if (ordered.Add(row.Key)) order.Add(row.Key);
                if (!string.IsNullOrEmpty(row.Note)) notes[row.Key] = row.Note;
                context[row.Key] = row.Context;
                foreach (var column in row.Context.Keys)
                    if (!columns.Contains(column)) columns.Add(column);
            }
            var readOnly = tables.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value);
            return TextSheet.Export(codes, readOnly, order, notes, columns,
                (key, column) => context.TryGetValue(key, out var c) && c.TryGetValue(column, out var v) ? v : "");
        }

        // 모든 ITextSheetSource의 행.
        public static IEnumerable<TextSheetRow> SourceRows(IReadOnlyDictionary<string, string> sourceTable)
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<ITextSheetSource>().Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null))
            foreach (var row in ((ITextSheetSource)Activator.CreateInstance(type)).Rows(key => sourceTable.TryGetValue(key, out var v) ? v : null))
                yield return row;
        }

        [MenuItem(Menu + "시트 가져오기 (CSV)", priority = 42)]
        static void ImportMenu()
        {
            var settings = EnsureSettings();
            FillTables(settings);
            var path = EditorUtility.OpenFilePanel("텍스트 시트 가져오기", "", "csv");
            if (string.IsNullOrEmpty(path)) return;
            // UTF-8만(DB CSV와 같은 검사): CP949로 저장한 시트는 한글이 U+FFFD로 깨진 채 들어가 원문 표까지 덮을 수 있다.
            if (!DataCsv.TryReadUtf8(path, "텍스트 시트 가져오기", out var csv)) return;
            var choice = EditorUtility.DisplayDialogComplex("텍스트 시트 가져오기",
                "어느 열을 가져올까요?\n\n번역 언어만: 원문(" + settings.sourceLanguage + ") 열은 건너뜁니다(원문은 에디터에서 쓰는 것이 기준).\n모든 언어: 원문 열도 덮어씁니다.",
                "번역 언어만", "취소", "모든 언어");
            if (choice == 1) return;
            var report = Import(settings, csv, includeSource: choice == 2);
            Debug.Log("[다국어] " + report);
        }

        // 시트 내용을 표에 반영하고 요약을 돌려준다. 빈 칸은 기존 값을 지우지 않는다.
        public static string Import(LocalizationSettings settings, string csv, bool includeSource)
        {
            var codes = settings.Codes.Where(c => includeSource || !string.Equals(c, settings.sourceLanguage, StringComparison.OrdinalIgnoreCase)).ToList();
            var result = TextSheet.Import(csv, codes);
            // 모듈 행(다이얼로그 시스템의 대사 등)의 원문은 그래프가 기준이므로 원문 언어 표에 넣지 않는다.
            // 지금 원문 표를 넘긴다(빈 표를 넘기면 모듈이 원문 표에 이미 있는 이름 — 프로필·장소의 '<id>.name' — 까지 제 행으로 내놓아 시트에서 고친 그 이름이 들어가지 않는다).
            if (result.Tables.TryGetValue(settings.sourceLanguage, out var sourceImport))
            {
                var sourceTable = settings.Find(settings.sourceLanguage)?.table?.ToDictionary() ?? new Dictionary<string, string>();
                foreach (var row in SourceRows(sourceTable))
                    if (!string.IsNullOrEmpty(row.Key)) sourceImport.Remove(row.Key);
            }
            var lines = new List<string>();
            foreach (var pair in result.Tables)
            {
                var table = settings.Find(pair.Key)?.table;
                if (table == null) continue;
                Undo.RecordObject(table, "텍스트 시트 가져오기");
                table.SetAll(pair.Value, removeOthers: false);
                Save(table);
                lines.Add($"{pair.Key} {pair.Value.Count}개");
            }
            GameLanguage.Reload();
            var summary = "가져옴: " + (lines.Count == 0 ? "없음" : string.Join(", ", lines));
            if (result.Warnings.Count > 0) summary += "\n" + string.Join("\n", result.Warnings);
            return summary;
        }

        [MenuItem(Menu + "번역 검사", priority = 43)]
        static void CheckMenu()
        {
            var settings = EnsureSettings();
            var issues = Check(settings).ToList();
            if (issues.Count == 0) Debug.Log("[다국어] 문제 없음");
            else Debug.LogWarning("[다국어] 번역 검사 " + issues.Count + "건\n" + string.Join("\n", issues));
        }

        // 원문 문법 오류, 번역 누락, 번역의 값·용어·태그 차이.
        public static IEnumerable<string> Check(LocalizationSettings settings)
        {
            var source = settings.Find(settings.sourceLanguage)?.table?.ToDictionary() ?? new Dictionary<string, string>();
            foreach (var row in SourceRows(source).ToList())
                if (!string.IsNullOrEmpty(row.Key)) source[row.Key] = row.Source ?? "";
            foreach (var pair in source)
            {
                var error = TextFormat.Parse(pair.Value).Error;
                if (error != null) yield return $"{settings.sourceLanguage} · {pair.Key}: 문법 오류: {error}";
            }
            foreach (var language in settings.languages.Where(l => l != null && l.table != null && !string.Equals(l.code, settings.sourceLanguage, StringComparison.OrdinalIgnoreCase)))
            {
                var table = language.table.ToDictionary();
                var missing = source.Keys.Count(k => !table.ContainsKey(k));
                if (missing > 0) yield return $"{language.code}: 번역 없음 {missing}개";
                foreach (var key in table.Keys.Where(k => !source.ContainsKey(k)))
                    yield return $"{language.code} · {key}: 원문에 없는 키";
                foreach (var pair in table.Where(p => source.ContainsKey(p.Key)))
                foreach (var issue in TextSheet.Check(source[pair.Key], pair.Value))
                    yield return $"{language.code} · {pair.Key}: {issue}";
            }
        }

        [MenuItem(Menu + "DB 이름 키 채우기", priority = 44)]
        static void FillNamesMenu()
        {
            var added = FillCodexNames(EnsureSettings());
            Debug.Log(added == 0 ? "[다국어] 새로 채울 이름 키가 없습니다." : $"[다국어] 원문 표에 이름 키 {added}개를 더했습니다(값은 ID, 원문 표에서 이름으로 바꿔 주세요).");
        }

        // DB 항목마다 '<id>.name' 키가 원문 표에 있게 한다(없으면 ID를 값으로 더한다).
        public static int FillCodexNames(LocalizationSettings settings)
        {
            FillTables(settings);
            var table = settings.Find(settings.sourceLanguage)?.table;
            if (table == null) return 0;
            var existing = new HashSet<string>(table.entries.Select(e => e.key), StringComparer.Ordinal);
            var add = EditorDatabase.Instance.Committed
                .Where(d => !string.IsNullOrEmpty(d.Id) && !existing.Contains(d.Id + ".name"))
                .Select(d => d.Id)
                .Distinct()
                .ToList();
            if (add.Count == 0) return 0;
            Undo.RecordObject(table, "DB 이름 키 채우기");
            foreach (var id in add) table.entries.Add(new TextTable.Entry { key = id + ".name", text = id });
            Save(table);
            GameLanguage.Reload();
            return add.Count;
        }
    }
}
