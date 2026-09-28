using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 디자인 시스템 진입점: 스타일시트 적용, 분류 팔레트, 요소 생성 도우미.
    public static class Gs
    {
        // 분류용 팔레트: 쨍한 파스텔. OKLCH 명도·채도를 맞추고 색상만 달리했다(포인트 컬러와 구분).
        public static readonly Color[] Palette =
        {
            Hex("#FB9795"), // 코랄
            Hex("#F9A870"), // 탠저린
            Hex("#E8D66B"), // 레몬
            Hex("#A4CD79"), // 라임
            Hex("#71D6A3"), // 민트
            Hex("#44D6D6"), // 틸
            Hex("#67CAFF"), // 스카이
            Hex("#A0BAFF"), // 페리윙클
            Hex("#D0AAFC"), // 라벤더
            Hex("#F59ECF"), // 핑크
        };

        public static readonly string[] PaletteNames = { "코랄", "탠저린", "레몬", "라임", "민트", "틸", "스카이", "페리윙클", "라벤더", "핑크" };

        // 포인트 컬러: 핫핑크 rgb(253, 93, 131)
        public static readonly Color Accent = Hex("#FD5D83");
        public static readonly Color Background = Hex("#0E0F11");
        public static readonly Color Surface = Hex("#16171A");
        public static readonly Color Surface2 = Hex("#1D1E22");
        public static readonly Color Text = Hex("#F2F2F3");
        public static readonly Color Success = Hex("#71D6A3");
        public static readonly Color Danger = Hex("#FB9795");
        public static readonly Color Warning = Hex("#E8D66B");

        static readonly Dictionary<string, StyleSheet> Sheets = new Dictionary<string, StyleSheet>();

        public static void Apply(VisualElement root)
        {
            AddSheet(root, "GsTheme");
            AddSheet(root, "GsExperimental");
            root.AddToClassList("gs-root");
        }

        static void AddSheet(VisualElement root, string name)
        {
            var sheet = LoadSheet(name);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }

        // 라이브러리 폴더 위치와 무관하게 이름으로 찾는다.
        public static StyleSheet LoadSheet(string name)
        {
            if (Sheets.TryGetValue(name, out var cached) && cached != null) return cached;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:StyleSheet"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                Sheets[name] = sheet;
                return sheet;
            }
            return null;
        }

        // 같은 문자열은 항상 같은 색을 받는다(분류·태그·시스템 구분).
        public static Color PaletteFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return Palette[0];
            return Palette[(int)(RandomStreams.Fnv1a64(key) % (ulong)Palette.Length)];
        }

        public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        // 배경 위에 반투명 색을 얹었을 때의 실제 색(그라데이션 가림막 색을 배경과 맞출 때 쓴다).
        public static Color Blend(Color background, Color overlay, float alpha) =>
            new Color(Mathf.Lerp(background.r, overlay.r, alpha), Mathf.Lerp(background.g, overlay.g, alpha), Mathf.Lerp(background.b, overlay.b, alpha), 1f);

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;

        public static Label Label(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }

        public static VisualElement Row(params string[] classes)
        {
            var row = new VisualElement();
            row.AddToClassList("gs-row");
            foreach (var c in classes) row.AddToClassList(c);
            return row;
        }

        public static VisualElement Col(params string[] classes)
        {
            var col = new VisualElement();
            col.AddToClassList("gs-col");
            foreach (var c in classes) col.AddToClassList(c);
            return col;
        }

        public static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement();
            foreach (var c in classes) box.AddToClassList(c);
            return box;
        }

        public static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("gs-spacer");
            return spacer;
        }

        public static VisualElement Space(float height)
        {
            var space = new VisualElement();
            space.style.height = height;
            space.style.flexShrink = 0;
            return space;
        }

        public static VisualElement HSpace(float width)
        {
            var space = new VisualElement();
            space.style.width = width;
            space.style.flexShrink = 0;
            return space;
        }

        // 제목(좌상단) + 부제 + 오른쪽 도구 영역을 가진 패널.
        public static VisualElement Panel(string title, string caption, out VisualElement tools, out VisualElement body)
        {
            var panel = Box("gs-panel");
            var header = Box("gs-panel__header");
            var titles = Col();
            titles.Add(Label(title, "gs-title"));
            if (!string.IsNullOrEmpty(caption)) titles.Add(Label(caption, "gs-caption", "gs-panel__caption"));
            header.Add(titles);
            header.Add(Spacer());
            tools = Row();
            header.Add(tools);
            panel.Add(header);
            body = Col();
            panel.Add(body);
            return panel;
        }
    }
}
