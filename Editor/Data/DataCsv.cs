using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // DB 항목 ↔ CSV: 구글 시트·엑셀에서 수치를 한꺼번에 고칠 때(밸런스 조정).
    // 한 줄 = 항목 하나(첫 칸 id), 칸 = 단순 필드(숫자·글자·참거짓·enum). 묶음 필드 안쪽은 "curve.step", 목록은 줄마다 "entries[0].weight"·"tags[1]"처럼.
    // Block 칸([SerializeReference]: 트리거·조건·결과 …)은 빠진다.
    // 인스턴스 탭은 값의 클래스마다 따로 내보낸다(칸 = 그 클래스의 필드).
    // 가져오기는 DB의 다른 변경처럼 '적용 전'으로 들어간다(Ctrl+S로 적용, 되돌리기 가능). 없는 id는 새 항목으로 만든다.
    // 목록의 길이는 값이 있는 마지막 줄 번호까지다(뒤쪽 줄을 모두 비우면 그만큼 줄어든다). 새로 생긴 줄은 그 클래스의 기본값에서 시작한다.
    // enum 칸은 코드 이름(Flat)과 DB에 보이는 이름(플랫(+값)) 모두 읽는다. 내보낼 때는 코드 이름.
    public static class DataCsv
    {
        const string InstanceRoot = "value";
        static readonly Regex Index = new Regex(@"\[(\d+)\]", RegexOptions.CultureInvariant);

        public static string Export(Type kind, Type valueClass = null)
        {
            var items = Items(kind, valueClass);
            var instance = valueClass != null;
            var columns = Order(items.SelectMany(item => Columns(item, instance)));
            var rows = new List<IEnumerable<string>> { new[] { "id" }.Concat(columns).ToList() };
            foreach (var item in items)
            {
                var so = new SerializedObject(item);
                rows.Add(new[] { item.Id }.Concat(columns.Select(c => Format(so.FindProperty(Full(c, instance))))).ToList());
            }
            return Csv.Write(rows);
        }

        // 결과 보고(몇 줄 고침·새로 만듦·모르는 칸).
        public static string Import(Type kind, Type valueClass, string text)
        {
            var rows = Csv.Read(text);
            if (rows.Count < 2) return "읽을 줄이 없습니다(첫 줄은 칸 이름, 그 아래가 항목).";
            var header = rows[0].Select(h => h.Trim()).ToArray();
            var idColumn = Csv.Column(header, "id");
            if (idColumn < 0) return "첫 줄에 'id' 칸이 없습니다.";
            var instance = valueClass != null;
            var db = EditorDatabase.Instance;
            var staging = EditorDatabase.Staging;
            int changed = 0, created = 0;
            var unknown = new HashSet<string>(StringComparer.Ordinal);
            var problems = new List<string>();
            foreach (var row in rows.Skip(1))
            {
                var id = Csv.Cell(row, idColumn).Trim();
                if (id.Length == 0) continue;
                var existing = db.All.FirstOrDefault(d => d != null && d.Id == id && d.GetType() == kind);
                GameData target;
                if (existing != null) target = staging.Work(existing);
                else
                {
                    try { target = staging.Add(kind, id); }
                    catch (ArgumentException e)
                    {
                        problems.Add($"{id}: {e.Message.Replace("[MomoiProject] ", "")}");
                        continue;
                    }
                    if (target is InstanceData data && instance)
                    {
                        data.instanceType = valueClass.FullName;
                        data.value = DataModel.Create(valueClass);
                    }
                    created++;
                }
                var so = new SerializedObject(target);
                // 목록 길이부터 맞춘다(바깥 목록 먼저 → 안쪽 목록).
                foreach (var (list, size) in ListSizes(header, row, idColumn, instance))
                {
                    var property = so.FindProperty(list);
                    if (property != null && IsList(property)) Resize(property, size);
                }
                var any = false;
                for (int c = 0; c < header.Length; c++)
                {
                    if (c == idColumn || header[c].Length == 0) continue;
                    var cell = Csv.Cell(row, c);
                    var inList = header[c].Contains("[");
                    if (inList && cell.Trim().Length == 0) continue;   // 목록 줄의 빈 칸은 그대로(기본값)
                    var property = so.FindProperty(Full(header[c], instance));
                    if (property == null || !IsSimple(property))
                    {
                        unknown.Add(header[c]);
                        continue;
                    }
                    if (Parse(property, cell, out var error)) any = true;
                    else problems.Add($"{id}.{header[c]}: {error}");
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                if (any && existing != null) changed++;
            }
            staging.Touch();
            var report = new StringBuilder($"{changed}개 고침, {created}개 새로 만듦(적용 전 — Ctrl+S로 적용).");
            if (unknown.Count > 0) report.Append($"\n모르는 칸(건너뜀): {string.Join(", ", unknown)}");
            foreach (var problem in problems.Take(20)) report.Append("\n" + problem);
            if (problems.Count > 20) report.Append($"\n… 외 {problems.Count - 20}개");
            return report.ToString();
        }

        // 파일 고르기와 함께(DB 목록의 CSV 메뉴).
        public static void ExportToFile(Type kind, Type valueClass = null)
        {
            var name = (valueClass?.Name ?? DataTypes.Find(kind)?.DisplayName ?? kind.Name) + ".csv";
            var path = EditorUtility.SaveFilePanel("CSV로 내보내기", "", name, "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, Export(kind, valueClass), new UTF8Encoding(true));
            EditorUtility.RevealInFinder(path);
        }

        public static void ImportFromFile(Type kind, Type valueClass = null)
        {
            var path = EditorUtility.OpenFilePanel("CSV 가져오기", "", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var report = Import(kind, valueClass, File.ReadAllText(path));
            EditorUtility.DisplayDialog("CSV 가져오기", report, "확인");
        }

        // 인스턴스 탭: 값의 클래스 목록(내보내기 메뉴용).
        public static IEnumerable<Type> InstanceClasses() =>
            EditorDatabase.Instance.OfType<InstanceData>().Select(i => i.value?.GetType()).Where(t => t != null).Distinct().OrderBy(t => t.Name, StringComparer.Ordinal);

        static List<GameData> Items(Type kind, Type valueClass) =>
            EditorDatabase.Instance.OfType(kind)
                .Where(d => valueClass == null || (d is InstanceData i && i.value != null && i.value.GetType() == valueClass))
                .OrderBy(d => d.Id, StringComparer.Ordinal).ToList();

        // 칸 이름 "entries[0].weight" ↔ 속성 경로 "entries.Array.data[0].weight".
        static string Full(string column, bool instance)
        {
            var path = Index.Replace(column, ".Array.data[$1]");
            return instance ? InstanceRoot + "." + path : path;
        }

        static string Column(string propertyPath, bool instance)
        {
            var column = propertyPath.Replace(".Array.data[", "[");
            return instance && column.StartsWith(InstanceRoot + ".", StringComparison.Ordinal) ? column.Substring(InstanceRoot.Length + 1) : column;
        }

        // 칸 순서: 필드가 처음 나온 순서. 목록은 그 목록이 처음 나온 자리에 줄 번호 순서로 모이고, 한 줄 안에서는 필드 순서
        // (entries[0].item, entries[0].weight, entries[1].item …). 항목마다 목록 길이가 달라도 한데 모인다.
        static List<string> Order(IEnumerable<string> columns)
        {
            var roots = new Dictionary<string, int>(StringComparer.Ordinal);
            var patterns = new Dictionary<string, int>(StringComparer.Ordinal);
            var all = new List<string>();
            foreach (var column in columns)
            {
                if (all.Contains(column)) continue;
                all.Add(column);
                var root = RootOf(column);
                if (!roots.ContainsKey(root)) roots[root] = roots.Count;
                var pattern = Index.Replace(column, "[]");
                if (!patterns.ContainsKey(pattern)) patterns[pattern] = patterns.Count;
            }
            return all.OrderBy(c => roots[RootOf(c)])
                      .ThenBy(c => string.Join(".", Index.Matches(c).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString("D6", CultureInfo.InvariantCulture))), StringComparer.Ordinal)
                      .ThenBy(c => patterns[Index.Replace(c, "[]")])
                      .ToList();
        }

        // 목록 칸이면 첫 목록까지의 앞부분(entries[0].weight → entries), 아니면 칸 이름 그대로.
        static string RootOf(string column)
        {
            var at = column.IndexOf('[');
            return at < 0 ? column : column.Substring(0, at);
        }

        static IEnumerable<string> Columns(GameData item, bool instance)
        {
            var so = new SerializedObject(item);
            if (instance)
            {
                var root = so.FindProperty(InstanceRoot);
                if (root == null) yield break;
                foreach (var child in Children(root))
                    foreach (var leaf in Leaves(child))
                        yield return Column(leaf.propertyPath, true);
                yield break;
            }
            var it = so.GetIterator();
            if (!it.NextVisible(true)) yield break;
            do
            {
                if (it.name == "m_Script") continue;
                foreach (var leaf in Leaves(it.Copy())) yield return Column(leaf.propertyPath, false);
            }
            while (it.NextVisible(false));
        }

        // 단순 칸 전부(묶음 필드 안쪽, 목록의 줄마다). Block 칸은 건너뛴다.
        static IEnumerable<SerializedProperty> Leaves(SerializedProperty property)
        {
            if (IsSimple(property))
            {
                yield return property.Copy();
                yield break;
            }
            if (IsList(property))
            {
                for (int i = 0; i < property.arraySize; i++)
                    foreach (var leaf in Leaves(property.GetArrayElementAtIndex(i))) yield return leaf;
                yield break;
            }
            if (IsGroup(property))
                foreach (var child in Children(property))
                    foreach (var leaf in Leaves(child)) yield return leaf;
        }

        static IEnumerable<SerializedProperty> Children(SerializedProperty parent)
        {
            var it = parent.Copy();
            var end = parent.GetEndProperty();
            if (!it.NextVisible(true)) yield break;
            while (!SerializedProperty.EqualContents(it, end))
            {
                yield return it.Copy();
                if (!it.NextVisible(false)) break;
            }
        }

        // 이 줄에서 목록마다 필요한 길이: 값이 있는 칸의 가장 큰 번호 + 1(목록 칸이 머리에 있는데 모두 비면 0). 바깥 목록이 먼저 온다.
        static IEnumerable<(string list, int size)> ListSizes(string[] header, string[] row, int idColumn, bool instance)
        {
            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int c = 0; c < header.Length; c++)
            {
                if (c == idColumn || !header[c].Contains("[")) continue;
                var filled = Csv.Cell(row, c).Trim().Length > 0;
                foreach (Match match in Index.Matches(header[c]))
                {
                    var list = Full(header[c].Substring(0, match.Index), instance);
                    var need = filled ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1 : 0;
                    sizes[list] = Math.Max(sizes.TryGetValue(list, out var known) ? known : 0, need);
                }
            }
            return sizes.OrderBy(p => p.Key.Length).Select(p => (p.Key, p.Value));
        }

        // 목록 길이를 맞춘다. 새로 생긴 줄은 그 클래스의 기본값(복사된 앞 줄 값·Block 참조를 남기지 않는다).
        static void Resize(SerializedProperty list, int size)
        {
            var before = list.arraySize;
            if (before == size) return;
            list.arraySize = size;
            for (int i = before; i < size; i++) Reset(list.GetArrayElementAtIndex(i));
        }

        static void Reset(SerializedProperty element)
        {
            try
            {
                var field = PropertyUtil.GetField(element, out _);
                var type = field == null ? null : PropertyUtil.ElementType(field.FieldType);
                if (type != null && !type.IsAbstract && element.propertyType == SerializedPropertyType.Generic)
                {
                    element.boxedValue = DataModel.Create(type);
                    return;
                }
            }
            catch (Exception)
            {
                // 아래에서 Block 참조만 비운다.
            }
            foreach (var child in Children(element))
                if (child.propertyType == SerializedPropertyType.ManagedReference) child.managedReferenceValue = null;
        }

        static bool IsList(SerializedProperty property) =>
            property.isArray && property.propertyType == SerializedPropertyType.Generic;

        static bool IsGroup(SerializedProperty property) =>
            property.propertyType == SerializedPropertyType.Generic && !property.isArray && property.hasVisibleChildren;

        static bool IsSimple(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                    return true;
                default:
                    return false;
            }
        }

        static string Format(SerializedProperty property)
        {
            if (property == null) return "";
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float: return property.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length ? property.enumNames[property.enumValueIndex] : "";
                case SerializedPropertyType.String: return property.stringValue;
                default: return "";
            }
        }

        static bool Parse(SerializedProperty property, string cell, out string error)
        {
            error = null;
            var text = (cell ?? "").Trim();
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)) { property.longValue = whole; return true; }
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rounded) && Math.Abs(rounded - Math.Round(rounded)) < 1e-9) { property.longValue = (long)Math.Round(rounded); return true; }
                    error = $"정수가 아닙니다('{text}')";
                    return false;
                case SerializedPropertyType.Float:
                    if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { property.doubleValue = number; return true; }
                    error = $"숫자가 아닙니다('{text}')";
                    return false;
                case SerializedPropertyType.Boolean:
                    switch (text.ToLowerInvariant())
                    {
                        case "true": case "1": case "o": case "y": case "yes": case "참": property.boolValue = true; return true;
                        case "false": case "0": case "x": case "n": case "no": case "거짓": case "": property.boolValue = false; return true;
                    }
                    error = $"참·거짓이 아닙니다('{text}')";
                    return false;
                case SerializedPropertyType.Enum:
                {
                    var index = Array.FindIndex(property.enumNames, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                    if (index < 0) index = Array.FindIndex(property.enumDisplayNames, n => n == text);
                    if (index >= 0) { property.enumValueIndex = index; return true; }
                    error = $"고를 수 있는 값이 아닙니다('{text}': {string.Join(", ", property.enumNames)})";
                    return false;
                }
                case SerializedPropertyType.String:
                    property.stringValue = cell ?? "";
                    return true;
                default:
                    error = "고칠 수 없는 칸입니다";
                    return false;
            }
        }
    }
}
