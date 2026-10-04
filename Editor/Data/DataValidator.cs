using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GBible.EditorTools
{
    public sealed class ValidationIssue
    {
        public GameData Asset;
        public ValidationSeverity Severity;
        public string Message;
    }

    // 저장·빌드 시점 검증. 오류가 있으면 빌드를 막는다.
    public static class DataValidator
    {
        sealed class Context : IValidationContext
        {
            readonly Dictionary<string, StatData> _stats;
            readonly HashSet<string> _achievements;
            public GameData Current;
            public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

            public Context(IEnumerable<GameData> all)
            {
                _stats = new Dictionary<string, StatData>(StringComparer.Ordinal);
                foreach (var stat in all.OfType<StatData>())
                    if (!string.IsNullOrEmpty(stat.Id)) _stats[stat.Id] = stat;
                _achievements = new HashSet<string>(all.OfType<AchievementData>().Select(a => a.Id).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            }

            public bool StatExists(string id) => id != null && _stats.ContainsKey(id);
            public bool AchievementExists(string id) => id != null && _achievements.Contains(id);

            public StatDefinitionInfo? FindStat(string id) =>
                id != null && _stats.TryGetValue(id, out var s) ? new StatDefinitionInfo(s.Id, s.scope, s.aggregate) : (StatDefinitionInfo?)null;

            public void Report(ValidationSeverity severity, string message) =>
                Issues.Add(new ValidationIssue { Asset = Current, Severity = severity, Message = message });
        }

        public static List<ValidationIssue> ValidateAll() => Validate(EditorDatabase.Instance.All);

        public static List<ValidationIssue> Validate(IReadOnlyList<GameData> all)
        {
            // 사라진 항목(적용·되돌리기로 지워진 작업본 등)은 건너뛴다.
            all = all.Where(d => d != null).ToList();
            var context = new Context(all);

            foreach (var group in all.Where(d => !string.IsNullOrEmpty(d.Id)).GroupBy(d => d.Id).Where(g => g.Count() > 1))
                foreach (var data in group)
                {
                    context.Current = data;
                    context.Error($"ID '{data.Id}'가 {group.Count()}개 항목에서 중복됩니다(복제한 에셋일 수 있음). 'ID 바꾸기'로 고치세요.");
                }

            foreach (var data in all)
            {
                context.Current = data;
                if (string.IsNullOrEmpty(data.Id)) context.Error("ID가 없습니다.");
                else if (!IdRules.IsValid(data.Id, out var idError)) context.Error($"ID '{data.Id}': {idError} 'ID 바꾸기'로 고치세요.");
                CheckReferences(data, all, context);
                CheckKeywords(data, context);
                CheckRequired(data, context);
                try
                {
                    data.ValidateData(context);
                }
                catch (Exception e)
                {
                    context.Error($"검증 중 오류: {e.Message}");
                }

                CheckSlots(data, all, context);
                // 같은 ID에 [BindTo] 코드가 여럿이면 모두 실행되고 순서가 정해지지 않는다.
                var bound = BoundOutcomes.For(data.Id);
                if (bound.Count > 1) context.Warning($"이 항목에 [BindTo] 코드가 {bound.Count}개 연결되어 있습니다({string.Join(", ", bound.Select(b => b.Name))}). 모두 실행되고 순서는 정해지지 않으니 하나로 합치세요.");
            }

            FindCycles(all.OfType<AchievementData>().ToList(), context);
            CheckSpriteReviews(all, context);
            return context.Issues;
        }

        // 트리거·결과 칸(모든 기능 공용): 칸의 잠금(고를 수 있는 종류·고정), 조건이 값을 관찰하는지, 플레이어 데이터 검사·주기의 형식.
        static void CheckSlots(GameData data, IReadOnlyList<GameData> all, Context context)
        {
            var slots = TriggerSlots.Of(data).ToList();
            foreach (var slot in slots)
            {
                var where = string.IsNullOrEmpty(slot.Label) ? "" : slot.Label + ": ";
                if (slot.Trigger != null)
                {
                    foreach (var leaf in slot.Trigger.Leaves())
                        if ((leaf.Kind & slot.TriggerAllowed) == 0)
                            context.Error($"{where}이 칸에서는 '{BlockTypes.DisplayName(leaf.GetType())}' 트리거를 쓸 수 없습니다(쓸 수 있는 것: {TriggerSlots.Text(slot.TriggerAllowed)}).");
                    foreach (var condition in slot.Trigger.Conditions())
                    {
                        CheckPlayerDataFields(condition, all, context);
                        var keys = new HashSet<string>();
                        condition.CollectDependencies(keys);
                        if (keys.Count == 0 && condition.PollInterval <= 0 && !condition.AutoDependencies)
                            context.Error($"{where}조건이 아무 값도 관찰하지 않아 발동할 수 없습니다(블록 계약 위반).");
                    }
                }
                if (slot.Outcomes != null)
                {
                    foreach (var outcome in slot.Outcomes.Where(o => o != null))
                    {
                        if ((outcome.Kind & slot.OutcomeAllowed) == 0)
                            context.Error($"{where}이 칸에서는 '{BlockTypes.DisplayName(outcome.GetType())}' 결과를 쓸 수 없습니다(쓸 수 있는 것: {TriggerSlots.Text(slot.OutcomeAllowed)}).");
                        if (outcome is PlayerDataReward change) CheckPlayerDataReward(change, all, context);
                        if (outcome is RecordOutcome record) CheckRecord(record, slot.Trigger ?? slots.FirstOrDefault(s => s.Owner == slot.Owner && s.Trigger != null)?.Trigger, where, context);
                    }
                    if (slot.OutcomeFixed && slot.Outcomes.Count(o => o != null) != 1)
                        context.Error($"{where}결과가 '{TriggerSlots.Text(slot.OutcomeAllowed)}' 하나로 고정된 칸인데 {slot.Outcomes.Count(o => o != null)}개입니다.");
                }
            }
        }

        // 로그에 기록: '반영할 값'은 같은 칸 짝의 트리거가 보는 이벤트에서 읽는다. 이벤트가 없거나, 여러 이벤트 중 그 숫자 필드가 없는 것이 있으면 오류.
        static void CheckRecord(RecordOutcome record, Trigger when, string where, Context context)
        {
            if (string.IsNullOrEmpty(record.field)) return;
            var events = when?.EventTypes().ToList() ?? new List<Type>();
            if (events.Count == 0)
            {
                context.Error($"{where}로그에 기록의 반영할 값 '{record.field}'을 읽을 이벤트가 트리거에 없습니다(비우면 횟수 1).");
                return;
            }
            foreach (var type in events)
                if (TypeResolver.NumberGetter(type, record.field, out var error) == null)
                    context.Error($"{where}{EventBus.DisplayNameOf(type)}: {error}");
        }

        // 플레이어 데이터 검사 블록: 플레이어 데이터가 있는지, 그 값 형식 기준으로 경로·비교·값이 맞는지.
        static void CheckPlayerDataFields(Condition condition, IReadOnlyList<GameData> all, Context context)
        {
            if (condition is PlayerDataCondition check && !string.IsNullOrEmpty(check.key))
            {
                var key = all.OfType<PlayerDataEntry>().FirstOrDefault(k => k.Id == check.key);
                if (key == null) context.Error($"플레이어 데이터 검사: 없는 플레이어 데이터 '{check.key}'를 가리킵니다.");
                else if (key.ValueType != null && check.filter != null)
                {
                    var problems = new List<string>();
                    check.filter.Validate(key.ValueType, problems);
                    foreach (var problem in problems) context.Error($"플레이어 데이터 검사({key.DisplayName}): {problem}");
                }
            }
            foreach (var child in condition.Children) CheckPlayerDataFields(child, all, context);
        }

        // 플레이어 데이터에 주기 보상: 플레이어 데이터가 있는지, 필드 형식 기준으로 값이 맞는지, 고른 인스턴스가 있고 형식이 맞는지.
        static void CheckPlayerDataReward(PlayerDataReward change, IReadOnlyList<GameData> all, Context context)
        {
            if (string.IsNullOrEmpty(change.key)) return;
            var key = all.OfType<PlayerDataEntry>().FirstOrDefault(k => k.Id == change.key);
            if (key == null)
            {
                context.Error($"플레이어 데이터에 주기: 없는 플레이어 데이터 '{change.key}'를 가리킵니다.");
                return;
            }
            var label = $"플레이어 데이터에 주기({PlayerDataGrants.PathLabel(key.DisplayName, change.path)})";
            var problems = new List<string>();
            change.Validate(key.ValueType, problems);
            foreach (var problem in problems) context.Error($"{label}: {problem}");
            foreach (var (id, type) in change.InstanceRefs(key.ValueType))
            {
                var template = all.OfType<InstanceData>().FirstOrDefault(t => t.Id == id);
                if (template == null) context.Error($"{label}: 없는 인스턴스 '{id}'를 가리킵니다.");
                else if (template.value != null && !type.IsInstanceOfType(template.value))
                    context.Error($"{label}: 인스턴스 '{id}'({template.value.GetType().Name})는 {DataModel.FriendlyName(type)}이(가) 아닙니다.");
            }
        }

        // 필드 규칙의 '필수': 비어 있으면(빈 글자·빈 참조·빈 목록) 오류. 인스턴스 값 같은 안쪽 클래스까지 본다.
        static void CheckRequired(GameData data, Context context)
        {
            var so = new UnityEditor.SerializedObject(data);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                var field = PropertyUtil.GetField(it, out _);
                if (field == null || !EditorDatabase.IsRequired(field)) continue;
                // 목록의 요소마다가 아니라 목록 칸 자체로 한 번만 본다.
                if (it.propertyPath.EndsWith("]", StringComparison.Ordinal) || it.propertyPath.EndsWith(".Array.size", StringComparison.Ordinal)) continue;
                bool empty;
                switch (it.propertyType)
                {
                    case UnityEditor.SerializedPropertyType.String: empty = string.IsNullOrEmpty(it.stringValue); break;
                    case UnityEditor.SerializedPropertyType.ObjectReference: empty = it.objectReferenceValue == null; break;
                    case UnityEditor.SerializedPropertyType.ManagedReference: empty = it.managedReferenceValue == null; break;
                    default: empty = it.isArray && it.propertyType == UnityEditor.SerializedPropertyType.Generic && it.arraySize == 0; break;
                }
                if (empty) context.Error($"필수 값 '{it.displayName}'({it.propertyPath})이 비어 있습니다.");
            }
        }
        // [Keyword(typeof(그룹))] 필드의 값이 그 그룹의 키워드인지(저장 전 변경 포함). 비어 있는 값은 필수 검사가 본다.
        static void CheckKeywords(GameData data, Context context)
        {
            var so = new UnityEditor.SerializedObject(data);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != UnityEditor.SerializedPropertyType.String || string.IsNullOrEmpty(it.stringValue)) continue;
                var field = PropertyUtil.GetField(it, out _);
                var group = KeywordTools.GroupOf(field);
                if (group == null) continue;
                if (KeywordTools.KeywordsOf(group).Any(k => k.Id == it.stringValue)) continue;
                var name = KeywordTools.FindGroup(group)?.DisplayName ?? group;
                context.Error($"{field.Name}: {it.stringValue}은(는) 키워드 그룹 {name}에 없는 키워드입니다. 목록에서 다시 고르거나 키워드 탭에서 더하세요.");
            }
        }

        // [DataRef] 필드가 없는 항목을 가리키는지(수치·도전과제 참조는 블록 검증이 따로 본다).
        static void CheckReferences(GameData data, IReadOnlyList<GameData> all, Context context)
        {
            try
            {
                EditorDatabase.ForEachReference(data, (property, refType) =>
                {
                    if (refType == typeof(StatData) || refType == typeof(AchievementData)) return;
                    var id = property.stringValue;
                    var kind = DataTypes.Find(refType)?.DisplayName ?? refType.Name;
                    if (string.IsNullOrEmpty(id))
                    {
                        // 상위 그룹은 비어 있으면 맨 위(정상).
                        if (data is INestedData && property.propertyPath == "parent") return;
                        context.Warning($"{property.displayName}: {kind}이(가) 선택되지 않은 칸이 있습니다.");
                        return;
                    }
                    var target = all.FirstOrDefault(d => d != null && d.Id == id);
                    if (target == null || !refType.IsInstanceOfType(target))
                    {
                        context.Error($"{property.displayName}: 없는 {kind} '{id}'를 가리킵니다. 목록에서 다시 고르거나 {kind}을(를) 먼저 만드세요.");
                        return;
                    }
                    // [DataRef(typeof(Card))]: 값이 그 클래스인 인스턴스여야 한다(카드 칸에 몬스터를 고른 경우).
                    var field = PropertyUtil.GetField(property, out _);
                    if (!EditorDatabase.MatchesRef(field, target))
                        context.Error($"{property.displayName}: '{id}'은(는) {EditorDatabase.RefKindLabel(field)} 인스턴스가 아닙니다. 이 칸에는 {EditorDatabase.RefKindLabel(field)}만 고를 수 있습니다.");
                });
            }
            catch (Exception e)
            {
                context.Error($"참조 검사 중 오류: {e.Message}");
            }
        }

        // 규격이 바뀌어 다시 확인해야 하는 예외 촬영(규격 항목의 경고로).
        static void CheckSpriteReviews(IReadOnlyList<GameData> all, Context context)
        {
            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(SpriteSpecEditor.RegistryPath);
            if (registry == null) return;
            foreach (var preset in all.OfType<SpritePresetData>())
            {
                var pending = registry.Framings.Where(f => f.OverrideFor(preset.Id)?.needsReview == true).Select(f => f.source != null ? f.source.name : "?").ToList();
                if (pending.Count == 0) continue;
                context.Current = preset;
                context.Warning($"규격이 바뀌어 예외 촬영을 확인해야 합니다: {string.Join(", ", pending)}");
            }
        }
        // A 달성 → B 조건 → A 조건 같은 순환 참조.
        static void FindCycles(List<AchievementData> achievements, Context context)
        {
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var a in achievements)
            {
                var when = a.EffectiveTrigger;
                if (string.IsNullOrEmpty(a.Id) || when == null) continue;
                var keys = new HashSet<string>();
                foreach (var condition in when.Conditions()) condition.CollectDependencies(keys);
                edges[a.Id] = keys.Where(k => k.StartsWith(PlayerDataKeys.AchievementPrefix, StringComparison.Ordinal))
                                  .Select(k => k.Substring(PlayerDataKeys.AchievementPrefix.Length)).ToList();
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in edges.Keys) Visit(id, new Stack<string>());

            void Visit(string id, Stack<string> path)
            {
                state.TryGetValue(id, out var s);
                if (s == 2) return;
                if (s == 1)
                {
                    var cycle = path.Reverse().SkipWhile(p => p != id).Concat(new[] { id }).ToList();
                    foreach (var member in cycle.Distinct())
                    {
                        if (!reported.Add(member)) continue;
                        context.Current = achievements.FirstOrDefault(a => a.Id == member);
                        context.Error("도전과제 순환 참조: " + string.Join(" → ", cycle.Select(c => EditorDatabase.Instance.AchievementName(c))));
                    }
                    return;
                }
                state[id] = 1;
                path.Push(id);
                if (edges.TryGetValue(id, out var next))
                    foreach (var n in next) Visit(n, path);
                path.Pop();
                state[id] = 2;
            }
        }
    }

    sealed class DataValidatorBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var errors = DataValidator.ValidateAll().Where(i => i.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count == 0) return;
            var lines = errors.Take(20).Select(e => $"- {(e.Asset != null ? e.Asset.DisplayName : "?")}: {e.Message}");
            throw new BuildFailedException($"[MomoiProject] 데이터 검증 오류 {errors.Count}건으로 빌드를 중단합니다. DB 창에서 확인하세요.\n" + string.Join("\n", lines));
        }
    }
}
