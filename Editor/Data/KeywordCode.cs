using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GBible.EditorTools
{
    // 키워드 코드 고치기: 도감 › 키워드 탭에서 한 변경을 키워드 그룹 코드(static class + const string)에 쓴다.
    // 새 그룹은 Assets/Code/Keywords/그룹이름.cs 파일로 만든다. 프로그래머가 다른 파일에 쓴 그룹도 찾아서 고친다.
    // 글자만 고치고, 파일 쓰기·다시 컴파일은 ClassStaging(도감 저장)이 한 번에 한다.
    public static class KeywordCode
    {
        public const string Folder = "Assets/Code/Keywords";

        static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        // 키워드 ID → 상수 이름(예: legendary → Legendary, fire_resist → FireResist, 2star → K2star).
        public static string ConstName(string id)
        {
            var parts = Regex.Split(id ?? "", "[^A-Za-z0-9]+").Where(p => p.Length > 0);
            var name = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
            if (name.Length == 0) return "Keyword";
            return char.IsDigit(name[0]) ? "K" + name : name;
        }

        public static bool IsValidName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(name) || !Identifier.IsMatch(name)) error = "영문·숫자·_로, 숫자로 시작하지 않게 쓰세요.";
            else if (!char.IsUpper(name[0])) error = "대문자로 시작하세요(예: Rarity, Legendary).";
            return error == null;
        }

        // 그룹 형식이 정의된 파일(바깥 클래스 기준). 못 찾으면 null.
        public static string FileOf(Type group)
        {
            if (group == null) return null;
            var outer = group;
            while (outer.DeclaringType != null) outer = outer.DeclaringType;
            var direct = Directory.Exists(Folder) ? Path.Combine(Folder, outer.Name + ".cs").Replace('\\', '/') : null;
            if (direct != null && File.Exists(direct) && Regex.IsMatch(File.ReadAllText(direct), @"\bclass\s+" + outer.Name + @"\b")) return direct;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                if (text.Contains("KeywordGroup") && Regex.IsMatch(text, @"\bclass\s+" + Regex.Escape(outer.Name) + @"\b")) return path;
            }
            return null;
        }

        // 새 그룹 파일.
        public static string NewGroupFile(string ns, string className, string label)
        {
            var b = new StringBuilder();
            b.AppendLine("// 키워드 그룹. 도감 › 키워드 탭에서 고친 내용이 이 파일에 쓰입니다. 코드에서 직접 고쳐도 됩니다.");
            b.AppendLine("// 필드 값에는 키워드 ID(글자)가 저장됩니다. 연결: [Keyword(typeof(" + className + "))] public string 필드;");
            b.AppendLine("using GBible;");
            b.AppendLine();
            var indent = "";
            if (!string.IsNullOrEmpty(ns))
            {
                b.AppendLine($"namespace {ns}");
                b.AppendLine("{");
                indent = "    ";
            }
            b.AppendLine($"{indent}{Attribute(label)}");
            b.AppendLine($"{indent}public static class {className}");
            b.AppendLine($"{indent}{{");
            b.AppendLine($"{indent}}}");
            if (!string.IsNullOrEmpty(ns)) b.AppendLine("}");
            return b.ToString();
        }

        static string Attribute(string label) => string.IsNullOrEmpty(label) ? "[KeywordGroup]" : $"[KeywordGroup(\"{Escape(label)}\")]";

        static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // 그룹 안에 하위 그룹(안쪽 static class)을 더한다.
        public static bool TryAddGroup(ref string text, string parentClass, string className, string label, out string error)
        {
            if (!ClassCode.TryFindBody(text, parentClass, out var open, out var close))
            {
                error = $"'{parentClass}' 그룹을 파일에서 찾지 못했습니다.";
                return false;
            }
            if (Regex.IsMatch(text.Substring(open, close - open), @"\bclass\s+" + Regex.Escape(className) + @"\b"))
            {
                error = $"'{className}' 그룹이 이미 있습니다.";
                return false;
            }
            var nl = ClassCode.NewLine(text);
            var indent = ClassCode.IndentOf(text, ClassCode.LineStart(text, close)) + "    ";
            var block = $"{indent}{Attribute(label)}{nl}{indent}public static class {className}{nl}{indent}{{{nl}{indent}}}{nl}";
            text = text.Insert(ClassCode.LineStart(text, close), block);
            error = null;
            return true;
        }

        public static bool TryAddKeyword(ref string text, string groupClass, string constName, string id, out string error)
        {
            if (!ClassCode.TryFindBody(text, groupClass, out var open, out var close))
            {
                error = $"'{groupClass}' 그룹을 파일에서 찾지 못했습니다.";
                return false;
            }
            if (Regex.IsMatch(text.Substring(open, close - open), @"\bconst\s+string\s+" + Regex.Escape(constName) + @"\b"))
            {
                error = $"'{constName}' 키워드가 이미 있습니다.";
                return false;
            }
            var nl = ClassCode.NewLine(text);
            // 하위 그룹보다 위, 마지막 키워드 다음에 넣는다.
            var body = text.Substring(open + 1, close - open - 1);
            var nested = Regex.Match(body, @"^[ \t]*\[KeywordGroup", RegexOptions.Multiline);
            var at = nested.Success ? open + 1 + nested.Index : ClassCode.LineStart(text, close);
            var indent = ClassCode.IndentOf(text, ClassCode.LineStart(text, close)) + "    ";
            text = text.Insert(at, $"{indent}public const string {constName} = \"{Escape(id)}\";{nl}");
            error = null;
            return true;
        }

        static bool TryFindConst(string text, string groupClass, string constName, out int lineStart, out int lineEnd, out string error)
        {
            lineStart = lineEnd = -1;
            if (!ClassCode.TryFindBody(text, groupClass, out var open, out var close))
            {
                error = $"'{groupClass}' 그룹을 파일에서 찾지 못했습니다.";
                return false;
            }
            var match = new Regex(@"^[ \t]*public\s+const\s+string\s+" + Regex.Escape(constName) + @"\s*=[^;]*;[ \t]*\r?\n?", RegexOptions.Multiline)
                .Match(text, open, close - open);
            if (!match.Success)
            {
                error = $"'{groupClass}.{constName}' 키워드를 찾지 못했습니다.";
                return false;
            }
            lineStart = match.Index;
            lineEnd = match.Index + match.Length;
            error = null;
            return true;
        }

        public static bool TryRemoveKeyword(ref string text, string groupClass, string constName, out string error)
        {
            if (!TryFindConst(text, groupClass, constName, out var start, out var end, out error)) return false;
            text = text.Remove(start, end - start);
            return true;
        }

        // 상수 이름만 바꾼다(저장되는 ID 값은 그대로).
        public static bool TryRenameKeyword(ref string text, string groupClass, string constName, string newName, out string error)
        {
            if (!TryFindConst(text, groupClass, constName, out var start, out var end, out error)) return false;
            var line = text.Substring(start, end - start);
            var renamed = Regex.Replace(line, @"(\bconst\s+string\s+)" + Regex.Escape(constName) + @"\b", "${1}" + newName);
            text = text.Substring(0, start) + renamed + text.Substring(end);
            return true;
        }

        // 그룹(클래스 블록과 그 위의 어트리뷰트·주석 줄)을 지운다.
        public static bool TryRemoveGroup(ref string text, string groupClass, out string error)
        {
            if (!ClassCode.TryFindBody(text, groupClass, out _, out var close))
            {
                error = $"'{groupClass}' 그룹을 파일에서 찾지 못했습니다.";
                return false;
            }
            var source = text;
            var declaration = Regex.Matches(source, @"\bclass\s+" + Regex.Escape(groupClass) + @"\b").Cast<Match>().First(m => !ClassCode.InCommentOrString(source, m.Index));
            var start = ClassCode.LineStart(text, declaration.Index);
            for (var previous = start; previous > 0;)
            {
                var lineStart = ClassCode.LineStart(text, previous - 1);
                var line = text.Substring(lineStart, previous - lineStart).Trim();
                if (!line.StartsWith("[", StringComparison.Ordinal) && !line.StartsWith("//", StringComparison.Ordinal)) break;
                start = lineStart;
                previous = lineStart;
            }
            var end = text.IndexOf('\n', close);
            end = end < 0 ? text.Length : end + 1;
            text = text.Remove(start, end - start);
            error = null;
            return true;
        }

        // 파일에 그룹 하나만 있으면(바깥 그룹을 지우면) 파일째 지운다.
        public static bool IsOnlyGroupInFile(string text, string groupClass) =>
            Regex.Matches(text, @"\bclass\s+[A-Za-z_]\w*").Count == 1 && Regex.IsMatch(text, @"\bclass\s+" + Regex.Escape(groupClass) + @"\b");

        // 필드 선언 위에 [Keyword(typeof(그룹))]를 붙인다(이미 있으면 바꾼다).
        public static bool TrySetFieldKeyword(ref string text, string className, string field, string groupTypeName, out string error)
        {
            if (!ClassCode.TryFindBody(text, className, out var open, out var close))
            {
                error = $"'{className}' 클래스를 파일에서 찾지 못했습니다.";
                return false;
            }
            var line = new Regex(@"^(?<indent>[ \t]*)(?<rest>[^\n]*\b(?:public|private|protected|internal)\b[^\n;=]*\b" + Regex.Escape(field) + @"\b\s*(?:=[^;\n]*)?;)", RegexOptions.Multiline)
                .Match(text, open, close - open);
            if (!line.Success || ClassCode.InCommentOrString(text, line.Groups["rest"].Index))
            {
                error = $"'{className}.{field}' 필드 선언을 찾지 못했습니다(한 줄 선언만 고칩니다).";
                return false;
            }
            var rest = Regex.Replace(line.Groups["rest"].Value, @"\[\s*Keyword\s*\(\s*typeof\([^)]*\)\s*\)\s*\]\s*", "");
            var replaced = $"{line.Groups["indent"].Value}[Keyword(typeof({groupTypeName}))] {rest}";
            text = text.Substring(0, line.Index) + replaced + text.Substring(line.Index + line.Length);
            ClassCode.EnsureUsing(ref text, "GBible");
            error = null;
            return true;
        }
    }
}
