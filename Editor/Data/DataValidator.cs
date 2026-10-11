using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
            // 지금 항목에서 트리거·결과 칸 검사가 이미 '없는 플레이어 데이터'로 알린 키(참조 검사가 같은 키를 다시 알리지 않게).
            public readonly HashSet<string> ReportedKeys = new HashSet<string>(StringComparer.Ordinal);

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

        // DB 창이 보는 모습(저장 안 된 변경 포함).
        public static List<ValidationIssue> ValidateAll() => Validate(EditorDatabase.Instance.All);

        // 적용된(저장한) 데이터만 — 빌드 검사. 저장 안 된 변경은 빌드에 들어가지 않으므로 그것으로 막거나 통과시키지 않는다(필드 규칙도 적용된 것만).
        public static List<ValidationIssue> ValidateCommitted()
        {
            using (EditorDatabase.CommittedRules()) return Validate(EditorDatabase.Instance.Committed);
        }

        public static List<ValidationIssue> Validate(IReadOnlyList<GameData> all)
        {
            // 사라진 항목(적용·되돌리기로 지워진 작업본 등)은 건너뛴다.
            all = all.Where(d => d != null).ToList();
            var context = new Context(all);

            // 같은 ID — 대소문자만 달라도 같은 ID로 본다(ID 바꾸기·만들기의 규칙과 같다).
            foreach (var group in all.Where(d => !string.IsNullOrEmpty(d.Id)).GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var spellings = group.Select(d => d.Id).Distinct(StringComparer.Ordinal).ToList();
                foreach (var data in group)
                {
                    context.Current = data;
                    // 글자까지 같은 ID는 참조가 어느 쪽을 가리키는지 알 수 없어, ID를 바꾼 쪽으로 참조를 옮기지 않는다(CodexStaging.Rename).
                    var same = group.Count(d => d.Id == data.Id) > 1;
                    context.Error(same
                        ? $"ID '{data.Id}'가 {group.Count()}개 항목에서 중복됩니다(복제한 에셋일 수 있음). 하나를 'ID 바꾸기'로 고치세요 — 이때 이 ID를 가리키던 칸은 옮기지 않고 " +
                          "남은 항목을 그대로 가리킵니다(바꾼 항목을 가리켜야 하는 칸은 다시 고르세요)."
                        : $"ID '{data.Id}'가 {group.Count()}개 항목에서 중복됩니다(대소문자만 다름: {string.Join(", ", spellings)} — 같은 ID로 봅니다). 하나를 'ID 바꾸기'로 고치세요.");
                }
            }

            // 같은 종류 안에서 생성 코드(GameIds) 이름이 같아지는 ID(bossDragon·boss.dragon → BossDragon). 하나는 BossDragon_2가 되어 코드가 헷갈린다.
            foreach (var group in all.Where(d => !string.IsNullOrEmpty(d.Id)).GroupBy(d => (d.GetType(), IdRules.ToIdentifier(d.Id))))
            {
                var ids = group.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (ids.Count < 2) continue;
                foreach (var data in group)
                {
                    context.Current = data;
                    var others = string.Join(", ", ids.Where(i => !string.Equals(i, data.Id, StringComparison.OrdinalIgnoreCase)).Select(i => $"'{i}'"));
                    context.Error($"ID '{data.Id}'는 같은 종류의 {others}와(과) 생성 코드 이름(GameIds의 {group.Key.Item2})이 같습니다. 점(.)·대문자 자리만 다른 ID는 쓸 수 없습니다 — 하나를 'ID 바꾸기'로 고치세요.");
                }
            }
            CheckReleasedRenames(all, context);

            foreach (var data in all)
            {
                context.Current = data;
                context.ReportedKeys.Clear();
                if (string.IsNullOrEmpty(data.Id)) context.Error("ID가 없습니다.");
                else if (!IdRules.IsValid(data.Id, out var idError)) context.Error($"ID '{data.Id}': {idError} 'ID 바꾸기'로 고치세요.");
                // 트리거·결과 칸을 먼저: 플레이어 데이터 검사·주기의 없는 키는 여기서 자세히 알리고, 아래 참조 검사는 같은 키를 다시 알리지 않는다.
                CheckSlots(data, all, context);
                // 트리거 칸이 아닌 조건(가중치 표 줄의 조건)도 플레이어 데이터 검사의 키·경로를 같은 규칙으로 본다(오타면 그 줄이 말없이 안 나온다).
                if (data is WeightTableData table)
                    foreach (var entry in table.entries)
                        if (entry?.condition != null) CheckPlayerDataFields(entry.condition, all, context);
                CheckReferences(data, all, context);
                CheckStrings(data, all, context);
                CheckRequired(data, context);
                try
                {
                    data.ValidateData(context);
                }
                catch (Exception e)
                {
                    context.Error($"검증 중 오류: {e.Message}");
                }

                // 프리팹이 없어졌는데 다른 항목이 가리키는 등으로 지우지 않고 둔 항목(EditorDatabase.RemoveOrphanPrefabEntries).
                if (data is PrefabData prefab && EditorDatabase.IsOrphanPrefab(prefab))
                    context.Warning("프리팹이 없습니다(지웠거나 .meta 없이 옮겼을 수 있습니다). 프리팹 칸에 다시 넣거나, 필요 없으면 Project 창에서 이 항목 에셋을 지우세요.");
                // 같은 ID에 [BindTo] 코드가 여럿이면 모두 실행되고 순서가 정해지지 않는다.
                var bound = BoundOutcomes.For(data.Id);
                if (bound.Count > 1) context.Warning($"이 항목에 [BindTo] 코드가 {bound.Count}개 연결되어 있습니다({string.Join(", ", bound.Select(b => b.Name))}). 모두 실행되고 순서는 정해지지 않으니 하나로 합치세요.");
            }

            FindCycles(all.OfType<AchievementData>().ToList(), context);
            CheckSpriteReviews(all, context);
            return context.Issues;
        }

        // 출시 뒤 ID 변경 이력의 예전 ID를 지금 다른 항목이 쓰면, 예전 저장의 그 ID(바꾸기 전 항목)와 이 항목을 구분할 수 없다
        // (런타임은 그 줄을 쓰지 않으므로 예전 저장의 값이 이 항목으로 읽힌다). 에디터는 이런 ID를 막는다 — 이미 있는 것을 알린다.
        static void CheckReleasedRenames(IReadOnlyList<GameData> all, Context context)
        {
            var database = UnityEditor.AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath);
            if (database == null || !database.IsReleased) return;
            foreach (var rename in database.Renames)
            {
                if (rename == null || string.IsNullOrEmpty(rename.oldId) || rename.oldId == rename.newId) continue;
                foreach (var data in all.Where(d => string.Equals(d.Id, rename.oldId, StringComparison.OrdinalIgnoreCase)))
                {
                    // 같은 항목이 예전 이름으로 돌아가는 중(저장 전 — 적용된 ID가 이력이 이어지는 ID)이면 괜찮다(저장하면 이력이 그 항목으로 정리된다).
                    var applied = EditorDatabase.Staging.OriginalOf(data);
                    if (applied != null && applied.GetType().FullName == rename.dataType && applied.Id == rename.newId) continue;
                    context.Current = data;
                    context.Error($"ID '{data.Id}'는 출시 뒤 '{rename.newId}'(으)로 바꾼 예전 ID입니다. 예전 저장의 '{data.Id}'(바꾸기 전 항목)와 구분할 수 없으니 이 항목의 ID를 바꾸세요.");
                }
            }
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
                if (key == null)
                {
                    context.ReportedKeys.Add(check.key);
                    context.Error($"플레이어 데이터 검사: 없는 플레이어 데이터 '{check.key}'를 가리킵니다.");
                }
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
                context.ReportedKeys.Add(change.key);
                context.Error($"플레이어 데이터에 주기: 없는 플레이어 데이터 '{change.key}'를 가리킵니다.");
                return;
            }
            var label = $"플레이어 데이터에 주기({PlayerDataGrants.PathLabel(key.DisplayName, change.path)})";
            var problems = new List<string>();
            change.Validate(key.ValueType, problems);
            foreach (var problem in problems) context.Error($"{label}: {problem}");
            CheckRewardKeywords(change, key.ValueType, label, context);
            foreach (var (id, type) in change.InstanceRefs(key.ValueType))
            {
                var template = all.OfType<InstanceData>().FirstOrDefault(t => t.Id == id);
                if (template == null) context.Error($"{label}: 없는 인스턴스 '{id}'를 가리킵니다.");
                else if (template.value != null && !type.IsInstanceOfType(template.value))
                    context.Error($"{label}: 인스턴스 '{id}'({template.value.GetType().Name})는 {DataModel.FriendlyName(type)}이(가) 아닙니다.");
            }
        }

        // 키워드가 연결된 필드([Keyword(typeof(그룹))])에 주는 값이 그 그룹의 키워드인지(값 하나·목록 요소·딕셔너리 키). 편집 칸은 목록에서만 고른다.
        static void CheckRewardKeywords(PlayerDataReward change, Type root, string label, Context context)
        {
            if (root == null) return;
            var target = FieldPaths.TypeAt(root, change.path, out var member, out _);
            var group = KeywordTools.GroupOf(member as FieldInfo);
            if (target == null || group == null) return;
            IEnumerable<string> values;
            switch (PlayerDataGrants.KindOf(target))
            {
                case GrantKind.Value: values = new[] { change.value }; break;
                case GrantKind.List: values = change.items; break;
                case GrantKind.Dictionary: values = change.keys; break;
                default: return;
            }
            var keywords = new HashSet<string>(KeywordTools.KeywordsOf(group).Select(k => k.Id), StringComparer.Ordinal);
            var name = KeywordTools.FindGroup(group)?.DisplayName ?? group;
            foreach (var value in (values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrEmpty(v) && !keywords.Contains(v)).Distinct())
                context.Error($"{label}: {value}은(는) 키워드 그룹 {name}에 없는 키워드입니다. 목록에서 다시 고르거나, 필요한 키워드면 코드에 더하세요(AI에게).");
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
        // 글자 칸 한 번 훑기(저장 전 변경 포함):
        //   · [Keyword(typeof(그룹))] 필드의 값이 그 그룹의 키워드인지. 비어 있는 값은 필수 검사가 본다.
        //   · 값 틀({balance:ID}·{data:키.경로}·{count:키.경로})이 있는 밸런스 값·플레이어 데이터를 가리키는지 — ID를 바꿔도 틀 안의 글자는 따라가지 않으니 끊긴 것을 알린다
        //     (없는 밸런스 값은 실행하면 행동이 실패하므로 오류, 없는 플레이어 데이터는 빈 글로 넘어가므로 경고).
        static void CheckStrings(GameData data, IReadOnlyList<GameData> all, Context context)
        {
            var so = new UnityEditor.SerializedObject(data);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != UnityEditor.SerializedPropertyType.String || string.IsNullOrEmpty(it.stringValue)) continue;
                var field = PropertyUtil.GetField(it, out _);
                if (it.stringValue.IndexOf('{') >= 0) CheckTemplates(it.stringValue, field, it, all, context);
                var group = KeywordTools.GroupOf(field);
                if (group == null) continue;
                if (KeywordTools.KeywordsOf(group).Any(k => k.Id == it.stringValue)) continue;
                var name = KeywordTools.FindGroup(group)?.DisplayName ?? group;
                context.Error($"{field.Name}: {it.stringValue}은(는) 키워드 그룹 {name}에 없는 키워드입니다. 목록에서 다시 고르거나, 필요한 키워드면 코드에 더하세요(AI에게).");
            }
        }

        static readonly Regex TemplateToken = new Regex(@"\{(balance|data|count):([^}]+)\}", RegexOptions.CultureInvariant);

        static void CheckTemplates(string text, FieldInfo field, UnityEditor.SerializedProperty property, IReadOnlyList<GameData> all, Context context)
        {
            foreach (Match match in TemplateToken.Matches(text))
            {
                var kind = match.Groups[1].Value;
                var value = match.Groups[2].Value.Trim();
                if (kind == "balance")
                {
                    // 오류: 실행하면 없는 밸런스 값을 읽다가 그 행동 전체가 실패한다({data:}·{count:}는 빈 글 + 경고로 넘어가므로 경고).
                    if (!all.OfType<BalanceData>().Any(b => b.Id == value))
                        context.Error($"{LabelOf(field, property)}: 값 틀 '{match.Value}'의 밸런스 값 '{value}'가 없습니다 — 이대로면 이 결과가 도는 행동이 실패합니다(ID를 바꿨다면 틀의 글자도 고치세요).");
                    continue;
                }
                // {data:키.경로}·{count:키.경로}: 정의된 가장 긴 앞부분(점까지)이 플레이어 데이터 키 — 실행(ModifyIntentOutcome.DataAt)과 같다(player.inventory.gold).
                var keys = all.OfType<PlayerDataEntry>().Select(k => k.Id).ToList();
                var found = false;
                for (var cut = value.Length; cut > 0 && !found; cut = value.LastIndexOf('.', cut - 1))
                    found = keys.Contains(value.Substring(0, cut));
                if (!found)
                {
                    var first = value.IndexOf('.') is var dot && dot > 0 ? value.Substring(0, dot) : value;
                    context.Warning($"{LabelOf(field, property)}: 값 틀 '{match.Value}'의 플레이어 데이터 '{first}'가 없습니다(ID를 바꿨다면 틀의 글자도 고치세요).");
                }
            }
        }

        // ID 참조 칸([DataRef]·필드 규칙·[PlayerDataRef])이 없는 항목을 가리키는지(수치·도전과제 참조는 블록 검증이 따로 본다).
        static void CheckReferences(GameData data, IReadOnlyList<GameData> all, Context context)
        {
            try
            {
                EditorDatabase.ForEachReference(data, (property, refType) =>
                {
                    if (refType == typeof(StatData) || refType == typeof(AchievementData)) return;
                    var id = property.stringValue;
                    var kind = DataTypes.Label(refType);
                    var field = PropertyUtil.GetField(property, out _);
                    var name = LabelOf(field, property);
                    // 플레이어 데이터 칸([PlayerDataRef]): 비어도 되는 칸이 있다(가중치 표의 단계 키 등 — 꼭 골라야 하는 칸은 그 항목의 검증이 알린다).
                    // 라이브러리 키($statuses·stat:… — DB 항목이 아님)와, 트리거·결과 칸 검사가 이미 알린 키는 넘어간다.
                    var playerData = refType == typeof(PlayerDataEntry) && field != null && field.IsDefined(typeof(PlayerDataRefAttribute), false);
                    if (playerData && (string.IsNullOrEmpty(id) || id.StartsWith("$", StringComparison.Ordinal) || id.IndexOf(':') >= 0 || context.ReportedKeys.Contains(id))) return;
                    if (string.IsNullOrEmpty(id))
                    {
                        if (!WarnWhenEmpty(data, property, field)) return;
                        context.Warning($"{name}: {kind}이(가) 선택되지 않았습니다.");
                        return;
                    }
                    var target = all.FirstOrDefault(d => d != null && d.Id == id);
                    if (target == null || !refType.IsInstanceOfType(target))
                    {
                        context.Error($"{name}: 없는 {kind} '{id}'를 가리킵니다. 목록에서 다시 고르거나 {kind}을(를) 먼저 만드세요.");
                        return;
                    }
                    // [DataRef(typeof(Card))]: 값이 그 클래스인 인스턴스여야 한다(카드 칸에 몬스터를 고른 경우).
                    if (!EditorDatabase.MatchesRef(field, target))
                        context.Error($"{name}: '{id}'은(는) {EditorDatabase.RefKindLabel(field)} 인스턴스가 아닙니다. 이 칸에는 {EditorDatabase.RefKindLabel(field)}만 고를 수 있습니다.");
                });
            }
            catch (Exception e)
            {
                context.Error($"참조 검사 중 오류: {e.Message}");
            }
        }

        // 빈 참조 칸을 경고할지. 비어도 되는 칸은 넘어간다:
        //   · [ShowIf]로 숨은 칸(가중치 표 줄의 종류가 '플레이어 데이터 목록'이면 '항목')
        //   · Block 안의 칸 — 고르지 않으면 안 되는 칸은 그 Block의 검증이 오류로 알린다(상태 효과 검사는 종류만 골라도 된다)
        //   · 다른 칸의 [ShowIf] 스위치 — 비어 있으면 '끔'(무게를 밸런스 값에서)
        //   · 필수(필드 규칙) — 필수 검사가 오류로 알린다
        //   · 상위 그룹 — 비어 있으면 맨 위
        static bool WarnWhenEmpty(GameData data, UnityEditor.SerializedProperty property, FieldInfo field)
        {
            if (data is INestedData && property.propertyPath == "parent") return false;
            if (field == null || EditorDatabase.IsRequired(field)) return false;
            if (field.GetCustomAttribute<DataRefAttribute>()?.Optional == true) return false;
            if (InsideBlock(property) || UI.ShowIfRules.IsSwitch(field)) return false;
            // 목록 칸(List<string>)의 요소는 그 목록 칸의 [ShowIf]를 따른다.
            return UI.ShowIfRules.Visible(property);
        }

        // 칸이 Block(트리거·조건·결과·효과·대상·값 조건) 안에 있는지: 위쪽 칸 중 Block 값을 가진 관리 참조가 있으면.
        static bool InsideBlock(UnityEditor.SerializedProperty property)
        {
            var so = property.serializedObject;
            var path = property.propertyPath;
            while (true)
            {
                var cut = path.LastIndexOf('.');
                if (cut < 0) return false;
                path = path.Substring(0, cut);
                if (path.EndsWith(".Array", StringComparison.Ordinal) || path == "Array") continue;
                var holder = so.FindProperty(path);
                if (holder != null && holder.propertyType == UnityEditor.SerializedPropertyType.ManagedReference && IsBlock(holder.managedReferenceValue)) return true;
            }
        }

        static bool IsBlock(object value) =>
            value is Trigger || value is Condition || value is Outcome || value is Effect || value is TargetSelector || value is ValueFilter;

        // 메시지에 쓰는 칸 이름: 보이는 이름([FieldLabel])과 변수 이름(입력 칸의 이름표 — 오류면 그 칸이 빨갛게 보인다).
        static string LabelOf(FieldInfo field, UnityEditor.SerializedProperty property)
        {
            if (field == null) return property.displayName;
            var shown = field.GetCustomAttribute<FieldLabelAttribute>()?.Text;
            return string.IsNullOrEmpty(shown) ? field.Name : $"{shown}({field.Name})";
        }

        // 스프라이트 틀: 크기가 바뀌어 다시 확인해야 하는 예외 촬영(틀의 경고로), 필드 규칙의 미리보기 크기가 그 틀에 없음(규칙의 경고로).
        static void CheckSpriteReviews(IReadOnlyList<GameData> all, Context context)
        {
            var frames = all.OfType<SpriteFrameData>().GroupBy(f => f.Id ?? "").ToDictionary(g => g.Key, g => g.First());
            foreach (var rule in all.OfType<FieldRuleData>())
            {
                if (string.IsNullOrEmpty(rule.spriteFrame) || string.IsNullOrEmpty(rule.previewSize)) continue;
                if (!frames.TryGetValue(rule.spriteFrame, out var frame) || frame.Find(rule.previewSize) != null) continue;
                context.Current = rule;
                context.Warning($"미리보기 크기 '{rule.previewSize}'이(가) 틀 '{rule.spriteFrame}'에 없습니다(원본이 대신 보인다). DB › 클래스 › 클래스 상세에서 다시 고르세요.");
            }
            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(SpriteSpecEditor.RegistryPath);
            if (registry == null) return;
            foreach (var frame in frames.Values)
            {
                var pending = registry.Framings
                    .Where(f => f != null && f.frame == frame.Id)
                    .SelectMany(f => f.overrides.Where(o => o.needsReview).Select(o => $"{(f.source != null ? f.source.name : "?")} · {o.size}"))
                    .ToList();
                if (pending.Count == 0) continue;
                context.Current = frame;
                context.Warning($"크기가 바뀌어 예외 촬영을 확인해야 합니다(틀 인스펙터의 '이 틀로 자른 원본' › 맞추기): {string.Join(", ", pending)}");
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
            // 빌드에는 적용(저장)된 데이터만 들어간다. 저장 안 된 변경이 있으면 알리고, 창이 있으면 그대로 빌드할지 묻는다
            // (빌드 중에 저장하면 에셋·GameIds가 빌드 도중에 바뀌므로 여기서 저장하지는 않는다).
            if (EditorDatabase.Staging.HasChanges)
            {
                var message = $"DB에 저장 안 된 변경 {EditorDatabase.Staging.Changes().Count}건이 있습니다. 빌드에는 저장(Ctrl+S)한 데이터만 들어갑니다.";
                if (!UnityEngine.Application.isBatchMode &&
                    !UnityEditor.EditorUtility.DisplayDialog("DB에 저장 안 된 변경", message + "\n\n넣으려면 빌드를 취소하고 DB 창에서 저장(Ctrl+S)한 뒤 다시 빌드하세요.", "그대로 빌드", "빌드 취소"))
                    throw new BuildFailedException("[MomoiProject] DB에 저장 안 된 변경이 있어 빌드를 취소했습니다. DB 창에서 저장(Ctrl+S)한 뒤 다시 빌드하세요.");
                UnityEngine.Debug.LogWarning("[MomoiProject] " + message);
            }
            var errors = DataValidator.ValidateCommitted().Where(i => i.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count == 0) return;
            var lines = errors.Take(20).Select(e => $"- {(e.Asset != null ? e.Asset.DisplayName : "?")}: {e.Message}");
            throw new BuildFailedException($"[MomoiProject] 데이터 검증 오류 {errors.Count}건으로 빌드를 중단합니다. DB 창에서 확인하세요.\n" + string.Join("\n", lines));
        }
    }
}
