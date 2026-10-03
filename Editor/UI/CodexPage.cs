using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB 탭의 전용 화면에 넘기는 것들.
    public sealed class CodexPageContext
    {
        // 확인 창를 띄울 DB 창 뿌리(gs-root).
        public VisualElement Host;
        // 화면을 그릴 곳(DB 본문 스크롤 안).
        public VisualElement Content;
        // DB 검색 칸의 글자.
        public string Search = "";
        public List<ValidationIssue> Issues = new List<ValidationIssue>();
        // 항목의 상세 화면(작업본)을 연다.
        public Action<GameData> OpenDetail;
        // 전용 화면이 스스로 다시 그리기를 요청한다.
        public Action Refresh;
        // 그룹 하나만 그린다(그룹을 새 창에서 열었을 때). null이면 전체.
        public string Group;
    }

    // 시트·카드 대신 쓰는 DB 탭 화면. [Database(Page = "키")]가 붙은 종류의 탭에서 쓰인다(TypeCache로 찾는다).
    // 이 화면에서의 변경도 EditorDatabase.Staging에 모아 두고, 저장(Ctrl+S)할 때 적용한다.
    public abstract class CodexPage
    {
        public abstract string Key { get; }
        public abstract string Caption(CodexPageContext context);
        public abstract void Build(CodexPageContext context);
        // 머리글 만들기 버튼의 글자(예: "+ 새 그룹")와 동작. 글자가 null이면 버튼을 숨긴다.
        public virtual string CreateLabel => "+ 새로 만들기";
        public abstract void Create(CodexPageContext context);

        // DB 종류에 딸리지 않은 독립 탭(클래스·이벤트): 사이드바에 보일 이름·그룹·순서. 종류의 전용 화면이면 TabLabel이 null.
        public virtual string TabLabel => null;
        public virtual string TabGroup => null;
        public virtual int TabOrder => 0;
        // 사이드바 항목 오른쪽의 수(없으면 null).
        public virtual string TabCount => null;

        static Dictionary<string, CodexPage> _pages;

        public static IEnumerable<CodexPage> All
        {
            get
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
                return _pages.Values;
            }
        }

        public static CodexPage Find(string key)
        {
            if (key == null) return null;
            // DB 종류의 공용 목록(전용 화면이 없는 종류): "data:" + 종류 전체 이름.
            if (key.StartsWith(DataListPage.Prefix, StringComparison.Ordinal))
            {
                var info = DataTypes.All.FirstOrDefault(i => i.Type.FullName == key.Substring(DataListPage.Prefix.Length));
                return info != null ? new DataListPage(info.Type) : null;
            }
            foreach (var page in All)
                if (page.Key == key) return page;
            return null;
        }
    }
}
