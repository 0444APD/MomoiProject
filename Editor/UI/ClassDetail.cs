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
    // 클래스 상세(상세 창의 탭): 한 클래스의 필드를 보고 더하고 빼며, 필드마다 연결을 정한다.
    //  · string 필드(하나) · string 배열·리스트 필드(여러 개) → 어떤 키워드 그룹에서 고를지
    //  · 스프라이트 필드 → 어떤 스프라이트 규격 그룹으로 베이크할지, 목록에 보일 미리보기 규격
    //  · 필수 여부
    // 연결(필드 규칙)도, 필드 더하기·빼기·이름 바꾸기(코드 변경)도 도감의 다른 변경과 함께 모아 두었다가 저장(Ctrl+S)할 때 한 번에 쓴다.
    public static class ClassDetail
    {
        // 도감·키워드 탭·값 칸 어디서든: 클래스 상세를 열고 focusField가 있으면 그 필드를 강조한다.
        public static void Open(string fullName, string focusField = null)
        {
            var entry = ClassCatalog.Find(fullName);
            if (entry == null)
            {
                Debug.LogWarning($"[MomoiProject] 클래스 '{fullName}'을(를) 찾을 수 없습니다.");
                return;
            }
            var focus = focusField;
            DetailWindow.OpenCustom("class:" + fullName, entry.Name, "클래스", host =>
            {
                var content = Build(host, fullName, focus);
                // 강조는 처음 열 때 한 번만.
                focus = null;
                return content;
            });
        }

        public static void Open(Type type, string focusField = null)
        {
            if (type != null) Open(type.FullName, focusField);
        }

        internal static VisualElement Build(VisualElement host, string fullName, string focusField)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            var root = scroll.contentContainer;
            root.style.paddingLeft = 20;
            root.style.paddingRight = 20;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 24;

            var entry = ClassCatalog.Find(fullName);
            if (entry == null)
            {
                root.Add(Gs.Label("이 클래스는 더 이상 없습니다.", "gs-caption"));
                return scroll;
            }

            // ── 머리: 이름·종류·그룹·파일 ──
            var head = Gs.Row();
            head.Add(Gs.Label(entry.Name, "gs-title"));
            var kind = new GsBadge(ClassStaging.KindText(entry.Kind.ToString()) + (string.IsNullOrEmpty(entry.Label) || entry.Label == entry.Name ? "" : $" · {entry.Label}"), Gs.PaletteFor("kind:" + entry.Kind));
            kind.style.marginLeft = 10;
            head.Add(kind);
            if (entry.PendingNew) head.Add(Badge("새 클래스 · 적용 전", Gs.Warning));
            if (entry.PendingDelete) head.Add(Badge("삭제 · 적용 전", Gs.Danger));
            if (entry.Locked) head.Add(Badge("잠김", Gs.PaletteFor("locked")));
            root.Add(head);
            var name = Gs.Label(entry.FullName, "gs-caption");
            name.style.marginTop = 2;
            root.Add(name);

            if (entry.Locked)
            {
                var locked = new GsNotice(entry.LockReason + " 필드의 키워드·스프라이트 규격 연결은 바꿀 수 있습니다.", GsNoticeKind.Warning);
                locked.style.marginTop = 10;
                root.Add(locked);
            }

            // 데이터 클래스인데 플레이어 데이터 클래스가 아니면: 쓰는 순간 기록되지 않는다(행동이 끝날 때 비교). 한 번에 바꿀 수 있게.
            if (entry.Kind == ClassKind.Data && !entry.Locked && entry.Type != null && !IsPlayerDataClass(entry))
            {
                var notice = new GsNotice("플레이어 데이터 클래스가 아닙니다. 플레이어 데이터에 넣으면 필드 쓰기가 그 순간이 아니라 행동이 끝날 때 비교로 기록됩니다(더 느림).", GsNoticeKind.Warning);
                notice.style.marginTop = 10;
                var upgrade = new GsButton("플레이어 데이터 클래스로 바꾸기", () => ClassStaging.UpgradeToPlayerData(entry.FullName), GsButtonKind.Primary);
                upgrade.tooltip = "[PlayerData]를 붙이고 필드 구역의 List·배열 필드를 GameList로 바꿉니다. 저장(Ctrl+S)할 때 적용됩니다.";
                notice.Add(upgrade);
                root.Add(notice);
            }

            var tools = Gs.Row("gs-wrap");
            tools.style.marginTop = 12;
            if (!entry.AutoGroup)
            {
                GsButton groupButton = null;
                groupButton = new GsButton("폴더: " + GroupLabel(entry.Group), () => PickGroup(groupButton, entry), GsButtonKind.Default);
                groupButton.tooltip = "Assets/Code 아래 폴더. 바꾸면 저장(Ctrl+S)할 때 스크립트 파일을 그 폴더로 옮깁니다.";
                tools.Add(groupButton);
                tools.Add(Gs.HSpace(6));
            }
            else if (!entry.Locked) tools.Add(Gs.Label("묶음: " + entry.Group + " (자동)", "gs-caption"));
            var file = entry.Type != null ? ClassCatalog.FileOf(entry.FullName) : null;
            if (!string.IsNullOrEmpty(file))
            {
                var ping = new GsButton("스크립트 위치", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(file)), GsButtonKind.Ghost);
                ping.tooltip = file;
                tools.Add(ping);
            }
            if (entry.Kind == ClassKind.Codex && entry.Type != null)
                tools.Add(new GsButton("도감 탭 열기", () => DatabaseWindow.ShowType(entry.Type), GsButtonKind.Ghost));
            tools.Add(Gs.Spacer());
            if (!entry.PendingDelete && !entry.Locked)
                tools.Add(new GsButton("클래스 삭제", () => Delete(host, entry), GsButtonKind.Ghost));
            root.Add(tools);

            // ── 적용 전 코드 변경 ──
            var pending = ClassStaging.For(entry.FullName).ToList();
            if (pending.Count > 0)
            {
                var notice = new GsNotice($"이 클래스의 코드 변경 {pending.Count}건이 저장을 기다립니다. 저장하면 스크립트 파일에 쓰고 Unity가 다시 컴파일합니다.", GsNoticeKind.Warning);
                notice.style.marginTop = 12;
                notice.Add(new GsButton("저장  Ctrl+S", () => CodexChanges.Save(), GsButtonKind.Primary));
                root.Add(notice);
            }

            // ── 필드 ──
            var fieldsHead = Gs.Row();
            fieldsHead.style.marginTop = 20;
            fieldsHead.style.marginBottom = 6;
            fieldsHead.Add(Gs.Label("필드", "gs-heading"));
            fieldsHead.Add(Gs.Spacer());
            if (!entry.Locked && !entry.PendingDelete)
                fieldsHead.Add(new GsButton("+ 필드", () => AddField(host, entry), GsButtonKind.Primary));
            root.Add(fieldsHead);

            var fields = ClassCatalog.Fields(entry);
            if (fields.Count == 0)
                root.Add(Gs.Label(entry.Locked ? "필드가 없습니다." : "아직 필드가 없습니다. '+ 필드'로 더하거나 스크립트에서 직접 선언하세요.", "gs-caption"));
            VisualElement focusRow = null;
            foreach (var field in fields)
            {
                var row = FieldRow(host, entry, field);
                if (focusField != null && field.Name == focusField)
                {
                    row.style.borderLeftWidth = 2;
                    row.style.borderLeftColor = Gs.Accent;
                    focusRow = row;
                }
                root.Add(row);
            }
            if (focusRow != null) focusRow.schedule.Execute(() =>
            {
                if (focusRow.panel != null) scroll.ScrollTo(focusRow);
            }).StartingIn(60);

            if (!entry.Locked && entry.Type != null && fields.Any(f => f.Field != null && f.InheritedFrom == null && !f.Editable))
            {
                var note = Gs.Label("'코드' 표시가 붙은 필드는 스크립트의 필드 구역(<fields>) 밖에 직접 쓴 것입니다. 연결은 여기서 정하고, 이름 바꾸기·삭제는 스크립트에서 합니다.", "gs-caption");
                note.style.marginTop = 10;
                note.style.whiteSpace = WhiteSpace.Normal;
                root.Add(note);
            }
            return scroll;
        }

        // 플레이어 데이터 클래스인지(또는 그렇게 만들어질 것인지): [PlayerData]가 있거나, 새로 만들 데이터 클래스이거나, 바꾸기를 기다리는 중.
        static bool IsPlayerDataClass(ClassEntry entry)
        {
            if (entry.Kind != ClassKind.Data) return false;
            if (entry.Type == null) return true;
            if (entry.Type.IsDefined(typeof(PlayerDataAttribute), true)) return true;
            return ClassStaging.For(entry.FullName).Any(o => o.op == ClassOp.UpgradePlayerData);
        }

        static GsBadge Badge(string text, Color color)
        {
            var badge = new GsBadge(text, color);
            badge.style.marginLeft = 6;
            return badge;
        }

        // ── 필드 한 줄 ─────────────────────────────────────────

        static VisualElement FieldRow(VisualElement host, ClassEntry entry, ClassFieldEntry field)
        {
            var row = Gs.Col("gs-kw__group");
            row.style.marginBottom = 6;
            if (field.PendingRemove) row.style.opacity = 0.45f;

            var top = Gs.Row();
            var names = Gs.Col("gs-grow");
            var title = Gs.Row();
            title.Add(Gs.Label(field.Name, "gs-heading"));
            if (field.PendingAdd) title.Add(Badge("새 필드 · 적용 전", Gs.Warning));
            if (field.PendingRemove) title.Add(Badge("삭제 · 적용 전", Gs.Danger));
            if (field.RenamedFrom != null) title.Add(Badge($"이름 바꿈({field.RenamedFrom}) · 적용 전", Gs.Warning));
            if (field.InheritedFrom != null) title.Add(Badge("상속: " + field.InheritedFrom, Gs.PaletteFor("inherited")));
            else if (field.Field != null && !field.Editable && !entry.Locked) title.Add(Badge("코드", Gs.PaletteFor("code")));
            names.Add(title);
            var label = field.Field?.GetCustomAttribute<FieldLabelAttribute>()?.Text;
            names.Add(Gs.Label(field.TypeLabel + (string.IsNullOrEmpty(label) ? "" : $"  ·  {label}"), "gs-caption"));
            top.Add(names);

            if (field.Editable && !field.PendingRemove && !entry.PendingDelete)
            {
                var rename = new GsButton("이름", () => Rename(host, entry, field), GsButtonKind.Ghost);
                rename.tooltip = "필드 이름 바꾸기(저장된 값은 [FormerlySerializedAs]로 이어집니다)";
                top.Add(rename);
                top.Add(new GsButton("삭제", () => ClassStaging.RemoveField(entry.FullName, field.Name), GsButtonKind.Ghost));
            }
            else if (field.PendingRemove)
            {
                var op = ClassStaging.For(entry.FullName).FirstOrDefault(o => o.op == ClassOp.RemoveField && o.field == field.Name);
                if (op != null) top.Add(new GsButton("삭제 취소", () => ClassStaging.Cancel(op), GsButtonKind.Ghost));
            }
            row.Add(top);

            // 연결: 컴파일된 필드에만(적용 전 필드는 적용 뒤에).
            var info = field.Field;
            if (info == null)
            {
                if (field.PendingAdd) row.Add(Caption("적용한 뒤에 키워드 그룹·스프라이트 규격 그룹을 연결할 수 있습니다."));
                return row;
            }
            if (field.PendingRemove) return row;
            var declaring = info.DeclaringType;

            if (KeywordTools.CanConnect(info))
            {
                var group = KeywordTools.GroupOf(declaring, info);
                var rule = KeywordTools.RuleOf(declaring, info);
                string text;
                if (group == null) text = rule == null || rule.source == FieldRuleSource.Free ? "연결 없음(자유 입력)" : rule.source == FieldRuleSource.Data ? "도감 항목에서 고름" : "플레이어 데이터에서 고름";
                else text = group == "" ? "모든 키워드" : group;
                var many = KeywordTools.IsMulti(info) ? "여러 개 고름" : "하나 고름";
                row.Add(LinkRow("키워드 그룹", text, many, group != null, anchor => PickKeywordGroup(host, anchor, declaring, info, group)));
            }
            else if (KeywordTools.IsKeywordField(info) && EditorDatabase.RefTypeOf(info) is Type reference)
            {
                row.Add(Caption($"코드에서 '{DataTypes.Find(reference)?.DisplayName ?? reference.Name}' 항목을 고르도록 정해진 필드입니다."));
            }

            if (KeywordTools.IsSpriteField(info))
            {
                var rule = KeywordTools.RuleOf(declaring, info);
                var spriteGroup = rule?.spriteGroup ?? "";
                row.Add(LinkRow("스프라이트 규격 그룹", spriteGroup == "" ? "연결 없음" : spriteGroup, "원본을 넣으면 그룹의 모든 규격으로 베이크", spriteGroup != "",
                    anchor => PickSpriteGroup(anchor, declaring, info, spriteGroup)));
                if (spriteGroup != "")
                    row.Add(LinkRow("미리보기 규격", string.IsNullOrEmpty(rule.previewPreset) ? "원본 그대로" : rule.previewPreset, "목록·카드에 보일 규격", !string.IsNullOrEmpty(rule.previewPreset),
                        anchor => PickPreview(anchor, declaring, info, spriteGroup, rule.previewPreset)));
            }

            if (declaring != typeof(GameData))
            {
                var required = EditorDatabase.IsRequired(info);
                var toggle = new Toggle("필수 (비어 있으면 도감 검증 오류)") { value = required };
                toggle.style.marginTop = 6;
                toggle.RegisterValueChangedCallback(e => KeywordTools.SetRequired(declaring, info, e.newValue));
                row.Add(toggle);
            }
            return row;
        }

        static Label Caption(string text)
        {
            var caption = Gs.Label(text, "gs-caption");
            caption.style.marginTop = 6;
            caption.style.whiteSpace = WhiteSpace.Normal;
            return caption;
        }

        // "이름표   [값 ▾]   설명" 한 줄. 값을 누르면 목록이 열린다.
        static VisualElement LinkRow(string label, string value, string hint, bool linked, Action<VisualElement> pick)
        {
            var line = Gs.Row();
            line.style.marginTop = 8;
            var name = Gs.Label(label, "gs-detail__label");
            name.style.width = 130;
            line.Add(name);
            var box = Gs.Box("gs-inline", "gs-inline--editable");
            box.style.minWidth = 160;
            var text = Gs.Label(value, "gs-inline__value");
            if (!linked) text.AddToClassList("gs-inline__empty");
            box.Add(text);
            box.RegisterCallback<ClickEvent>(_ => pick(box));
            line.Add(box);
            var caption = Gs.Label(hint, "gs-caption");
            caption.style.marginLeft = 10;
            line.Add(caption);
            return line;
        }

        // ── 연결 고르기 ────────────────────────────────────────

        static void PickKeywordGroup(VisualElement host, VisualElement anchor, Type type, FieldInfo field, string current)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("연결 없음", () => KeywordTools.Disconnect(type, field)) { Detail = "자유롭게 글자를 입력합니다", Selected = current == null },
                new GsDropdownItem("모든 키워드", () => KeywordTools.Connect(type, field, "")) { Detail = "그룹을 가리지 않고 모든 키워드에서 고릅니다", Selected = current == "" },
            };
            foreach (var group in KeywordTools.Groups())
            {
                var id = group.Id;
                items.Add(new GsDropdownItem(id, () => KeywordTools.Connect(type, field, id))
                {
                    Group = "키워드 그룹",
                    Caption = string.Join(" · ", KeywordTools.KeywordsOf(id).Take(6).Select(k => k.Id)),
                    Selected = current == id,
                });
            }
            items.Add(new GsDropdownItem("+ 새 그룹 만들어 연결", () =>
                KeywordDialogs.NewGroup(host, $"'{field.Name}'에 쓸 새 키워드 그룹", id => KeywordTools.Connect(type, field, id), staged: true)) { Group = "만들기" });
            GsDropdown.Show(anchor, items, placeholder: "폴더 검색");
        }

        static void PickSpriteGroup(VisualElement anchor, Type type, FieldInfo field, string current)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("연결 없음", () => KeywordTools.SetSpriteGroup(type, field, "")) { Selected = current == "" },
            };
            foreach (var group in CodexGroups.Groups(typeof(SpritePresetGroupData)))
            {
                var id = group.Id;
                items.Add(new GsDropdownItem(id, () => KeywordTools.SetSpriteGroup(type, field, id))
                {
                    Group = "스프라이트 규격 그룹",
                    Caption = string.Join(" · ", CodexGroups.Items(typeof(SpritePresetData), id).Cast<SpritePresetData>().Select(p => $"{p.Id} {p.width}×{p.height}")),
                    Selected = current == id,
                });
            }
            if (items.Count == 1) items.Add(new GsDropdownItem("도감 › 스프라이트 규격에서 그룹을 먼저 만드세요") { Disabled = true });
            GsDropdown.Show(anchor, items, placeholder: "폴더 검색");
        }

        static void PickPreview(VisualElement anchor, Type type, FieldInfo field, string group, string current)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("원본 그대로", () => KeywordTools.SetPreviewPreset(type, field, "")) { Selected = string.IsNullOrEmpty(current) },
            };
            foreach (var preset in CodexGroups.Items(typeof(SpritePresetData), group).Cast<SpritePresetData>())
            {
                var id = preset.Id;
                items.Add(new GsDropdownItem(id, () => KeywordTools.SetPreviewPreset(type, field, id)) { Caption = $"{preset.width}×{preset.height}", Selected = current == id });
            }
            GsDropdown.Show(anchor, items, placeholder: "규격 검색");
        }

        // 그룹 표시: "" = Code 폴더 바로 아래, Outside = Code 폴더 밖.
        public static string GroupLabel(string group) =>
            group == ClassFolders.Outside ? "Code 폴더 밖" : string.IsNullOrEmpty(group) ? "Code (최상위)" : group;

        static void PickGroup(VisualElement anchor, ClassEntry entry)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem(GroupLabel(""), () => ClassStaging.MoveClass(entry.FullName, "")) { Caption = ClassFolders.Root, Selected = entry.Group == "" },
            };
            foreach (var group in ClassFolders.Groups())
            {
                var id = group;
                items.Add(new GsDropdownItem(id, () => ClassStaging.MoveClass(entry.FullName, id)) { Caption = ClassFolders.FolderOf(id), Selected = entry.Group == id });
            }
            GsDropdown.Show(anchor, items, placeholder: "폴더 검색");
        }

        // ── 코드 변경 ──────────────────────────────────────────

        static void AddField(VisualElement host, ClassEntry entry)
        {
            var choices = ClassCatalog.TypeChoices(entry.Kind);
            var choice = choices[0];
            var many = 0;

            var box = Gs.Box("gs-dialog");
            box.Add(Gs.Label($"'{entry.Name}'에 필드 추가", "gs-title"));
            var message = Gs.Label("필드는 스크립트의 필드 구역에 선언으로 들어갑니다. 저장(Ctrl+S)할 때 파일에 쓰고 다시 컴파일합니다.", "gs-text-2");
            message.style.marginTop = 8;
            message.style.whiteSpace = WhiteSpace.Normal;
            box.Add(message);

            var input = new TextField();
            input.AddToClassList("gs-field__input");
            var nameField = new GsField("필드 이름", input) { Open = () => input.Focus() };
            nameField.SetHelp("영문 소문자로 시작(예: level, maxCount). 코드의 변수 이름이 됩니다.");
            nameField.style.marginTop = 14;
            box.Add(nameField);

            var typeValue = Gs.Label("", "gs-inline__value");
            var typeBox = Gs.Box("gs-inline", "gs-inline--editable");
            typeBox.Add(typeValue);
            var typeField = new GsField("형식", typeBox);
            typeField.style.marginTop = 14;
            box.Add(typeField);

            // 플레이어 데이터 클래스는 리스트를 GameList(쓰는 순간 기록되는 리스트)로 만든다. 배열은 쓰지 않는다.
            var playerDataClass = IsPlayerDataClass(entry);
            var count = playerDataClass ? new GsSegmented("하나", "리스트") : new GsSegmented("하나", "리스트", "배열");
            count.Select(0);
            count.style.marginTop = 14;
            box.Add(count);

            var buttons = Gs.Box("gs-dialog__buttons");
            box.Add(buttons);
            var modal = GsModal.Show(host, box);
            GsButton confirm = null;

            string Problem()
            {
                var name = input.value?.Trim() ?? "";
                if (!ClassCode.IsValidFieldName(name, out var error)) return error;
                if (ClassCatalog.Fields(entry).Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) || f.RenamedFrom == name)) return $"'{name}' 필드가 이미 있습니다.";
                return null;
            }

            void Refresh(bool showError)
            {
                typeValue.text = choice.Label + (string.IsNullOrEmpty(choice.Caption) ? "" : $"   {choice.Caption}");
                count.style.display = choice.AllowMany ? DisplayStyle.Flex : DisplayStyle.None;
                var problem = Problem();
                nameField.SetError(showError ? problem : null);
                if (!showError || problem == null) nameField.SetHelp("영문 소문자로 시작(예: level, maxCount). 코드의 변수 이름이 됩니다.");
                confirm?.SetEnabled(problem == null);
            }

            typeBox.RegisterCallback<ClickEvent>(_ =>
            {
                var items = choices.Select(c => new GsDropdownItem(c.Label, () =>
                {
                    choice = c;
                    Refresh(!string.IsNullOrEmpty(input.value));
                }) { Group = c.Group, Caption = c.Caption, Selected = c == choice }).ToList();
                GsDropdown.Show(typeBox, items, placeholder: "형식 검색");
            });
            count.Changed += index => many = index;
            input.RegisterValueChangedCallback(_ => Refresh(true));

            void Submit()
            {
                if (Problem() != null) return;
                var name = input.value.Trim();
                var (declaration, typeLabel, usings) = ClassCatalog.Declare(choice, choice.AllowMany ? many : 0, name, playerDataClass);
                modal.Close();
                ClassStaging.AddField(entry.FullName, name, declaration, typeLabel, usings);
            }

            buttons.Add(new GsButton("취소", modal.Close, GsButtonKind.Ghost));
            confirm = new GsButton("추가", Submit, GsButtonKind.Primary);
            buttons.Add(confirm);
            box.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                e.StopPropagation();
                Submit();
            }, TrickleDown.TrickleDown);
            Refresh(false);
            input.schedule.Execute(() => input.Focus()).StartingIn(40);
        }

        static void Rename(VisualElement host, ClassEntry entry, ClassFieldEntry field)
        {
            GsDialog.Form(host, $"'{field.Name}' 이름 바꾸기", "코드의 변수 이름이 바뀝니다. 에셋과 저장 파일의 값은 [FormerlySerializedAs]로 이어지지만, 이 필드를 쓰는 게임 코드는 직접 고쳐야 합니다(컴파일 오류로 알려 줍니다).",
                new[] { new DialogField { Key = "name", Label = "새 이름", Value = field.Name } },
                "바꾸기",
                values =>
                {
                    var name = values["name"].Trim();
                    if (name == field.Name) return "새 이름을 입력하세요.";
                    if (!ClassCode.IsValidFieldName(name, out var error)) return error;
                    return ClassCatalog.Fields(entry).Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ? $"'{name}' 필드가 이미 있습니다." : null;
                },
                values => ClassStaging.RenameField(entry.FullName, field.Name, values["name"].Trim()));
        }

        static void Delete(VisualElement host, ClassEntry entry)
        {
            var blocker = ClassCatalog.DeleteBlocker(entry);
            if (blocker != null)
            {
                GsDialog.Show(host, "삭제할 수 없습니다", blocker, null, new DialogButton("확인", GsButtonKind.Primary, null));
                return;
            }
            var message = entry.PendingNew
                ? $"아직 적용하지 않은 새 클래스 '{entry.Name}'을(를) 만들지 않습니다."
                : $"'{entry.Name}' 클래스의 스크립트 파일을 지웁니다(저장(Ctrl+S)할 때 적용, 파일은 휴지통으로). 이 클래스를 쓰는 게임 코드가 있으면 컴파일 오류가 납니다.";
            GsDialog.Confirm(host, "클래스 삭제", message, "삭제", () =>
            {
                ClassStaging.DeleteClass(entry.FullName);
                if (entry.PendingNew) DetailWindow.CloseCustom("class:" + entry.FullName);
            });
        }

    }
}
