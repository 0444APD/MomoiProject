using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 도감 그룹 하나를 따로 띄운 창(그룹 제목의 ↗). 도감 탭과 같은 화면을 그 그룹만 그린다.
    // 바꾼 것은 도감과 같은 저장 흐름(Ctrl+S)을 탄다. 여러 개를 띄울 수 있다.
    public sealed class CodexGroupWindow : EditorWindow
    {
        [SerializeField] string _page;
        [SerializeField] string _group;

        bool _queued;

        public static void Open(string page, string group, string title)
        {
            var window = GsWindows.Open<CodexGroupWindow>(title, new Vector2(560, 680));
            window._page = page;
            window._group = group ?? "";
            window.Rebuild();
        }

        void OnEnable()
        {
            EditorDatabase.Changed += Queue;
            ClassStaging.Changed += Queue;
            ClassRegistry.Changed += Queue;
            CodexFolders.Changed += Queue;
            EditorApplication.projectChanged += Queue;
        }

        void OnDisable()
        {
            EditorDatabase.Changed -= Queue;
            ClassStaging.Changed -= Queue;
            ClassRegistry.Changed -= Queue;
            CodexFolders.Changed -= Queue;
            EditorApplication.projectChanged -= Queue;
        }

        void CreateGUI() => Rebuild();

        void Queue()
        {
            if (_queued) return;
            _queued = true;
            rootVisualElement.schedule.Execute(Run);

            void Run()
            {
                // 에셋을 가져오는 중에는 끝날 때까지 미룬다(도감 창과 같은 규칙).
                if (EditorApplication.isUpdating)
                {
                    rootVisualElement.schedule.Execute(Run).StartingIn(100);
                    return;
                }
                _queued = false;
                Rebuild();
            }
        }

        void Rebuild()
        {
            rootVisualElement.Clear();
            var root = Gs.Col();
            Gs.Apply(root);
            root.style.flexGrow = 1;
            root.style.paddingLeft = root.style.paddingRight = 20;
            root.style.paddingTop = 16;
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.S || !e.actionKey || e.shiftKey || e.altKey) return;
                e.StopPropagation();
                CodexChanges.Save();
            }, TrickleDown.TrickleDown);
            rootVisualElement.Add(root);

            var page = string.IsNullOrEmpty(_page) ? null : CodexPage.Find(_page);
            if (page == null)
            {
                root.Add(Gs.Label("이 창의 화면을 찾을 수 없습니다. 닫고 도감에서 다시 여세요.", "gs-caption"));
                return;
            }
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            page.Build(new CodexPageContext
            {
                Host = root,
                Content = scroll.contentContainer,
                Search = "",
                Issues = DataValidator.ValidateAll(),
                OpenDetail = DatabaseWindow.ShowItem,
                Refresh = Queue,
                Group = _group,
            });
        }
    }
}
