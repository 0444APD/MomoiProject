using System.Linq;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 시작 설정 창: 새 프로젝트에서 처음 한 번 저절로 열린다(나중에는 메뉴로). 고르는 것은 Product Name·선택 모듈·Undo 방식·언어뿐이고,
    // 코드 구조(Assets/Code/Rules·Client·Editor·Tests + asmdef)·시작 파일·DB 에셋은 '적용'이 자동으로 만든다.
    public sealed class StartSetupWindow : EditorWindow
    {
        StartSetup.Choices _choices;
        VisualElement _root;

        [MenuItem(StartSetup.MenuPath, priority = 30)]
        public static void Open()
        {
            var window = GsWindows.Get<StartSetupWindow>();
            window.titleContent = new GUIContent("시작 설정");
            window.minSize = new Vector2(520, 560);
        }

        void CreateGUI()
        {
            _choices = StartSetup.Current();
            Build();
        }

        void Build()
        {
            rootVisualElement.Clear();
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _root = Gs.Col();
            Gs.Apply(_root);
            _root.style.paddingLeft = 20;
            _root.style.paddingRight = 20;
            _root.style.paddingTop = 16;
            _root.style.paddingBottom = 20;
            scroll.Add(_root);
            rootVisualElement.Add(scroll);

            _root.Add(Gs.Label("시작 설정", "gs-title"));
            Paragraph(StartSetup.IsDone
                ? "이 프로젝트는 시작 설정을 마쳤습니다. 바꾸고 싶은 것만 고쳐 다시 적용할 수 있습니다(이미 있는 파일은 덮어쓰지 않습니다)."
                : "새 프로젝트에서 한 번 하는 준비입니다. 아래 네 가지만 고르고 '적용'을 누르면 코드 폴더·asmdef·시작 파일·DB가 만들어집니다. 모두 나중에 바꿀 수 있습니다.");

            Section("Product Name", "빌드 파일·창 제목·저장 폴더에 쓰이는 게임 이름(Unity Player Settings). 언제 바꿔도 된다.");
            var product = new TextField { value = _choices.productName };
            product.RegisterValueChangedCallback(e => _choices.productName = e.newValue);
            _root.Add(product);

            Section("선택 모듈", "켜면 그 기능의 코드·DB 탭·메뉴가 생긴다. 나중에 DB 사이드바 '기능 활성화'로도 켜고 끈다(asmdef 참조는 저절로 맞춰진다).");
            foreach (var feature in GameFeatures.All)
            {
                var toggle = new Toggle(feature.Name) { value = _choices.features.Contains(feature.Id) };
                toggle.SetEnabled(feature.Installed);
                toggle.tooltip = feature.Installed ? feature.Description : "이 프로젝트에 모듈 코드가 없습니다.";
                var id = feature.Id;
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _choices.features.Add(id);
                    else _choices.features.Remove(id);
                });
                _root.Add(toggle);
                var caption = Gs.Label(feature.Description, "gs-caption");
                caption.style.whiteSpace = WhiteSpace.Normal;
                caption.style.marginLeft = 18;
                caption.style.marginBottom = 4;
                _root.Add(caption);
            }

            Section("Undo 방식", "턴제는 행동 단위, 실시간은 스냅샷. 나중에 DB › 시작 파일 › Bootstrap 화면에서 보관 수·시드와 함께 바꾼다.");
            var history = new DropdownField(new[] { "행동 단위(턴제)", "스냅샷(실시간)", "쓰지 않음" }.ToList(), (int)_choices.history);
            history.RegisterValueChangedCallback(_ => _choices.history = (HistoryMode)history.index);
            _root.Add(history);

            Section("언어", "원문 언어와 지원할 언어. 나중에 다국어 설정에서 더하고 뺀다.");
            var codes = StartSetup.Languages.Select(l => $"{l.name} ({l.code})").ToList();
            var sourceIndex = System.Array.FindIndex(StartSetup.Languages, l => l.code == _choices.sourceLanguage);
            var source = new DropdownField("원문 언어", codes, sourceIndex < 0 ? 0 : sourceIndex);
            source.RegisterValueChangedCallback(_ =>
            {
                _choices.sourceLanguage = StartSetup.Languages[source.index].code;
                _choices.languages.Add(_choices.sourceLanguage);
                Build();
            });
            _root.Add(source);
            foreach (var (code, name) in StartSetup.Languages)
            {
                var toggle = new Toggle($"{name} ({code})") { value = _choices.languages.Contains(code) };
                toggle.SetEnabled(code != _choices.sourceLanguage);
                var c = code;
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _choices.languages.Add(c);
                    else _choices.languages.Remove(c);
                });
                _root.Add(toggle);
            }

            Section("자동으로 만드는 것", null);
            Paragraph($"코드 폴더와 asmdef: {string.Join(" · ", StartSetup.Parts.Select(p => $"{StartSetup.FolderOf(p)} ({StartSetup.AssemblyOf(p)})"))}. 네임스페이스 {StartSetup.Prefix}.\n" +
                      "시작 파일: Client/Bootstrap.cs(시작 설정 코드), Rules/PlayerData.cs(판·프로필 데이터 뼈대).\n" +
                      "DB 에셋(GameDatabase)과 다국어 설정.");

            var actions = Gs.Row();
            actions.style.marginTop = 16;
            actions.Add(new GsButton("적용", Apply, GsButtonKind.Primary));
            actions.Add(Gs.HSpace(6));
            actions.Add(new GsButton("닫기", Close, GsButtonKind.Ghost));
            _root.Add(actions);
        }

        void Apply()
        {
            var done = StartSetup.Apply(_choices);
            Build();
            Section("적용함", null);
            Paragraph(done.Count == 0 ? "바꿀 것이 없었습니다." : string.Join("\n", done));
            Paragraph("다음: Rules/PlayerData.cs의 RunState·Profile에 필드를 채우고, DB(MomoiProject › DB)에서 플레이어 데이터 'run'·'profile'을 만들어 값 형식으로 고른다. DB 사이드바 맨 위 '시작 파일'에서 Bootstrap·RunState·Profile로 바로 간다. 자세한 순서는 Documentation~/Guide/01-StartProject.md.");
        }

        void Section(string title, string caption)
        {
            _root.Add(Gs.Space(14));
            _root.Add(Gs.Label(title, "gs-heading"));
            if (!string.IsNullOrEmpty(caption)) Paragraph(caption);
        }

        void Paragraph(string text)
        {
            var label = Gs.Label(text, "gs-text-2");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 4;
            label.style.marginBottom = 4;
            _root.Add(label);
        }
    }
}
