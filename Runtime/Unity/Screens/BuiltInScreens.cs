using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GBible.Data;
using UnityEngine.UIElements;

namespace GBible
{
    // 기본 화면: 코드로 만든 범용 화면(UXML 없음). 모양은 테마 USS의 클래스(momoi-panel · momoi-title · momoi-button …)와 변수로 바꾼다.
    // 글은 텍스트 키(momoi.ui.…)가 있으면 그 번역, 없으면 한국어 기본 글.

    // 기본 화면이 함께 쓰는 도우미.
    public static class ScreenUi
    {
        // 텍스트 키면 번역, 아니면 그대로.
        public static string Text(string keyOrText) => string.IsNullOrEmpty(keyOrText) ? "" : Texts.Has(keyOrText) ? Texts.Get(keyOrText) : keyOrText;

        // 라이브러리 글: 키가 있으면 그 번역({이름} 틀), 없으면 기본 글.
        public static string Text(string key, string fallback, params (string name, object value)[] args) =>
            Texts.Has(key) ? Texts.Get(key, args) : Texts.Catalog.FormatTemplate(fallback, TextArgs.From(args));

        // 가운데 패널(제목이 있으면 맨 위에).
        public static VisualElement Panel(VisualElement root, string title)
        {
            var center = new VisualElement();
            center.AddToClassList("momoi-center");
            root.Add(center);
            var panel = new VisualElement();
            panel.AddToClassList("momoi-panel");
            center.Add(panel);
            if (!string.IsNullOrEmpty(title))
            {
                var label = new Label(title);
                label.AddToClassList("momoi-title");
                panel.Add(label);
            }
            return panel;
        }

        public static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("momoi-row");
            parent.Add(row);
            return row;
        }

        public static Button Button(VisualElement parent, string text, Action click, bool primary = false)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("momoi-button");
            if (primary) button.AddToClassList("momoi-button--primary");
            parent.Add(button);
            return button;
        }

        public static string Duration(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
        }
    }

    // 고르기 창: 지금 기다리는 선택(Game.Choice)을 보이고 답한다. 선택 대기가 생기면 저절로 열리고 없어지면 닫힌다(DB › 화면 설정 '고르기 창 저절로').
    // 하나만 고르면 누르는 즉시 답하고, 여럿이면 고른 뒤 확인. 최소 0이면 건너뛰기.
    public sealed class ChoiceWindow : GameScreen
    {
        readonly List<string> _picked = new List<string>();
        string _choiceId;
        Label _title, _hint;
        VisualElement _options;
        Button _skip, _confirm;

        protected internal override string[] Watch => new[] { ChoiceSystem.Key };
        protected internal override bool WaitForPresentation => false;

        protected internal override void OnOpen()
        {
            var panel = ScreenUi.Panel(Root, null);
            panel.AddToClassList("momoi-choice");
            _title = new Label();
            _title.AddToClassList("momoi-title");
            panel.Add(_title);
            _hint = new Label();
            _hint.AddToClassList("momoi-hint");
            panel.Add(_hint);
            _options = new VisualElement();
            _options.AddToClassList("momoi-options");
            panel.Add(_options);
            var row = ScreenUi.Row(panel);
            _skip = ScreenUi.Button(row, ScreenUi.Text("momoi.ui.skip", "건너뛰기"), () => Answer(new List<string>()));
            _confirm = ScreenUi.Button(row, ScreenUi.Text("momoi.ui.ok", "확인"), () => Answer(_picked.ToList()), primary: true);
            _choiceId = null;
            _picked.Clear();
        }

        protected internal override void Draw()
        {
            var choice = Game?.Choice;
            if (choice == null) return;
            if (choice.id != _choiceId)
            {
                _choiceId = choice.id;
                _picked.Clear();
            }
            var single = choice.max == 1;
            _title.text = string.IsNullOrEmpty(choice.prompt) ? ScreenUi.Text("momoi.ui.choose", "고르기") : ScreenUi.Text(choice.prompt);
            _hint.text = single ? ""
                : choice.min == choice.max ? ScreenUi.Text("momoi.ui.chooseCount", "{count}개를 고르세요", ("count", choice.min))
                : ScreenUi.Text("momoi.ui.chooseRange", "{min}~{max}개를 고르세요", ("min", choice.min), ("max", choice.max));
            _options.Clear();
            foreach (var option in choice.options)
            {
                var id = option.id;
                var button = ScreenUi.Button(_options, ScreenUi.Text(option.label ?? id), () => Pick(id));
                button.AddToClassList("momoi-option");
                // 설명 글: 텍스트 표의 '<id>.desc'(있을 때만).
                if (Texts.Has(id + ".desc"))
                {
                    var description = new Label(Texts.Get(id + ".desc"));
                    description.AddToClassList("momoi-option__desc");
                    button.Add(description);
                }
                if (_picked.Contains(id)) button.AddToClassList("momoi-option--selected");
            }
            _skip.style.display = choice.min == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _confirm.style.display = single ? DisplayStyle.None : DisplayStyle.Flex;
            _confirm.SetEnabled(_picked.Count >= choice.min && _picked.Count <= choice.max);
        }

        void Pick(string id)
        {
            var choice = Game?.Choice;
            if (choice == null) return;
            if (choice.max == 1)
            {
                Answer(new List<string> { id });
                return;
            }
            if (!_picked.Remove(id) && _picked.Count < choice.max) _picked.Add(id);
            Draw();
        }

        void Answer(List<string> picked)
        {
            var choice = Game?.Choice;
            if (choice == null) return;
            try { Game.Answer(choice.id, picked.Cast<object>()); }
            catch (GameFailure failure) { GameScreens.Toast(failure.Message); }
        }
    }

    // 확인 팝업의 내용(GameScreens.Confirm이 만든다).
    public sealed class ConfirmRequest
    {
        public string message;
        public string yesText;
        public string noText;
        public Action yes;
        public Action no;
    }

    // 예/아니오. 뒤로(Esc)·닫기는 아니오.
    public sealed class ConfirmWindow : GameScreen
    {
        ConfirmRequest _request;
        bool _answered;

        protected internal override string[] Watch => new[] { "$momoi.confirm" };   // 플레이어 데이터가 아니다: 열 때와 언어가 바뀔 때만 그린다

        protected internal override void OnOpen()
        {
            _request = Argument as ConfirmRequest ?? new ConfirmRequest();
            _answered = false;
            var panel = ScreenUi.Panel(Root, null);
            panel.AddToClassList("momoi-confirm");
            var message = new Label(ScreenUi.Text(_request.message));
            message.AddToClassList("momoi-message");
            panel.Add(message);
            var row = ScreenUi.Row(panel);
            ScreenUi.Button(row, string.IsNullOrEmpty(_request.noText) ? ScreenUi.Text("momoi.ui.no", "아니오") : ScreenUi.Text(_request.noText), () => Finish(false));
            ScreenUi.Button(row, string.IsNullOrEmpty(_request.yesText) ? ScreenUi.Text("momoi.ui.yes", "예") : ScreenUi.Text(_request.yesText), () => Finish(true), primary: true);
        }

        void Finish(bool yes)
        {
            _answered = true;
            var request = _request;
            Close();
            (yes ? request?.yes : request?.no)?.Invoke();
        }

        protected internal override void OnClose()
        {
            if (_answered) return;
            _answered = true;
            _request?.no?.Invoke();
        }
    }

    // 설정: DB › 설정 항목을 분류마다 묶어 그린다(켜기/끄기 = 토글, 숫자 = 슬라이더, 선택지 = 드롭다운, 글 = 입력칸). 바꾸면 바로 GameSettings.Set.
    public sealed class SettingsWindow : GameScreen
    {
        VisualElement _body;

        protected internal override string[] Watch => new[] { "$momoi.settings" };   // 플레이어 데이터가 아니다: 열 때와 언어가 바뀔 때만 다시 만든다
        protected internal override bool WaitForPresentation => false;

        protected internal override void OnOpen()
        {
            var panel = ScreenUi.Panel(Root, ScreenUi.Text("momoi.ui.settings", "설정"));
            panel.AddToClassList("momoi-settings");
            var scroll = new ScrollView();
            scroll.AddToClassList("momoi-scroll");
            panel.Add(scroll);
            _body = scroll.contentContainer;
            var row = ScreenUi.Row(panel);
            ScreenUi.Button(row, ScreenUi.Text("momoi.ui.resetAll", "기본값으로"), () =>
                GameScreens.Confirm(ScreenUi.Text("momoi.ui.resetAllAsk", "모든 설정을 기본값으로 되돌릴까요?"), () =>
                {
                    GameSettings.ResetAll();
                    Draw();
                }));
            ScreenUi.Button(row, ScreenUi.Text("momoi.ui.close", "닫기"), Close, primary: true);
        }

        protected internal override void Draw()
        {
            if (_body == null) return;
            _body.Clear();
            var settings = GameSettings.All;
            if (settings.Count == 0)
            {
                _body.Add(new Label(ScreenUi.Text("momoi.ui.noSettings", "설정 항목이 없습니다(DB › 설정).")));
                return;
            }
            foreach (var group in settings.GroupBy(s => s.category ?? ""))
            {
                if (group.Key.Length > 0)
                {
                    var header = new Label(ScreenUi.Text(group.Key));
                    header.AddToClassList("momoi-section");
                    _body.Add(header);
                }
                foreach (var setting in group) _body.Add(Control(setting));
            }
        }

        static VisualElement Control(SettingData setting)
        {
            var id = setting.Id;
            var label = GameSettings.Label(id);
            VisualElement control;
            switch (setting.spec.kind)
            {
                case SettingKind.Toggle:
                {
                    var toggle = new Toggle(label) { value = GameSettings.Toggle(id) };
                    toggle.RegisterValueChangedCallback(e => GameSettings.Set(id, e.newValue));
                    control = toggle;
                    break;
                }
                case SettingKind.Slider:
                {
                    var spec = setting.spec;
                    var row = new VisualElement();
                    row.AddToClassList("momoi-row");
                    var slider = new Slider(label, (float)spec.min, (float)Math.Max(spec.max, spec.min)) { value = (float)GameSettings.Number(id) };
                    slider.style.flexGrow = 1;
                    var shown = new Label(Shown(setting, GameSettings.Number(id)));
                    shown.AddToClassList("momoi-value");
                    slider.RegisterValueChangedCallback(e =>
                    {
                        GameSettings.Set(id, (double)e.newValue);
                        shown.text = Shown(setting, GameSettings.Number(id));
                    });
                    row.Add(slider);
                    row.Add(shown);
                    control = row;
                    break;
                }
                case SettingKind.Choice:
                {
                    var choices = GameSettings.ChoicesOf(id);
                    var current = GameSettings.Choice(id);
                    var index = Math.Max(0, choices.ToList().FindIndex(c => c.value == current));
                    var dropdown = new DropdownField(label, choices.Select(c => ScreenUi.Text(c.label)).ToList(), choices.Count == 0 ? -1 : index);
                    dropdown.RegisterValueChangedCallback(_ =>
                    {
                        if (dropdown.index >= 0 && dropdown.index < choices.Count) GameSettings.Set(id, choices[dropdown.index].value);
                    });
                    control = dropdown;
                    break;
                }
                default:
                {
                    var field = new TextField(label) { value = GameSettings.Text(id) };
                    field.RegisterCallback<FocusOutEvent>(_ => GameSettings.Set(id, field.value));
                    control = field;
                    break;
                }
            }
            control.AddToClassList("momoi-setting");
            return control;
        }

        // 0~1 음량은 %, 그 밖에는 숫자.
        static string Shown(SettingData setting, double value) =>
            setting.spec.min == 0 && setting.spec.max == 1 ? $"{Math.Round(value * 100)}%" : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // 슬롯 화면의 내용(GameScreens.Open(GameScreens.Slots, new SlotsRequest { … })). 비우면 슬롯 "1"·"2"·"3".
    public sealed class SlotsRequest
    {
        public int count = 3;
        // 슬롯 이름(비우면 "1" … count). 이름은 폴더 이름이 된다.
        public List<string> names;
        // 고른 뒤 할 일(슬롯은 이미 바뀌어 있다). 비우면 화면만 닫는다.
        public Action<string> picked;
        public bool allowDelete = true;
    }

    // 슬롯: 슬롯마다 요약(GBibleRuntime.SlotSummary)·플레이 시간·저장한 때를 보이고, 고르면 그 슬롯으로 바꾼다. 지우기는 확인을 거친다.
    public sealed class SlotsWindow : GameScreen
    {
        VisualElement _body;
        SlotsRequest _request;

        protected internal override string[] Watch => new[] { "$momoi.slots" };   // 열 때와 언어가 바뀔 때만 다시 만든다
        protected internal override bool WaitForPresentation => false;

        protected internal override void OnOpen()
        {
            _request = Argument as SlotsRequest ?? new SlotsRequest();
            var panel = ScreenUi.Panel(Root, ScreenUi.Text("momoi.ui.slots", "슬롯"));
            panel.AddToClassList("momoi-slots");
            _body = new VisualElement();
            panel.Add(_body);
            ScreenUi.Button(ScreenUi.Row(panel), ScreenUi.Text("momoi.ui.close", "닫기"), Close);
        }

        protected internal override void Draw()
        {
            if (_body == null) return;
            _body.Clear();
            for (int i = 0; i < Math.Max(1, _request.count); i++)
            {
                var name = _request.names != null && i < _request.names.Count ? _request.names[i] : (i + 1).ToString(CultureInfo.InvariantCulture);
                var info = GBibleRuntime.SlotInfoOf(name);
                var row = ScreenUi.Row(_body);
                row.AddToClassList("momoi-slot");
                var text = info.IsEmpty
                    ? ScreenUi.Text("momoi.ui.emptySlot", "{slot} — 빈 슬롯", ("slot", name))
                    : ScreenUi.Text("momoi.ui.slotLine", "{slot} — {summary} ({time})", ("slot", name), ("summary", info.Summary), ("time", ScreenUi.Duration(info.PlayTime)));
                var pick = ScreenUi.Button(row, text, () => Pick(name), primary: name == GBibleRuntime.Slot);
                pick.style.flexGrow = 1;
                if (_request.allowDelete && !info.IsEmpty && name != GBibleRuntime.Slot)
                    ScreenUi.Button(row, ScreenUi.Text("momoi.ui.delete", "지우기"), () =>
                        GameScreens.Confirm(ScreenUi.Text("momoi.ui.deleteSlotAsk", "슬롯 {slot}을(를) 지울까요?", ("slot", name)), () =>
                        {
                            GBibleRuntime.DeleteSlot(name);
                            Draw();
                        }));
            }
        }

        void Pick(string name)
        {
            if (name != GBibleRuntime.Slot) GBibleRuntime.UseSlot(name);
            var picked = _request?.picked;
            Close();
            picked?.Invoke(name);
        }
    }

    // 일시정지: 계속하기 · 설정 · 타이틀로(GameScreens.QuitToTitle이 있을 때) · 게임 종료. 열린 동안 게임 시간이 멈춘다.
    public sealed class PauseWindow : GameScreen
    {
        protected internal override string[] Watch => new[] { "$momoi.pause" };

        protected internal override void OnOpen()
        {
            var panel = ScreenUi.Panel(Root, ScreenUi.Text("momoi.ui.pause", "일시정지"));
            panel.AddToClassList("momoi-pause");
            ScreenUi.Button(panel, ScreenUi.Text("momoi.ui.resume", "계속하기"), Close, primary: true);
            ScreenUi.Button(panel, ScreenUi.Text("momoi.ui.settings", "설정"), () => GameScreens.Open(GameScreens.Settings));
            if (GameScreens.QuitToTitle != null)
                ScreenUi.Button(panel, ScreenUi.Text("momoi.ui.toTitle", "타이틀로"), () =>
                {
                    var toTitle = GameScreens.QuitToTitle;
                    Close();
                    toTitle?.Invoke();
                });
            ScreenUi.Button(panel, ScreenUi.Text("momoi.ui.quit", "게임 종료"), GameScreens.Quit);
        }
    }
}
