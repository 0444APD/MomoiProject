using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 예전 키워드 에셋(키워드 그룹·키워드 DB 항목)을 키워드 코드로 한 번 옮긴다(키워드가 코드 원본이 된 2026-10-02 이전 데이터).
    //   · 그룹마다 Assets/Code/Keywords/그룹.cs(하위 그룹은 안쪽 클래스), 키워드는 const string(ID 그대로라 저장된 값이 이어진다).
    //   · 필드 규칙의 키워드 연결은 그 필드에 [Keyword(typeof(그룹))]를 쓴다(프로젝트 코드만. 라이브러리 클래스는 연결을 버린다).
    //   · 옛 에셋 파일은 지우지 않고 프로젝트 폴더의 _RemovedAssets로 옮긴다.
    // 에셋 형식은 이미 지워졌으므로 YAML 글자를 직접 읽는다.
    static class KeywordMigration
    {
        const string GroupMark = "GBible.Data.KeywordGroupData";
        const string KeywordMark = "GBible.Data.KeywordData";

        [InitializeOnLoadMethod]
        static void Schedule() => EditorApplication.delayCall += Run;

        sealed class OldGroup
        {
            public string Id;
            public string Parent;
            public string Path;
            public string ClassName;
            public readonly List<(string id, string path)> Keywords = new List<(string, string)>();
        }

        static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var key = "GBible.KeywordMigration." + Application.dataPath;
            if (EditorPrefs.GetBool(key, false)) return;
            List<OldGroup> groups;
            List<(string id, string group, string path)> keywords;
            try
            {
                Scan(out groups, out keywords);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] 예전 키워드 에셋을 읽지 못했습니다: " + e.Message);
                return;
            }
            if (groups.Count == 0 && keywords.Count == 0)
            {
                EditorPrefs.SetBool(key, true);
                return;
            }
            try
            {
                Migrate(groups, keywords);
                EditorPrefs.SetBool(key, true);
            }
            catch (Exception e)
            {
                Debug.LogError("[MomoiProject] 예전 키워드 에셋을 코드로 옮기지 못했습니다: " + e);
            }
        }

        static string Field(string text, string name)
        {
            var match = Regex.Match(text, @"^  " + name + @":[ \t]*(?<v>[^\r\n]*)$", RegexOptions.Multiline);
            return match.Success ? match.Groups["v"].Value.Trim().Trim('\'', '"') : "";
        }

        static void Scan(out List<OldGroup> groups, out List<(string id, string group, string path)> keywords)
        {
            groups = new List<OldGroup>();
            keywords = new List<(string, string, string)>();
            foreach (var file in Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories))
            {
                string head;
                using (var reader = new StreamReader(file))
                {
                    var buffer = new char[2048];
                    head = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length));
                }
                if (!head.Contains(GroupMark) && !head.Contains(KeywordMark)) continue;
                var text = File.ReadAllText(file);
                var path = file.Replace('\\', '/');
                if (text.Contains("::" + GroupMark + "\n") || text.Contains("::" + GroupMark + "\r") || Regex.IsMatch(text, Regex.Escape(GroupMark) + @"\s*$", RegexOptions.Multiline))
                    groups.Add(new OldGroup { Id = Field(text, "id"), Parent = Field(text, "parent"), Path = path });
                else if (Regex.IsMatch(text, Regex.Escape(KeywordMark) + @"\s*$", RegexOptions.Multiline))
                    keywords.Add((Field(text, "id"), Field(text, "group"), path));
            }
        }

        static void Migrate(List<OldGroup> groups, List<(string id, string group, string path)> keywords)
        {
            // 그룹에 속하지 않은 키워드는 '이전한 키워드' 그룹으로.
            foreach (var (id, group, path) in keywords)
            {
                var owner = groups.FirstOrDefault(g => g.Id == group);
                if (owner == null)
                {
                    owner = groups.FirstOrDefault(g => g.Id == "migratedKeywords") ?? new OldGroup { Id = "migratedKeywords" };
                    if (!groups.Contains(owner)) groups.Add(owner);
                }
                owner.Keywords.Add((id, path));
            }
            // 클래스 이름: 그룹 ID로 만들고, 있는 형식·다른 그룹과 겹치지 않게.
            var used = new HashSet<string>(Keywords.Groups.Select(g => g.Name), StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var name = KeywordCode.ConstName(group.Id);
                for (var i = 2; used.Contains(name); i++) name = KeywordCode.ConstName(group.Id) + i;
                used.Add(name);
                group.ClassName = name;
            }

            var ns = ClassRegistry.Current.codeNamespace;
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fileOf = new Dictionary<OldGroup, string>();
            var problems = new List<string>();
            bool IsTop(OldGroup g) => string.IsNullOrEmpty(g.Parent) || groups.All(p => p.Id != g.Parent);
            // 부모 먼저.
            var ordered = new List<OldGroup>();
            void Visit(OldGroup g)
            {
                if (ordered.Contains(g)) return;
                if (!IsTop(g)) Visit(groups.First(p => p.Id == g.Parent));
                ordered.Add(g);
            }
            foreach (var group in groups) Visit(group);

            foreach (var group in ordered)
            {
                string file;
                string text;
                if (IsTop(group))
                {
                    file = $"{KeywordCode.Folder}/{group.ClassName}.cs";
                    text = KeywordCode.NewGroupFile(ns, group.ClassName, group.Id);
                }
                else
                {
                    var parent = groups.First(p => p.Id == group.Parent);
                    file = fileOf[parent];
                    text = texts[file];
                    if (!KeywordCode.TryAddGroup(ref text, parent.ClassName, group.ClassName, group.Id, out var error)) problems.Add(error);
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (id, _) in group.Keywords)
                {
                    var name = KeywordCode.ConstName(id);
                    for (var i = 2; names.Contains(name); i++) name = KeywordCode.ConstName(id) + i;
                    names.Add(name);
                    if (!KeywordCode.TryAddKeyword(ref text, group.ClassName, name, id, out var error)) problems.Add(error);
                }
                texts[file] = text;
                fileOf[group] = file;
            }

            // 필드 규칙의 키워드 연결 → [Keyword(typeof(그룹))] (프로젝트 코드의 클래스만).
            var relinked = 0;
            var dropped = 0;
            foreach (var rule in EditorDatabase.Instance.OfType<FieldRuleData>().ToList())
            {
                if (rule.source != FieldRuleSource.Keyword) continue;
                var group = groups.FirstOrDefault(g => g.Id == rule.keywordGroup);
                var target = rule.TargetType;
                var path = target != null ? ClassCatalog.FileOf(target.FullName) : null;
                if (group != null && path != null && !path.StartsWith("Assets/Code/System/", StringComparison.Ordinal))
                {
                    var text = texts.TryGetValue(path, out var pending) ? pending : File.ReadAllText(path);
                    var typeName = string.IsNullOrEmpty(ns) ? Qualified(groups, group) : Qualified(groups, group);
                    if (KeywordCode.TrySetFieldKeyword(ref text, target.Name, rule.field, typeName, out var error))
                    {
                        if (!string.IsNullOrEmpty(ns)) ClassCode.EnsureUsing(ref text, ns);
                        texts[path] = text;
                        relinked++;
                    }
                    else problems.Add(error);
                }
                else if (!string.IsNullOrEmpty(rule.keywordGroup)) dropped++;
                rule.source = FieldRuleSource.Free;
                rule.keywordGroup = "";
                EditorUtility.SetDirty(rule);
                AssetDatabase.SaveAssetIfDirty(rule);
            }

            foreach (var pair in texts)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));
                File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false));
            }

            // 옛 에셋을 프로젝트 밖(_RemovedAssets)으로.
            var moved = 0;
            var backup = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "_RemovedAssets", "키워드 에셋 " + DateTime.Now.ToString("yyyy-MM-dd HHmm"));
            foreach (var path in groups.Where(g => g.Path != null).Select(g => g.Path).Concat(keywords.Select(k => k.path)))
            {
                var target = Path.Combine(backup, path.Substring("Assets/".Length));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(path, target);
                if (File.Exists(path + ".meta")) File.Move(path + ".meta", target + ".meta");
                moved++;
            }
            AssetDatabase.Refresh();
            EditorDatabase.MarkDirty();
            Debug.Log($"[MomoiProject] 예전 키워드 에셋을 코드로 옮겼습니다: 그룹 {groups.Count}개 · 키워드 {keywords.Count}개 → {KeywordCode.Folder}/" +
                      (relinked > 0 ? $", 필드 연결 {relinked}개 → [Keyword]" : "") +
                      (dropped > 0 ? $", 라이브러리 클래스의 연결 {dropped}개는 버림" : "") +
                      $". 옛 에셋 {moved}개는 {backup}에 있습니다.");
            foreach (var problem in problems) Debug.LogWarning("[MomoiProject] 키워드 이전 · " + problem);
        }

        static string Qualified(List<OldGroup> groups, OldGroup group)
        {
            var parts = new List<string>();
            for (var g = group; g != null; g = groups.FirstOrDefault(p => p.Id == g.Parent && !string.IsNullOrEmpty(g.Parent)))
                parts.Insert(0, g.ClassName);
            return string.Join(".", parts);
        }
    }
}
