using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 내 코드 범위: 클래스를 고르는 목록에 어떤 스크립트가 들어가는지 보고 고친다.
    // 기본 규칙만으로 충분하면 손댈 필요 없다(폴더 지정 불필요).
    public sealed class ProjectCodeWindow : EditorWindow
    {
        VisualElement _root;

        [MenuItem("Game Systems/내 코드 범위", priority = 32)]
        public static void Open()
        {
            var window = GetWindow<ProjectCodeWindow>();
            window.titleContent = new GUIContent("내 코드 범위");
            window.minSize = new Vector2(560, 520);
        }

        void CreateGUI() => Build();

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

            _root.Add(Gs.Label("내 코드 범위", "gs-title"));
            var intro = Gs.Label("클래스를 고르는 목록(플레이어 데이터 값 형식, 템플릿, 필드 규칙, 키워드 연결)에는 이 프로젝트에서 직접 만든 데이터 클래스만 보입니다. " +
                                 "Unity·패키지·이 라이브러리·Plugins 폴더의 스크립트, 컴포넌트·ScriptableObject(도감 종류 제외)·어트리뷰트·정적 유틸 클래스는 자동으로 빠집니다. " +
                                 "프로젝트 구조가 달라 더 거를 것이 있을 때만 아래를 고칩니다.", "gs-text-2");
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginTop = 6;
            intro.style.marginBottom = 14;
            _root.Add(intro);

            var settings = ProjectCode.Current;
            var include = Lines("쓸 폴더(한 줄에 하나, 비우면 Assets 전체)", settings.includeFolders);
            var exclude = Lines("뺄 폴더", settings.excludeFolders);
            var namespaces = Lines("뺄 네임스페이스", settings.excludeNamespaces);
            var save = Gs.Row();
            save.style.marginTop = 8;
            save.Add(new GsButton("저장", () =>
            {
                ProjectCode.Save(new ProjectCode.Settings
                {
                    includeFolders = Split(include.value),
                    excludeFolders = Split(exclude.value),
                    excludeNamespaces = Split(namespaces.value),
                });
                Build();
            }, GsButtonKind.Primary));
            save.Add(Gs.HSpace(6));
            save.Add(new GsButton("기본값으로", () =>
            {
                ProjectCode.Save(new ProjectCode.Settings());
                Build();
            }, GsButtonKind.Ghost));
            _root.Add(save);

            _root.Add(Gs.Space(20));
            _root.Add(Gs.Label("어셈블리", "gs-heading"));
            foreach (var pair in ProjectCode.Assemblies.OrderBy(p => p.Value.included ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                var row = Gs.Row();
                row.style.marginTop = 3;
                row.Add(Gs.Label(pair.Value.included ? "✓" : "–", pair.Value.included ? "gs-accent-text" : "gs-text-3"));
                row.Add(Gs.HSpace(8));
                row.Add(Gs.Label(pair.Key, pair.Value.included ? "gs-body" : "gs-text-3"));
                row.Add(Gs.Spacer());
                row.Add(Gs.Label(pair.Value.reason, "gs-caption"));
                _root.Add(row);
            }

            _root.Add(Gs.Space(20));
            var classes = ClassTypes.All().Where(t => ClassTypes.GroupOf(t) != "이벤트").ToList();
            var enums = StorableTypes.All().Where(t => t.IsEnum).ToList();
            _root.Add(Gs.Label($"목록에 보일 클래스 {classes.Count}개 · enum {enums.Count}개", "gs-heading"));
            foreach (var group in classes.GroupBy(ClassTypes.GroupOf))
            {
                var head = Gs.Label(group.Key, "gs-caption");
                head.style.marginTop = 8;
                _root.Add(head);
                var names = Gs.Label(string.Join(", ", group.Select(t => t.FullName)), "gs-body");
                names.style.whiteSpace = WhiteSpace.Normal;
                _root.Add(names);
            }
            if (enums.Count > 0)
            {
                var head = Gs.Label("enum", "gs-caption");
                head.style.marginTop = 8;
                _root.Add(head);
                var names = Gs.Label(string.Join(", ", enums.Select(t => t.FullName)), "gs-body");
                names.style.whiteSpace = WhiteSpace.Normal;
                _root.Add(names);
            }
        }

        TextField Lines(string label, List<string> values)
        {
            _root.Add(Gs.Label(label, "gs-caption"));
            var field = new TextField { multiline = true, value = string.Join("\n", values) };
            field.style.minHeight = 48;
            field.style.marginBottom = 8;
            _root.Add(field);
            return field;
        }

        static List<string> Split(string text) =>
            (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
    }
}
