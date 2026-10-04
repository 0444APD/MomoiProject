using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 보상 목록(도전과제 보상, 다이얼로그의 값 바꾸기 등 '주는' 구문 공통).
    //   처음엔 '+ 보상 추가'만 있다. 누르면 플레이어 데이터 필드 목록이 뜨고, 고르면 한 줄이 생긴다.
    //   한 줄 = 왼쪽: 고른 필드 / 오른쪽: 그 필드 형식에 맞는 값 칸. 그 아래에 다시 '+ 보상 추가'.
    //   이미 고른 필드는 목록에 다시 나오지 않는다(중복 방지).
    // 값 칸 규칙(PlayerDataGrants.KindOf):
    //   숫자 → 증감만 / 글자·참거짓·enum → 값(키워드가 연결된 글자는 목록) / 클래스 → 인스턴스 중 하나(여기서 새로 만들지 않는다) /
    //   컬렉션 → '+'로 요소를 더하고, 요소마다 다시 숫자·값·인스턴스 규칙.
    // 플레이어 데이터가 아닌 보상(다이얼로그 재생 등)은 같은 목록의 '다른 보상'에서 고르고 블록 편집기로 그린다.
    // 결과 칸(모든 기능 공용: 로그·도전과제·능력·아이템 사용 …). 위의 보상 규칙에 더해:
    //   '+ 결과 추가' 목록 = 플레이어 데이터 필드(플레이어 데이터에 주기) · 로그에 기록 · 대상에게 효과 · 코딩한 결과.
    //   칸의 잠금([OutcomeSlot])에 없는 종류는 목록에 나오지 않는다. 고정 칸(로그 = 로그에 기록)은 하나만 두고 세부 칸만 고친다.
    public static class RewardListEditor
    {
        public static VisualElement Build(SerializedProperty list, Action onValue, Action onStructure) => Build(list, typeof(Reward), onValue, onStructure);

        // [BindTo]로 연결된 코드 결과: 보기만(클래스·함수 이름, 받는 이벤트, cs 열기).
        static VisualElement BoundView(string id)
        {
            var box = Gs.Col();
            var notice = new GsNotice("코드로 쓴 결과가 이 항목에 연결되어 있습니다([BindTo]). 결과는 코드에서 고칩니다.", GsNoticeKind.Info);
            box.Add(notice);
            foreach (var code in BoundOutcomes.For(id))
            {
                var row = Gs.Row("gs-sheet__row");
                row.style.marginTop = 4;
                var name = Gs.Col("gs-grow");
                var type = code.Type;
                if (code.Method == null)
                {
                    name.Add(Gs.Label(BlockTypes.DisplayName(type) ?? type.Name, "gs-heading"));
                    name.Add(Gs.Label(type.FullName + (BlockTypes.Description(type) is string d && d.Length > 0 ? "  ·  " + d : ""), "gs-caption"));
                }
                else
                {
                    name.Add(Gs.Label(code.Name, "gs-heading"));
                    name.Add(Gs.Label(code.EventType == null ? "이벤트를 받지 않는 함수" : "받는 이벤트: " + code.EventType.Name, "gs-caption"));
                }
                row.Add(name);
                row.Add(new GsButton("cs 열기", () => OpenScript(type), GsButtonKind.Ghost));
                box.Add(row);
            }
            return box;
        }

        static void OpenScript(Type type)
        {
            while (type.DeclaringType != null) type = type.DeclaringType;
            var pattern = new System.Text.RegularExpressions.Regex(@"\b(class|struct)\s+" + type.Name + @"\b");
            // 파일 이름이 클래스 이름과 같은 것부터, 없으면 모든 스크립트에서 찾는다(파일 하나에 여러 클래스가 있을 때).
            var named = AssetDatabase.FindAssets(type.Name + " t:MonoScript");
            foreach (var guid in named.Concat(AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }).Except(named)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !pattern.IsMatch(System.IO.File.ReadAllText(path))) continue;
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(path));
                return;
            }
            Debug.LogWarning($"[MomoiProject] '{type.FullName}'의 스크립트 파일을 찾지 못했습니다.");
        }

        public static VisualElement Build(SerializedProperty list, Type elementType, Action onValue, Action onStructure)
        {
            var container = Gs.Col("gs-rewards");
            var slot = TriggerSlots.OutcomeSlotOf(list);
            // 코드로 쓴 결과가 이 항목에 연결되어 있으면([BindTo]) 고를 수 없다: 연결된 클래스를 보이고 cs를 연다.
            // 코드가 대신하지 않는 칸(OutcomeSlot BindTo = false: 상태 효과의 걸릴 때·끝날 때)은 그대로 고친다.
            if (list.serializedObject.targetObject is GBible.Data.GameData owner && BoundOutcomes.Has(owner.Id) && (slot?.BindTo ?? true))
            {
                container.Add(BoundView(owner.Id));
                return container;
            }
            var allowed = slot?.Allowed ?? OutcomeKinds.All;
            // 보상(Reward) 목록에는 Reward만 들어간다.
            if (elementType == typeof(Reward)) allowed &= OutcomeKinds.PlayerData | OutcomeKinds.Coded;
            var isFixed = slot?.Fixed ?? false;

            // 고정 칸: 비어 있으면 그 결과 하나를 만들어 둔다(세부 칸만 고친다).
            if (isFixed && list.arraySize == 0 && Default(allowed) is Outcome fixedOutcome)
            {
                list.serializedObject.Update();
                list.InsertArrayElementAtIndex(0);
                list.GetArrayElementAtIndex(0).managedReferenceValue = fixedOutcome;
                list.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).managedReferenceValue is PlayerDataReward grant)
                    used.Add(Slot(grant.key, grant.path));

            for (int i = 0; i < list.arraySize; i++)
            {
                var index = i;
                var element = list.GetArrayElementAtIndex(i);
                void Remove()
                {
                    list.serializedObject.Update();
                    list.GetArrayElementAtIndex(index).managedReferenceValue = null;
                    var size = list.arraySize;
                    list.DeleteArrayElementAtIndex(index);
                    if (list.arraySize == size) list.DeleteArrayElementAtIndex(index);
                    list.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                }
                var tools = isFixed ? Array.Empty<VisualElement>() : new VisualElement[] { RemoveTool(Remove) };
                switch (element.managedReferenceValue)
                {
                    case PlayerDataReward _:
                        container.Add(Row(element, onValue, onStructure, tools));
                        break;
                    case RecordOutcome _:
                        container.Add(RecordRow(element, onValue, isFixed, tools));
                        break;
                    default:
                        container.Add(new BlockEditor(element, elementType, onValue, onStructure, isFixed ? null : (Action)Remove));
                        break;
                }
            }

            if (isFixed)
            {
                if (list.arraySize == 0) container.Add(Problem($"이 칸은 '{TriggerSlots.Text(allowed)}' 하나로 고정입니다."));
                return container;
            }
            GsButton add = null;
            add = new GsButton("+ 결과 추가", () => GsDropdown.Show(add, AddItems(list, allowed, used, onStructure), placeholder: "결과·필드 검색"), GsButtonKind.Ghost);
            add.tooltip = $"무엇을 할지 더합니다({TriggerSlots.Text(allowed)}). 플레이어 데이터의 필드는 이미 고른 것이 다시 나오지 않습니다.";
            add.style.alignSelf = Align.FlexStart;
            container.Add(add);
            return container;
        }

        // 고정 칸의 기본 결과.
        static Outcome Default(OutcomeKinds allowed)
        {
            if (allowed == OutcomeKinds.Record) return new RecordOutcome();
            if (allowed == OutcomeKinds.Targeted) return new TargetedOutcome();
            return null;
        }

        static string Slot(string key, string path) => (key ?? "") + "|" + (path ?? "");

        // '+ 결과 추가' 목록: 칸이 허락하는 종류만. 플레이어 데이터마다 줄 수 있는 필드(이미 고른 것 제외) · 로그에 기록 · 대상에게 효과 · 코딩한 결과.
        static List<GsDropdownItem> AddItems(SerializedProperty list, OutcomeKinds allowed, HashSet<string> used, Action onStructure)
        {
            void Insert(Outcome outcome)
            {
                list.serializedObject.Update();
                var at = list.arraySize;
                list.InsertArrayElementAtIndex(at);
                list.GetArrayElementAtIndex(at).managedReferenceValue = outcome;
                list.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }

            var items = new List<GsDropdownItem>();
            if ((allowed & OutcomeKinds.Record) != 0)
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(RecordOutcome)), () => Insert(new RecordOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(RecordOutcome)) });
            if ((allowed & OutcomeKinds.Targeted) != 0)
            {
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(TargetedOutcome)), () => Insert(new TargetedOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(TargetedOutcome)) });
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(ChoiceOutcome)), () => Insert(new ChoiceOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(ChoiceOutcome)) });
            }
            if ((allowed & OutcomeKinds.Intent) != 0)
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(ModifyIntentOutcome)), () => Insert(new ModifyIntentOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(ModifyIntentOutcome)) });
            if ((allowed & OutcomeKinds.Event) != 0)
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(PublishEventOutcome)), () => Insert(new PublishEventOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(PublishEventOutcome)) });

            if ((allowed & OutcomeKinds.PlayerData) != 0)
            {
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(PayOutcome)), () => Insert(new PayOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(PayOutcome)) });
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(RollTableOutcome)), () => Insert(new RollTableOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(RollTableOutcome)) });
                items.Add(new GsDropdownItem(BlockTypes.DisplayName(typeof(ChooseFromTableOutcome)), () => Insert(new ChooseFromTableOutcome())) { Group = "결과", Detail = BlockTypes.Description(typeof(ChooseFromTableOutcome)) });
            }
            if ((allowed & OutcomeKinds.PlayerData) != 0)
            {
                var keys = EditorDatabase.Instance.OfType<PlayerDataEntry>().OrderBy(k => k.Id).ToList();
                var before = items.Count;
                foreach (var key in keys)
                {
                    var root = key.ValueType;
                    foreach (var path in PlayerDataGrants.Paths(root))
                    {
                        if (used.Contains(Slot(key.Id, path))) continue;
                        var id = key.Id;
                        var picked = path;
                        var type = FieldPaths.TypeAt(root, path, out _);
                        items.Add(new GsDropdownItem(string.IsNullOrEmpty(path) ? key.Id : FieldFilter.PathLabel(path), () => Insert(new PlayerDataReward { key = id, path = picked }))
                        {
                            Group = "플레이어 데이터에 주기 · " + key.Id,
                            Caption = KindCaption(type),
                        });
                    }
                }
                if (keys.Count == 0) items.Add(new GsDropdownItem("DB › 플레이어 데이터 탭에 아직 항목이 없습니다") { Group = "플레이어 데이터에 주기", Disabled = true });
                else if (items.Count == before) items.Add(new GsDropdownItem("더 고를 필드가 없습니다(모두 골랐습니다)") { Group = "플레이어 데이터에 주기", Disabled = true });
            }

            if ((allowed & OutcomeKinds.Coded) != 0)
                foreach (var type in BlockTypes.ConcreteTypes(typeof(Reward)).Where(t => t != typeof(PlayerDataReward)))
                {
                    var picked = type;
                    items.Add(new GsDropdownItem(BlockTypes.DisplayName(type), () => Insert((Outcome)Activator.CreateInstance(picked)))
                    {
                        Group = "코딩한 결과",
                        Detail = BlockTypes.Description(type),
                    });
                }
            if (items.Count == 0) items.Add(new GsDropdownItem("이 칸에 더할 수 있는 결과가 없습니다") { Disabled = true });
            return items;
        }

        // 로그에 기록 한 줄: [로그] [반영할 값] × [배수]. 고정 칸(로그 탭)이면 로그는 이 로그 자신이라 칸을 숨긴다.
        static VisualElement RecordRow(SerializedProperty block, Action onValue, bool isFixed, IEnumerable<VisualElement> tools)
        {
            var record = (RecordOutcome)block.managedReferenceValue;
            var row = Gs.Row("gs-reward");
            row.tooltip = record.Natural(EditorDatabase.Instance);
            var left = Gs.Col("gs-reward__field");
            left.Add(Gs.Label(BlockTypes.DisplayName(typeof(RecordOutcome)), "gs-reward__name"));
            left.Add(Gs.Label(isFixed ? "이 로그 · 고정" : "로그 · 값 × 배수", "gs-caption"));
            row.Add(left);
            var right = Gs.Col("gs-reward__value", "gs-grow");
            if (!isFixed) right.Add(Labeled("로그", Inline(block.FindPropertyRelative(nameof(RecordOutcome.stat)), onValue), "비우면 이 결과를 가진 로그"));
            right.Add(Labeled("반영할 값", Inline(block.FindPropertyRelative(nameof(RecordOutcome.field)), onValue), "트리거 이벤트의 숫자 필드 · 비우면 횟수 1"));
            right.Add(Labeled("배수", Inline(block.FindPropertyRelative(nameof(RecordOutcome.multiplier)), onValue), "예: 2배로 세기, 음수로 빼기"));
            row.Add(right);
            foreach (var tool in tools) row.Add(tool);
            return row;
        }

        static string KindCaption(Type type)
        {
            switch (PlayerDataGrants.KindOf(type))
            {
                case GrantKind.Number: return "숫자 · 증감";
                case GrantKind.Value: return DataModel.FriendlyName(type) + " · 값";
                case GrantKind.Instance: return DataModel.FriendlyName(type) + " · 인스턴스";
                case GrantKind.List: return DataModel.FriendlyName(type) + " · 요소 넣기";
                case GrantKind.Dictionary: return DataModel.FriendlyName(type) + " · 키와 값 넣기";
                default: return "";
            }
        }

        static GsButton RemoveTool(Action remove)
        {
            var button = new GsButton("✕", remove, GsButtonKind.Ghost);
            button.AddToClassList("gs-block__action");
            button.tooltip = "이 보상 삭제";
            return button;
        }

        // ── 보상 한 줄: 왼쪽 필드, 오른쪽 값 칸 ─────────────────────

        public static VisualElement Row(SerializedProperty block, Action onValue, Action onStructure, IEnumerable<VisualElement> tools)
        {
            var reward = (PlayerDataReward)block.managedReferenceValue;
            var row = Gs.Row("gs-reward");
            row.tooltip = reward.Natural(EditorDatabase.Instance);

            var key = EditorDatabase.Instance.FindById(reward.key) as PlayerDataEntry;
            var root = key?.ValueType;
            MemberInfo leaf = null;
            var target = root == null ? null : FieldPaths.TypeAt(root, reward.path, out leaf, out _);

            var left = Gs.Col("gs-reward__field");
            var fieldName = string.IsNullOrEmpty(reward.path) ? (reward.key ?? "?") : $"{reward.key} › {FieldFilter.PathLabel(reward.path)}";
            left.Add(Gs.Label(fieldName, "gs-reward__name"));
            left.Add(Gs.Label(target != null ? KindCaption(target) : "", "gs-caption"));
            row.Add(left);

            var right = Gs.Col("gs-reward__value", "gs-grow");
            if (key == null) right.Add(Problem($"없는 플레이어 데이터 '{reward.key}' — 삭제하고 다시 고르세요."));
            else if (target == null) right.Add(Problem($"'{reward.path}' 필드가 없습니다(필드 이름이 바뀌었는지 확인) — 삭제하고 다시 고르세요."));
            else
            {
                var amount = block.FindPropertyRelative(nameof(PlayerDataReward.amount));
                var value = block.FindPropertyRelative(nameof(PlayerDataReward.value));
                var items = block.FindPropertyRelative(nameof(PlayerDataReward.items));
                switch (PlayerDataGrants.KindOf(target))
                {
                    case GrantKind.Number:
                        right.Add(Labeled("증감", Inline(amount, onValue), "음수면 감소"));
                        break;
                    case GrantKind.Value:
                        right.Add(ValueSlot(value, target, leaf as FieldInfo, onValue, onStructure));
                        break;
                    case GrantKind.Instance:
                        right.Add(InstanceSlot(value, target, onStructure));
                        break;
                    case GrantKind.List:
                        right.Add(ListSlots(items, target, leaf as FieldInfo, onValue, onStructure));
                        break;
                    case GrantKind.Dictionary:
                        right.Add(DictionarySlots(block.FindPropertyRelative(nameof(PlayerDataReward.keys)), items, target, leaf as FieldInfo, onValue, onStructure));
                        break;
                    default:
                        right.Add(Problem($"{DataModel.FriendlyName(target)}은(는) 보상으로 줄 수 없는 형식입니다."));
                        break;
                }
            }
            row.Add(right);
            foreach (var tool in tools) row.Add(tool);
            return row;
        }

        static Label Problem(string text)
        {
            var label = Gs.Label(text, "gs-caption");
            label.style.color = Gs.Danger;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        static VisualElement Labeled(string label, VisualElement content, string hint)
        {
            var line = Gs.Row();
            line.style.alignItems = Align.Center;
            var name = Gs.Label(label, "gs-caption");
            name.style.marginRight = 8;
            line.Add(name);
            line.Add(content);
            if (hint != null)
            {
                var caption = Gs.Label(hint, "gs-caption");
                caption.style.marginLeft = 8;
                caption.style.opacity = 0.7f;
                line.Add(caption);
            }
            return line;
        }

        static GsInlineProperty Inline(SerializedProperty property, Action onValue)
        {
            var inline = new GsInlineProperty(property);
            inline.AddToClassList("gs-reward__input");
            inline.Changed += () => onValue?.Invoke();
            return inline;
        }

        // 누르면 목록이 열리는 값 칸(인스턴스·키워드·enum·참거짓).
        static VisualElement Pill(string text, bool empty, bool broken, Action<VisualElement> open)
        {
            var box = Gs.Box("gs-inline", "gs-inline--editable", "gs-reward__input");
            if (broken) box.AddToClassList("gs-inline--broken");
            var label = Gs.Label(text, "gs-inline__value");
            if (empty) label.AddToClassList("gs-inline__empty");
            box.Add(label);
            box.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                open(box);
            });
            return box;
        }

        static void Set(SerializedProperty property, string text, Action onStructure)
        {
            property.serializedObject.Update();
            property.stringValue = text;
            property.serializedObject.ApplyModifiedProperties();
            onStructure?.Invoke();
        }

        // 클래스: DB › 인스턴스에서 만든 그 형식의 인스턴스 중 하나.
        static VisualElement InstanceSlot(SerializedProperty property, Type type, Action onStructure)
        {
            var db = EditorDatabase.Instance;
            var id = property.stringValue;
            var template = string.IsNullOrEmpty(id) ? null : db.OfType<InstanceData>().FirstOrDefault(t => t.Id == id);
            var broken = !string.IsNullOrEmpty(id) && (template == null || (template.value != null && !type.IsInstanceOfType(template.value)));
            var text = string.IsNullOrEmpty(id) ? "인스턴스 선택" : template == null ? $"없는 인스턴스: {id}" : id;
            var pill = Pill(text, string.IsNullOrEmpty(id), broken, anchor =>
            {
                var items = db.OfType<InstanceData>()
                    .Where(t => t.value != null && type.IsInstanceOfType(t.value))
                    .OrderBy(t => db.GroupLabel(t) ?? "").ThenBy(t => t.Id)
                    .Select(t =>
                    {
                        var pick = t.Id;
                        return new GsDropdownItem(pick, () => Set(property, pick, onStructure))
                        {
                            Group = db.GroupLabel(t),
                            Caption = DataModel.Summary(t.value),
                            Selected = pick == id,
                        };
                    }).ToList();
                if (items.Count == 0)
                    items.Add(new GsDropdownItem($"{DataModel.FriendlyName(type)} 인스턴스가 없습니다 — DB › 인스턴스에서 먼저 만드세요") { Disabled = true });
                GsDropdown.Show(anchor, items, placeholder: "인스턴스 검색");
            });
            pill.tooltip = template != null ? DataModel.Summary(template.value) + "\n고치려면 DB › 인스턴스에서 고칩니다." : $"{DataModel.FriendlyName(type)} 인스턴스 중 하나를 고릅니다.";
            return pill;
        }

        // 글자·참거짓·enum(그리고 컬렉션의 숫자 요소): 키워드가 연결된 글자·enum·참거짓은 목록, 나머지는 입력.
        static VisualElement ValueSlot(SerializedProperty property, Type type, FieldInfo leaf, Action onValue, Action onStructure)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            var current = property.stringValue;
            var dataRef = EditorDatabase.RefTypeOf(leaf);
            if (dataRef != null && type == typeof(string))
            {
                var db = EditorDatabase.Instance;
                var target = string.IsNullOrEmpty(current) ? null : db.FindById(current);
                if (target != null && !EditorDatabase.MatchesRef(leaf, target)) target = null;
                var kind = EditorDatabase.RefKindLabel(leaf);
                return Pill(string.IsNullOrEmpty(current) ? $"{kind} 선택" : target != null ? target.DisplayName : $"없는 {kind}: {current}",
                    string.IsNullOrEmpty(current), !string.IsNullOrEmpty(current) && target == null, anchor =>
                    {
                        var items = db.RefCandidates(leaf)
                            .OrderBy(d => db.GroupLabel(d) ?? "").ThenBy(d => CodexGroups.OrderOf(d)).ThenBy(d => d.Id)
                            .Select(d =>
                            {
                                var pick = d.Id;
                                return new GsDropdownItem(pick, () => Set(property, pick, onStructure)) { Group = db.GroupLabel(d), Selected = pick == current };
                            }).ToList();
                        if (items.Count == 0) items.Add(new GsDropdownItem($"DB › {kind} 탭에 아직 항목이 없습니다") { Disabled = true });
                        GsDropdown.Show(anchor, items, placeholder: $"{kind} 검색");
                    });
            }
            if (type == typeof(bool))
                return Pill(current == "true" ? "참" : current == "false" ? "거짓" : "참/거짓 선택", current != "true" && current != "false", false, anchor =>
                    GsDropdown.Show(anchor, new List<GsDropdownItem>
                    {
                        new GsDropdownItem("참", () => Set(property, "true", onStructure)) { Selected = current == "true" },
                        new GsDropdownItem("거짓", () => Set(property, "false", onStructure)) { Selected = current == "false" },
                    }));
            if (type.IsEnum)
                return Pill(string.IsNullOrEmpty(current) ? "값 선택" : current, string.IsNullOrEmpty(current), !string.IsNullOrEmpty(current) && !Enum.IsDefined(type, current), anchor =>
                    GsDropdown.Show(anchor, Enum.GetNames(type).Select(name => new GsDropdownItem(name, () => Set(property, name, onStructure)) { Selected = current == name }).ToList()));
            return Inline(property, onValue);
        }

        // 딕셔너리: [키] → [값] 줄들 + '+'. 키는 값 규칙(키워드가 연결된 글자·enum은 목록), 값은 숫자(증감, 없던 키는 0에서)·값·인스턴스(있던 키는 덮어쓴다).
        static VisualElement DictionarySlots(SerializedProperty keys, SerializedProperty items, Type dictionary, FieldInfo leaf, Action onValue, Action onStructure)
        {
            var keyType = PlayerDataGrants.KeyTypeOf(dictionary);
            var valueType = FieldPaths.ElementTypeOf(dictionary);
            var kind = PlayerDataGrants.ElementKind(dictionary);
            var col = Gs.Col();
            // 두 목록의 길이를 맞춘다(예전 데이터·코드에서 넣은 것).
            if (items.arraySize < keys.arraySize)
            {
                items.serializedObject.Update();
                while (items.arraySize < keys.arraySize)
                {
                    items.InsertArrayElementAtIndex(items.arraySize);
                    items.GetArrayElementAtIndex(items.arraySize - 1).stringValue = "";
                }
                items.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
            for (int i = 0; i < keys.arraySize; i++)
            {
                var index = i;
                var line = Gs.Row("gs-reward__item");
                line.Add(ValueSlot(keys.GetArrayElementAtIndex(i), keyType, leaf, onValue, onStructure));
                var arrow = Gs.Label("→", "gs-caption");
                arrow.style.marginLeft = arrow.style.marginRight = 6;
                line.Add(arrow);
                var value = items.GetArrayElementAtIndex(i);
                if (kind == GrantKind.Instance) line.Add(InstanceSlot(value, valueType, onStructure));
                else if (kind == GrantKind.Number) line.Add(Labeled("증감", Inline(value, onValue), null));
                else line.Add(ValueSlot(value, valueType, null, onValue, onStructure));
                var remove = new GsButton("✕", () =>
                {
                    keys.serializedObject.Update();
                    keys.DeleteArrayElementAtIndex(index);
                    if (index < items.arraySize) items.DeleteArrayElementAtIndex(index);
                    keys.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                }, GsButtonKind.Ghost);
                remove.tooltip = "이 키 빼기";
                line.Add(remove);
                col.Add(line);
            }
            var add = new GsButton("+", () =>
            {
                keys.serializedObject.Update();
                keys.InsertArrayElementAtIndex(keys.arraySize);
                keys.GetArrayElementAtIndex(keys.arraySize - 1).stringValue = "";
                while (items.arraySize < keys.arraySize)
                {
                    items.InsertArrayElementAtIndex(items.arraySize);
                    items.GetArrayElementAtIndex(items.arraySize - 1).stringValue = "";
                }
                keys.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }, GsButtonKind.Ghost);
            add.tooltip = kind == GrantKind.Number ? "키 더하기(값은 증감, 없던 키는 0에서)" : "키 더하기(있던 키면 값을 덮어쓴다)";
            add.style.alignSelf = Align.FlexStart;
            col.Add(add);
            return col;
        }

        // 컬렉션: 요소 칸들 + '+'(요소 더하기). 요소마다 숫자·값·인스턴스 규칙.
        static VisualElement ListSlots(SerializedProperty items, Type collection, FieldInfo leaf, Action onValue, Action onStructure)
        {
            var element = FieldPaths.ElementTypeOf(collection);
            var kind = PlayerDataGrants.ElementKind(collection);
            var col = Gs.Col();
            for (int i = 0; i < items.arraySize; i++)
            {
                var index = i;
                var slot = items.GetArrayElementAtIndex(i);
                var line = Gs.Row("gs-reward__item");
                line.Add(kind == GrantKind.Instance ? InstanceSlot(slot, element, onStructure) : ValueSlot(slot, element, leaf, onValue, onStructure));
                var remove = new GsButton("✕", () =>
                {
                    items.serializedObject.Update();
                    items.DeleteArrayElementAtIndex(index);
                    items.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                }, GsButtonKind.Ghost);
                remove.tooltip = "이 요소 빼기";
                line.Add(remove);
                col.Add(line);
            }
            var add = new GsButton("+", () =>
            {
                items.serializedObject.Update();
                var at = items.arraySize;
                items.InsertArrayElementAtIndex(at);
                items.GetArrayElementAtIndex(at).stringValue = "";
                items.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }, GsButtonKind.Ghost);
            add.tooltip = $"{DataModel.FriendlyName(element)} 요소 더하기";
            add.style.alignSelf = Align.FlexStart;
            col.Add(add);
            return col;
        }
    }
}
