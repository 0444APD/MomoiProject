using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 에디터에서 GameData 에셋 전체를 다루는 창구: 조회, 생성, ID 부여, 이름 조회, GameDatabase 동기화.
    public sealed class EditorDatabase : INameResolver
    {
        public const string DataRoot = ProjectFolders.Data;
        public const string DatabaseAssetPath = DataRoot + "/Resources/MomoiProject/GameDatabase.asset";

        static EditorDatabase _instance;
        public static EditorDatabase Instance => _instance ??= new EditorDatabase();

        List<GameData> _all;
        List<GameData> _view;
        public static event Action Changed;

        // DB에서 모아 둔(아직 적용하지 않은) 변경. 저장(Ctrl+S)할 때 적용하거나 되돌린다.
        public static CodexStaging Staging { get; } = new CodexStaging();

        // DB가 보는 모습: 실제 에셋 + 적용 대기 중인 변경(추가·수정·삭제). 목록·드롭다운·검증이 이것을 쓴다.
        public IReadOnlyList<GameData> All => Staging.IsEmpty ? Committed : _view ??= Staging.Overlay(Committed);

        // 실제 에셋만(적용된 것). 생성 코드·런타임 목록 동기화처럼 Unity에 쓰는 쪽이 쓴다.
        public IReadOnlyList<GameData> Committed
        {
            get
            {
                if (_all != null) return _all;
                // 추상 베이스 이름 검색에 기대지 않고 구체 타입마다 찾는다.
                _all = DataTypes.All
                    .SelectMany(info => AssetDatabase.FindAssets("t:" + info.Type.Name))
                    .Distinct()
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameData>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(d => d != null && DataTypes.IsRegistered(d.GetType()))
                    .Distinct()
                    .OrderBy(d => d.Id, StringComparer.Ordinal)
                    .ToList();
                return _all;
            }
        }

        public IEnumerable<GameData> OfType(Type type) => All.Where(d => d.GetType() == type);
        public IEnumerable<T> OfType<T>() where T : GameData => All.OfType<T>();

        public GameData FindById(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(d => d.Id == id);

        // 드롭다운 그룹 머리글(지금은 없음).
        public string GroupLabel(GameData data) => null;

        public static void MarkDirty(bool notify = true)
        {
            if (_instance != null)
            {
                _instance._all = null;
                _instance._view = null;
            }
            _rules = null;
            if (notify) Changed?.Invoke();
        }

        // 적용 대기 중인 변경이 바뀌었다(에셋은 그대로, DB가 보는 모습만 다시 만든다).
        public static void StagingChanged()
        {
            if (_instance != null) _instance._view = null;
            _rules = null;
            Changed?.Invoke();
        }

        // ── 이름 조회(조건·보상 문장용) ──────────────────────────

        public string StatName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(수치 없음)";
            return OfType<StatData>().FirstOrDefault(s => s.Id == id)?.DisplayName ?? $"(없는 수치: {id})";
        }

        public string AchievementName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(도전과제 없음)";
            return OfType<AchievementData>().FirstOrDefault(a => a.Id == id)?.DisplayName ?? $"(없는 도전과제: {id})";
        }

        // ── 생성과 ID ─────────────────────────────────────────

        // id를 주지 않으면 접두어+연번(예: keyword3)으로 제안한 ID를 쓴다. DB는 만들기 전에 ID를 묻는다.
        public GameData Create(Type type, string id = null)
        {
            var info = DataTypes.Find(type) ?? throw new ArgumentException($"{type.Name}은 데이터베이스 타입이 아닙니다.");
            id = string.IsNullOrEmpty(id) ? NextId(info) : id;
            if (!CanUseId(id, null, type, out var error)) throw new ArgumentException("[MomoiProject] " + error);
            var data = (GameData)ScriptableObject.CreateInstance(type);
            data.EditorAssignId(id);
            CreateAsset(data);
            AssetDatabase.SaveAssets();
            MarkDirty();
            SyncDatabaseAsset();
            return data;
        }

        // 메모리의 항목을 에셋으로 저장한다(종류 폴더, 파일 이름 = ID). DB의 '적용'이 새 항목에 쓴다.
        // Undo로 남기지 않는다(Ctrl+Z가 만든 에셋의 오브젝트만 지워 깨진 에셋 파일이 남지 않게 — 적용은 Undo되지 않는다).
        public void CreateAsset(GameData data)
        {
            var info = DataTypes.Find(data.GetType()) ?? throw new ArgumentException($"{data.GetType().Name}은 데이터베이스 타입이 아닙니다.");
            var folder = FolderOf(info);
            EnsureFolder(folder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{data.Id}.asset");
            AssetDatabase.CreateAsset(data, path);
        }

        // 새 항목 ID 제안: 접두어 + 연번(예: keyword3). 만들기 창에서 바로 고쳐 쓰는 자리 표시용.
        public string NextId(DataTypeInfo info)
        {
            var pattern = new Regex("^" + Regex.Escape(info.IdPrefix) + "(\\d+)$");
            var max = 0;
            foreach (var data in All)
            {
                var match = pattern.Match(data.Id ?? "");
                if (match.Success) max = Math.Max(max, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            return info.IdPrefix + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ── ID 규칙·이름 바꾸기·참조 찾기 ─────────────────────────

        // 형식이 맞고 다른 항목이 쓰지 않는 ID인지. self는 이름을 바꾸는 항목 자신(자기 ID는 중복으로 치지 않는다).
        public bool CanUseId(string id, GameData self, out string error) => CanUseId(id, self, self?.GetType(), out error);

        // type: 이 ID를 쓸 항목의 종류(만들 때 — 이름을 바꿀 때는 self의 종류). 같은 종류 안에서 생성 코드(GameIds) 이름이 같아지는 ID도 막는다
        // (bossDragon·boss.dragon → 둘 다 BossDragon). 종류를 모르면(null) 이 검사는 건너뛰고 만들 때(Add) 본다.
        public bool CanUseId(string id, GameData self, Type type, out string error)
        {
            if (!IdRules.IsValid(id, out error)) return false;
            // 대소문자만 다른 ID도 막는다(에셋 파일 이름이 겹치고, 읽을 때 헷갈린다).
            var owner = All.FirstOrDefault(d => d != null && !IsSelf(d, self) && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (owner != null)
            {
                error = $"'{id}'는 이미 '{owner.DisplayName}'({DataTypes.Find(owner.GetType())?.DisplayName})이(가) 쓰고 있습니다.";
                return false;
            }
            if (type != null)
            {
                var name = IdRules.ToIdentifier(id);
                var twin = All.FirstOrDefault(d => d != null && !IsSelf(d, self) && d.GetType() == type && !string.IsNullOrEmpty(d.Id) && IdRules.ToIdentifier(d.Id) == name);
                if (twin != null)
                {
                    error = $"'{id}'는 같은 종류의 '{twin.DisplayName}'과(와) 생성 코드 이름(GameIds의 {name})이 같아집니다. 점(.)·대문자 자리만 다른 ID는 쓸 수 없습니다 — 다른 이름을 고르세요.";
                    return false;
                }
            }
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            error = database != null && database.IsReleased ? ReleasedConflict(database, id, self == null ? null : Staging.OriginalOf(self), Staging.PendingRenames()) : null;
            return error == null;
        }

        // 같은 항목인지(작업본과 그 원본은 같은 항목).
        static bool IsSelf(GameData data, GameData self) =>
            self != null && (data == self || (Staging.OriginalOf(data) is GameData original && original == Staging.OriginalOf(self)));

        // 출시 뒤 이 ID를 쓰면 저장 호환이 깨지는지(깨지면 문구). ID 변경 이력은 저장을 불러올 때마다 적용되므로, 이력의 예전 ID를 다른 항목이
        // 다시 쓰면 그 항목으로 저장한 값까지 매번 예전 항목으로 바뀐다. 같은 항목이 예전 이름으로 돌아가는 것은 된다(이력이 그 항목의 지금 ID로 이어질 때).
        // committedSelf: 이름을 바꾸는 항목의 적용된 에셋(새 항목이면 null). pending: 이번 저장에서 ID가 바뀌는 다른 항목(저장하면 이력에 들어간다).
        internal static string ReleasedConflict(GameDatabase database, string id, GameData committedSelf, IEnumerable<(GameData item, string from, string to)> pending)
        {
            foreach (var rename in database.Renames)
            {
                if (rename == null || !string.Equals(rename.oldId, id, StringComparison.OrdinalIgnoreCase)) continue;
                if (committedSelf != null && rename.dataType == committedSelf.GetType().FullName && rename.newId == committedSelf.Id) continue;
                return $"'{id}'는 출시 뒤 '{rename.newId}'(으)로 바꾼 예전 ID입니다. 예전 저장의 '{id}'는 불러올 때마다 '{rename.newId}'(으)로 이어지므로 다른 항목에 다시 쓸 수 없습니다.";
            }
            if (pending == null) return null;
            foreach (var (item, from, to) in pending)
            {
                if (item == null || item == committedSelf || !string.Equals(from, id, StringComparison.OrdinalIgnoreCase)) continue;
                return $"'{id}'는 '{to}'(으)로 바꾸는 중인 항목의 예전 ID입니다. 출시 뒤에는 저장하면 이력에 남아 예전 저장의 '{id}'가 '{to}'(으)로 이어지므로 다른 항목에 쓸 수 없습니다.";
            }
            return null;
        }

        public static bool IsReleased => AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath)?.IsReleased ?? false;

        internal const string ReleasedMenu = "MomoiProject/출시됨 (저장 호환 유지)";

        [MenuItem(ReleasedMenu, priority = 40)]
        static void ToggleReleased()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (database == null)
            {
                Debug.LogWarning("[MomoiProject] GameDatabase가 아직 없습니다. DB 창을 한 번 여세요.");
                return;
            }
            var next = !database.IsReleased;
            var message = next
                ? "출시 상태로 바꿉니다. 앞으로 ID 이름을 바꾸거나 데이터 구조를 바꿔도 기존 저장을 지우지 않고 이어받습니다(ID 변경 이력 기록, 구조 변경은 저장 버전 변환 필요)."
                : "개발 중 상태로 바꿉니다. 데이터 구조가 호환되지 않게 바뀌면 저장을 변환하지 않고 지웁니다.";
            if (!EditorUtility.DisplayDialog("출시 상태", message, "바꾸기", "취소")) return;
            Undo.RecordObject(database, "출시 상태 변경");
            database.EditorSetReleased(next);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        [MenuItem(ReleasedMenu, true)]
        static bool ToggleReleasedValidate()
        {
            Menu.SetChecked(ReleasedMenu, IsReleased);
            return true;
        }

        public sealed class IdUsage
        {
            public GameData Owner;
            public string PropertyPath;
            public string Label;
        }

        // 이 항목의 ID를 가리키는 모든 곳(조건·보상·키워드 칸 등 ID 참조 필드, 플레이어 데이터 칸).
        // 이 목록 밖(ID 바꾸기·지우기가 따라가지 않는 곳): 씬 컴포넌트의 키(데이터 연결 등), UXML의 binding-path, 값 틀({balance:ID}·{data:키.경로}) —
        // 값 틀은 검증이 끊긴 것을 경고한다.
        public List<IdUsage> FindUsages(GameData target)
        {
            var result = new List<IdUsage>();
            if (target == null || string.IsNullOrEmpty(target.Id)) return result;
            foreach (var data in All)
            {
                ForEachReference(data, (property, refType) =>
                {
                    if (property.stringValue == target.Id && refType.IsInstanceOfType(target))
                        result.Add(new IdUsage { Owner = data, PropertyPath = property.propertyPath, Label = $"{data.DisplayName} · {property.displayName}" });
                });
            }
            return result;
        }

        // ID 참조 필드(수치·도전과제·[DataRef]·[PlayerDataRef])를 모두 방문한다. 조건·보상 블록 안쪽까지 들어간다.
        public static void ForEachReference(GameData data, Action<SerializedProperty, Type> visit)
        {
            var so = new SerializedObject(data);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.String) continue;
                var refType = RefTypeOf(PropertyUtil.GetField(it, out _));
                if (refType != null) visit(it, refType);
            }
        }

        // 참조 칸의 값을 바꾼다: replace(값, 참조 종류)가 새 값을 돌려주면 그것으로(null이면 그대로). Undo로 남기지 않는다. 돌려주는 값: 바꾼 칸 수.
        internal static int ReplaceReferences(GameData owner, Func<string, Type, string> replace)
        {
            if (owner == null) return 0;
            var so = new SerializedObject(owner);
            var it = so.GetIterator();
            var count = 0;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.String || string.IsNullOrEmpty(it.stringValue)) continue;
                var refType = RefTypeOf(PropertyUtil.GetField(it, out _));
                if (refType == null) continue;
                var next = replace(it.stringValue, refType);
                if (next == null || next == it.stringValue) continue;
                it.stringValue = next;
                count++;
            }
            if (count > 0) so.ApplyModifiedPropertiesWithoutUndo();
            return count;
        }

        public static Type RefTypeOf(System.Reflection.FieldInfo field)
        {
            if (field == null) return null;
            if (field.IsDefined(typeof(StatRefAttribute), false)) return typeof(StatData);
            if (field.IsDefined(typeof(AchievementRefAttribute), false)) return typeof(AchievementData);
            // 플레이어 데이터 키 칸(가진 목록·플레이어 데이터 검사·주기·가중치 표의 목록 줄 …): 키를 바꾸면 따라가고, 쓰는 곳이 있으면 지우지 못한다.
            if (field.IsDefined(typeof(PlayerDataRefAttribute), false)) return typeof(PlayerDataEntry);
            var attribute = ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
            // [DataRef(typeof(Card))]처럼 GameData가 아닌 클래스: 종류는 인스턴스, 고를 수 있는 것은 RefClassOf로 거른다.
            // [DataRef(typeof(IStatusData))]처럼 선택 요소의 규격(IDataKind): 그 규격을 붙인 DB 종류(프로젝트의 StatusData …).
            if (attribute != null) return IsKindRef(attribute) ? attribute : typeof(InstanceData);
            // 필드 규칙(DB에서 연결): 코드에 어트리뷰트가 없어도 같은 대우를 받는다.
            var rule = RuleFor(field);
            // 규칙의 값 출처는 글자 필드(또는 string 리스트)에만 적용한다(스프라이트 필드의 규칙은 규격 그룹용).
            if (rule == null || (field.FieldType != typeof(string) && FieldPaths.ElementTypeOf(field.FieldType) != typeof(string))) return null;
            switch (rule.source)
            {
                case FieldRuleSource.PlayerDataKey: return typeof(PlayerDataEntry);
                case FieldRuleSource.Data: return TypeResolver.Find(rule.dataType);
                default: return null;
            }
        }

        // 이 필드에 걸린 필드 규칙(없으면 null). 필드를 선언한 클래스와 그 부모 클래스 기준.
        public static FieldRuleData RuleFor(System.Reflection.FieldInfo field)
        {
            if (field == null) return null;
            if (_rules == null)
            {
                _rules = new Dictionary<string, FieldRuleData>(StringComparer.Ordinal);
                var source = _committedRules ? Instance.Committed.OfType<FieldRuleData>() : Instance.OfType<FieldRuleData>();
                foreach (var rule in source)
                    if (!string.IsNullOrEmpty(rule.targetType) && !string.IsNullOrEmpty(rule.field)) _rules[rule.targetType + "|" + rule.field] = rule;
            }
            for (var type = field.ReflectedType; type != null; type = type.BaseType)
                if (_rules.TryGetValue(type.FullName + "|" + field.Name, out var found)) return found;
            return null;
        }

        static Dictionary<string, FieldRuleData> _rules;
        static bool _committedRules;

        // 빌드 검사처럼 적용된 데이터만 봐야 할 때: 그동안 필드 규칙도 적용된 것만 쓴다(using으로 감싼다).
        internal static IDisposable CommittedRules()
        {
            _committedRules = true;
            _rules = null;
            return new Restore(() =>
            {
                _committedRules = false;
                _rules = null;
            });
        }

        sealed class Restore : IDisposable
        {
            Action _done;
            public Restore(Action done) => _done = done;

            public void Dispose()
            {
                _done?.Invoke();
                _done = null;
            }
        }

        // 이 필드에서 고를 수 있는 항목(타입 + 키워드 그룹으로 거른다. 그룹이 비어 있으면 모든 키워드).
        // 종류는 그 형식이거나 물려받은 형식이면 된다([DataRef(typeof(GameData))] = 모든 DB 항목 — 가중치 표 '항목'). MatchesRef·검증과 같은 기준.
        public IEnumerable<GameData> RefCandidates(System.Reflection.FieldInfo field)
        {
            var type = RefTypeOf(field);
            if (type == null) return Enumerable.Empty<GameData>();
            return All.Where(item => item != null && MatchesRef(field, item));
        }

        // [DataRef(typeof(Card))]처럼 인스턴스 값의 클래스로 거르는 필드면 그 클래스(아니면 null).
        public static Type RefClassOf(System.Reflection.FieldInfo field)
        {
            if (field == null) return null;
            var attribute = ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
            return attribute != null && !IsKindRef(attribute) ? attribute : null;
        }

        // DB 종류를 가리키는 [DataRef] 형식인가(GameData 종류이거나 선택 요소의 규격).
        static bool IsKindRef(Type type) => typeof(GameData).IsAssignableFrom(type) || typeof(IDataKind).IsAssignableFrom(type);

        // 그 항목이 이 필드에서 고를 수 있는 것인가(종류 + 인스턴스 클래스).
        public static bool MatchesRef(System.Reflection.FieldInfo field, GameData target)
        {
            if (target == null) return false;
            var type = RefTypeOf(field);
            if (type == null || !type.IsInstanceOfType(target)) return false;
            var cls = RefClassOf(field);
            return cls == null || target is InstanceData instance && instance.InstanceType != null && cls.IsAssignableFrom(instance.InstanceType);
        }

        // 화면에 보일 참조 종류 이름("카드 인스턴스", "유물" …).
        public static string RefKindLabel(System.Reflection.FieldInfo field)
        {
            var cls = RefClassOf(field);
            if (cls != null) return DataModel.FriendlyName(cls);
            var type = RefTypeOf(field);
            return type == null ? "" : DataTypes.Label(type);
        }

        public static bool IsRequired(System.Reflection.FieldInfo field) => RuleFor(field)?.required ?? false;

        // 바로 ID를 바꾼다(작업본을 거치지 않는 도구용 — 스프라이트 틀 인스펙터). DB 창은 모아 두었다가 저장할 때 적용한다(CodexStaging.Rename).
        // 이 ID를 가리키는 모든 필드(적용된 에셋과 저장 안 된 작업본 모두), 에셋 파일 이름, 이름 텍스트('<ID>.name'), 생성 코드(GameIds)가 함께 바뀐다.
        // 같은 ID를 쓰는 항목이 또 있으면(복제한 에셋) 참조·이름 텍스트는 옮기지 않는다(남은 항목의 것). 이 항목에 저장 안 된 변경이 있으면 바꾸지 않는다(먼저 저장).
        // Undo로 남기지 않는다(파일 이름·이력·GameIds는 Undo되지 않으므로 Ctrl+Z가 반쯤 되돌리지 않게).
        // 출시 후: 예전 이름으로 저장된 플레이어 데이터를 런타임이 새 이름으로 이어받도록 변경 이력을 남긴다.
        // 개발 중: 이력을 남기지 않는다(호환되지 않는 저장은 런타임이 지우고 새로 시작).
        public int Rename(GameData data, string newId)
        {
            var oldId = data.Id;
            if (oldId == newId) return 0;
            // 상세 창에 열어 둔 것만으로 생긴(바뀐 것 없는) 작업본은 함께 바꾸고, 저장 안 된 변경·삭제 대기가 있으면 바꾸지 않는다.
            if (Staging.IsWorking(data) || Staging.HasPending(data))
                throw new ArgumentException("[MomoiProject] 이 항목에 저장 안 된 변경이 있어 바로 바꿀 수 없습니다. DB에서 저장(Ctrl+S)한 뒤 다시 바꾸세요.");
            var copy = Staging.WorkingCopyOf(data);
            if (!CanUseId(newId, data, out var error)) throw new ArgumentException("[MomoiProject] " + error);

            var shared = Committed.Concat(All).Any(d => d != null && !IsSelf(d, data) && d.Id == oldId);
            var touched = new List<UnityEngine.Object> { data };
            var count = 0;
            if (!shared)
            {
                // 적용된 에셋과 저장 안 된 작업본·새 항목을 모두(저장하든 되돌리든 맞게).
                foreach (var owner in Committed.Concat(All.Where(Staging.IsWorking)).Distinct().ToList())
                {
                    var n = ReplaceReferences(owner, (value, refType) => value == oldId && refType.IsInstanceOfType(data) ? newId : null);
                    if (n == 0) continue;
                    count += n;
                    if (Staging.IsWorking(owner)) continue;
                    EditorUtility.SetDirty(owner);
                    touched.Add(owner);
                }
            }
            data.EditorAssignId(newId);
            EditorUtility.SetDirty(data);
            if (copy != null) copy.EditorAssignId(newId);
            RenameFiles(new[] { (data, oldId, newId) }, null);

            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (!shared)
            {
                if (database != null && database.IsReleased)
                {
                    database.EditorAddRename(data.GetType().FullName, oldId, newId);
                    EditorUtility.SetDirty(database);
                }
                var ids = new Dictionary<string, string>(StringComparer.Ordinal) { [oldId] = newId };
                MoveNameTexts(ids, null);
                if (data is PlayerDataEntry && MoveSceneKey(database, ids)) EditorUtility.SetDirty(database);
            }
            AssetDatabase.SaveAssets();
            foreach (var asset in touched) Undo.ClearUndo(asset);
            if (database != null) Undo.ClearUndo(database);
            MarkDirty();
            return count;
        }

        // 에셋 파일 이름을 새 ID로(파일 이름이 예전 ID인 것만 — Project 창에서 복제한 'x 1' 같은 이름은 그대로). 서로 바꾸기(a↔b)·대소문자만 바꾸기도
        // 겹치지 않게 임시 이름을 거쳐 두 단계로 바꾼다. 새 이름의 파일이 이미 있으면(ID와 상관없는 파일) 비슷한 이름으로. notes: 알릴 것(null이면 경고 로그).
        internal static void RenameFiles(IEnumerable<(GameData asset, string from, string to)> renames, List<string> notes)
        {
            var moving = new List<(GameData asset, string from, string to, string folder, string extension)>();
            foreach (var (asset, from, to) in renames)
            {
                var path = asset == null ? null : AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(to) || from == to || Path.GetFileNameWithoutExtension(path) != from) continue;
                var error = AssetDatabase.RenameAsset(path, "_renaming_" + AssetDatabase.AssetPathToGUID(path));
                if (!string.IsNullOrEmpty(error))
                {
                    Note(notes, $"{path}: 파일 이름을 '{to}'(으)로 바꾸지 못했습니다({error}).");
                    continue;
                }
                moving.Add((asset, from, to, Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetExtension(path)));
            }
            foreach (var (asset, from, to, folder, extension) in moving)
            {
                var path = AssetDatabase.GetAssetPath(asset);
                var target = $"{folder}/{to}{extension}";
                var name = File.Exists(target) ? Path.GetFileNameWithoutExtension(AssetDatabase.GenerateUniqueAssetPath(target)) : to;
                var error = AssetDatabase.RenameAsset(path, name);
                if (string.IsNullOrEmpty(error))
                {
                    if (name != to) Note(notes, $"{folder}/{to}{extension}이(가) 이미 있어 '{name}{extension}'(으)로 바꿨습니다.");
                    continue;
                }
                // 새 이름으로 못 바꾸면 예전 이름으로 돌려 둔다(임시 이름이 남지 않게).
                var back = File.Exists($"{folder}/{from}{extension}") ? null : AssetDatabase.RenameAsset(path, from);
                Note(notes, $"{path}: 파일 이름을 '{to}'(으)로 바꾸지 못했습니다({error}){(string.IsNullOrEmpty(back) && back != null ? " — 예전 이름 그대로" : "")}.");
            }
        }

        // 이름 텍스트 키('<ID>.name')를 새 ID로 — 모든 언어 표에서 한꺼번에(서로 바꾸기도 맞다). 새 키가 따로 이미 있으면 그 글을 덮지 않고 둔다.
        internal static void MoveNameTexts(IReadOnlyDictionary<string, string> ids, List<string> notes)
        {
            if (ids == null || ids.Count == 0) return;
            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(TextSheetTools.SettingsPath);
            if (settings == null) return;
            var keys = ids.Where(p => !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value))
                .ToDictionary(p => p.Key + ".name", p => p.Value + ".name", StringComparer.Ordinal);
            var changed = false;
            foreach (var table in settings.languages.Where(l => l != null && l.table != null).Select(l => l.table).Distinct())
            {
                var present = new HashSet<string>(table.entries.Select(e => e.key ?? ""), StringComparer.Ordinal);
                var moved = false;
                for (int i = 0; i < table.entries.Count; i++)
                {
                    var entry = table.entries[i];
                    if (entry.key == null || !keys.TryGetValue(entry.key, out var to)) continue;
                    if (present.Contains(to) && !keys.ContainsKey(to))
                    {
                        Note(notes, $"텍스트 표({table.language})에 '{to}'이(가) 이미 있어 '{entry.key}'를 옮기지 않았습니다.");
                        continue;
                    }
                    entry.key = to;
                    table.entries[i] = entry;
                    moved = true;
                }
                if (!moved) continue;
                EditorUtility.SetDirty(table);
                AssetDatabase.SaveAssetIfDirty(table);
                Undo.ClearUndo(table);
                changed = true;
            }
            if (changed) GameLanguage.Reload();
        }

        // 씬 흐름(DB › Bootstrap)이 보는 플레이어 데이터 키를 새 ID로. 바꿨으면 true.
        internal static bool MoveSceneKey(GameDatabase database, IReadOnlyDictionary<string, string> keys)
        {
            if (database == null || keys == null || string.IsNullOrEmpty(database.SceneKey) || !keys.TryGetValue(database.SceneKey, out var to)) return false;
            database.EditorSetSceneFlow(to, database.ScenePath, database.EmptyScene);
            return true;
        }

        static void Note(List<string> notes, string message)
        {
            if (notes != null) notes.Add(message);
            else Debug.LogWarning("[MomoiProject] " + message);
        }

        // 연결한 프리팹 에셋이 없어진 프리팹 항목(지웠거나 .meta 없이 옮긴 것).
        internal static bool IsOrphanPrefab(PrefabData entry) =>
            entry != null && entry.prefab == null && !string.IsNullOrEmpty(entry.PrefabGuid) && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.PrefabGuid));

        static readonly HashSet<string> _orphansReported = new HashSet<string>(StringComparer.Ordinal);

        // 프로젝트에서 프리팹 에셋을 지우면 그 프리팹을 가리키던 DB 항목도 지운다(연결만 남은 빈 항목이 생기지 않게).
        // 지우지 않고 두는 때(다시 연결할지 지울지는 개발자가 정한다 — 검증이 "프리팹이 없습니다"로 알린다):
        //   다른 항목이 가리킨다 · 작업본이 있다(저장 안 된 변경·상세 창에 열림) · 같은 때 다른 프리팹이 들어왔다(.meta 없이 옮긴 프리팹일 수 있다).
        public int RemoveOrphanPrefabEntries(bool prefabsArrived = false)
        {
            int removed = 0;
            var kept = new List<string>();
            foreach (var entry in Committed.OfType<PrefabData>().ToList())
            {
                if (!IsOrphanPrefab(entry)) continue;
                if (prefabsArrived || Staging.IsStaged(entry) || FindUsages(entry).Count > 0)
                {
                    if (_orphansReported.Add(entry.Id ?? entry.name)) kept.Add(entry.DisplayName);
                    continue;
                }
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(entry));
                Debug.Log($"[MomoiProject] 프리팹이 지워져 DB 항목 '{entry.DisplayName}'을(를) 정리했습니다.");
                removed++;
            }
            if (kept.Count > 0)
                Debug.LogWarning($"[MomoiProject] 프리팹이 없어진 DB 항목을 지우지 않고 두었습니다: {string.Join(", ", kept)} — 다른 항목이 가리키거나, DB에서 고치는 중(저장 안 된 변경·상세 창)이거나, " +
                                 "프리팹을 .meta 없이 옮겼을 수 있습니다. 프리팹 칸에 다시 넣거나, 필요 없으면 Project 창에서 그 항목 에셋을 지우세요.");
            if (removed > 0) MarkDirty();
            return removed;
        }

        // ID 없는 항목(Create 메뉴로 만든 것 등)에 ID를 부여하고, 런타임용 GameDatabase 목록을 맞춘다.
        public void Sync()
        {
            _all = null;
            foreach (var data in Committed.Where(d => string.IsNullOrEmpty(d.Id)).ToList())
            {
                // Project 창에서 만든 항목(Create 메뉴): 파일 이름을 ID로(쓸 수 있으면), 아니면 연번.
                var info = DataTypes.Find(data.GetType());
                var fromName = IdRules.Normalize(data.name ?? "");
                data.EditorAssignId(fromName.Length > 1 && CanUseId(fromName, data, out _) ? fromName : NextId(info));
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                _all = null;
            }
            // 예전 저장 형식(로그의 반영 규칙 한 줄, 도전과제의 조건·보상, 능력의 대상·효과)을 트리거·결과로 옮긴다.
            foreach (var data in Committed.OfType<ILegacyData>().ToList())
            {
                if (!data.MigrateLegacy()) continue;
                var asset = (GameData)data;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                Debug.Log($"[MomoiProject] '{asset.DisplayName}'을(를) 트리거·결과 형식으로 옮겼습니다.");
                _all = null;
            }
            // 저장 전 작업본(옮기기 전에 만든 것)도 같은 형식으로(저장하면 원본에 그대로 들어간다).
            foreach (var data in All.OfType<ILegacyData>().Where(d => Staging.IsWorking((GameData)d)).ToList())
                data.MigrateLegacy();
            SyncDatabaseAsset();
            MarkDirty();
        }

        static bool _legacyWarned;

        public void SyncDatabaseAsset()
        {
            // 예전 자리의 데이터 폴더가 아직 있으면(옮기기 전) 새 자리에 GameDatabase를 따로 만들지 않는다 — 옮긴 뒤(Upkeep) 맞춘다.
            if (ProjectFolders.AnyLegacy())
            {
                // 옮기기를 해 봤는데도 남았으면(옮기지 못한 파일) 알린다 — 조용히 멈추면 새 항목이 게임에 안 보이는 까닭을 모른다. 도메인마다 한 번.
                if (ProjectFolders.MoveTried && !_legacyWarned)
                {
                    _legacyWarned = true;
                    Debug.LogWarning($"[MomoiProject] 예전 폴더 {string.Join(", ", ProjectFolders.LegacyLeft())}가 남아 DB 목록(GameDatabase)을 맞추지 못했습니다 — " +
                                     "그 안의 에셋을 Assets/MomoiProject 아래 같은 자리로 옮기면(Unity Project 창에서 끌어 옮기기) 다시 맞춥니다. 위에 '옮기지 못해' 경고가 있으면 그 파일부터.");
                }
                return;
            }
            var entries = Committed.ToList();
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabaseAssetPath);
            if (database == null)
            {
                if (entries.Count == 0) return;
                EnsureFolder(Path.GetDirectoryName(DatabaseAssetPath).Replace('\\', '/'));
                database = ScriptableObject.CreateInstance<GameDatabase>();
                AssetDatabase.CreateAsset(database, DatabaseAssetPath);
            }
            if (database.Entries.SequenceEqual(entries)) return;
            database.EditorSetEntries(entries);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static string Sanitize(string name) => Regex.Replace(name, "[\\\\/:*?\"<>|]", "_");

        // 종류의 폴더(영어): ID 앞부분을 대문자로 시작(ability → Ability, playerData → PlayerData). 프로젝트에 저절로 생기는 폴더 이름에 한글을 쓰지 않는다.
        public static string FolderOf(DataTypeInfo info) => $"{DataRoot}/{FolderName(info)}";

        static string FolderName(DataTypeInfo info)
        {
            var name = Regex.Replace(info.IdPrefix ?? "", "[^A-Za-z0-9]", "");
            if (name.Length == 0 || !char.IsLetter(name[0])) name = Regex.Replace(Regex.Replace(info.Type.Name, "Data$", ""), "[^A-Za-z0-9]", "");
            return name.Length == 0 ? "Data" : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        // 에디터를 열 때(시작 설정을 마친 프로젝트): ① 옛 한글 종류 폴더(표시 이름)를 영어 폴더로 옮긴다(GUID 유지 — 참조가 끊기지 않는다)
        // ② 하나만 두는 설정 종류([Database(Single = true)])가 비어 있으면 기본값으로 하나 만든다.
        public static void Upkeep()
        {
            // 예전 자리(Assets/Code · Assets/MomoiProjectData · Assets/UI …)의 시스템 폴더를 Assets/MomoiProject 아래로.
            ProjectFolders.MoveLegacy();
            Instance.SyncDatabaseAsset();
            // 게임 씬 폴더(Assets/Scenes)를 개발자 폴더로(테스트 씬은 시스템 쪽).
            ProjectFolders.MoveScenes(null);
            // 스크립트가 없는 asmdef(Editor·Tests)에 자리 표시 파일(Unity의 "스크립트가 없다" 경고를 막는다).
            StartSetup.EnsurePlaceholders(null);
            foreach (var info in DataTypes.All)
            {
                var legacy = $"{DataRoot}/{Sanitize(info.DisplayName)}";
                var target = FolderOf(info);
                if (legacy == target || !AssetDatabase.IsValidFolder(legacy)) continue;
                if (!AssetDatabase.IsValidFolder(target))
                {
                    var error = AssetDatabase.MoveAsset(legacy, target);
                    if (string.IsNullOrEmpty(error)) Debug.Log($"[MomoiProject] 폴더 이름을 영어로 바꿨습니다: {legacy} → {target}");
                    else Debug.LogWarning($"[MomoiProject] {legacy}를 {target}(으)로 옮기지 못했습니다: {error}");
                    continue;
                }
                // 둘 다 있으면 안의 것을 하나씩 옮긴다(이름이 겹치는 것은 그대로 둔다).
                foreach (var child in Directory.GetFileSystemEntries(legacy).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                {
                    var from = child.Replace('\\', '/');
                    var to = target + "/" + Path.GetFileName(from);
                    if (!File.Exists(to) && !Directory.Exists(to)) AssetDatabase.MoveAsset(from, to);
                }
                if (!Directory.EnumerateFileSystemEntries(legacy).Any()) AssetDatabase.DeleteAsset(legacy);
            }
            if (!StartSetup.IsDone) return;
            foreach (var info in DataTypes.All.Where(i => i.Single))
            {
                if (Instance.All.Any(d => d != null && d.GetType() == info.Type)) continue;
                // 화면 설정은 '화면 준비'와 같이(Panel Settings·테마까지).
                if (info.Type == typeof(ScreenSettingsData)) ScreenSetup.Ensure(commit: true);
                else Instance.Create(info.Type, info.IdPrefix);
                Debug.Log($"[MomoiProject] DB › {info.DisplayName} 항목을 기본값으로 만들었습니다(프로젝트에 하나만 두는 설정).");
            }
        }
    }

    // GameData 에셋이 추가·삭제·이동되면 자동으로 동기화한다.
    sealed class EditorDatabasePostprocessor : AssetPostprocessor
    {
        static bool _scheduled;
        // 이번에 모아 둔 사이 프리팹이 들어왔는지(.meta 없이 옮긴 프리팹이면 예전 GUID는 지워지고 새 GUID로 들어온다 — 그 항목은 지우지 않는다).
        static bool _prefabsArrived;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool relevant = deleted.Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) ||
                            imported.Concat(moved).Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                                                            typeof(GameData).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(p)));
            // 관계없는 때에 들어온 프리팹(프리팹을 고쳐 저장한 것 등)은 세지 않는다 — 이번 묶음이나 기다리는 정리와 같은 때만.
            if ((relevant || _scheduled) && imported.Concat(moved).Any(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))) _prefabsArrived = true;
            if (!relevant || _scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                var arrived = _prefabsArrived;
                _prefabsArrived = false;
                EditorDatabase.Instance.RemoveOrphanPrefabEntries(arrived);
                EditorDatabase.Instance.Sync();
            };
        }
    }
}
