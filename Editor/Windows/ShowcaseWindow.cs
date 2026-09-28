using System.Collections.Generic;
using System.Linq;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 살아 있는 스타일 가이드: 디자인 토큰, 분류 팔레트, 카드 A·B·C, 시트, 상세 패널, 부품, 모션, 필터 실험.
    public sealed class ShowcaseWindow : EditorWindow
    {
        readonly List<ShowcaseItem> _items = new List<ShowcaseItem>();
        readonly List<Texture2D> _textures = new List<Texture2D>();
        VisualElement _root;
        VisualElement _cardsBody;
        GsSheet _sheet;

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
            _root = Gs.Col();
            Gs.Apply(_root);
            host.Add(_root);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _root.Add(scroll);
            var page = scroll.contentContainer;
            page.style.paddingLeft = 40;
            page.style.paddingRight = 40;
            page.style.paddingTop = 36;
            page.style.paddingBottom = 60;

            page.Add(Gs.Label("UI 샘플", "gs-display"));
            var intro = Gs.Label("Game Systems 디자인 시스템의 모든 부품. 새 도구는 이 부품(패널 단위)을 조립해서 만든다.", "gs-caption");
            intro.style.marginTop = 6;
            page.Add(intro);
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
                item.name = names[i];
                item.displayName = names[i];
                item.rarity = (ShowcaseItem.Rarity)(i % 4);
                item.price = 120 * (i + 1);
                item.weight = 0.5f + i * 0.7f;
                item.owned = i % 2 == 0;
                item.description = i % 2 == 0 ? "오래된 전설이 깃든 물건. 새벽빛을 받으면 칼날에 희미한 문양이 떠오른다고 전해진다." : "";
                _items.Add(item);
                _textures.Add(MakeTexture(Gs.Palette[(i * 2) % Gs.Palette.Length], Gs.Palette[(i * 2 + 5) % Gs.Palette.Length]));
            }
        }

        Texture2D TextureOf(Object item) => _textures[Mathf.Max(0, _items.IndexOf(item as ShowcaseItem))];

        // 모든 카드·시트가 같은 상세 패널을 연다.
        void OpenDetail(Object item, string focusPath = null)
        {
            var index = _items.IndexOf(item as ShowcaseItem);
            GsDetailPanel.Open(_root, item, new DetailOptions
            {
                Subtitle = "UI 샘플 아이템",
                Image = _ => _textures[Mathf.Max(0, index)],
                Placeholder = Gs.Palette[index % Gs.Palette.Length],
                FocusPath = focusPath,
                Saved = _ => RefreshItems(),
            });
        }

        void RefreshItems()
        {
            BuildCards();
            BuildSheet();
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
            var panel = Gs.Panel("디자인 토큰", "다크 테마 · 포인트 컬러 1개(핫핑크) · 분류 팔레트 10색", out _, out var body);

            var surfaces = Gs.Row("gs-wrap");
            surfaces.Add(Swatch("배경", Gs.Background, "#0E0F11"));
            surfaces.Add(Swatch("패널", Gs.Surface, "#16171A"));
            surfaces.Add(Swatch("패널 2", Gs.Surface2, "#1D1E22"));
            surfaces.Add(Swatch("포인트", Gs.Accent, "#FD5D83"));
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
            var panel = Gs.Panel("카드", "A 세로형 · B 가로형 · C 바형 — 클릭하면 모두 같은 상세 패널이 열린다", out _, out _cardsBody);
            BuildCards();
            return panel;
        }

        void BuildCards()
        {
            if (_cardsBody == null) return;
            _cardsBody.Clear();

            _cardsBody.Add(Gs.Label("A. 세로형", "gs-caption"));
            _cardsBody.Add(Gs.Space(8));
            var vertical = Gs.Row("gs-wrap");
            for (int i = 0; i < 4; i++) vertical.Add(MakeCard(i, CardLayout.Vertical));
            _cardsBody.Add(vertical);

            _cardsBody.Add(Gs.Label("B. 가로형 (16:9)", "gs-caption"));
            _cardsBody.Add(Gs.Space(8));
            var horizontal = Gs.Row("gs-wrap");
            for (int i = 0; i < 2; i++) horizontal.Add(MakeCard(i + 1, CardLayout.Horizontal));
            _cardsBody.Add(horizontal);

            _cardsBody.Add(Gs.Label("C. 바형", "gs-caption"));
            _cardsBody.Add(Gs.Space(8));
            var bars = Gs.Col();
            bars.style.maxWidth = 620;
            for (int i = 0; i < 3; i++) bars.Add(MakeCard(i + 2, CardLayout.Bar));
            _cardsBody.Add(bars);
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
                card.AddStat("가격", item.price.ToString());
                card.AddStat("무게", item.weight.ToString("0.#"));
            }
            card.RegisterCallback<ClickEvent>(_ => OpenDetail(item));
            return card;
        }

        // ── 시트 ───────────────────────────────────────────────

        VisualElement Sheet()
        {
            var panel = Gs.Panel("시트", "읽기 전용 · 행을 더블클릭하면 상세 패널에서 보고 편집한다 · 머리줄 경계를 끌어 열 폭 조절", out _, out var body);
            _sheet = new GsSheet(Gs.Surface) { Thumbnail = TextureOf };
            _sheet.OpenRequested += OpenDetail;
            body.Add(_sheet);
            BuildSheet();
            return panel;
        }

        void BuildSheet()
        {
            _sheet?.Build(new[]
            {
                new SheetColumn { Title = "이름", Width = 170, Path = "displayName", Bold = true },
                new SheetColumn { Title = "등급", Width = 100, Path = "rarity" },
                new SheetColumn { Title = "가격", Width = 90, Path = "price" },
                new SheetColumn { Title = "무게", Width = 90, Path = "weight" },
                new SheetColumn { Title = "보유", Width = 80, Path = "owned" },
                new SheetColumn { Title = "설명", Width = 220, Path = "description" },
            }, _items.Cast<Object>());
        }

        // ── 부품 ───────────────────────────────────────────────

        VisualElement Components()
        {
            var panel = Gs.Panel("부품", "버튼 · 분할 선택 · 검색 · 배지 · 진행 바 · 알림 · 확인 창", out _, out var body);

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
            buttons.Add(Gs.HSpace(8));
            buttons.Add(new GsButton("확인 창 열기", () => GsDialog.Confirm(_root, "변경 사항 저장", "아래 2개 항목을 저장합니다.", "저장", null, new List<FieldChange>
            {
                new FieldChange { Label = "가격", Old = "120", New = "150" },
                new FieldChange { Label = "등급", Old = "일반", New = "희귀" },
            }), GsButtonKind.Ghost));
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
            controls.Add(Gs.HSpace(12));

            // 드롭다운: 항목이 많으면 검색창(한글 초성 검색 가능), 그룹 머리글, 현재 값 표시, ↑↓ Enter Esc.
            var chosen = "처치 수 (누적)";
            GsButton dropdown = null;
            dropdown = new GsButton(chosen + "  ▾", () =>
            {
                var groups = new[]
                {
                    ("전투", new[] { "처치 수 (누적)", "처치 수 (이번 판)", "최고 콤보", "받은 피해", "치명타 횟수" }),
                    ("재화", new[] { "골드", "보석", "경험치" }),
                    ("탐험", new[] { "연 상자 수", "발견한 지역", "이동 거리" }),
                };
                var items = new List<GsDropdownItem>();
                foreach (var (group, names) in groups)
                    foreach (var name in names)
                    {
                        var n = name;
                        items.Add(new GsDropdownItem(n, () =>
                        {
                            chosen = n;
                            dropdown.Text = n + "  ▾";
                        }) { Group = group, Selected = n == chosen, Caption = group == "재화" ? "합계" : null });
                    }
                GsDropdown.Show(dropdown, items, placeholder: "수치 검색 (초성 가능: ㅊㅊ)");
            });
            controls.Add(dropdown);
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

            body.Add(new GsChangeView("변경 표시", "120", "150"));
            body.Add(Gs.Space(16));

            body.Add(new GsNotice("정보 알림: 덜 중요한 안내는 옅게 표시한다.", GsNoticeKind.Info));
            body.Add(new GsNotice("검증 통과", GsNoticeKind.Ok));
            body.Add(new GsNotice("경고: 확인이 필요한 항목이 있다.", GsNoticeKind.Warning));
            body.Add(new GsNotice("오류: 존재하지 않는 수치를 참조한다.", GsNoticeKind.Error));
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
