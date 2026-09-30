using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UIFont = UnityEngine.Font;
using SdfFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace GameSystems.Dialogue
{
    // 대화 UI(UI Toolkit). UXML의 요소를 '이름'으로 찾아 쓴다: 이름 규격만 지키면 모양·배치·스프라이트는 프로젝트마다 자유롭게 바꾼다.
    // 모양 변화는 루트에 붙는 상태 클래스(USS)로, 키프레임·반복·프레임 애니메이션은 모션 칸(UiMotion 에셋)으로 건다.
    //
    // 상태 클래스(루트): dialogue--open · dialogue--staging(연출 대기) · dialogue--typing · dialogue--waiting(클릭 대기)
    //                    · dialogue--choosing · dialogue--narration(화자 없음) · dialogue--speaker-<캐릭터 ID>
    // 선택지 클래스: dialogue-choice · dialogue-choice--locked · dialogue-choice--picked
    //
    // 프로젝트별 추가:
    //  · 글(소속·직함 등): 캐릭터·대사의 '추가 글'에 키를 적고 UXML에 name="dialogue-extra-<키>" 요소를 둔다(Label이면 글이 들어가고,
    //    비었으면 그 요소에 dialogue-extra--empty, 루트에 dialogue--has-<키> 클래스가 붙는다). 코드 없음.
    //  · 동작하는 UI: IDialogueUIExtension(또는 DialogueUIExtension) 컴포넌트를 이 오브젝트나 자식에 붙인다.
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Game Systems/대화 UI")]
    public sealed class DialogueUI : MonoBehaviour
    {
        // 요소 이름 규격. 바꾼 UXML에서 이름이 다르면 여기서 맞춘다.
        [Serializable]
        public sealed class ElementNames
        {
            [Tooltip("대화 UI 전체(여기를 누르면 다음으로)")] public string root = "dialogue-root";
            public string box = "dialogue-box";
            public string nameBox = "dialogue-name-box";
            public string name = "dialogue-name";
            [Tooltip("대사 글(Label)")] public string text = "dialogue-text";
            [Tooltip("클릭 대기 표시")] public string next = "dialogue-next";
            [Tooltip("선택지를 담는 곳")] public string choices = "dialogue-choices";
            public string letterboxTop = "dialogue-letterbox-top";
            public string letterboxBottom = "dialogue-letterbox-bottom";
            [Tooltip("번쩍임(색 덮개)")] public string flash = "dialogue-flash";
            [Tooltip("검은 화면(배경 전환 경유)")] public string fade = "dialogue-fade";
            public string tooltip = "dialogue-tooltip";
            public string tooltipText = "dialogue-tooltip-text";
            [Tooltip("선택지 템플릿 안에서 글을 넣을 Label")] public string choiceText = "choice-text";
            [Tooltip("대사 로그 창(열면 보인다)")] public string log = "dialogue-log";
            [Tooltip("로그 줄을 담는 ScrollView")] public string logList = "dialogue-log-list";
            public string logButton = "dialogue-log-button";
            public string logClose = "dialogue-log-close";

            public IEnumerable<(string field, string name, bool required)> All()
            {
                yield return (nameof(root), root, true);
                yield return (nameof(box), box, false);
                yield return (nameof(nameBox), nameBox, false);
                yield return (nameof(name), name, true);
                yield return (nameof(text), text, true);
                yield return (nameof(next), next, false);
                yield return (nameof(choices), choices, true);
                yield return (nameof(letterboxTop), letterboxTop, false);
                yield return (nameof(letterboxBottom), letterboxBottom, false);
                yield return (nameof(flash), flash, false);
                yield return (nameof(fade), fade, false);
                yield return (nameof(tooltip), tooltip, false);
                yield return (nameof(tooltipText), tooltipText, false);
                yield return (nameof(log), log, false);
                yield return (nameof(logList), logList, false);
                yield return (nameof(logButton), logButton, false);
                yield return (nameof(logClose), logClose, false);
            }
        }

        // 모션을 거는 순간.
        public enum Moment
        {
            [InspectorName("열림")] Open,
            [InspectorName("닫힘")] Close,
            [InspectorName("줄 시작(글자 나오기 시작)")] LineStart,
            [InspectorName("화자 바뀜")] SpeakerChanged,
            [InspectorName("글 다 나옴(클릭 대기)")] Waiting,
            [InspectorName("선택지 나타남")] ChoicesShown,
            [InspectorName("선택지 고름")] ChoicePicked,
        }

        [Serializable]
        public sealed class MotionSlot
        {
            public Moment moment;
            [Tooltip("요소 이름(비우면 대화 상자). 선택지 순간에는 선택지 각각에 건다.")]
            public string element = "";
            public UiMotion motion;
        }

        public const string ExtraPrefix = "dialogue-extra-";

        public ElementNames names = new ElementNames();
        [Tooltip("선택지 하나의 모양(UXML). 비우면 버튼을 만든다.")]
        public VisualTreeAsset choiceTemplate;
        [Tooltip("로그 한 줄의 모양(UXML: log-portrait · log-name · log-text). 비우면 기본 모양을 만든다.")]
        public VisualTreeAsset logEntryTemplate;
        [Tooltip("글상자에서 휠을 위로 굴리면 로그를 연다")]
        public bool wheelOpensLog = true;
        public List<MotionSlot> motions = new List<MotionSlot>();
        [Tooltip("한글 등 글자가 있는 폰트(TTF). SDF 폰트 에셋을 쓰려면 아래 칸.")]
        public UIFont font;
        public SdfFontAsset fontAsset;
        [Tooltip("선택지를 고른 뒤 다음으로 넘어가기까지 기다리는 시간(고름 모션을 보여 줄 틈)")]
        public float choicePickDelay = 0.15f;
        [Tooltip("닫힌 뒤 입력에서 빠지기까지의 시간(USS 사라지기 전환 길이)")]
        public float closeHideDelay = 0.3f;

        public event Action AdvanceRequested;
        public event Action<int> ChoiceRequested;
        public event Action LogOpened;
        public event Action LogClosed;

        // 로그 내용(대화 재생기가 실행기의 로그로 채운다).
        public Func<IReadOnlyList<DialogueLogEntry>> LogSource = () => Array.Empty<DialogueLogEntry>();
        public bool IsLogOpen { get; private set; }

        readonly UiMotionRunner _motions = new UiMotionRunner();
        readonly List<VisualElement> _choiceElements = new List<VisualElement>();
        readonly List<(string key, VisualElement element)> _extras = new List<(string, VisualElement)>();
        IDialogueUIExtension[] _extensions = Array.Empty<IDialogueUIExtension>();
        VisualElement _root;
        VisualElement _box;
        VisualElement _nameBox;
        Label _name;
        Label _text;
        VisualElement _next;
        VisualElement _choices;
        VisualElement _letterboxTop;
        VisualElement _letterboxBottom;
        VisualElement _flash;
        VisualElement _fade;
        VisualElement _tooltip;
        Label _tooltipText;
        VisualElement _log;
        ScrollView _logList;
        string _fullText = "";
        string _speaker;
        bool _open;
        IReadOnlyList<DialogueChoice> _choiceData = Array.Empty<DialogueChoice>();

        public VisualElement Root => _root;
        public bool IsOpen => _open;
        public UiMotionRunner Motions => _motions;

        VisualElement _boundTree;
        List<string> _missing = new List<string>();

        void OnEnable() => Bind();

        // UIDocument가 트리를 다시 만들었으면(켜고 끄기 등) 다시 찾는다.
        bool Ready()
        {
            var tree = GetComponent<UIDocument>()?.rootVisualElement;
            if (tree != null && tree != _boundTree) Bind();
            return _root != null;
        }

        void Update() => _motions.Update(Time.unscaledDeltaTime);

        // UXML에서 요소를 찾는다. 빠진 필수 요소 이름을 돌려준다.
        public List<string> Bind()
        {
            var missing = new List<string>();
            var document = GetComponent<UIDocument>();
            var tree = document != null ? document.rootVisualElement : null;
            if (tree == null)
            {
                missing.Add("(UIDocument 없음)");
                return missing;
            }
            if (tree == _boundTree) return new List<string>(_missing);
            _boundTree = tree;
            T Find<T>(string elementName, bool required) where T : VisualElement
            {
                var found = string.IsNullOrEmpty(elementName) ? null : tree.Q<T>(elementName);
                if (found == null && required) missing.Add(elementName);
                return found;
            }

            _root = Find<VisualElement>(names.root, true) ?? tree;
            _box = Find<VisualElement>(names.box, false);
            _nameBox = Find<VisualElement>(names.nameBox, false);
            _name = Find<Label>(names.name, true);
            _text = Find<Label>(names.text, true);
            _next = Find<VisualElement>(names.next, false);
            _choices = Find<VisualElement>(names.choices, true);
            _letterboxTop = Find<VisualElement>(names.letterboxTop, false);
            _letterboxBottom = Find<VisualElement>(names.letterboxBottom, false);
            _flash = Find<VisualElement>(names.flash, false);
            _fade = Find<VisualElement>(names.fade, false);
            _tooltip = Find<VisualElement>(names.tooltip, false);
            _tooltipText = Find<Label>(names.tooltipText, false);
            _log = Find<VisualElement>(names.log, false);
            _logList = Find<ScrollView>(names.logList, false);
            var logButton = Find<VisualElement>(names.logButton, false);
            var logClose = Find<VisualElement>(names.logClose, false);
            logButton?.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                OpenLog();
            });
            logClose?.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                CloseLog();
            });
            _log?.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            if (_log != null) _log.style.display = DisplayStyle.None;
            IsLogOpen = false;
            _extras.Clear();            tree.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name) && element.name.StartsWith(ExtraPrefix, StringComparison.Ordinal))
                    _extras.Add((element.name.Substring(ExtraPrefix.Length), element));
            });
            _extensions = GetComponentsInChildren<IDialogueUIExtension>(true);

            if (font != null) tree.style.unityFontDefinition = FontDefinition.FromFont(font);
            else if (fontAsset != null) tree.style.unityFontDefinition = FontDefinition.FromSDFFont(fontAsset);

            _root.focusable = true;
            _root.RegisterCallback<ClickEvent>(OnClick);
            _root.RegisterCallback<KeyDownEvent>(OnKey);
            _root.RegisterCallback<WheelEvent>(e =>
            {
                if (wheelOpensLog && !IsLogOpen && _log != null && e.delta.y < 0)
                {
                    OpenLog();
                    e.StopPropagation();
                }
            });
            if (_text != null)
            {
                _text.enableRichText = true;
                _text.RegisterCallback<UnityEngine.UIElements.Experimental.PointerOverLinkTagEvent>(e => ShowTooltip(e.linkID, e.position));
                _text.RegisterCallback<UnityEngine.UIElements.Experimental.PointerOutLinkTagEvent>(_ => HideTooltip());
            }
            HideTooltip();
            SetOpen(false, instant: true);
            _missing = missing;
            if (missing.Count > 0) Debug.LogWarning("[대화 UI] UXML에 필요한 요소가 없습니다: " + string.Join(", ", missing), this);
            return new List<string>(missing);
        }

        // ── 열고 닫기 ────────────────────────────────────────

        public void SetOpen(bool open, bool instant = false)
        {
            if (!Ready()) return;
            if (_root == null) return;
            if (open == _open && !instant) return;
            _open = open;
            _root.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
            if (open)
            {
                // 보이게 한 다음 프레임에 클래스를 붙여야 USS 전환(나타나기)이 재생된다.
                _root.style.display = DisplayStyle.Flex;
                if (instant) _root.AddToClassList("dialogue--open");
                else _root.schedule.Execute(() => _root.EnableInClassList("dialogue--open", _open));
            }
            else
            {
                _root.RemoveFromClassList("dialogue--open");
                // 닫힌 UI가 게임 클릭을 막지 않게 사라지는 전환이 끝나면 숨긴다.
                if (instant) _root.style.display = DisplayStyle.None;
                else _root.schedule.Execute(() =>
                {
                    if (!_open) _root.style.display = DisplayStyle.None;
                }).StartingIn((long)(closeHideDelay * 1000));
            }
            if (!open)
            {
                if (IsLogOpen) CloseLog();
                ClearStateClasses();
                _choiceData = Array.Empty<DialogueChoice>();
                ClearChoices();
                HideTooltip();
            }
            if (!instant) PlayMoment(open ? Moment.Open : Moment.Close);
            foreach (var extension in _extensions)
                if (open) extension.OnOpened(this);
                else extension.OnClosed(this);
            if (open) _root.Focus();
        }

        void ClearStateClasses()
        {
            foreach (var cls in _root.GetClasses().Where(c => c.StartsWith("dialogue--", StringComparison.Ordinal) && c != "dialogue--open").ToList())
                _root.RemoveFromClassList(cls);
        }

        void SetState(string state)
        {
            foreach (var cls in new[] { "dialogue--staging", "dialogue--typing", "dialogue--waiting", "dialogue--choosing" })
                _root.EnableInClassList(cls, cls == state);
        }

        // ── 대사 ─────────────────────────────────────────────

        // 연출 대기(대사 시작 신호 전). 글상자는 비운다.
        public void ShowStaging()
        {
            if (!Ready()) return;
            if (_root == null) return;
            SetOpen(true);
            SetState("dialogue--staging");
        }

        public void ShowLine(DialogueLineInfo info)
        {
            if (!Ready()) return;
            var speaker = info.Speaker;
            var speakerName = info.SpeakerName;
            var text = info.Text;
            var nameColor = info.NameColor;
            if (_root == null) return;
            SetOpen(true);
            var changed = speaker != _speaker;
            if (!string.IsNullOrEmpty(_speaker)) _root.RemoveFromClassList("dialogue--speaker-" + _speaker);
            _speaker = speaker;
            // 해설 = 보일 이름이 없음(프로필 없이 '다른 이름'만 있는 화자도 이름표가 보인다).
            var narration = string.IsNullOrEmpty(speakerName);
            _root.EnableInClassList("dialogue--narration", narration);
            _root.EnableInClassList("dialogue--thought", info.Thought);
            if (!string.IsNullOrEmpty(speaker)) _root.AddToClassList("dialogue--speaker-" + speaker);
            if (_name != null)
            {
                _name.text = speakerName ?? "";
                _name.style.color = narration ? StyleKeyword.Null : new StyleColor(nameColor);
            }
            if (_nameBox != null) _nameBox.style.display = narration ? DisplayStyle.None : DisplayStyle.Flex;
            _fullText = text ?? "";
            if (_text != null) _text.text = TextReveal.Reveal(_fullText, 0);
            SetState("dialogue--typing");
            if (_next != null) _motions.Stop(_next);
            StopLoopingMotions();
            foreach (var (key, element) in _extras)
            {
                var value = info.Extras != null && info.Extras.TryGetValue(key, out var found) ? found : "";
                if (element is Label label) label.text = value;
                var empty = string.IsNullOrEmpty(value);
                element.EnableInClassList("dialogue-extra--empty", empty);
                _root.EnableInClassList("dialogue--has-" + key, !empty);
            }
            if (changed) PlayMoment(Moment.SpeakerChanged);
            PlayMoment(Moment.LineStart);
            foreach (var extension in _extensions) extension.OnLine(this, info);
        }

        public void SetVisibleCharacters(int count, int total)
        {
            if (!Ready()) return;
            if (_text == null) return;
            _text.text = TextReveal.Reveal(_fullText, count);
            if (count >= total && _root.ClassListContains("dialogue--typing"))
            {
                SetState("dialogue--waiting");
                PlayMoment(Moment.Waiting);
            }
        }

        // ── 선택지 ───────────────────────────────────────────

        public void ShowChoices(IReadOnlyList<DialogueChoice> choices)
        {
            if (!Ready()) return;
            if (_choices == null) return;
            ClearChoices();
            _choiceData = choices ?? Array.Empty<DialogueChoice>();
            if (_choiceData.Count == 0) return;
            SetState("dialogue--choosing");
            for (var i = 0; i < _choiceData.Count; i++)
            {
                var choice = _choiceData[i];
                var element = MakeChoice(choice.Text);
                element.AddToClassList("dialogue-choice");
                element.EnableInClassList("dialogue-choice--locked", !choice.Enabled);
                element.SetEnabled(choice.Enabled);
                var index = i;
                element.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    Pick(index);
                });
                _choices.Add(element);
                _choiceElements.Add(element);
            }
            PlayMoment(Moment.ChoicesShown);
            foreach (var extension in _extensions) extension.OnChoices(this, _choiceData);
        }

        VisualElement MakeChoice(string text)
        {
            if (choiceTemplate == null) return new Button { text = text };
            var instance = choiceTemplate.Instantiate();
            var label = instance.Q<Label>(names.choiceText) ?? instance.Q<Label>();
            if (label != null)
            {
                label.enableRichText = true;
                label.text = text;
            }
            else if (instance.Q<Button>() is Button button) button.text = text;
            // TemplateContainer가 크기를 먹지 않게 안쪽 첫 요소를 그대로 쓴다.
            return instance.childCount == 1 ? instance[0] : instance;
        }

        public void Pick(int index)
        {
            if (index < 0 || index >= _choiceData.Count || !_choiceData[index].Enabled) return;
            var element = index < _choiceElements.Count ? _choiceElements[index] : null;
            element?.AddToClassList("dialogue-choice--picked");
            PlayMoment(Moment.ChoicePicked, element);
            if (choicePickDelay > 0 && element != null)
                element.schedule.Execute(() => ChoiceRequested?.Invoke(index)).StartingIn((long)(choicePickDelay * 1000));
            else ChoiceRequested?.Invoke(index);
        }

        void ClearChoices()
        {
            foreach (var element in _choiceElements)
            {
                _motions.Stop(element);
                element.RemoveFromHierarchy();
            }
            _choiceElements.Clear();
            _choices?.Clear();
        }

        // ── 화면 덮개(레터박스·번쩍임·검은 화면) ─────────────

        public void SetLetterbox(float thickness)
        {
            if (!Ready()) return;
            var percent = new Length(Mathf.Clamp01(thickness) * 100f, LengthUnit.Percent);
            if (_letterboxTop != null) _letterboxTop.style.height = percent;
            if (_letterboxBottom != null) _letterboxBottom.style.height = percent;
        }

        public void SetFlash(Color color, float amount)
        {
            if (!Ready()) return;
            if (_flash == null) return;
            _flash.style.backgroundColor = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(amount));
        }

        public void SetFade(float amount)
        {
            if (!Ready()) return;
            if (_fade == null) return;
            _fade.style.opacity = Mathf.Clamp01(amount);
        }

        public VisualElement Element(string elementName) =>
            string.IsNullOrEmpty(elementName) ? _box ?? _root : GetComponent<UIDocument>()?.rootVisualElement?.Q(elementName);

        // ── 입력 ─────────────────────────────────────────────

        void OnClick(ClickEvent e)
        {
            if (!_open || IsLogOpen) return;
            if (_root.ClassListContains("dialogue--choosing")) return;
            AdvanceRequested?.Invoke();
        }

        void OnKey(KeyDownEvent e)
        {
            if (!_open) return;
            if (IsLogOpen)
            {
                if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space) CloseLog();
                e.StopPropagation();
                return;
            }
            if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (!_root.ClassListContains("dialogue--choosing")) AdvanceRequested?.Invoke();
                e.StopPropagation();
            }
            else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
            {
                Pick(e.keyCode - KeyCode.Alpha1);
                e.StopPropagation();
            }
        }

        // ── 대사 로그 ────────────────────────────────────────
        // 위에서 아래로 읽는 목록. 줄마다 말하는 이 · 얼굴(정사각형) · 대사, 내가 고른 선택지는 오른쪽 정렬.
        // 줄 클래스: dialogue-log__entry · --choice · --narration · --thought · --no-portrait

        public void OpenLog()
        {
            if (!Ready() || _log == null || _logList == null || IsLogOpen) return;
            IsLogOpen = true;
            _logList.Clear();
            foreach (var entry in LogSource?.Invoke() ?? Array.Empty<DialogueLogEntry>()) _logList.Add(LogRow(entry));
            _log.style.display = DisplayStyle.Flex;
            _root.AddToClassList("dialogue--log-open");
            // 맨 아래(가장 최근)부터 보인다.
            _logList.schedule.Execute(() => _logList.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(16);
            LogOpened?.Invoke();
        }

        public void CloseLog()
        {
            if (!IsLogOpen) return;
            IsLogOpen = false;
            if (_log != null) _log.style.display = DisplayStyle.None;
            _root.RemoveFromClassList("dialogue--log-open");
            _root.Focus();
            LogClosed?.Invoke();
        }

        VisualElement LogRow(DialogueLogEntry entry)
        {
            VisualElement row;
            VisualElement portrait;
            Label nameLabel;
            Label textLabel;
            if (logEntryTemplate != null)
            {
                var instance = logEntryTemplate.Instantiate();
                row = instance.childCount == 1 ? instance[0] : instance;
                portrait = row.Q("log-portrait");
                nameLabel = row.Q<Label>("log-name");
                textLabel = row.Q<Label>("log-text");
            }
            else
            {
                row = new VisualElement();
                portrait = new VisualElement();
                portrait.AddToClassList("dialogue-log__portrait");
                var body = new VisualElement();
                body.AddToClassList("dialogue-log__body");
                nameLabel = new Label();
                nameLabel.AddToClassList("dialogue-log__name");
                textLabel = new Label();
                textLabel.AddToClassList("dialogue-log__text");
                body.Add(nameLabel);
                body.Add(textLabel);
                row.Add(portrait);
                row.Add(body);
            }
            row.AddToClassList("dialogue-log__entry");
            row.EnableInClassList("dialogue-log__entry--choice", entry.Choice);
            row.EnableInClassList("dialogue-log__entry--narration", !entry.Choice && string.IsNullOrEmpty(entry.SpeakerName));
            row.EnableInClassList("dialogue-log__entry--thought", entry.Thought);
            row.EnableInClassList("dialogue-log__entry--no-portrait", entry.Portrait == null);
            if (portrait != null)
            {
                portrait.style.backgroundImage = entry.Portrait != null ? new StyleBackground(entry.Portrait) : StyleKeyword.Null;
                portrait.style.display = entry.Portrait != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (nameLabel != null)
            {
                nameLabel.text = entry.SpeakerName ?? "";
                nameLabel.style.display = string.IsNullOrEmpty(entry.SpeakerName) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (textLabel != null)
            {
                textLabel.enableRichText = true;
                textLabel.text = entry.Text ?? "";
            }
            return row;
        }

        // ── 용어 툴팁 ────────────────────────────────────────

        void ShowTooltip(string linkId, Vector3 position)
        {
            var term = GameLanguage.TermOfLink(linkId);
            if (_tooltip == null || term == null) return;
            var key = term + ".desc";
            if (!Texts.Has(key)) return;
            if (_tooltipText != null) _tooltipText.text = Texts.Get(key);
            _tooltip.style.display = DisplayStyle.Flex;
            var local = _tooltip.parent?.WorldToLocal(position) ?? (Vector2)position;
            _tooltip.style.left = local.x + 12;
            _tooltip.style.top = local.y - 12;
        }

        void HideTooltip()
        {
            if (_tooltip != null) _tooltip.style.display = DisplayStyle.None;
        }

        // ── 모션 ─────────────────────────────────────────────

        void PlayMoment(Moment moment, VisualElement choice = null)
        {
            foreach (var slot in motions)
            {
                if (slot.moment != moment || slot.motion == null) continue;
                if (moment == Moment.ChoicesShown && string.IsNullOrEmpty(slot.element))
                {
                    var i = 0;
                    foreach (var element in _choiceElements)
                    {
                        var target = element;
                        // 차례로 나타나게 조금씩 늦춘다.
                        target.schedule.Execute(() => _motions.Play(target, slot.motion)).StartingIn(i++ * 50L);
                    }
                    continue;
                }
                var targetElement = moment == Moment.ChoicePicked && string.IsNullOrEmpty(slot.element) ? choice : Element(slot.element);
                _motions.Play(targetElement, slot.motion);
            }
        }

        void StopLoopingMotions()
        {
            foreach (var slot in motions)
            {
                if (slot.motion == null || !slot.motion.IsLooping) continue;
                var element = Element(slot.element);
                if (element != null && _motions.IsPlaying(element)) _motions.Stop(element);
            }
        }
    }

    // 타이핑 효과: 글 전체를 두고 아직 안 나온 부분만 투명하게 한다(줄바꿈 위치가 타이핑 중에 흔들리지 않는다).
    public static class TextReveal
    {
        public static string Reveal(string text, int visible)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var total = DialogueRunner.VisibleLength(text);
            if (visible >= total) return text;
            var b = new StringBuilder(text.Length + 16);
            var count = 0;
            var i = 0;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '<')
                {
                    var close = text.IndexOf('>', i);
                    if (close < 0) break;
                    b.Append(text, i, close - i + 1);
                    i = close;
                    continue;
                }
                if (count >= visible) break;
                b.Append(c);
                count++;
            }
            b.Append("<alpha=#00>");
            // 숨긴 부분의 색 태그는 알파를 되살리므로 뺀다(크기·굵기 태그는 줄바꿈 위치를 위해 남긴다).
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '<')
                {
                    var close = text.IndexOf('>', i);
                    if (close < 0)
                    {
                        b.Append(text, i, text.Length - i);
                        break;
                    }
                    var tag = text.Substring(i, close - i + 1);
                    var lower = tag.ToLowerInvariant();
                    if (!(lower.StartsWith("<color") || lower.StartsWith("</color") || lower.StartsWith("<alpha") || lower.StartsWith("<mark") || lower.StartsWith("</mark") || lower.StartsWith("<#")))
                        b.Append(tag);
                    i = close;
                    continue;
                }
                b.Append(c);
            }
            return b.ToString();
        }
    }
}
