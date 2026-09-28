using System.Collections.Generic;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 살아 있는 스타일 가이드: 디자인 토큰, 분류 팔레트, 카드 A·B·C, 시트, 부품, 모션, 필터 실험.
    public sealed class ShowcaseWindow : EditorWindow
    {
        readonly List<ShowcaseItem> _items = new List<ShowcaseItem>();
        readonly List<Texture2D> _textures = new List<Texture2D>();

        [MenuItem("Game Systems/UI 샘플", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<ShowcaseWindow>();
            window.titleContent = new GUIContent("UI 샘플");
            window.minSize = new Vector2(900, 600);
        }

        void OnDisable()
        {
            foreach (var item in _items) if (item != null) DestroyImmediate(item);
            foreach (var texture in _textures) if (texture != null) DestroyImmediate(texture);
            _items.Clear();
            _textures.Clear();
        }

        void CreateGUI() => Build(rootVisualElement);

        internal void Build(VisualElement host)
        {
            host.Clear();
            var root = Gs.Col();
            Gs.Apply(root);
            host.Add(root);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            var page = scroll.contentContainer;
            page.style.paddingLeft = 40;
            page.style.paddingRight = 40;
            page.style.paddingTop = 36;
            page.style.paddingBottom = 60;

            page.Add(Gs.Label("UI 샘플", "gs-display"));
            page.Add(Gs.Space(4));
            page.Add(Gs.Label("Game Systems 디자인 시스템의 모든 부품. 새 도구는 이 부품들을 조립해서 만든다.", "gs-caption"));
            page.Add(Gs.Space(28));

            EnsureSamples();
            var sections = new List<VisualElement>
            {
                Tokens(),
                Cards(),
                Sheet(),
                Components(),
                Motion(),
                Filters(),
            };
            foreach (var section in sections)
            {
                section.style.marginBottom = 24;
                page.Add(section);
            }
            GsMotion.Appear(sections);
        }

        void EnsureSamples()
        {
            if (_items.Count > 0) return;
            string[] names = { "새벽의 검", "바람 망토", "황혼 반지", "심연의 방패", "불씨 부적" };
            for (int i = 0; i < names.Length; i++)
            {
                var item = CreateInstance<ShowcaseItem>();
                item.hideFlags = HideFlags.DontSave;
                item.displayName = names[i];
                item.rarity = (ShowcaseItem.Rarity)(i % 4);
                item.price = 120 * (i + 1);
                item.weight = 0.5f + i * 0.7f;
                item.owned = i % 2 == 0;
                item.description = i % 2 == 0 ? "오래된 전설이 깃든 물건" : "";
                _items.Add(item);
                _textures.Add(MakeTexture(Gs.Palette[(i * 2) % Gs.Palette.Length], Gs.Palette[(i * 2 + 5) % Gs.Palette.Length]));
            }
        }

        // 샘플 이미지: 두 색 대각 그라데이션 + 옅은 원.
        static Texture2D MakeTexture(Color a, Color b)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var t = (x + y) / (2f * size);
                    var c = Color.Lerp(a * 0.55f, b * 0.9f, t);
                    var dx = x - size * 0.62f;
                    var dy = y - size * 0.58f;
                    if (dx * dx + dy * dy < size * size * 0.06f) c = Color.Lerp(c, Color.white, 0.18f);
                    c.a = 1;
                    pixels[y * size + x] = c;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // ── 토큰 ───────────────────────────────────────────────

        VisualElement Tokens()
        {
            var panel = Gs.Panel("디자인 토큰", "다크 테마 · 포인트 컬러 1개 · 분류 팔레트 10색", out _, out var body);

            var surfaces = Gs.Row("gs-wrap");
            surfaces.Add(Swatch("배경", Gs.Background, "#0E0F11"));
            surfaces.Add(Swatch("패널", Gs.Surface, "#16171A"));
            surfaces.Add(Swatch("패널 2", Gs.Surface2, "#1D1E22"));
            surfaces.Add(Swatch("포인트", Gs.Accent, "#F86927"));
            body.Add(surfaces);
            body.Add(Gs.Space(16));

            body.Add(Gs.Label("분류 팔레트", "gs-caption"));
            body.Add(Gs.Space(8));
            var palette = Gs.Row("gs-wrap");
            for (int i = 0; i < Gs.Palette.Length; i++)
                palette.Add(Swatch(Gs.PaletteNames[i], Gs.Palette[i], "#" + ColorUtility.ToHtmlStringRGB(Gs.Palette[i])));
            body.Add(palette);
            body.Add(Gs.Space(16));

            body.Add(Gs.Label("글자 위계", "gs-caption"));
            body.Add(Gs.Space(8));
            body.Add(Gs.Label("제목 22 — 화면 제목", "gs-display"));
            body.Add(Gs.Label("제목 15 — 패널 제목", "gs-title"));
            body.Add(Gs.Label("본문 12 — 일반 정보", "gs-body"));
            body.Add(Gs.Label("보조 12 — 덜 중요한 정보는 옅게", "gs-text-2"));
            body.Add(Gs.Label("캡션 11 — 가장 옅은 정보", "gs-caption"));
            return panel;
        }

        static VisualElement Swatch(string name, Color color, string hex)
        {
            var box = Gs.Col();
            box.style.marginRight = 14;
            box.style.marginBottom = 10;
            var chip = new VisualElement();
            chip.style.width = 64;
            chip.style.height = 40;
            chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius = chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 10;
            chip.style.backgroundColor = color;
            chip.style.borderTopWidth = chip.style.borderBottomWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = 1;
            chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = new Color(1, 1, 1, 0.08f);
            box.Add(chip);
            box.Add(Gs.Space(6));
            box.Add(Gs.Label(name, "gs-body"));
            box.Add(Gs.Label(hex, "gs-caption"));
            return box;
        }

        // ── 카드 ───────────────────────────────────────────────

        VisualElement Cards()
        {
            var panel = Gs.Panel("카드", "A 세로형 · B 가로형 · C 바형 — 정보의 양과 중요도에 따라 고른다", out _, out var body);

            body.Add(Gs.Label("A. 세로형", "gs-caption"));
            body.Add(Gs.Space(8));
            var vertical = Gs.Row("gs-wrap");
            for (int i = 0; i < 4; i++) vertical.Add(MakeCard(i, CardLayout.Vertical));
            body.Add(vertical);

            body.Add(Gs.Label("B. 가로형 (16:9)", "gs-caption"));
            body.Add(Gs.Space(8));
            var horizontal = Gs.Row("gs-wrap");
            for (int i = 0; i < 2; i++) horizontal.Add(MakeCard(i + 1, CardLayout.Horizontal));
            body.Add(horizontal);

            body.Add(Gs.Label("C. 바형", "gs-caption"));
            body.Add(Gs.Space(8));
            var bars = Gs.Col();
            bars.style.maxWidth = 620;
            for (int i = 0; i < 3; i++) bars.Add(MakeCard(i + 2, CardLayout.Bar));
            body.Add(bars);
            return panel;
        }

        GsCard MakeCard(int index, CardLayout layout)
        {
            var item = _items[index % _items.Count];
            var so = new SerializedObject(item);
            var card = new GsCard(layout);
            card.SetImage(_textures[index % _textures.Count], Gs.Palette[index]);
            card.Title = item.displayName;
            card.Subtitle = layout == CardLayout.Bar ? $"가격 {item.price}  ·  무게 {item.weight:0.#}" : string.IsNullOrEmpty(item.description) ? "설명 없음" : item.description;
            var rarity = PropertyUtil.Summary(so.FindProperty("rarity"));
            card.AddBadge(rarity, Gs.PaletteFor("rarity:" + rarity));
            if (item.owned) card.AddBadge("보유", Gs.Palette[4]);

            if (layout != CardLayout.Bar)
            {
                AddInlineStat(card, "가격", so.FindProperty("price"));
                AddInlineStat(card, "무게", so.FindProperty("weight"));
            }
            card.RegisterCallback<ClickEvent>(_ => card.SetSelected(!card.ClassListContains("gs-card--selected")));
            return card;
        }

        static void AddInlineStat(GsCard card, string label, SerializedProperty property)
        {
            var box = Gs.Box("gs-card__stat");
            box.Add(Gs.Label(label, "gs-card__stat-label"));
            box.Add(new GsInlineProperty(property));
            card.AddToStats(box);
        }

        // ── 시트 ───────────────────────────────────────────────

        VisualElement Sheet()
        {
            var panel = Gs.Panel("시트", "값은 평소 글자로 보이고, 클릭하면 편집으로 바뀐다 (Enter 적용 · Esc 취소)", out _, out var body);
            var header = Gs.Box("gs-sheet__header");
            header.Add(SheetCell(Gs.Label(""), 40));
            foreach (var (title, width) in new[] { ("이름", 180f), ("등급", 110f), ("가격", 100f), ("무게", 100f), ("보유", 90f), ("설명", 220f) })
                header.Add(SheetCell(Gs.Label(title), width));
            body.Add(header);

            for (int i = 0; i < _items.Count; i++)
            {
                var so = new SerializedObject(_items[i]);
                var row = Gs.Box("gs-sheet__row");
                var thumb = Gs.Box("gs-sheet__thumb");
                thumb.style.backgroundImage = new StyleBackground(_textures[i]);
                row.Add(SheetCell(thumb, 40));
                var name = new GsInlineProperty(so.FindProperty("displayName"));
                name.Q<Label>().AddToClassList("gs-heading");
                row.Add(SheetCell(name, 180));
                row.Add(SheetCell(new GsInlineProperty(so.FindProperty("rarity")), 110));
                row.Add(SheetCell(new GsInlineProperty(so.FindProperty("price")), 100));
                row.Add(SheetCell(new GsInlineProperty(so.FindProperty("weight")), 100));
                row.Add(SheetCell(new GsInlineProperty(so.FindProperty("owned")), 90));
                row.Add(SheetCell(new GsInlineProperty(so.FindProperty("description")), 220));
                var index = i;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    foreach (var r in body.Query(className: "gs-sheet__row").ToList()) r.RemoveFromClassList("gs-sheet__row--selected");
                    row.AddToClassList("gs-sheet__row--selected");
                });
                body.Add(row);
            }
            return panel;
        }

        static VisualElement SheetCell(VisualElement content, float width)
        {
            var cell = Gs.Box("gs-sheet__cell");
            cell.style.width = width;
            cell.Add(content);
            return cell;
        }

        // ── 부품 ───────────────────────────────────────────────

        VisualElement Components()
        {
            var panel = Gs.Panel("부품", "버튼 · 분할 선택 · 검색 · 배지 · 진행 바 · 알림", out _, out var body);

            var buttons = Gs.Row();
            buttons.Add(new GsButton("+ 새로 만들기", null, GsButtonKind.Primary));
            buttons.Add(Gs.HSpace(8));
            buttons.Add(new GsButton("기본 버튼", null));
            buttons.Add(Gs.HSpace(8));
            buttons.Add(new GsButton("글자형 버튼", null, GsButtonKind.Ghost));
            buttons.Add(Gs.HSpace(8));
            var disabled = new GsButton("비활성", null);
            disabled.SetEnabled(false);
            buttons.Add(disabled);
            body.Add(buttons);
            body.Add(Gs.Space(16));

            var controls = Gs.Row();
            var segmented = new GsSegmented("시트", "세로 카드", "가로 카드", "바");
            segmented.Select(0);
            controls.Add(segmented);
            controls.Add(Gs.HSpace(12));
            var search = new GsSearchField("검색   tag:  invalid");
            search.style.width = 240;
            controls.Add(search);
            body.Add(controls);
            body.Add(Gs.Space(16));

            var badges = Gs.Row("gs-wrap");
            foreach (var word in new[] { "무기", "방어구", "소모품", "퀘스트", "재료", "희귀", "이벤트", "한정" })
                badges.Add(new GsBadge(word, Gs.PaletteFor(word)));
            body.Add(badges);
            body.Add(Gs.Space(16));

            var progressRow = Gs.Row();
            var progress = new GsProgress();
            progress.style.width = 260;
            progress.SetValue(38, 100);
            progressRow.Add(progress);
            progressRow.Add(Gs.HSpace(12));
            var progressLabel = Gs.Label("38 / 100", "gs-caption");
            progressRow.Add(progressLabel);
            progressRow.Add(Gs.HSpace(16));
            var value = 38;
            progressRow.Add(new GsButton("+15", () =>
            {
                value = Mathf.Min(100, value + 15);
                progress.SetValue(value, 100);
                progressLabel.text = $"{value} / 100";
            }, GsButtonKind.Ghost));
            progressRow.Add(new GsButton("초기화", () =>
            {
                value = 0;
                progress.SetValue(0, 100);
                progressLabel.text = "0 / 100";
            }, GsButtonKind.Ghost));
            body.Add(progressRow);
            body.Add(Gs.Space(16));

            body.Add(new GsNotice("정보 알림: 덜 중요한 안내는 옅게 표시한다.", GsNoticeKind.Info));
            body.Add(new GsNotice("검증 통과", GsNoticeKind.Ok));
            body.Add(new GsNotice("경고: 확인이 필요한 항목이 있다.", GsNoticeKind.Warning));
            body.Add(new GsNotice("오류: 존재하지 않는 통계를 참조한다.", GsNoticeKind.Error));
            return panel;
        }

        // ── 모션 ───────────────────────────────────────────────

        VisualElement Motion()
        {
            var panel = Gs.Panel("모션", "USS 트랜지션 + 자체 스프링 (DOTween 없음)", out var tools, out var body);

            var springRow = Gs.Row();
            var spring = new GsButton("눌러 보기 — 스프링", null, GsButtonKind.Primary);
            GsMotion.AddPressSpring(spring);
            springRow.Add(spring);
            springRow.Add(Gs.HSpace(16));
            springRow.Add(Gs.Label("카드에 마우스를 올리면 살짝 떠오른다(위 카드 섹션).", "gs-caption"));
            body.Add(springRow);
            body.Add(Gs.Space(18));

            var list = Gs.Col();
            list.style.maxWidth = 520;
            for (int i = 0; i < 5; i++)
            {
                var card = new GsCard(CardLayout.Bar);
                card.SetImage(_textures[i], Gs.Palette[i]);
                card.Title = _items[i].displayName;
                card.Subtitle = "순차 등장";
                list.Add(card);
            }
            tools.Add(new GsButton("순차 등장 다시 보기", () => GsMotion.Appear(list), GsButtonKind.Ghost));
            body.Add(list);
            return panel;
        }

        // ── 필터 실험 ──────────────────────────────────────────

        VisualElement Filters()
        {
            var panel = Gs.Panel("필터 실험 (Unity 6.3+)", "USS filter가 에디터 창에서 동작하는지 확인한다. 효과가 안 보이면 에디터 창에서는 미지원이다.", out _, out var body);
            var row = Gs.Row("gs-wrap");
            foreach (var (name, cls) in new[] { ("없음", ""), ("블러", "gs-exp-blur"), ("그림자", "gs-exp-shadow"), ("발광", "gs-exp-glow"), ("흑백", "gs-exp-gray"), ("색조 회전", "gs-exp-hue") })
            {
                var box = Gs.Col();
                box.style.marginRight = 20;
                box.style.marginBottom = 12;
                var tile = new VisualElement();
                tile.style.width = 110;
                tile.style.height = 80;
                tile.style.borderTopLeftRadius = tile.style.borderTopRightRadius = tile.style.borderBottomLeftRadius = tile.style.borderBottomRightRadius = 14;
                tile.style.backgroundImage = new StyleBackground(_textures[2]);
                if (!string.IsNullOrEmpty(cls)) tile.AddToClassList(cls);
                box.Add(tile);
                box.Add(Gs.Space(8));
                box.Add(Gs.Label(name, "gs-body"));
                box.Add(Gs.Label(string.IsNullOrEmpty(cls) ? "기준" : "." + cls, "gs-caption"));
                row.Add(box);
            }
            body.Add(row);
            return panel;
        }
    }
}
