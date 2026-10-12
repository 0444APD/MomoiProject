using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GBible.EditorTools
{
    public enum PendingKind
    {
        Added,
        Modified,
        Removed,
    }

    // 적용 대기 중인 변경 하나(항목 단위). Fields는 수정일 때 바뀐 필드들.
    public sealed class PendingChange
    {
        public PendingKind Kind;
        public GameData Item;
        public string Label;
        public List<FieldChange> Fields = new List<FieldChange>();
    }

    // DB의 변경을 Unity 에셋에 바로 쓰지 않고 모아 두는 곳. 저장(Ctrl+S)할 때 적용하거나 되돌린다.
    //  · 수정: 원본의 작업본(메모리 사본)을 고친다. 적용하면 원본에 복사한다.
    //  · ID 바꾸기(Rename): 이 항목과, 이 ID를 가리키는 항목들의 작업본을 그 자리에서 함께 고친다. 적용할 때는 참조를 다시 훑지 않고
    //    ID·파일 이름·이력만 바꾼다 — 그래서 a↔b 서로 바꾸기·비운 ID를 다른 항목이 다시 쓰기도 그대로 맞다.
    //    작업본의 ID만 직접 바꾼 경우(Work(…).EditorAssignId)는 적용할 때 예전 ID를 가리키던 곳을 옮긴다(그 예전 ID를 아무도 쓰지 않을 때만).
    //  · 추가: 메모리에만 있는 새 항목. 적용하면 에셋으로 만든다.
    //  · 삭제: 목록에서만 뺀다. 적용하면 휴지통으로.
    // EditorDatabase.All은 이 변경을 덮어씌운 모습을 돌려주므로, DB의 모든 화면(목록·드롭다운·검증)이 적용 전 상태를 그대로 본다.
    // 생성 코드(GameIds)·런타임 목록 동기화는 EditorDatabase.Committed(실제 에셋)만 본다.
    [InitializeOnLoad]
    public sealed class CodexStaging
    {
        readonly Dictionary<GameData, GameData> _working = new Dictionary<GameData, GameData>();
        readonly Dictionary<GameData, GameData> _originOf = new Dictionary<GameData, GameData>();
        readonly List<GameData> _added = new List<GameData>();
        readonly List<GameData> _removed = new List<GameData>();
        // ID 바꾸기(Rename)로 참조를 옮긴 원본 → 그 참조가 지금 가리키는 ID. 없으면 참조는 아직 적용된 ID를 가리킨다.
        readonly Dictionary<GameData, string> _refsAt = new Dictionary<GameData, string>();

        public bool IsEmpty => _working.Count == 0 && _added.Count == 0 && _removed.Count == 0;

        public bool IsAdded(GameData item) => item != null && _added.Contains(item);
        public bool IsWorking(GameData item) => item != null && (_originOf.ContainsKey(item) || _added.Contains(item));

        // 적용된 에셋에 작업본이 있거나(바뀐 것이 없어도 — 상세 창에 열어 둔 것) 삭제 대기인지.
        public bool IsStaged(GameData original) => original != null && (_working.ContainsKey(original) || _removed.Contains(original));

        // 적용된 에셋에 저장 안 된 변경이 있는지(작업본이 원본과 다르거나 삭제 대기).
        public bool HasPending(GameData original) =>
            original != null && (_removed.Contains(original) || (_working.TryGetValue(original, out var copy) && Differs(original, copy)));

        // 적용 대기 중인 변경이 있는 항목(새 항목이거나, 작업본이 원본과 다르다).
        public bool IsPending(GameData item) =>
            item != null && (_added.Contains(item) || (_originOf.TryGetValue(item, out var original) && Differs(original, item)));

        // 원본의 작업본(없으면 null).
        public GameData WorkingCopyOf(GameData original) => original != null && _working.TryGetValue(original, out var copy) ? copy : null;

        // 작업본의 원본(새 항목이면 null, 원본이면 자기 자신).
        public GameData OriginalOf(GameData item)
        {
            if (item == null) return null;
            if (_originOf.TryGetValue(item, out var original)) return original;
            return _added.Contains(item) ? null : item;
        }

        // 적용하면 ID가 바뀌는 항목: (원본, 적용된 ID, 새 ID). 출시 뒤 ID 검사(CanUseId)가 쓴다.
        internal IEnumerable<(GameData item, string from, string to)> PendingRenames() =>
            _working.Where(p => p.Key != null && p.Value != null && p.Key.Id != p.Value.Id).Select(p => (p.Key, p.Key.Id, p.Value.Id));

        // ── 목록에 덮어씌우기 ───────────────────────────────────

        public List<GameData> Overlay(IReadOnlyList<GameData> committed) =>
            committed.Where(d => !_removed.Contains(d))
                .Select(d => _working.TryGetValue(d, out var w) ? w : d)
                .Concat(_added.Where(a => a != null))
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();

        // ── 편집 ───────────────────────────────────────────────

        // 고칠 대상: 원본이면 작업본을 만들어 돌려주고, 이미 작업본이나 새 항목이면 그대로.
        public GameData Work(GameData item)
        {
            if (item == null || IsWorking(item)) return item;
            if (_working.TryGetValue(item, out var existing)) return existing;
            var copy = Object.Instantiate(item);
            copy.name = item.name;
            copy.hideFlags = HideFlags.DontSave;
            _working[item] = copy;
            _originOf[copy] = item;
            EditorDatabase.StagingChanged();
            return copy;
        }

        public GameData Add(Type type, string id)
        {
            var info = DataTypes.Find(type) ?? throw new ArgumentException($"{type.Name}은 DB 종류가 아닙니다.");
            if (!EditorDatabase.Instance.CanUseId(id, null, type, out var error)) throw new ArgumentException("[MomoiProject] " + error);
            var data = (GameData)ScriptableObject.CreateInstance(type);
            data.EditorAssignId(id);
            data.name = id;
            data.hideFlags = HideFlags.DontSave;
            _added.Add(data);
            EditorDatabase.StagingChanged();
            return data;
        }

        // ID 바꾸기: 이 항목과, 이 ID를 가리키는 항목들의 작업본을 함께 고친다(DB가 바로 새 ID로 보인다). 적용은 저장(Ctrl+S)할 때.
        // 같은 ID를 쓰는 항목이 또 있으면(Project 창에서 복제한 에셋) 참조는 옮기지 않는다 — 어느 쪽을 가리키던 것인지 알 수 없으니 남은 항목을 그대로 가리킨다.
        // DB 반영 스크립트도 이것으로 ID를 바꾼다. 돌려주는 값: 함께 바꾼 참조 수(같은 ID가 또 있어 옮기지 않았으면 -1).
        public int Rename(GameData item, string newId)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var db = EditorDatabase.Instance;
            var work = Work(item);
            var oldId = work.Id;
            if (oldId == newId) return 0;
            if (!db.CanUseId(newId, work, out var error)) throw new ArgumentException("[MomoiProject] " + error);
            var shared = !string.IsNullOrEmpty(oldId) && db.All.Any(d => d != null && d != work && d.Id == oldId);
            var count = 0;
            if (!shared)
            {
                foreach (var usage in db.FindUsages(work))
                {
                    var owner = Work(usage.Owner);
                    var so = new SerializedObject(owner);
                    var property = so.FindProperty(usage.PropertyPath);
                    if (property == null) continue;
                    property.stringValue = newId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    count++;
                }
            }
            work.EditorAssignId(newId);
            // 참조가 새 ID를 가리킨다(중복이라 옮기지 않았어도 — 남은 참조는 남은 항목의 것). 적용할 때 이 항목 때문에 다시 옮기지 않는다.
            var original = OriginalOf(work);
            if (original != null) _refsAt[original] = newId;
            EditorDatabase.StagingChanged();
            return shared ? -1 : count;
        }

        public void Remove(GameData item)
        {
            if (item == null) return;
            if (_added.Remove(item))
            {
                Object.DestroyImmediate(item);
            }
            else
            {
                var original = OriginalOf(item);
                if (_working.TryGetValue(original, out var copy))
                {
                    _working.Remove(original);
                    _originOf.Remove(copy);
                    Object.DestroyImmediate(copy);
                }
                _refsAt.Remove(original);
                if (!_removed.Contains(original)) _removed.Add(original);
            }
            EditorDatabase.StagingChanged();
        }

        // 작업본을 직접 고친 뒤(순서·그룹 옮기기 등) 화면을 갱신한다.
        public void Touch() => EditorDatabase.StagingChanged();

        // ── 변경 목록 ──────────────────────────────────────────

        public bool HasChanges => _added.Count > 0 || _removed.Count > 0 || _working.Any(p => Differs(p.Key, p.Value));

        public List<PendingChange> Changes()
        {
            var result = new List<PendingChange>();
            foreach (var item in _added.Where(a => a != null))
                result.Add(new PendingChange { Kind = PendingKind.Added, Item = item, Label = Label(item) });
            foreach (var (original, copy) in _working.Select(p => (p.Key, p.Value)))
            {
                var fields = FieldsOf(original, copy);
                if (fields.Count > 0) result.Add(new PendingChange { Kind = PendingKind.Modified, Item = copy, Label = Label(copy), Fields = fields });
            }
            foreach (var item in _removed.Where(r => r != null))
                result.Add(new PendingChange { Kind = PendingKind.Removed, Item = item, Label = Label(item) });
            return result;
        }

        static string Label(GameData item) => $"{DataTypes.Find(item.GetType())?.DisplayName} · {item.Id}";

        static bool Differs(GameData original, GameData copy) => original != null && copy != null && FieldsOf(original, copy).Count > 0;

        static List<FieldChange> FieldsOf(GameData original, GameData copy)
        {
            if (original == null || copy == null) return new List<FieldChange>();
            var before = new SerializedObject(original);
            var after = new SerializedObject(copy);
            var orderBefore = before.FindProperty("order");
            var orderAfter = after.FindProperty("order");
            var ordered = orderBefore != null && orderAfter != null && orderBefore.propertyType == SerializedPropertyType.Integer && orderAfter.propertyType == SerializedPropertyType.Integer;
            var (orderOld, orderNew) = ordered ? (orderBefore.intValue, orderAfter.intValue) : (0, 0);
            // 아래에서 따로 알아보기 쉬운 줄로 보이는 칸(ID·폴더·순서·상위 그룹)은 빼고, 숨은 칸([HideInInspector])까지 본다
            // — 숨은 칸만 바꾼 작업본(DB 반영 스크립트 등)도 변경으로 보이고 적용된다.
            var own = new List<string> { "id", "folder" };
            if (ordered) own.Add("order");
            if (original is INestedData) own.Add("parent");
            var fields = DraftDiff.Compute(before, after, own, hidden: true);
            if (original.Id != copy.Id) fields.Insert(0, new FieldChange { Path = "id", Label = "ID", Old = original.Id, New = copy.Id });
            if (orderOld != orderNew)
                fields.Add(new FieldChange { Path = "order", Label = "순서", Old = orderOld.ToString(), New = orderNew.ToString() });
            // DB 목록의 폴더·상위 그룹(숨은 칸이라 위의 비교에 들지 않는다).
            if (original.Folder != copy.Folder)
                fields.Add(new FieldChange { Path = "folder", Label = "폴더", Old = Place(original.Folder), New = Place(copy.Folder) });
            if (original is INestedData nestedBefore && copy is INestedData nestedAfter && (nestedBefore.Parent ?? "") != (nestedAfter.Parent ?? ""))
                fields.Add(new FieldChange { Path = "parent", Label = "상위 그룹", Old = Place(nestedBefore.Parent), New = Place(nestedAfter.Parent) });
            return fields;

            static string Place(string path) => string.IsNullOrEmpty(path) ? "(맨 위)" : path;
        }

        // ── 적용 계획 ──────────────────────────────────────────
        // 적용하기 전에 마지막 모습(적용한 뒤)을 만들어 겹침을 보고, 끊긴 참조를 어디로 옮길지 정한다. 문제가 있으면 아무것도 적용하지 않는다.

        // 마지막 모습의 항목 하나.
        internal sealed class FinalItem
        {
            public GameData Asset;      // 적용된 에셋(새 항목이면 null)
            public GameData Source;     // 적용할 내용(작업본 · 새 항목 · 그대로인 에셋)
            public string Before;       // 지금 적용된 ID(새 항목이면 null)
            public string After;        // 적용한 뒤 ID
            public string Pointer;      // 이 항목을 가리키는 참조가 지금 쓰는 ID
            public bool Moved => Before != After;
        }

        internal sealed class CommitPlan
        {
            public readonly List<FinalItem> Items = new List<FinalItem>();
            // 이대로 적용하면 참조·저장이 어긋나는 것(하나라도 있으면 적용하지 않는다).
            public readonly List<string> Problems = new List<string>();
            // 끊긴 참조 옮기기: 예전 ID → 따라갈 항목(그 예전 ID를 마지막 모습에서 아무도 쓰지 않을 때만).
            public readonly Dictionary<string, FinalItem> Follow = new Dictionary<string, FinalItem>(StringComparer.Ordinal);
            // ID가 바뀌는 적용된 에셋(파일 이름).
            public readonly List<FinalItem> Files = new List<FinalItem>();
            // 이력·이름 텍스트·씬 흐름 키도 옮길 것(예전 ID를 복제한 다른 에셋이 그대로 쓰면 뺀다).
            public readonly List<FinalItem> Renames = new List<FinalItem>();
            // 적용은 하지만 알릴 것(옮기지 못한 참조 등).
            public readonly List<string> Notes = new List<string>();
        }

        internal static CommitPlan Plan(IReadOnlyList<GameData> committed, IReadOnlyDictionary<GameData, GameData> working, IReadOnlyList<GameData> added,
            ICollection<GameData> removed, IReadOnlyDictionary<GameData, string> refsAt, GameDatabase database)
        {
            var plan = new CommitPlan();
            foreach (var asset in committed)
            {
                if (asset == null || removed.Contains(asset)) continue;
                var source = working.TryGetValue(asset, out var copy) && copy != null ? copy : asset;
                plan.Items.Add(new FinalItem
                {
                    Asset = asset, Source = source, Before = asset.Id, After = source.Id,
                    Pointer = refsAt != null && refsAt.TryGetValue(asset, out var at) ? at : asset.Id,
                });
            }
            foreach (var item in added)
                if (item != null) plan.Items.Add(new FinalItem { Source = item, After = item.Id, Pointer = item.Id });

            var byId = plan.Items.Where(i => !string.IsNullOrEmpty(i.After)).ToLookup(i => i.After, StringComparer.OrdinalIgnoreCase);
            var moved = plan.Items.Where(i => i.Moved).ToList();
            var pending = moved.Where(i => i.Asset != null).Select(i => (i.Asset, i.Before, i.After)).ToList();

            foreach (var item in moved)
            {
                if (string.IsNullOrEmpty(item.After)) continue;
                // ① 같은 ID(대소문자만 달라도)를 둘이 쓰게 된다.
                var twin = byId[item.After].FirstOrDefault(o => o != item);
                if (twin != null)
                {
                    plan.Problems.Add($"{Label(item.Source)}: '{item.After}'는 {Label(twin.Source)}도 쓰게 됩니다.");
                    continue;
                }
                // ② 출시 뒤: ID 변경 이력의 예전 ID·이번에 바꾸는 항목의 예전 ID를 다른 항목이 다시 쓴다.
                if (database != null && database.IsReleased)
                {
                    var why = EditorDatabase.ReleasedConflict(database, item.After, item.Asset, pending.Where(p => p.Asset != item.Asset));
                    if (why != null) plan.Problems.Add($"{Label(item.Source)}: {why}");
                }
            }

            foreach (var item in moved.Where(i => i.Asset != null))
            {
                plan.Files.Add(item);
                // 이 항목의 예전 ID들: 적용된 ID와, 참조가 지금 가리키는 ID.
                foreach (var old in new[] { item.Before, item.Pointer }.Distinct())
                {
                    if (string.IsNullOrEmpty(old) || old == item.After) continue;
                    var takers = byId[old].Where(o => o != item).ToList();
                    if (takers.Count == 0)
                    {
                        // 아무도 쓰지 않는 예전 ID: 남은 참조(작업본의 ID만 바꾼 경우, 밖에서 고친 에셋)는 이 항목을 따라간다.
                        // 같은 예전 ID를 둘이 바꾸면(복제한 에셋 둘 다) 어느 쪽인지 몰라 그대로 둔다.
                        if (!plan.Follow.TryGetValue(old, out var other)) plan.Follow[old] = item;
                        else if (other != null && other != item)
                        {
                            plan.Follow[old] = null;
                            plan.Notes.Add($"'{old}'를 가리키던 칸은 {Label(other.Source)}·{Label(item.Source)} 중 어느 쪽인지 몰라 그대로 두었습니다.");
                        }
                        continue;
                    }
                    // 예전 ID를 다른 항목이 새로 쓰는데 참조는 아직 그 ID(이 항목)를 가리킨다(작업본의 ID만 직접 바꿈) — 적용하면 엉뚱한 항목을 가리킨다.
                    // (복제한 에셋이 원래 같은 ID를 쓰고 있었으면 그 참조는 남은 항목의 것으로 둔다.)
                    var newcomer = takers.FirstOrDefault(t => t.Asset == null || !string.Equals(t.Before, old, StringComparison.OrdinalIgnoreCase));
                    if (old == item.Pointer && newcomer != null)
                        plan.Problems.Add($"{Label(item.Source)}: 참조가 아직 예전 ID '{old}'를 가리키는데 {Label(newcomer.Source)}이(가) 그 ID를 씁니다. ID를 서로 바꾸려면 DB의 ID 바꾸기(Staging.Rename)로 바꾸세요.");
                }
                // 이력·이름 텍스트·씬 흐름 키: 예전 ID를 복제한 다른 에셋이 그대로 쓰면 그것의 몫이라 옮기지 않는다.
                if (!string.IsNullOrEmpty(item.Before) && !plan.Items.Any(o => o != item && o.Asset != null && o.Before == item.Before)) plan.Renames.Add(item);
            }
            foreach (var key in plan.Follow.Where(p => p.Value == null).Select(p => p.Key).ToList()) plan.Follow.Remove(key);
            return plan;
        }

        // ── 적용·되돌리기 ──────────────────────────────────────

        // 적용(Ctrl+S)했다·버렸다. DB 반영 스크립트가 "돌았음"을 적용했을 때만 기록하려고 받는다.
        // 새 항목 몇 개만 적용한다 — 나머지(작업본·지우기·ID 바꾸기·다른 새 항목)는 그대로 '적용 전'으로 남긴다.
        // DB 반영 스크립트가 만든 새 항목을 저절로 적용할 때 쓴다(DbSeeds — 고치기·지우기와 개발자가 고치던 것은 Ctrl+S를 기다린다).
        public int CommitAdded(ICollection<GameData> items)
        {
            if (items == null || items.Count == 0) return 0;
            var working = new Dictionary<GameData, GameData>(_working);
            var origins = new Dictionary<GameData, GameData>(_originOf);
            var removed = new List<GameData>(_removed);
            var refs = new Dictionary<GameData, string>(_refsAt);
            var others = _added.Where(a => !items.Contains(a)).ToList();
            _working.Clear();
            _originOf.Clear();
            _removed.Clear();
            _refsAt.Clear();
            _added.RemoveAll(a => !items.Contains(a));
            try
            {
                return Commit();
            }
            finally
            {
                foreach (var pair in working) _working[pair.Key] = pair.Value;
                foreach (var pair in origins) _originOf[pair.Key] = pair.Value;
                foreach (var item in removed) if (!_removed.Contains(item)) _removed.Add(item);
                foreach (var pair in refs) _refsAt[pair.Key] = pair.Value;
                foreach (var item in others) if (!_added.Contains(item)) _added.Add(item);
                EditorDatabase.StagingChanged();
            }
        }

        // DB 반영 스크립트가 돌기 전·뒤를 비교한다(무엇을 새로 만들었고, 무엇을 고치거나 지웠나).
        internal HashSet<GameData> AddedNow() => new HashSet<GameData>(_added.Where(a => a != null));
        internal HashSet<GameData> WorkingNow() => new HashSet<GameData>(_working.Keys.Where(k => k != null));
        internal HashSet<GameData> RemovedNow() => new HashSet<GameData>(_removed.Where(r => r != null));

        public static event Action AfterCommit;
        public static event Action AfterDiscard;

        // 모아 둔 변경을 에셋에 쓴다. 순서: 겹침 검사(문제가 있으면 아무것도 하지 않음) → 지우기 → 작업본 내용(ID 포함) → 끊긴 참조 → 파일 이름(두 단계)
        // → 이력·이름 텍스트·씬 흐름 키 → 새 항목. 하나가 실패하면 그것만 남겨 두고(다음 저장에 다시) 알린다. Undo에는 남기지 않는다
        // (파일 이름·이력·GameIds는 Undo되지 않으므로 Ctrl+Z가 반쯤 되돌리지 않게, 적용한 에셋의 예전 Undo 기록도 지운다).
        public int Commit()
        {
            if (IsEmpty) return 0;
            var db = EditorDatabase.Instance;
            DropVanished();
            EditorDatabase.MarkDirty(notify: false);
            var working = _working.ToList();
            var added = _added.Where(a => a != null).ToList();
            var removed = _removed.Where(r => r != null).ToList();
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath);

            var plan = Plan(db.Committed, _working, added, removed, _refsAt, database);
            if (plan.Problems.Count > 0)
            {
                Report("저장하지 못했습니다 — 아무것도 적용하지 않았고 변경은 그대로 남아 있습니다. DB에서 고친 뒤 다시 저장(Ctrl+S)하세요", plan.Problems, error: true);
                return 0;
            }

            var failed = new List<string>();
            var touched = new HashSet<Object>();
            var applied = new HashSet<GameData>();
            int count = 0;

            // 끊긴 참조(메모리의 작업본·새 항목) — 여기서 멈춰도 DB가 보는 모습은 맞다.
            if (plan.Follow.Count > 0)
                foreach (var item in plan.Items.Where(i => i.Source != i.Asset))
                    EditorDatabase.ReplaceReferences(item.Source, (value, refType) => FollowOf(plan, value, refType));

            // ① 지우기 — 하나라도 못 지우면 멈춘다(그 ID·파일 이름을 쓰려던 다른 변경과 겹치지 않게).
            foreach (var item in removed)
            {
                string why = null;
                try
                {
                    var path = AssetDatabase.GetAssetPath(item);
                    if (!string.IsNullOrEmpty(path) && !AssetDatabase.MoveAssetToTrash(path)) why = "휴지통으로 옮기지 못했습니다.";
                }
                catch (Exception e)
                {
                    why = e.Message;
                }
                if (why != null)
                {
                    failed.Add($"{Label(item)}: {why}");
                    plan.Notes.Clear();
                    Finish(failed, plan, touched, database);
                    return count;
                }
                _removed.Remove(item);
                _refsAt.Remove(item);
                count++;
            }

            // ② 작업본의 내용을 원본에(ID 포함 — 참조는 ID를 바꿀 때 이미 새 ID로 옮겼다).
            foreach (var (original, copy) in working)
            {
                try
                {
                    if (Differs(original, copy))
                    {
                        var name = original.name;
                        var flags = original.hideFlags;
                        EditorUtility.CopySerialized(copy, original);
                        original.name = name;
                        original.hideFlags = flags;
                        EditorUtility.SetDirty(original);
                        touched.Add(original);
                        count++;
                    }
                    _working.Remove(original);
                    _originOf.Remove(copy);
                    _refsAt.Remove(original);
                    Object.DestroyImmediate(copy);
                    applied.Add(original);
                }
                catch (Exception e)
                {
                    failed.Add($"{Label(copy)}: {e.Message}");
                }
            }
            EditorDatabase.MarkDirty(notify: false);

            // ③ 작업본이 없던 에셋의 끊긴 참조(ID를 바꾼 뒤 밖에서 고친 에셋 등) — 따라갈 항목이 적용됐을 때만, 한 번.
            if (plan.Follow.Count > 0)
                foreach (var item in plan.Items.Where(i => i.Source == i.Asset))
                {
                    var n = EditorDatabase.ReplaceReferences(item.Asset, (value, refType) =>
                        plan.Follow.TryGetValue(value, out var to) && to.Asset != null && applied.Contains(to.Asset) ? FollowOf(plan, value, refType) : null);
                    if (n == 0) continue;
                    EditorUtility.SetDirty(item.Asset);
                    touched.Add(item.Asset);
                }

            // ④ 파일 이름(파일 이름 = 예전 ID인 것만, 서로 바꾸기도 겹치지 않게 두 단계).
            EditorDatabase.RenameFiles(plan.Files.Where(i => applied.Contains(i.Asset)).Select(i => (i.Asset, i.Before, i.After)), plan.Notes);

            // ⑤ 출시 이력 · 이름 텍스트('<ID>.name') · 씬 흐름(Bootstrap)의 플레이어 데이터 키.
            var renames = plan.Renames.Where(i => applied.Contains(i.Asset)).ToList();
            if (renames.Count > 0)
            {
                if (database != null && database.IsReleased)
                {
                    foreach (var item in renames) database.EditorAddRename(item.Asset.GetType().FullName, item.Before, item.After);
                    EditorUtility.SetDirty(database);
                }
                EditorDatabase.MoveNameTexts(renames.ToDictionary(i => i.Before, i => i.After, StringComparer.Ordinal), plan.Notes);
                if (EditorDatabase.MoveSceneKey(database, renames.Where(i => i.Asset is PlayerDataEntry).ToDictionary(i => i.Before, i => i.After, StringComparer.Ordinal)))
                    EditorUtility.SetDirty(database);
            }

            // ⑥ 새 항목(지운 것·이름을 바꾼 것이 비운 파일 이름을 쓸 수 있게 맨 뒤에).
            foreach (var item in added)
            {
                try
                {
                    item.hideFlags = HideFlags.None;
                    db.CreateAsset(item);
                    _added.Remove(item);
                    touched.Add(item);
                    count++;
                }
                catch (Exception e)
                {
                    if (item != null && !AssetDatabase.Contains(item)) item.hideFlags = HideFlags.DontSave;
                    failed.Add($"{Label(item)}: {e.Message}");
                }
            }

            Finish(failed, plan, touched, database);
            return count;
        }

        // 끊긴 참조가 따라갈 새 ID(아니면 null).
        static string FollowOf(CommitPlan plan, string value, Type refType) =>
            plan.Follow.TryGetValue(value, out var to) && to != null && refType.IsInstanceOfType(to.Source) ? to.After : null;

        void Finish(List<string> failed, CommitPlan plan, HashSet<Object> touched, GameDatabase database)
        {
            AssetDatabase.SaveAssets();
            // 적용한 에셋의 예전 Undo 기록을 지운다(그 기록을 Ctrl+Z로 되돌리면 이번 적용까지 반쯤 되돌아간다).
            foreach (var asset in touched) if (asset != null) Undo.ClearUndo(asset);
            EditorDatabase.MarkDirty(notify: false);
            EditorDatabase.Instance.SyncDatabaseAsset();
            if (database != null) Undo.ClearUndo(database);
            EditorDatabase.MarkDirty();
            if (failed.Count > 0) Report("일부를 저장하지 못했습니다 — 저장하지 못한 변경은 그대로 남아 있습니다(다시 저장하면 이어서 적용)", failed, error: true);
            if (plan.Notes.Count > 0) Report("저장했습니다. 확인할 것", plan.Notes, error: false);
            if (IsEmpty) AfterCommit?.Invoke();
        }

        // 원본 에셋이 밖에서 지워진 작업본·삭제 대기는 치운다.
        void DropVanished()
        {
            foreach (var pair in _working.Where(p => p.Key == null || p.Value == null).ToList())
            {
                _working.Remove(pair.Key);
                if (pair.Value != null)
                {
                    _originOf.Remove(pair.Value);
                    Object.DestroyImmediate(pair.Value);
                }
            }
            foreach (var key in _originOf.Keys.Where(k => k == null).ToList()) _originOf.Remove(key);
            foreach (var key in _refsAt.Keys.Where(k => k == null).ToList()) _refsAt.Remove(key);
            _removed.RemoveAll(r => r == null);
            _added.RemoveAll(a => a == null);
        }

        static string _lastReport;
        static double _lastReportAt;

        static void Report(string title, List<string> lines, bool error)
        {
            var text = $"[MomoiProject] {title}:\n" + string.Join("\n", lines.Take(20).Select(l => "· " + l)) + (lines.Count > 20 ? $"\n… 외 {lines.Count - 20}건" : "");
            // Ctrl+S는 단축키와 창의 키 입력으로 두 번 불릴 수 있다 — 같은 알림은 한 번만.
            if (text == _lastReport && EditorApplication.timeSinceStartup - _lastReportAt < 1) return;
            _lastReport = text;
            _lastReportAt = EditorApplication.timeSinceStartup;
            if (error) Debug.LogError(text);
            else Debug.LogWarning(text);
        }

        public void Discard()
        {
            foreach (var copy in _working.Values) if (copy != null) Object.DestroyImmediate(copy);
            foreach (var item in _added) if (item != null) Object.DestroyImmediate(item);
            _working.Clear();
            _originOf.Clear();
            _added.Clear();
            _removed.Clear();
            _refsAt.Clear();
            EditorDatabase.StagingChanged();
            AfterDiscard?.Invoke();
        }

        // ── 스크립트 다시 불러오기·플레이 전후 ─────────────────────
        // 스크립트가 다시 컴파일되면 메모리 사본이 사라지므로 JSON으로 잠시 보관했다가 되살린다.

        static readonly string StashKey = LegacyNames.Session("GBible.CodexStaging");

        [Serializable]
        sealed class Stash
        {
            public List<string> workingGuids = new List<string>();
            public List<string> workingJson = new List<string>();
            public List<string> addedTypes = new List<string>();
            public List<string> addedJson = new List<string>();
            public List<string> removedGuids = new List<string>();
            public List<string> refsGuids = new List<string>();
            public List<string> refsIds = new List<string>();
        }

        static CodexStaging()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => EditorDatabase.Staging.SaveStash();
            EditorApplication.delayCall += () => EditorDatabase.Staging.LoadStash();
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.wantsToQuit += () => AskBeforeLeaving("Unity를 닫기 전에") != AskResult.Cancel;
        }

        // 이번 도메인에서 맡겨 둔 것을 되찾았나. 되찾기 전에 다시 읽기가 또 오면(컴파일이 이어짐) 비어 있는 지금 모습으로 덮지 않는다 —
        // 덮으면 DB 반영 스크립트가 고친 '적용 전' 작업본이 사라진다.
        bool _stashLoaded;

        void SaveStash()
        {
            if (!_stashLoaded && !string.IsNullOrEmpty(SessionState.GetString(StashKey, ""))) return;
            if (IsEmpty)
            {
                SessionState.EraseString(StashKey);
                return;
            }
            var stash = new Stash();
            foreach (var (original, copy) in _working.Select(p => (p.Key, p.Value)))
            {
                if (original == null || copy == null) continue;
                stash.workingGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)));
                stash.workingJson.Add(EditorJsonUtility.ToJson(copy));
            }
            foreach (var item in _added.Where(a => a != null))
            {
                stash.addedTypes.Add(item.GetType().AssemblyQualifiedName);
                stash.addedJson.Add(EditorJsonUtility.ToJson(item));
            }
            foreach (var item in _removed.Where(r => r != null))
                stash.removedGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)));
            foreach (var pair in _refsAt.Where(p => p.Key != null))
            {
                stash.refsGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pair.Key)));
                stash.refsIds.Add(pair.Value ?? "");
            }
            SessionState.SetString(StashKey, JsonUtility.ToJson(stash));
        }

        void LoadStash()
        {
            _stashLoaded = true;
            var json = SessionState.GetString(StashKey, "");
            SessionState.EraseString(StashKey);
            if (string.IsNullOrEmpty(json)) return;
            Stash stash;
            try { stash = JsonUtility.FromJson<Stash>(json); }
            catch (Exception) { return; }
            for (int i = 0; i < stash.workingGuids.Count; i++)
            {
                var original = AssetDatabase.LoadAssetAtPath<GameData>(AssetDatabase.GUIDToAssetPath(stash.workingGuids[i]));
                if (original == null || _working.ContainsKey(original)) continue;
                var copy = Object.Instantiate(original);
                EditorJsonUtility.FromJsonOverwrite(stash.workingJson[i], copy);
                copy.name = original.name;
                copy.hideFlags = HideFlags.DontSave;
                _working[original] = copy;
                _originOf[copy] = original;
            }
            for (int i = 0; i < stash.addedTypes.Count; i++)
            {
                var type = Type.GetType(stash.addedTypes[i]);
                if (type == null || !typeof(GameData).IsAssignableFrom(type)) continue;
                var item = (GameData)ScriptableObject.CreateInstance(type);
                EditorJsonUtility.FromJsonOverwrite(stash.addedJson[i], item);
                item.name = item.Id;
                item.hideFlags = HideFlags.DontSave;
                _added.Add(item);
            }
            foreach (var guid in stash.removedGuids)
            {
                var item = AssetDatabase.LoadAssetAtPath<GameData>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && !_removed.Contains(item)) _removed.Add(item);
            }
            for (int i = 0; i < stash.refsGuids.Count && i < stash.refsIds.Count; i++)
            {
                var original = AssetDatabase.LoadAssetAtPath<GameData>(AssetDatabase.GUIDToAssetPath(stash.refsGuids[i]));
                if (original != null && _working.ContainsKey(original)) _refsAt[original] = stash.refsIds[i];
            }
            EditorDatabase.StagingChanged();
        }

        // 플레이는 적용된 데이터로 돈다. 적용 대기 중인 변경이 있으면 먼저 묻는다.
        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            // AI의 플레이 요청은 묻지 않는다(창이 뜨면 답할 사람이 없어 Unity가 멈춘다) — 적용된 데이터로 돌고, 결과 파일에 '적용 전' 건수를 남긴다.
            if (PlayRequests.IsStarting) return;
            if (AskBeforeLeaving("플레이하기 전에") == AskResult.Cancel) EditorApplication.isPlaying = false;
        }

        public enum AskResult
        {
            Nothing,
            Applied,
            Reverted,
            Cancel,
        }

        // 창 밖(플레이·종료·창 닫기)에서 묻는 간단한 확인 창. DB 창 안에서는 변경 목록 확인 창를 쓴다.
        public static AskResult AskBeforeLeaving(string when)
        {
            if (!CodexChanges.HasChanges) return AskResult.Nothing;
            // DB 항목·필드 연결·클래스 코드·클래스 그룹을 함께 묻는다.
            var all = CodexChanges.Rows().Select(c => string.IsNullOrEmpty(c.New) ? $"· {c.Label}" : $"· {c.Label}  {c.New}").ToList();
            var lines = string.Join("\n", all.Take(12)) + (all.Count > 12 ? $"\n… 외 {all.Count - 12}건" : "");
            var choice = EditorUtility.DisplayDialogComplex("DB에 저장 안 된 변경",
                $"{when} DB의 변경 {all.Count}건을 저장할지 정하세요.\n\n{lines}", "저장", "취소", "되돌리기");
            switch (choice)
            {
                case 0:
                    CodexChanges.Save();
                    if (!CodexChanges.HasChanges) return AskResult.Applied;
                    // 저장이 막혔다(겹치는 ID 등 — 까닭은 Console): 저장된 것처럼 넘어가지 않고 다시 묻는다.
                    return EditorUtility.DisplayDialog("DB 저장 실패",
                        $"DB 변경을 저장하지 못했습니다(까닭은 Console). {when} 저장하지 않은 채로 계속할까요?\n(남은 변경은 DB에 그대로 있습니다 — Unity를 닫으면 사라집니다.)", "계속", "취소")
                        ? AskResult.Nothing
                        : AskResult.Cancel;
                case 2:
                    CodexChanges.Discard();
                    return AskResult.Reverted;
                default:
                    return AskResult.Cancel;
            }
        }

        public static string KindText(PendingKind kind) => kind == PendingKind.Added ? "추가" : kind == PendingKind.Removed ? "삭제" : "수정";
    }
}
