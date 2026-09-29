using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameSystems.EditorTools
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

    // 도감의 변경을 Unity 에셋에 바로 쓰지 않고 모아 두는 곳. 탭을 옮길 때 적용하거나 되돌린다.
    //  · 수정: 원본의 작업본(메모리 사본)을 고친다. 적용하면 원본에 복사하고, ID가 바뀌었으면 ID 바꾸기(참조 갱신)를 한다.
    //  · 추가: 메모리에만 있는 새 항목. 적용하면 에셋으로 만든다.
    //  · 삭제: 목록에서만 뺀다. 적용하면 휴지통으로.
    // EditorDatabase.All은 이 변경을 덮어씌운 모습을 돌려주므로, 도감의 모든 화면(목록·드롭다운·검증)이 적용 전 상태를 그대로 본다.
    // 생성 코드(GameIds)·런타임 목록 동기화는 EditorDatabase.Committed(실제 에셋)만 본다.
    [InitializeOnLoad]
    public sealed class CodexStaging
    {
        readonly Dictionary<GameData, GameData> _working = new Dictionary<GameData, GameData>();
        readonly Dictionary<GameData, GameData> _originOf = new Dictionary<GameData, GameData>();
        readonly List<GameData> _added = new List<GameData>();
        readonly List<GameData> _removed = new List<GameData>();

        public bool IsEmpty => _working.Count == 0 && _added.Count == 0 && _removed.Count == 0;

        public bool IsAdded(GameData item) => item != null && _added.Contains(item);
        public bool IsWorking(GameData item) => item != null && (_originOf.ContainsKey(item) || _added.Contains(item));

        // 적용 대기 중인 변경이 있는 항목(새 항목이거나, 작업본이 원본과 다르다).
        public bool IsPending(GameData item) =>
            item != null && (_added.Contains(item) || (_originOf.TryGetValue(item, out var original) && Differs(original, item)));

        // 작업본의 원본(새 항목이면 null, 원본이면 자기 자신).
        public GameData OriginalOf(GameData item)
        {
            if (item == null) return null;
            if (_originOf.TryGetValue(item, out var original)) return original;
            return _added.Contains(item) ? null : item;
        }

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
            var info = DataTypes.Find(type) ?? throw new ArgumentException($"{type.Name}은 도감 종류가 아닙니다.");
            if (!EditorDatabase.Instance.CanUseId(id, null, out var error)) throw new ArgumentException("[GameSystems] " + error);
            var data = (GameData)ScriptableObject.CreateInstance(type);
            data.EditorAssignId(id);
            data.name = id;
            data.hideFlags = HideFlags.DontSave;
            _added.Add(data);
            EditorDatabase.StagingChanged();
            return data;
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
            var fields = DraftDiff.Compute(new SerializedObject(original), new SerializedObject(copy));
            if (original.Id != copy.Id) fields.Insert(0, new FieldChange { Path = "id", Label = "ID", Old = original.Id, New = copy.Id });
            var orderBefore = new SerializedObject(original).FindProperty("order");
            var orderAfter = new SerializedObject(copy).FindProperty("order");
            if (orderBefore != null && orderAfter != null && orderBefore.intValue != orderAfter.intValue)
                fields.Add(new FieldChange { Path = "order", Label = "순서", Old = orderBefore.intValue.ToString(), New = orderAfter.intValue.ToString() });
            return fields;
        }

        // ── 적용·되돌리기 ──────────────────────────────────────

        public int Commit()
        {
            if (IsEmpty) return 0;
            var db = EditorDatabase.Instance;
            var working = _working.ToList();
            var added = _added.Where(a => a != null).ToList();
            var removed = _removed.Where(r => r != null).ToList();
            _working.Clear();
            _originOf.Clear();
            _added.Clear();
            _removed.Clear();
            EditorDatabase.MarkDirty(notify: false);

            int count = 0;
            foreach (var item in removed)
            {
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(item));
                count++;
            }

            var renames = new List<(GameData item, string id)>();
            foreach (var (original, copy) in working.Select(p => (p.Key, p.Value)))
            {
                if (!Differs(original, copy))
                {
                    Object.DestroyImmediate(copy);
                    continue;
                }
                var oldId = original.Id;
                var name = original.name;
                var flags = original.hideFlags;
                Undo.RecordObject(original, "도감 변경 적용");
                EditorUtility.CopySerialized(copy, original);
                original.name = name;
                original.hideFlags = flags;
                if (copy.Id != oldId) renames.Add((original, copy.Id));
                original.EditorAssignId(oldId);
                EditorUtility.SetDirty(original);
                Object.DestroyImmediate(copy);
                count++;
            }

            foreach (var item in added)
            {
                item.hideFlags = HideFlags.None;
                db.CreateAsset(item);
                count++;
            }
            AssetDatabase.SaveAssets();
            EditorDatabase.MarkDirty(notify: false);

            // ID 바꾸기는 내용을 모두 옮긴 뒤에(다른 항목의 참조까지 실제 에셋에서 함께 바뀐다).
            foreach (var (item, id) in renames)
                if (db.CanUseId(id, item, out _)) db.Rename(item, id);
                else Debug.LogWarning($"[GameSystems] '{item.DisplayName}'의 ID를 '{id}'(으)로 바꾸지 못했습니다(이미 쓰는 ID).");

            db.SyncDatabaseAsset();
            EditorDatabase.MarkDirty();
            return count;
        }

        public void Discard()
        {
            foreach (var copy in _working.Values) if (copy != null) Object.DestroyImmediate(copy);
            foreach (var item in _added) if (item != null) Object.DestroyImmediate(item);
            _working.Clear();
            _originOf.Clear();
            _added.Clear();
            _removed.Clear();
            EditorDatabase.StagingChanged();
        }

        // ── 스크립트 다시 불러오기·플레이 전후 ─────────────────────
        // 스크립트가 다시 컴파일되면 메모리 사본이 사라지므로 JSON으로 잠시 보관했다가 되살린다.

        const string StashKey = "GameSystems.CodexStaging";

        [Serializable]
        sealed class Stash
        {
            public List<string> workingGuids = new List<string>();
            public List<string> workingJson = new List<string>();
            public List<string> addedTypes = new List<string>();
            public List<string> addedJson = new List<string>();
            public List<string> removedGuids = new List<string>();
        }

        static CodexStaging()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => EditorDatabase.Staging.SaveStash();
            EditorApplication.delayCall += () => EditorDatabase.Staging.LoadStash();
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.wantsToQuit += () => AskBeforeLeaving("Unity를 닫기 전에") != AskResult.Cancel;
        }

        void SaveStash()
        {
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
            SessionState.SetString(StashKey, JsonUtility.ToJson(stash));
        }

        void LoadStash()
        {
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
            EditorDatabase.StagingChanged();
        }

        // 플레이는 적용된 데이터로 돈다. 적용 대기 중인 변경이 있으면 먼저 묻는다.
        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            if (AskBeforeLeaving("플레이하기 전에") == AskResult.Cancel) EditorApplication.isPlaying = false;
        }

        public enum AskResult
        {
            Nothing,
            Applied,
            Reverted,
            Cancel,
        }

        // 창 밖(플레이·종료·창 닫기)에서 묻는 간단한 확인 창. 도감 창 안에서는 변경 목록 대화 상자를 쓴다.
        public static AskResult AskBeforeLeaving(string when)
        {
            var staging = EditorDatabase.Staging;
            if (!staging.HasChanges) return AskResult.Nothing;
            var changes = staging.Changes();
            var lines = string.Join("\n", changes.Take(12).Select(c => $"· {KindText(c.Kind)}  {c.Label}")) + (changes.Count > 12 ? $"\n… 외 {changes.Count - 12}건" : "");
            var choice = EditorUtility.DisplayDialogComplex("도감에 적용하지 않은 변경",
                $"{when} 도감의 변경 {changes.Count}건을 Unity에 적용할지 정하세요.\n\n{lines}", "적용", "취소", "되돌리기");
            switch (choice)
            {
                case 0:
                    staging.Commit();
                    return AskResult.Applied;
                case 2:
                    staging.Discard();
                    return AskResult.Reverted;
                default:
                    return AskResult.Cancel;
            }
        }

        public static string KindText(PendingKind kind) => kind == PendingKind.Added ? "추가" : kind == PendingKind.Removed ? "삭제" : "수정";
    }
}
