using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 텍스트 시트(CSV) 주고받기. 규칙은 최소한만 둔다(세부 규칙은 대사 시스템과 함께 정한다).
    //
    //   #gbible-text,1          ← 첫 칸이 '#'인 줄은 설명·형식 버전(읽을 때 건너뛴다. 머리줄 아래에서는 key 칸이 '#'인 줄)
    //   key,ko,en,ja,note            ← 머리줄. 'key' 열과 언어 코드 열을 '이름'으로 찾는다(열 순서·다른 열은 상관없음)
    //   ui.start,시작하기,Start,スタート,
    //
    // 읽을 때는 이름이 맞는 언어 열만 가져온다. 빈 칸은 '번역 없음'으로 보고 건너뛴다(기존 값을 지우지 않는다).
    public static class TextSheet
    {
        public const string FormatLine = "#gbible-text";
        public const int FormatVersion = 1;
        public const string KeyColumn = "key";
        public const string NoteColumn = "note";

        // contextColumns: 키 다음에 두는 읽기 전용 문맥 열(예: dialogue·location·speaker). 가져올 때는 읽지 않는다.
        public static string Export(IReadOnlyList<string> languages, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables,
            IEnumerable<string> keyOrder = null, IReadOnlyDictionary<string, string> notes = null,
            IReadOnlyList<string> contextColumns = null, Func<string, string, string> context = null)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in (keyOrder ?? Enumerable.Empty<string>()).Concat(languages.SelectMany(l => tables.TryGetValue(l, out var t) ? t.Keys : Enumerable.Empty<string>())))
                if (seen.Add(key)) keys.Add(key);
            contextColumns ??= Array.Empty<string>();

            var rows = new List<IEnumerable<string>>
            {
                new[] { FormatLine, FormatVersion.ToString() },
                new[] { KeyColumn }.Concat(contextColumns).Concat(languages).Concat(new[] { NoteColumn }),
            };
            foreach (var key in keys)
            {
                var row = new List<string> { key };
                foreach (var column in contextColumns) row.Add(context?.Invoke(key, column) ?? "");
                foreach (var language in languages)
                    row.Add(tables.TryGetValue(language, out var table) && table.TryGetValue(key, out var value) ? value : "");
                row.Add(notes != null && notes.TryGetValue(key, out var note) ? note : "");
                rows.Add(row);
            }
            return Csv.Write(rows);
        }

        public sealed class ImportResult
        {
            // 언어 → (키 → 문장)
            public readonly Dictionary<string, Dictionary<string, string>> Tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Warnings = new List<string>();
            public int Version = FormatVersion;
        }

        // languages: 가져올 언어(null이면 머리줄의 key·note 외 모든 열).
        public static ImportResult Import(string csv, IEnumerable<string> languages = null)
        {
            var result = new ImportResult();
            var rows = Csv.Read(csv);
            string[] header = null;
            var line = 0;
            foreach (var row in rows)
            {
                line++;
                var first = row.Length > 0 ? row[0].Trim() : "";
                if (first.StartsWith("#", StringComparison.Ordinal))
                {
                    if (first == FormatLine && row.Length > 1 && int.TryParse(row[1], out var version))
                    {
                        result.Version = version;
                        if (version > FormatVersion) result.Warnings.Add($"시트 형식 버전 {version}은(는) 이 도구({FormatVersion})보다 새 버전입니다.");
                    }
                    continue;
                }
                if (header == null)
                {
                    header = row;
                    if (Csv.Column(header, KeyColumn) < 0)
                    {
                        result.Warnings.Add("머리줄에 'key' 열이 없습니다.");
                        return result;
                    }
                    continue;
                }
                break;
            }
            if (header == null)
            {
                result.Warnings.Add("머리줄이 없습니다.");
                return result;
            }

            var keyColumn = Csv.Column(header, KeyColumn);
            var wanted = languages?.ToList();
            var columns = new List<(string language, int column)>();
            for (var i = 0; i < header.Length; i++)
            {
                var name = header[i].Trim();
                if (i == keyColumn || name.Length == 0 || string.Equals(name, NoteColumn, StringComparison.OrdinalIgnoreCase)) continue;
                if (wanted != null && !wanted.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                columns.Add((wanted?.First(w => string.Equals(w, name, StringComparison.OrdinalIgnoreCase)) ?? name, i));
            }
            if (wanted != null)
                foreach (var language in wanted.Where(w => columns.All(c => !string.Equals(c.language, w, StringComparison.OrdinalIgnoreCase))))
                    result.Warnings.Add($"'{language}' 열이 시트에 없습니다.");
            foreach (var (language, _) in columns) result.Tables[language] = new Dictionary<string, string>(StringComparer.Ordinal);

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var headerSeen = false;
            line = 0;
            foreach (var row in rows)
            {
                line++;
                if (!headerSeen)
                {
                    var first = row.Length > 0 ? row[0].Trim() : "";
                    if (!first.StartsWith("#", StringComparison.Ordinal)) headerSeen = true;
                    continue;
                }
                // 머리줄 아래의 설명 줄은 key 칸이 '#'으로 시작한다(앞으로 옮긴 메모·언어 열의 '#'은 글이다).
                var key = Csv.Cell(row, keyColumn).Trim();
                if (key.Length == 0 || key.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!keys.Add(key)) result.Warnings.Add($"{line}번째 줄: 키 '{key}'가 두 번 나옵니다(뒤의 값을 씁니다).");
                foreach (var (language, column) in columns)
                {
                    var value = Csv.Cell(row, column);
                    if (value.Length > 0) result.Tables[language][key] = value;
                }
            }
            return result;
        }

        // ── 검사 ─────────────────────────────────────────────

        // 번역문이 원문과 같은 값·용어를 쓰는지, 문법 오류는 없는지.
        public static IEnumerable<string> Check(string source, string translation)
        {
            var target = TextFormat.Parse(translation);
            if (target.Error != null)
            {
                yield return "문법 오류: " + target.Error;
                yield break;
            }
            var original = TextFormat.Parse(source);
            if (original.Error != null) yield break;
            var sourceArgs = new HashSet<string>(original.Arguments, StringComparer.Ordinal);
            var targetArgs = new HashSet<string>(target.Arguments, StringComparer.Ordinal);
            // 용어에 넘기는 값([[potion, count]])도 코드가 넘기는 값이다: 번역이 그 값을 보여도 되고, 번역의 용어가 넘기지 않는 값을 쓰면 알린다.
            var passed = new HashSet<string>(sourceArgs.Concat(original.TermArguments), StringComparer.Ordinal);
            foreach (var missing in sourceArgs.Except(targetArgs)) yield return $"원문의 값 {{{missing}}}이(가) 없습니다.";
            foreach (var extra in targetArgs.Concat(target.TermArguments).Except(passed)) yield return $"원문에 없는 값 {{{extra}}}을(를) 씁니다.";
            var sourceTerms = new HashSet<string>(original.Terms, StringComparer.Ordinal);
            var targetTerms = new HashSet<string>(target.Terms, StringComparer.Ordinal);
            foreach (var missing in sourceTerms.Except(targetTerms)) yield return $"원문의 용어 [[{missing}]]이(가) 없습니다.";
            foreach (var extra in targetTerms.Except(sourceTerms)) yield return $"원문에 없는 용어 [[{extra}]]을(를) 씁니다.";
            // 경우(plural·select)마다 되풀이한 태그는 한 벌로 센다(영어 one {<b>#</b> …} other {<b>#</b> …} = 원문 <b>{n}</b>).
            var sourceTags = Sorted(original.TagCounts(Tags));
            var targetTags = Sorted(target.TagCounts(Tags));
            if (!sourceTags.SequenceEqual(targetTags))
                yield return $"태그가 원문과 다릅니다(원문: {string.Join(" ", sourceTags)} / 번역: {string.Join(" ", targetTags)}).";
        }

        static List<string> Sorted(Dictionary<string, int> counts)
        {
            var tags = counts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToList();
            tags.Sort(StringComparer.Ordinal);
            return tags;
        }

        // <…> 태그 이름 목록(정렬). 태그는 번역에서도 그대로 있어야 한다.
        static List<string> Tags(string text)
        {
            var tags = new List<string>();
            var start = -1;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '<') start = i;
                else if (text[i] == '>' && start >= 0)
                {
                    var inner = text.Substring(start + 1, i - start - 1);
                    var end = inner.IndexOfAny(new[] { '=', ' ' });
                    tags.Add("<" + (end < 0 ? inner : inner.Substring(0, end)) + ">");
                    start = -1;
                }
            }
            tags.Sort(StringComparer.Ordinal);
            return tags;
        }
    }
}
