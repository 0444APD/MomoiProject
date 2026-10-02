using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 클래스 코드에 대한 변경 하나(적용 전).
    [Serializable]
    public sealed class ClassOp
    {
        public const string Create = "create";
        public const string Delete = "delete";
        public const string AddField = "addField";
        public const string RemoveField = "removeField";
        public const string RenameField = "renameField";
        // 폴더(= Assets/Code 아래 폴더) 만들기·지우기·옮기기, 클래스를 다른 폴더로 옮기기.
        // 폴더 경로(group·newName)는 그 변경을 더한 때의 모습이다(앞선 변경이 반영된 경로). ClassFolders.Map이 뒤의 변경을 차례로 적용한다.
        public const string CreateGroup = "createGroup";
        public const string DeleteGroup = "deleteGroup";
        public const string MoveGroup = "moveGroup";
        public const string MoveClass = "moveClass";
        // 데이터 클래스를 플레이어 데이터 클래스로([PlayerData] + List→GameList).
        public const string UpgradePlayerData = "upgradePlayerData";

        public string op;
        // 클래스 전체 이름.
        public string type;
        // create: 종류(ClassKind 이름), 표시 이름, 그룹, 탭. 그룹 변경: group = 그룹 경로("전투/버프"), moveClass: 옮길 그룹.
        public string kind;
        public string label;
        public string group;
        public string tab;
        // 필드 변경: 필드 이름, 선언 줄, 화면용 형식 이름, 필요한 using, 새 이름.
        public string field;
        public string declaration;
        public string typeLabel;
        public List<string> usings = new List<string>();
        public string newName;

        public string ClassName => (type ?? "").Substring((type ?? "").LastIndexOf('.') + 1);
    }

    // 도감 › 클래스에서 한 코드 변경을 모아 두는 곳. 도감의 다른 변경처럼 저장(Ctrl+S)할 때 한 번에 파일에 쓴다.
    // 파일을 쓰면 Unity가 다시 컴파일하므로, 하나씩 쓰지 않고 모아서 한 번만 컴파일되게 한다.
    // 스크립트가 다시 불러와져도 남도록 SessionState에 둔다.
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

        public static bool IsNew(string type) => Ops.Any(o => o.op == ClassOp.Create && o.type == type);
        public static bool IsDeleted(string type) => Ops.Any(o => o.op == ClassOp.Delete && o.type == type);

        // ── 변경 더하기 ────────────────────────────────────────

        public static void CreateClass(string type, ClassKind kind, string label, string group, string tab)
        {
            Ops.Add(new ClassOp { op = ClassOp.Create, type = type, kind = kind.ToString(), label = label ?? "", group = group ?? "", tab = tab });
            Store();
        }

        public static void DeleteClass(string type)
        {
            // 아직 만들지 않은 클래스면 만들기 자체를 취소한다.
            if (IsNew(type)) Ops.RemoveAll(o => o.type == type);
            else
            {
                Ops.RemoveAll(o => o.type == type);
                Ops.Add(new ClassOp { op = ClassOp.Delete, type = type });
            }
            Store();
        }

        public static void AddField(string type, string field, string declaration, string typeLabel, IEnumerable<string> usings)
        {
            Ops.Add(new ClassOp { op = ClassOp.AddField, type = type, field = field, declaration = declaration, typeLabel = typeLabel, usings = usings?.ToList() ?? new List<string>() });
            Store();
        }

        public static void RemoveField(string type, string field)
        {
            // 적용 전에 더한 필드면 더하기를 취소한다(이름을 바꿨으면 바꾼 이름으로 찾는다).
            var added = Ops.FirstOrDefault(o => o.op == ClassOp.AddField && o.type == type && o.field == field);
            if (added != null) Ops.Remove(added);
            else
            {
                var renamed = Ops.FirstOrDefault(o => o.op == ClassOp.RenameField && o.type == type && o.newName == field);
                if (renamed != null)
                {
                    Ops.Remove(renamed);
                    field = renamed.field;
                }
                Ops.Add(new ClassOp { op = ClassOp.RemoveField, type = type, field = field });
            }
            Store();
        }

        public static void RenameField(string type, string field, string newName)
        {
            var added = Ops.FirstOrDefault(o => o.op == ClassOp.AddField && o.type == type && o.field == field);
            if (added != null)
            {
                added.declaration = System.Text.RegularExpressions.Regex.Replace(added.declaration, @"\b" + field + @"(\s*(?:=[^;]*)?;)", newName + "$1");
                added.field = newName;
            }
            else
            {
                var earlier = Ops.FirstOrDefault(o => o.op == ClassOp.RenameField && o.type == type && o.newName == field);
                if (earlier != null)
                {
                    if (earlier.field == newName) Ops.Remove(earlier);
                    else earlier.newName = newName;
                }
                else Ops.Add(new ClassOp { op = ClassOp.RenameField, type = type, field = field, newName = newName });
            }
            Store();
        }

        // ── 그룹(폴더) ─────────────────────────────────────────

        public static void CreateGroup(string group)
        {
            // 지우려던 그룹을 다시 만들면 지우기를 취소한다.
            if (Ops.RemoveAll(o => o.op == ClassOp.DeleteGroup && o.group == group) == 0)
                Ops.Add(new ClassOp { op = ClassOp.CreateGroup, group = group });
            Store();
        }

        // 그룹을 지운다. 안의 클래스·하위 그룹은 한 단계 위로 옮겨진다(지우지 않는다).
        public static void DeleteGroup(string group)
        {
            // 아직 만들지 않은 그룹이면 만들기를 취소한다(그 안에 만들려던 하위 그룹·클래스는 위로).
            if (Ops.RemoveAll(o => o.op == ClassOp.CreateGroup && o.group == group) > 0)
            {
                foreach (var op in Ops.Where(o => (o.op == ClassOp.CreateGroup || o.op == ClassOp.Create || o.op == ClassOp.MoveClass) && ClassFolders.IsUnder(o.group, group)))
                    op.group = op.group == group ? ClassFolders.Parent(group) : ClassFolders.Join(ClassFolders.Parent(group), op.group.Substring(group.Length + 1));
            }
            else Ops.Add(new ClassOp { op = ClassOp.DeleteGroup, group = group });
            Store();
        }

        // 클래스를 다른 폴더로 옮긴다(아직 만들지 않은 클래스면 저장할 때 그 폴더에 만든다).
        public static void MoveClass(string type, string group)
        {
            Ops.RemoveAll(o => o.op == ClassOp.MoveClass && o.type == type);
            Ops.Add(new ClassOp { op = ClassOp.MoveClass, type = type, group = group ?? "" });
            Store();
        }

        // 폴더를 옮긴다(안의 클래스·하위 폴더째로). to: 새 경로(예: "전투/버프" → "공용/버프").
        public static void MoveGroup(string group, string to)
        {
            if (string.IsNullOrEmpty(group) || group == to) return;
            Ops.Add(new ClassOp { op = ClassOp.MoveGroup, group = group, newName = to });
            Store();
        }

        public static void UpgradeToPlayerData(string type)
        {
            if (Ops.Any(o => o.op == ClassOp.UpgradePlayerData && o.type == type)) return;
            Ops.Add(new ClassOp { op = ClassOp.UpgradePlayerData, type = type });
            Store();
        }

        // 변경 내용을 직접 고친 뒤(새 클래스의 그룹 등) 보관하고 알린다.
        public static void Touch() => Store();

        // 한 변경을 취소한다(목록에서 뺀다).
        public static void Cancel(ClassOp op)
        {
            if (op.op == ClassOp.Create) Ops.RemoveAll(o => o.type == op.type);
            else Ops.Remove(op);
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
                var name = op.ClassName;
                switch (op.op)
                {
                    case ClassOp.Create:
                        rows.Add(new FieldChange { Label = $"코드 · 클래스 추가 · {name}", Old = "(없음)", New = KindText(op.kind) + (string.IsNullOrEmpty(op.label) ? "" : $" '{op.label}'") });
                        break;
                    case ClassOp.Delete:
                        rows.Add(new FieldChange { Label = $"코드 · 클래스 삭제 · {name}", Old = name, New = "(삭제)" });
                        break;
                    case ClassOp.AddField:
                        rows.Add(new FieldChange { Label = $"코드 · {name} › 필드 추가", Old = "(없음)", New = $"{op.field} : {op.typeLabel}" });
                        break;
                    case ClassOp.RemoveField:
                        rows.Add(new FieldChange { Label = $"코드 · {name} › 필드 삭제", Old = op.field, New = "(삭제)" });
                        break;
                    case ClassOp.RenameField:
                        rows.Add(new FieldChange { Label = $"코드 · {name} › 필드 이름", Old = op.field, New = op.newName });
                        break;
                    case ClassOp.CreateGroup:
                        rows.Add(new FieldChange { Label = "코드 · 폴더 추가", Old = "(없음)", New = ClassFolders.FolderOf(op.group) });
                        break;
                    case ClassOp.DeleteGroup:
                        rows.Add(new FieldChange { Label = "코드 · 폴더 삭제(안의 것은 위로)", Old = ClassFolders.FolderOf(op.group), New = "(삭제)" });
                        break;
                    case ClassOp.MoveGroup:
                        rows.Add(new FieldChange { Label = "코드 · 폴더 옮기기", Old = ClassFolders.FolderOf(op.group), New = ClassFolders.FolderOf(op.newName) });
                        break;
                    case ClassOp.UpgradePlayerData:
                        rows.Add(new FieldChange { Label = $"코드 · {name} › 플레이어 데이터 클래스로", Old = "데이터 클래스", New = "[PlayerData] · 리스트는 GameList" });
                        break;
                    case ClassOp.MoveClass:
                        rows.Add(new FieldChange { Label = $"코드 · {name} › 폴더", Old = IsNew(op.type) ? "(새 클래스)" : ClassFolders.PathOf(op.type) ?? "?", New = ClassFolders.FolderOf(ClassFolders.GroupOf(op.type)) });
                        break;
                }
            }
            return rows;
        }

        public static string KindText(string kind) =>
            kind == nameof(ClassKind.Codex) ? "도감 종류" : kind == nameof(ClassKind.Event) ? "이벤트" : "데이터 클래스";

        // ── 적용 ───────────────────────────────────────────────

        // 모아 둔 변경을 파일에 쓴다. 돌려주는 값: 적용한 변경 수. 실패한 변경은 problems에 담고 목록에 남긴다.
        public static int Commit(out List<string> problems)
        {
            problems = new List<string>();
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var created = new Dictionary<string, string>(StringComparer.Ordinal);
            var deleted = new List<(string type, string path)>();
            var done = new List<ClassOp>();

            // 폴더 변경을 먼저: 클래스의 최종 폴더를 계산해 두고 → 폴더 만들기·지우기(안의 것을 위로)·옮기기를 더한 차례대로 → 클래스 옮기기.
            var finalGroup = Ops.Where(o => o.op == ClassOp.Create || o.op == ClassOp.MoveClass)
                                .ToDictionary(o => o, o => ClassFolders.GroupOf(o.type) ?? "");
            foreach (var op in Ops.Where(o => o.op == ClassOp.CreateGroup || o.op == ClassOp.DeleteGroup || o.op == ClassOp.MoveGroup).ToList())
            {
                if (op.op == ClassOp.CreateGroup)
                {
                    if (!string.IsNullOrEmpty(op.group)) ClassFolders.EnsureFolder(ClassFolders.FolderOf(op.group));
                }
                else if (op.op == ClassOp.DeleteGroup) ClassFolders.DeleteFolder(op.group, problems);
                else ClassFolders.MoveFolder(op.group, op.newName, problems);
                done.Add(op);
            }
            foreach (var op in Ops.Where(o => o.op == ClassOp.MoveClass).ToList())
            {
                // 아직 만들지 않은 클래스는 만들 때 그 폴더에 만든다(아래 만들기).
                if (IsNew(op.type))
                {
                    done.Add(op);
                    continue;
                }
                var from = ClassCatalog.FileOf(op.type);
                if (string.IsNullOrEmpty(from))
                {
                    problems.Add($"{op.ClassName}: 클래스 파일을 찾지 못해 그룹을 옮기지 못했습니다.");
                    continue;
                }
                var folder = ClassFolders.FolderOf(finalGroup[op]);
                ClassFolders.EnsureFolder(folder);
                var to = folder + "/" + Path.GetFileName(from);
                var error = from == to ? null : AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error))
                {
                    problems.Add($"{op.ClassName}: '{to}'로 옮기지 못했습니다({error}).");
                    continue;
                }
                ClassRegistry.SetPath(op.type, to, save: false);
                done.Add(op);
            }
            ClassFolders.Forget();

            string PathFor(ClassOp op)
            {
                if (created.TryGetValue(op.type, out var path)) return path;
                return ClassCatalog.FileOf(op.type);
            }

            foreach (var op in Ops.Where(o => o.op != ClassOp.CreateGroup && o.op != ClassOp.DeleteGroup && o.op != ClassOp.MoveClass).ToList())
            {
                var name = op.ClassName;
                if (op.op == ClassOp.Create)
                {
                    var folder = ClassFolders.FolderOf(finalGroup[op]);
                    ClassFolders.EnsureFolder(folder);
                    var path = $"{folder}/{name}.cs";
                    if (File.Exists(path))
                    {
                        problems.Add($"{name}: '{path}' 파일이 이미 있습니다.");
                        continue;
                    }
                    Enum.TryParse(op.kind, out ClassKind kind);
                    texts[path] = ClassCode.NewClass(ClassRegistry.Current.codeNamespace, name, kind, op.label);
                    created[op.type] = path;
                    done.Add(op);
                    continue;
                }

                var file = PathFor(op);
                if (string.IsNullOrEmpty(file))
                {
                    problems.Add($"{name}: 클래스 파일을 찾지 못했습니다.");
                    continue;
                }
                if (op.op == ClassOp.Delete)
                {
                    deleted.Add((op.type, file));
                    done.Add(op);
                    continue;
                }
                if (!texts.TryGetValue(file, out var text)) text = File.ReadAllText(file);
                string error = null;
                var ok = false;
                switch (op.op)
                {
                    case ClassOp.AddField:
                        ok = ClassCode.TryAddField(ref text, name, op.declaration, out error);
                        if (ok) foreach (var ns in op.usings) ClassCode.EnsureUsing(ref text, ns);
                        break;
                    case ClassOp.RemoveField:
                        ok = ClassCode.TryRemoveField(ref text, name, op.field, out error);
                        break;
                    case ClassOp.RenameField:
                        ok = ClassCode.TryRenameField(ref text, name, op.field, op.newName, out error);
                        break;
                    case ClassOp.UpgradePlayerData:
                        ok = ClassCode.TryUpgradeToPlayerData(ref text, name, out error);
                        break;
                }
                if (!ok)
                {
                    problems.Add($"{name}: {error}");
                    continue;
                }
                texts[file] = text;
                done.Add(op);
            }

            foreach (var pair in texts)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pair.Key));
                File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false));
            }
            foreach (var op in done.Where(o => o.op == ClassOp.Create))
            {
                ClassRegistry.SetPath(op.type, created[op.type], save: false);
            }
            foreach (var (type, path) in deleted)
            {
                AssetDatabase.MoveAssetToTrash(path);
                ClassRegistry.Forget(type, save: false);
            }
            if (done.Count > 0) ClassRegistry.Commit();

            foreach (var op in done) Ops.Remove(op);
            Store();
            foreach (var problem in problems) Debug.LogWarning("[MomoiProject] 클래스 코드 변경을 적용하지 못했습니다 · " + problem);
            // 새 파일·고친 파일을 Unity가 읽어 다시 컴파일한다.
            if (done.Count > 0) AssetDatabase.Refresh();
            return done.Count;
        }
    }
}
