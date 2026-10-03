using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 코드에 대한 변경 하나(적용 전).
    [Serializable]
    public sealed class ClassOp
    {
        // 키워드 그룹 만들기(type = 상위 그룹 ID, 비면 새 파일 / newName = 클래스 이름 / label = 표시 이름).
        public const string KeywordGroup = "kwGroup";
        // 키워드 더하기·빼기·이름 바꾸기(type = 그룹 ID / field = 상수 이름 / declaration = 키워드 ID / newName = 새 상수 이름).
        public const string KeywordAdd = "kwAdd";
        public const string KeywordRemove = "kwRemove";
        public const string KeywordRename = "kwRename";
        // 키워드 그룹 지우기(type = 그룹 ID).
        public const string KeywordGroupRemove = "kwGroupRemove";
        // 필드에 키워드 그룹 연결([Keyword(typeof(그룹))]; type = 클래스 전체 이름, field, group = 그룹 형식 이름).
        public const string FieldKeyword = "fieldKeyword";
        // 필드의 어트리뷰트 하나 붙이기·떼기(type = 클래스 전체 이름, field, declaration = 어트리뷰트 글자, kind = "add" | "remove").
        public const string FieldAttribute = "fieldAttribute";

        public string op;
        public string type;
        public string kind;
        public string label;
        public string group;
        public string field;
        public string declaration;
        public string newName;

        public string ClassName => (type ?? "").Substring((type ?? "").LastIndexOf('.') + 1);
    }

    // DB에서 한 코드 변경(키워드 그룹·키워드, 필드의 어트리뷰트)을 모아 두는 곳. DB의 다른 변경처럼 저장(Ctrl+S)할 때 한 번에 파일에 쓴다.
    // 파일을 쓰면 Unity가 다시 컴파일하므로, 하나씩 쓰지 않고 모아서 한 번만 컴파일되게 한다.
    // 스크립트가 다시 불러와져도 남도록 SessionState에 둔다. 클래스 자체는 코드에서 만든다(DB는 만들지 않는다).
    public static class ClassStaging
    {
        static readonly string StashKey = LegacyNames.Session("GBible.ClassStaging");

        [Serializable]
        sealed class Stash
        {
            public List<ClassOp> ops = new List<ClassOp>();
        }

        static List<ClassOp> _ops;

        public static event Action Changed;

        static List<ClassOp> Ops
        {
            get
            {
                if (_ops != null) return _ops;
                var json = SessionState.GetString(StashKey, "");
                try { _ops = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Stash>(json)?.ops; }
                catch (Exception) { _ops = null; }
                return _ops ??= new List<ClassOp>();
            }
        }

        public static IReadOnlyList<ClassOp> All => Ops;
        public static bool HasChanges => Ops.Count > 0;

        static void Store()
        {
            if (Ops.Count == 0) SessionState.EraseString(StashKey);
            else SessionState.SetString(StashKey, JsonUtility.ToJson(new Stash { ops = Ops }));
            Changed?.Invoke();
        }

        public static IEnumerable<ClassOp> For(string type) => Ops.Where(o => o.type == type);

        // ── 키워드 ─────────────────────────────────────────────

        // 새 그룹의 ID(형식 전체 이름). parent가 비면 프로젝트 네임스페이스 바로 아래.
        public static string GroupId(string parent, string name)
        {
            if (!string.IsNullOrEmpty(parent)) return parent + "." + name;
            var ns = ClassRegistry.Current.codeNamespace;
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        public static string AddKeywordGroup(string parent, string name, string label)
        {
            var id = GroupId(parent, name);
            if (Ops.RemoveAll(o => o.op == ClassOp.KeywordGroupRemove && o.type == id) == 0)
                Ops.Add(new ClassOp { op = ClassOp.KeywordGroup, type = parent ?? "", newName = name, label = label ?? "" });
            Store();
            return id;
        }

        public static void AddKeyword(string group, string constName, string id)
        {
            var removed = Ops.FirstOrDefault(o => o.op == ClassOp.KeywordRemove && o.type == group && o.field == constName);
            if (removed != null) Ops.Remove(removed);
            else Ops.Add(new ClassOp { op = ClassOp.KeywordAdd, type = group, field = constName, declaration = id });
            Store();
        }

        public static void RemoveKeyword(string group, string constName)
        {
            // 적용 전에 더한 키워드면 더하기를 취소한다.
            if (Ops.RemoveAll(o => o.op == ClassOp.KeywordAdd && o.type == group && o.field == constName) == 0)
            {
                var renamed = Ops.FirstOrDefault(o => o.op == ClassOp.KeywordRename && o.type == group && o.newName == constName);
                if (renamed != null)
                {
                    Ops.Remove(renamed);
                    constName = renamed.field;
                }
                Ops.Add(new ClassOp { op = ClassOp.KeywordRemove, type = group, field = constName });
            }
            Store();
        }

        public static void RenameKeyword(string group, string constName, string newName)
        {
            var added = Ops.FirstOrDefault(o => o.op == ClassOp.KeywordAdd && o.type == group && o.field == constName);
            if (added != null) added.field = newName;
            else
            {
                var earlier = Ops.FirstOrDefault(o => o.op == ClassOp.KeywordRename && o.type == group && o.newName == constName);
                if (earlier != null)
                {
                    if (earlier.field == newName) Ops.Remove(earlier);
                    else earlier.newName = newName;
                }
                else Ops.Add(new ClassOp { op = ClassOp.KeywordRename, type = group, field = constName, newName = newName });
            }
            Store();
        }

        public static void RemoveKeywordGroup(string group)
        {
            // 적용 전에 만든 그룹이면 만들기와 그 안의 변경을 취소한다.
            var created = Ops.FirstOrDefault(o => o.op == ClassOp.KeywordGroup && GroupId(o.type, o.newName) == group);
            Ops.RemoveAll(o => o.type == group || (o.type != null && o.type.StartsWith(group + ".", StringComparison.Ordinal)));
            if (created != null) Ops.Remove(created);
            else Ops.Add(new ClassOp { op = ClassOp.KeywordGroupRemove, type = group });
            Store();
        }

        // 적용 전의 키워드 그룹(만들기만 있고 아직 코드에 없는 것): (ID, 상위 ID, 이름, 표시 이름).
        public static IEnumerable<(string id, string parent, string name, string label)> PendingGroups() =>
            Ops.Where(o => o.op == ClassOp.KeywordGroup).Select(o => (GroupId(o.type, o.newName), o.type, o.newName, o.label));

        public static bool IsGroupRemoved(string group) => Ops.Any(o => o.op == ClassOp.KeywordGroupRemove && o.type == group);

        // 필드에 키워드 그룹을 연결한다(코드에 [Keyword(typeof(그룹))]를 쓴다). groupTypeName = 코드에 쓸 이름(네임스페이스 뺌), ns = 필요한 using, groupId = 그룹 ID.
        public static void SetFieldKeyword(string type, string field, string groupTypeName, string ns, string groupId)
        {
            Ops.RemoveAll(o => (o.op == ClassOp.FieldKeyword || (o.op == ClassOp.FieldAttribute && o.declaration == "Keyword")) && o.type == type && o.field == field);
            Ops.Add(new ClassOp { op = ClassOp.FieldKeyword, type = type, field = field, group = groupTypeName, label = ns ?? "", kind = groupId });
            Store();
        }

        // 필드에 어트리뷰트를 붙이거나 뗀다(예: [Hide]). 같은 필드·같은 어트리뷰트의 반대 변경은 서로 지운다.
        public static void SetFieldAttribute(string type, string field, string attribute, bool add)
        {
            var opposite = Ops.FirstOrDefault(o => o.op == ClassOp.FieldAttribute && o.type == type && o.field == field && o.declaration == attribute);
            if (opposite != null) Ops.Remove(opposite);
            else Ops.Add(new ClassOp { op = ClassOp.FieldAttribute, type = type, field = field, declaration = attribute, kind = add ? "add" : "remove" });
            Store();
        }

        // 적용 전 어트리뷰트 변경을 반영한 결과(붙어 있는가). current = 지금 코드의 상태.
        public static bool PendingAttribute(string type, string field, string attribute, bool current)
        {
            var op = Ops.LastOrDefault(o => o.op == ClassOp.FieldAttribute && o.type == type && o.field == field && o.declaration == attribute);
            return op == null ? current : op.kind == "add";
        }

        public static void Touch() => Store();

        public static void Cancel(ClassOp op)
        {
            Ops.Remove(op);
            Store();
        }

        public static void Discard()
        {
            Ops.Clear();
            Store();
        }

        // ── 변경 목록(확인 창용) ───────────────────────────────

        public static List<FieldChange> Changes()
        {
            var rows = new List<FieldChange>();
            foreach (var op in Ops)
            {
                switch (op.op)
                {
                    case ClassOp.KeywordGroup:
                        rows.Add(new FieldChange { Label = "코드 · 키워드 그룹 추가", Old = "(없음)", New = GroupId(op.type, op.newName) + (string.IsNullOrEmpty(op.label) ? "" : $" '{op.label}'") });
                        break;
                    case ClassOp.KeywordGroupRemove:
                        rows.Add(new FieldChange { Label = "코드 · 키워드 그룹 삭제", Old = op.type, New = "(삭제)" });
                        break;
                    case ClassOp.KeywordAdd:
                        rows.Add(new FieldChange { Label = $"코드 · 키워드 {op.ClassName} › 추가", Old = "(없음)", New = $"{op.field} = \"{op.declaration}\"" });
                        break;
                    case ClassOp.KeywordRemove:
                        rows.Add(new FieldChange { Label = $"코드 · 키워드 {op.ClassName} › 삭제", Old = op.field, New = "(삭제)" });
                        break;
                    case ClassOp.KeywordRename:
                        rows.Add(new FieldChange { Label = $"코드 · 키워드 {op.ClassName} › 이름", Old = op.field, New = op.newName });
                        break;
                    case ClassOp.FieldKeyword:
                        rows.Add(new FieldChange { Label = $"코드 · {op.ClassName}.{op.field} › 키워드 그룹", Old = "", New = $"[Keyword(typeof({op.group}))]" });
                        break;
                    case ClassOp.FieldAttribute:
                        rows.Add(new FieldChange { Label = $"코드 · {op.ClassName}.{op.field} › 어트리뷰트", Old = op.kind == "add" ? "" : op.declaration, New = op.kind == "add" ? op.declaration : "(뗌)" });
                        break;
                }
            }
            return rows;
        }

        public static string KindText(string kind) =>
            kind == nameof(ClassKind.Codex) ? "DB 종류" : kind == nameof(ClassKind.Event) ? "이벤트" : "데이터 클래스";

        // ── 적용 ───────────────────────────────────────────────

        // 모아 둔 변경을 파일에 쓴다. 돌려주는 값: 적용한 변경 수. 실패한 변경은 problems에 담고 목록에 남긴다.
        public static int Commit(out List<string> problems)
        {
            problems = new List<string>();
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var created = new Dictionary<string, string>(StringComparer.Ordinal);
            var deleteFiles = new List<string>();
            var done = new List<ClassOp>();

            string FileOfGroup(string group)
            {
                if (created.TryGetValue(group, out var path)) return path;
                for (var outer = group; outer.Contains('.'); outer = outer.Substring(0, outer.LastIndexOf('.')))
                    if (created.TryGetValue(outer, out path)) return path;
                var info = Keywords.GroupById(group);
                return info == null ? null : KeywordCode.FileOf(info.Type);
            }

            string Read(string path) => texts.TryGetValue(path, out var text) ? text : File.ReadAllText(path);

            foreach (var op in Ops.ToList())
            {
                string error = null;
                var ok = false;
                string file;
                string text;
                switch (op.op)
                {
                    case ClassOp.KeywordGroup:
                        var id = GroupId(op.type, op.newName);
                        if (string.IsNullOrEmpty(op.type))
                        {
                            file = $"{KeywordCode.Folder}/{op.newName}.cs";
                            if (File.Exists(file) && !texts.ContainsKey(file))
                            {
                                error = $"'{file}' 파일이 이미 있습니다.";
                                break;
                            }
                            texts[file] = KeywordCode.NewGroupFile(ClassRegistry.Current.codeNamespace, op.newName, op.label);
                            created[id] = file;
                            ok = true;
                            break;
                        }
                        file = FileOfGroup(op.type);
                        if (file == null)
                        {
                            error = $"상위 그룹 '{op.type}'의 파일을 찾지 못했습니다.";
                            break;
                        }
                        text = Read(file);
                        ok = KeywordCode.TryAddGroup(ref text, op.type.Substring(op.type.LastIndexOf('.') + 1), op.newName, op.label, out error);
                        if (ok)
                        {
                            texts[file] = text;
                            created[id] = file;
                        }
                        break;
                    case ClassOp.KeywordAdd:
                    case ClassOp.KeywordRemove:
                    case ClassOp.KeywordRename:
                        file = FileOfGroup(op.type);
                        if (file == null)
                        {
                            error = $"키워드 그룹 '{op.type}'의 파일을 찾지 못했습니다.";
                            break;
                        }
                        text = Read(file);
                        ok = op.op == ClassOp.KeywordAdd ? KeywordCode.TryAddKeyword(ref text, op.ClassName, op.field, op.declaration, out error)
                            : op.op == ClassOp.KeywordRemove ? KeywordCode.TryRemoveKeyword(ref text, op.ClassName, op.field, out error)
                            : KeywordCode.TryRenameKeyword(ref text, op.ClassName, op.field, op.newName, out error);
                        if (ok) texts[file] = text;
                        break;
                    case ClassOp.KeywordGroupRemove:
                        file = FileOfGroup(op.type);
                        if (file == null)
                        {
                            error = $"키워드 그룹 '{op.type}'의 파일을 찾지 못했습니다.";
                            break;
                        }
                        text = Read(file);
                        if (KeywordCode.IsOnlyGroupInFile(text, op.ClassName))
                        {
                            deleteFiles.Add(file);
                            texts.Remove(file);
                            ok = true;
                            break;
                        }
                        ok = KeywordCode.TryRemoveGroup(ref text, op.ClassName, out error);
                        if (ok) texts[file] = text;
                        break;
                    case ClassOp.FieldKeyword:
                    case ClassOp.FieldAttribute:
                        file = ClassCatalog.FileOf(op.type);
                        if (string.IsNullOrEmpty(file))
                        {
                            error = $"'{op.type}' 클래스 파일을 찾지 못했습니다.";
                            break;
                        }
                        text = Read(file);
                        ok = op.op == ClassOp.FieldKeyword
                            ? KeywordCode.TrySetFieldKeyword(ref text, op.ClassName, op.field, op.group, out error)
                            : ClassCode.TrySetFieldAttribute(ref text, op.ClassName, op.field, op.declaration, op.kind == "add", out error);
                        if (ok && op.op == ClassOp.FieldKeyword && !string.IsNullOrEmpty(op.label)) ClassCode.EnsureUsing(ref text, op.label);
                        if (ok) texts[file] = text;
                        break;
                    default:
                        ok = true; // 예전 형식의 변경(클래스 만들기 등)은 버린다.
                        break;
                }
                if (!ok)
                {
                    problems.Add(error ?? op.op);
                    continue;
                }
                done.Add(op);
            }

            foreach (var pair in texts)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));
                File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false));
            }
            foreach (var path in deleteFiles) AssetDatabase.MoveAssetToTrash(path);

            foreach (var op in done) Ops.Remove(op);
            Store();
            foreach (var problem in problems) Debug.LogWarning("[MomoiProject] 코드 변경을 적용하지 못했습니다 · " + problem);
            // 새 파일·고친 파일을 Unity가 읽어 다시 컴파일한다.
            if (done.Count > 0) AssetDatabase.Refresh();
            return done.Count;
        }
    }
}
