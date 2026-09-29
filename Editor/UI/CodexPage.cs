using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 도감 탭의 전용 화면에 넘기는 것들.
    public sealed class CodexPageContext
    {
        // 대화 상자를 띄울 도감 창 뿌리(gs-root).
        public VisualElement Host;
        // 화면을 그릴 곳(도감 본문 스크롤 안).
        public VisualElement Content;
        // 도감 검색 칸의 글자.
        public string Search = "";
        public List<ValidationIssue> Issues = new List<ValidationIssue>();
        // 항목의 상세 화면(작업본)을 연다.
        public Action<GameData> OpenDetail;
        // 전용 화면이 스스로 다시 그리기를 요청한다.
        public Action Refresh;
        public bool Animate;
    }

    // 시트·카드 대신 쓰는 도감 탭 화면. [Database(Page = "키")]가 붙은 종류의 탭에서 쓰인다(TypeCache로 찾는다).
    // 이 화면에서의 변경도 EditorDatabase.Staging에 모아 두고, 탭을 옮길 때 적용한다.
    public abstract class CodexPage
    {
        public abstract string Key { get; }
        public abstract string Caption(CodexPageContext context);
        public abstract void Build(CodexPageContext context);
        // 머리글 만들기 버튼의 글자(예: "+ 새 그룹")와 동작.
        public virtual string CreateLabel => "+ 새로 만들기";
        public abstract void Create(CodexPageContext context);

        static Dictionary<string, CodexPage> _pages;

        public static CodexPage Find(string key)
        {
            if (_pages == null)
            {
                _pages = new Dictionary<string, CodexPage>(StringComparer.Ordinal);
                foreach (var type in TypeCache.GetTypesDerivedFrom<CodexPage>().Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null))
                {
                    var page = (CodexPage)Activator.CreateInstance(type);
                    _pages[page.Key] = page;
                }
            }
            return key != null && _pages.TryGetValue(key, out var found) ? found : null;
        }
    }
}
