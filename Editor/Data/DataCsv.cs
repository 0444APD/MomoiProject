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
    // 가져오기는 DB의 다른 변경처럼 '적용 전'으로 들어간다(Ctrl+S로 적용, 되돌리기 가능).
    // 없는 id: 개발자가 만드는 종류([Database(DeveloperAuthored = true)])나 구조 편집이면 새 항목으로 만들고, 로직 쪽 종류면 건너뛴다(새 항목은 AI가 만든다, EditorPolicy).
    // 목록 줄(가중치 표의 항목 등)을 더하고 빼는 것은 어느 종류든 된다.
    // 목록의 길이는 값이 있는 마지막 줄 번호까지다(뒤쪽 줄을 모두 비우면 그만큼 줄어든다). 새로 생긴 줄은 그 클래스·형식의 기본값에서 시작한다.
    // 목록 안의 빈 글자는 ""로 적는다(빈 칸은 '값 없음'이라 맨 뒤 빈 글자 줄이 사라지지 않게 — 내보낼 때 필요한 곳에 적는다).
    // enum 칸은 코드 이름(Flat)과 DB에 보이는 이름(플랫(+값)) 모두 읽는다. 내보낼 때는 코드 이름.
    // 숫자 칸은 천 단위 쉼표(1,000·1,000.5)와 소수 쉼표 하나(0,5)도 읽는다. 어느 쪽인지 알 수 없는 쉼표(1.000,5·0,500)는 그 칸을 오류로 알린다.
    // 파일은 UTF-8로 읽는다(Excel은 'CSV UTF-8'로 저장). 다른 인코딩이면 읽지 않고 알린다(한글이 깨진 채 들어가지 않게).
    public static class DataCsv
    {
        const string InstanceRoot = "value";
        // 목록 안의 빈 글자 표시(가져올 때 빈 글자로 읽는다).
        const string EmptyText = "\"\"";
        static readonly Regex Index = new Regex(@"\[(\d+)\]", RegexOptions.CultureInvariant);
        // 천 단위 쉼표(1,000 · -1,234,567 · 1,000.5)와 소수 쉼표 하나(0,5 · 3,14 — 쉼표 뒤가 세 자리면 천 단위와 헷갈려 받지 않는다).
        static readonly Regex Grouped = new Regex(@"^[+-]?[1-9][0-9]{0,2}(,[0-9]{3})+(\.[0-9]+)?$", RegexOptions.CultureInvariant);
        static readonly Regex DecimalComma = new Regex(@"^[+-]?[0-9]+,([0-9]{1,2}|[0-9]{4,})$", RegexOptions.CultureInvariant);

        public static string Export(Type kind, Type valueClass = null)
        {
            var items = Items(kind, valueClass);
            var instance = valueClass != null;
            var columns = Order(items.SelectMany(item => Columns(item, instance)));
            var rows = new List<IEnumerable<string>> { new[] { "id" }.Concat(columns).ToList() };
            foreach (var item in items)
            {
                var so = new SerializedObject(item);
                var cells = columns.Select(c => Format(so.FindProperty(Full(c, instance)))).ToArray();
                KeepTrailing(so, columns, cells, instance);
                rows.Add(new[] { item.Id }.Concat(cells).ToList());
            }
            return Csv.Write(rows);
        }

        // 목록의 마지막 줄 칸이 모두 비면(빈 글자 요소 등) 가져올 때 그 줄이 사라진다 → 그 줄의 첫 글자 칸에 "" 표시를 적는다.
        static void KeepTrailing(SerializedObject so, List<string> columns, string[] cells, bool instance)
        {
            var lists = columns.SelectMany(c => Index.Matches(c).Cast<Match>().Select(m => c.Substring(0, m.Index))).Distinct().ToList();
            foreach (var list in lists)
            {
                var property = so.FindProperty(Full(list, instance));
                if (property == null || !IsList(property) || property.arraySize == 0) continue;
                var last = $"{list}[{property.arraySize - 1}]";
                var own = Enumerable.Range(0, columns.Count)
                    .Where(i => columns[i] == last || columns[i].StartsWith(last + ".", StringComparison.Ordinal) || columns[i].StartsWith(last + "[", StringComparison.Ordinal))
                    .ToList();
                if (own.Count == 0 || own.Any(i => cells[i].Trim().Length > 0)) continue;
                var marker = own.Where(i => so.FindProperty(Full(columns[i], instance))?.propertyType == SerializedPropertyType.String).DefaultIfEmpty(-1).First();
                if (marker >= 0) cells[marker] = EmptyText;
            }
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
            // 로직 쪽 종류의 새 항목은 AI가 DB 반영 스크립트로 만든다(EditorPolicy) — 없는 id는 건너뛴다.
            var canCreate = EditorPolicy.CanAddRemove(kind);
            int changed = 0, created = 0, skipped = 0;
            var unknown = new HashSet<string>(StringComparer.Ordinal);
            var problems = new List<string>();
            foreach (var row in rows.Skip(1))
            {
                var id = Csv.Cell(row, idColumn).Trim();
                if (id.Length == 0) continue;
                var existing = db.All.FirstOrDefault(d => d != null && d.Id == id && d.GetType() == kind);
                GameData target;
                if (existing != null) target = staging.Work(existing);
                else if (!canCreate)
                {
                    skipped++;
                    continue;
                }
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
                // 고친 개수는 값이 실제로 바뀐 항목만 센다(목록 길이가 바뀐 것 포함). 같은 값을 다시 넣은 줄은 세지 않는다.
                var before = existing != null ? EditorJsonUtility.ToJson(target) : null;
                var so = new SerializedObject(target);
                // 목록 길이부터 맞춘다(바깥 목록 먼저 → 안쪽 목록).
                foreach (var (list, size) in ListSizes(header, row, idColumn, instance))
                {
                    var property = so.FindProperty(list);
                    if (property != null && IsList(property)) Resize(property, size);
                }
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
                    if (!Parse(property, cell, out var error)) problems.Add($"{id}.{header[c]}: {error}");
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                if (before != null && EditorJsonUtility.ToJson(target) != before) changed++;
            }
            staging.Touch();
            var report = new StringBuilder($"{changed}개 고침, {created}개 새로 만듦(적용 전 — Ctrl+S로 적용).");
            if (skipped > 0) report.Append($"\n없는 id {skipped}개는 건너뜀 — 새 항목은 AI가 만든다.");
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
            if (!TryReadUtf8(path, "CSV 가져오기", out var text)) return;
            var report = Import(kind, valueClass, text);
            EditorUtility.DisplayDialog("CSV 가져오기", report, "확인");
        }

        // UTF-8로만 읽는다. Excel의 기본 'CSV'(한국어 Windows = CP949)는 한글이 깨진 채(U+FFFD) 들어가므로 알리고 멈춘다(아무것도 바꾸지 않음).
        // 텍스트 시트 가져오기도 쓴다.
        internal static bool TryReadUtf8(string path, string title, out string text)
        {
            try
            {
                text = ReadUtf8(path);
                return true;
            }
            catch (DecoderFallbackException)
            {
                EditorUtility.DisplayDialog(title, "CSV를 UTF-8로 저장해 주세요(Excel: CSV UTF-8).\n아무것도 바꾸지 않았습니다.", "확인");
                text = null;
                return false;
            }
        }

        // 잘못된 바이트(UTF-8이 아님)면 DecoderFallbackException.
        // 엑셀에 열어 둔 채로도 읽는다(엑셀은 쓰기 권한으로 열어 두어 File.ReadAllText는 '다른 프로세스가 사용 중' IOException).
        internal static string ReadUtf8(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                return reader.ReadToEnd();
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

        // 목록 길이를 맞춘다. 새로 생긴 줄은 그 클래스·형식의 기본값(Unity가 늘릴 때 복사한 앞 줄 값·Block 참조를 남기지 않는다).
        static void Resize(SerializedProperty list, int size)
        {
            var before = list.arraySize;
            if (before == size) return;
            list.arraySize = size;
            for (int i = before; i < size; i++) Reset(list.GetArrayElementAtIndex(i));
        }

        static void Reset(SerializedProperty element)
        {
            // 단순 값 요소(글자·숫자 목록): 앞 줄 값이 복사되지 않게 빈 값으로(tags ["a"]에 tags[2]만 적으면 ["a", "", "c"]).
            switch (element.propertyType)
            {
                case SerializedPropertyType.String: element.stringValue = ""; return;
                case SerializedPropertyType.Integer: element.longValue = 0; return;
                case SerializedPropertyType.Float: element.doubleValue = 0; return;
                case SerializedPropertyType.Boolean: element.boolValue = false; return;
                case SerializedPropertyType.Enum: element.intValue = 0; return;
                case SerializedPropertyType.ObjectReference: element.objectReferenceValue = null; return;
                case SerializedPropertyType.ManagedReference: element.managedReferenceValue = null; return;
            }
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
                // float 칸은 float로 적는다(double로 바꾸면 0.1이 0.10000000149011612로 나온다).
                case SerializedPropertyType.Float:
                    return property.type == "float" ? property.floatValue.ToString("R", CultureInfo.InvariantCulture) : property.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length ? property.enumNames[property.enumValueIndex] : "";
                case SerializedPropertyType.String: return property.stringValue;
                default: return "";
            }
        }

        internal static bool Parse(SerializedProperty property, string cell, out string error)
        {
            error = null;
            var text = (cell ?? "").Trim();
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                {
                    var plain = PlainNumber(text);
                    if (!long.TryParse(plain, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                    {
                        if (!double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var rounded) || Math.Abs(rounded - Math.Round(rounded)) >= 1e-9)
                        {
                            error = plain == null ? CommaError(text) : $"정수가 아닙니다('{text}')";
                            return false;
                        }
                        // long 밖(1e19)이면 (long) 바꾸기가 엉뚱한 값이 된다.
                        if (Math.Abs(rounded) >= 9.2233720368547758E18)
                        {
                            error = RangeError(text);
                            return false;
                        }
                        whole = (long)Math.Round(rounded);
                    }
                    // 필드 형식(int·byte …)에 들어가지 않는 값은 넣지 않고 알린다(longValue는 범위를 보지 않아 그 형식으로 잘린 다른 값이 말없이 들어간다).
                    if (!Fits(property.numericType, whole))
                    {
                        error = RangeError(text);
                        return false;
                    }
                    property.longValue = whole;
                    return true;
                }
                case SerializedPropertyType.Float:
                {
                    var plain = PlainNumber(text);
                    if (double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { property.doubleValue = number; return true; }
                    error = plain == null ? CommaError(text) : $"숫자가 아닙니다('{text}')";
                    return false;
                }
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
                    // "" 표시 = 빈 글자(목록 맨 뒤의 빈 글자 줄을 남길 때 내보내기가 적는다).
                    property.stringValue = text == EmptyText ? "" : cell ?? "";
                    return true;
                default:
                    error = "고칠 수 없는 칸입니다";
                    return false;
            }
        }

        // 숫자 칸의 글자 → 읽을 수 있는 꼴(알 수 없으면 null). 쉼표가 없으면 그대로(1000.5 · 1e3).
        // 천 단위 쉼표(한국어 Excel·구글 시트의 1,000 · 1,000.5)는 뺀다. 소수 쉼표 하나(0,5)는 점이 없고 쉼표 뒤가 세 자리가 아닐 때만 점으로.
        // 그 밖의 쉼표(1.000,5 · 0,500 · 1,00,000)는 어느 쪽인지 알 수 없어 읽지 않는다 — 쉼표를 모두 점으로 바꾸면 1,000이 1이 되어 값이 1000배 작아진다.
        internal static string PlainNumber(string text)
        {
            if (text == null || text.IndexOf(',') < 0) return text;
            if (Grouped.IsMatch(text)) return text.Replace(",", "");
            if (DecimalComma.IsMatch(text)) return text.Replace(',', '.');
            return null;
        }

        static string CommaError(string text) => $"쉼표가 천 단위인지 소수점인지 알 수 없습니다('{text}' — 1000.5처럼 적어 주세요)";

        static string RangeError(string text) => $"이 칸의 정수 형식에 들어가지 않는 크기입니다('{text}')";

        // 정수 칸의 형식에 들어가는 값인가. ulong은 내보내기가 longValue로 적으므로(큰 값은 음수) 가리지 않는다.
        static bool Fits(SerializedPropertyNumericType type, long value)
        {
            switch (type)
            {
                case SerializedPropertyNumericType.Int8: return value >= sbyte.MinValue && value <= sbyte.MaxValue;
                case SerializedPropertyNumericType.UInt8: return value >= byte.MinValue && value <= byte.MaxValue;
                case SerializedPropertyNumericType.Int16: return value >= short.MinValue && value <= short.MaxValue;
                case SerializedPropertyNumericType.UInt16: return value >= ushort.MinValue && value <= ushort.MaxValue;
                case SerializedPropertyNumericType.Int32: return value >= int.MinValue && value <= int.MaxValue;
                case SerializedPropertyNumericType.UInt32: return value >= uint.MinValue && value <= uint.MaxValue;
                default: return true;
            }
        }
    }
}
