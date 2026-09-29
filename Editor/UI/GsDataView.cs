using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // ── 데이터 보기: 그리드 위에 블록을 끼워 맞춰 한 객체를 그린다 ─────────────
    //
    // 가상 그리드: 가로 12칸. 블록마다 행(y), 시작 칸(x), 폭(w), 높이(h: 0이면 내용만큼, 1 이상이면 40px 단위)을 정한다.
    // 같은 행의 블록은 나란히 놓이고, 행은 위에서 아래로 쌓인다.
    // 기본 배치: 왼쪽 스프라이트 나열(2개씩) + 오른쪽 데이터 나열(시트). 타입별로 배치를 바꿔 저장할 수 있다.
    //
    // 블록은 데이터가 어디서 왔는지 모른다(DataViewContext).
    //  · 데이터베이스 항목(편집 중 사본, SerializedObject) — 상세 패널
    //  · 게임 중 실제 값(리플렉션 + 고치는 방법) — 모니터 탐색기, 더미 생성기
    // 그래서 같은 타입은 어디서 보든 같은 배치로 보인다.
    //
    // 프로젝트 전용 블록: GsLayoutBlock을 상속하고 [LayoutBlock]을 붙이면 배치 편집 목록에 나타난다(패널 전체를 새로 짤 필요 없음).

    public abstract class DataViewContext
    {
        public abstract Type Type { get; }
        // 데이터 나열 블록이 그릴 최상위 필드 이름(숨김 제외, 순서대로).
        public abstract IEnumerable<string> Fields();
        // 필드 한 줄(이름 + 값 편집기). 데이터 나열 블록이 쓴다.
        public abstract VisualElement FieldRow(string path);
        // 필드 값만(카드용). 경로는 중첩 가능(weapon.power).
        public abstract VisualElement FieldValue(string path);
        public abstract UnityEngine.Object ObjectAt(string path);
        public abstract void SetObject(string path, UnityEngine.Object value);
        public abstract bool CanEdit { get; }
        // 보고 있는 도감 항목(있으면). 항목에 딸린 블록(선택지 목록 등)이 쓴다.
        public virtual UnityEngine.Object Target => null;

        public string LabelOf(string path)
        {
            var type = Type;
            MemberInfo member = null;
            foreach (var part in (path ?? "").Split('.'))
            {
                var found = type == null ? null : FieldPaths.MembersOf(type).FirstOrDefault(m => m.Name == part);
                if (found == null) return part;
                member = found.Info;
                type = found.Type;
            }
            return GsDataExplorer.LabelOf(member, path);
        }

        public Type TypeAt(string path) => FieldPaths.TypeAt(Type, path, out _);

        public static bool IsImageType(Type type) => type != null && (typeof(Sprite).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type));
    }

    [Serializable]
    public sealed class BlockPlacement
    {
        public string block;
        public int x;
        public int y;
        public int w = 12;
        public int h;
        public string title = "";
        // 대상 필드(특정 스프라이트·필드·클래스 표시).
        public string path = "";
        // 클래스 카드에 보일 안쪽 필드들.
        public List<string> paths = new List<string>();
        // 스프라이트 나열: 한 줄에 몇 개.
        public int perRow = 2;
    }

    [Serializable]
    public sealed class TypeLayout
    {
        public string type;
        public List<BlockPlacement> blocks = new List<BlockPlacement>();
    }

    // 타입별 배치 저장(프로젝트 공유: ProjectSettings/GameSystems/Layouts.json).
    public static class DataLayouts
    {
        public const int Columns = 12;
        public const float RowUnit = 40f;
        const string FilePath = "ProjectSettings/GameSystems/Layouts.json";

        [Serializable]
        sealed class FileData
        {
            public List<TypeLayout> layouts = new List<TypeLayout>();
        }

        static FileData _data;

        static FileData Data
        {
            get
            {
                if (_data != null) return _data;
                try
                {
                    _data = File.Exists(FilePath) ? JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath)) ?? new FileData() : new FileData();
                }
                catch (Exception)
                {
                    _data = new FileData();
                }
                return _data;
            }
        }

        public static bool HasCustom(Type type) => type != null && Data.layouts.Any(l => l.type == type.FullName);

        // 저장된 배치(없으면 기본 배치). 돌려받은 것은 사본이다.
        public static TypeLayout Get(Type type, DataViewContext context)
        {
            var saved = type == null ? null : Data.layouts.FirstOrDefault(l => l.type == type.FullName);
            return saved != null ? JsonUtility.FromJson<TypeLayout>(JsonUtility.ToJson(saved)) : Default(type, context);
        }

        public static TypeLayout Default(Type type, DataViewContext context)
        {
            var layout = new TypeLayout { type = type?.FullName };
            var hasImages = context.Fields().Any(f => DataViewContext.IsImageType(context.TypeAt(f)));
            if (hasImages)
            {
                layout.blocks.Add(new BlockPlacement { block = SpriteListBlock.Id, x = 0, y = 0, w = 4, perRow = 2 });
                layout.blocks.Add(new BlockPlacement { block = DataListBlock.Id, x = 4, y = 0, w = 8 });
            }
            else
            {
                layout.blocks.Add(new BlockPlacement { block = DataListBlock.Id, x = 0, y = 0, w = 12 });
            }
            int row = 1;
            foreach (var entry in GsLayoutBlock.Registry.Values)
            {
                var block = (GsLayoutBlock)Activator.CreateInstance(entry.type);
                if (block.AutoInclude(type)) layout.blocks.Add(new BlockPlacement { block = entry.info.Id, x = 0, y = row++, w = 12 });
            }
            return layout;
        }

        public static void Save(TypeLayout layout)
        {
            Data.layouts.RemoveAll(l => l.type == layout.type);
            Data.layouts.Add(layout);
            Write();
        }

        public static void Reset(Type type)
        {
            Data.layouts.RemoveAll(l => l.type == type.FullName);
            Write();
        }

        static void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
        }
    }

    // ── 블록 ────────────────────────────────────────────────────────

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class LayoutBlockAttribute : Attribute
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; set; }

        public LayoutBlockAttribute(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }

    // 배치 편집 화면이 이 블록에 어떤 설정 칸을 보여 줄지.
    [Flags]
    public enum BlockSettings
    {
        None = 0,
        ImagePath = 1,     // 스프라이트 필드 하나
        AnyPath = 2,       // 필드 하나
        ClassPath = 4,     // 클래스 필드 하나 + 안쪽 필드 여러 개
        PerRow = 8,
        Title = 16,
    }

    public abstract class GsLayoutBlock
    {
        public virtual BlockSettings Settings => BlockSettings.None;
        // 이 블록이 따로 보여 주는 최상위 필드(데이터 나열 블록은 이것들을 빼고 그린다).
        public virtual IEnumerable<string> Claims(BlockPlacement placement) => Array.Empty<string>();
        // 이 타입의 기본 배치에 자동으로 넣을지(예: 프리팹 항목 → 컴포넌트 블록).
        public virtual bool AutoInclude(Type type) => false;
        public abstract VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed);

        static Dictionary<string, (Type type, LayoutBlockAttribute info)> _registry;

        public static IReadOnlyDictionary<string, (Type type, LayoutBlockAttribute info)> Registry
        {
            get
            {
                if (_registry != null) return _registry;
                _registry = new Dictionary<string, (Type, LayoutBlockAttribute)>(StringComparer.Ordinal);
                foreach (var type in TypeCache.GetTypesDerivedFrom<GsLayoutBlock>())
                {
                    var info = type.GetCustomAttribute<LayoutBlockAttribute>();
                    if (type.IsAbstract || info == null) continue;
                    _registry[info.Id] = (type, info);
                }
                return _registry;
            }
        }

        public static GsLayoutBlock Create(string id) =>
            id != null && Registry.TryGetValue(id, out var entry) ? (GsLayoutBlock)Activator.CreateInstance(entry.type) : null;

        public static string NameOf(string id) => id != null && Registry.TryGetValue(id, out var entry) ? entry.info.DisplayName : $"(없는 블록: {id})";

        protected static VisualElement Titled(string title, VisualElement content)
        {
            if (string.IsNullOrEmpty(title)) return content;
            var box = Gs.Col();
            box.Add(Gs.Label(title.ToUpperInvariant(), "gs-detail__label"));
            box.Add(content);
            return box;
        }

        // 스프라이트 한 칸: 정사각 이미지 + 이름. 고칠 수 있으면 클릭해 바꾼다.
        protected static VisualElement ImageTile(DataViewContext context, string path, bool large = false)
        {
            var tile = Gs.Col("gs-view__image-tile");
            var image = Gs.Box("gs-view__image");
            if (large) image.AddToClassList("gs-view__image--large");
            var current = context.ObjectAt(path);
            if (current is Sprite sprite) image.style.backgroundImage = new StyleBackground(sprite);
            else if (current is Texture2D texture) image.style.backgroundImage = new StyleBackground(texture);
            else image.Add(Gs.Label("없음", "gs-caption", "gs-view__image-empty"));
            if (context.CanEdit)
            {
                var picker = new ObjectFieldProxy(context.TypeAt(path) ?? typeof(Sprite));
                picker.SetValue(current);
                picker.Changed += value => context.SetObject(path, value);
                image.Add(picker.Element);
                image.tooltip = "클릭해서 바꾸기 (끌어다 놓기도 됨)";
            }
            tile.Add(image);
            tile.Add(Gs.Label(context.LabelOf(path), "gs-caption", "gs-nowrap", "gs-view__image-label"));
            return tile;
        }
    }

    [LayoutBlock(Id, "스프라이트 나열", Description = "이 타입의 모든 스프라이트·이미지 필드를 한 줄에 N개씩 나열합니다.")]
    public sealed class SpriteListBlock : GsLayoutBlock
    {
        public const string Id = "sprites";
        public override BlockSettings Settings => BlockSettings.PerRow | BlockSettings.Title;

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var images = context.Fields().Where(f => DataViewContext.IsImageType(context.TypeAt(f)) && !claimed.Contains(f)).ToList();
            var grid = Gs.Row("gs-view__image-grid");
            var perRow = Mathf.Clamp(placement.perRow, 1, 8);
            foreach (var path in images)
            {
                var tile = ImageTile(context, path);
                tile.style.width = Length.Percent(100f / perRow);
                grid.Add(tile);
            }
            if (images.Count == 0) grid.Add(Gs.Label("스프라이트 필드가 없습니다", "gs-caption"));
            return Titled(placement.title, grid);
        }
    }

    [LayoutBlock(Id, "데이터 나열", Description = "스프라이트와 다른 블록이 보여 주는 필드를 뺀 나머지를 시트처럼 줄로 나열합니다.")]
    public sealed class DataListBlock : GsLayoutBlock
    {
        public const string Id = "data";
        public override BlockSettings Settings => BlockSettings.Title;

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var list = Gs.Col("gs-view__data");
            foreach (var path in context.Fields())
            {
                if (claimed.Contains(path) || DataViewContext.IsImageType(context.TypeAt(path))) continue;
                list.Add(context.FieldRow(path));
            }
            return Titled(placement.title, list);
        }
    }

    [LayoutBlock(Id, "특정 스프라이트", Description = "고른 스프라이트 필드 하나를 크게 띄웁니다.")]
    public sealed class SpriteFieldBlock : GsLayoutBlock
    {
        public const string Id = "sprite";
        public override BlockSettings Settings => BlockSettings.ImagePath;
        public override IEnumerable<string> Claims(BlockPlacement placement) => new[] { placement.path };

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed) =>
            string.IsNullOrEmpty(placement.path) ? Gs.Label("스프라이트 필드를 고르세요", "gs-caption") : ImageTile(context, placement.path, large: true);
    }

    [LayoutBlock(Id, "필드 카드", Description = "필드 하나를 작은 카드로 따로 보여 줍니다(시트 한 줄을 카드로 옮긴 것).")]
    public sealed class FieldCardBlock : GsLayoutBlock
    {
        public const string Id = "field";
        public override BlockSettings Settings => BlockSettings.AnyPath | BlockSettings.Title;
        public override IEnumerable<string> Claims(BlockPlacement placement) => new[] { placement.path };

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var card = Gs.Col("gs-view__card");
            if (string.IsNullOrEmpty(placement.path))
            {
                card.Add(Gs.Label("필드를 고르세요", "gs-caption"));
                return card;
            }
            card.Add(Gs.Label(string.IsNullOrEmpty(placement.title) ? context.LabelOf(placement.path) : placement.title, "gs-caption"));
            var value = context.FieldValue(placement.path);
            value.AddToClassList("gs-view__card-value");
            card.Add(value);
            return card;
        }
    }

    [LayoutBlock(Id, "클래스 카드", Description = "클래스 필드 하나를 카드로 보여 주고, 안쪽 필드 중 고른 것만 띄웁니다.")]
    public sealed class ClassCardBlock : GsLayoutBlock
    {
        public const string Id = "class";
        public override BlockSettings Settings => BlockSettings.ClassPath | BlockSettings.Title;
        public override IEnumerable<string> Claims(BlockPlacement placement) => new[] { placement.path };

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var card = Gs.Col("gs-view__card");
            if (string.IsNullOrEmpty(placement.path))
            {
                card.Add(Gs.Label("클래스 필드를 고르세요", "gs-caption"));
                return card;
            }
            card.Add(Gs.Label(string.IsNullOrEmpty(placement.title) ? context.LabelOf(placement.path) : placement.title, "gs-view__card-title"));
            var inner = placement.paths.Count > 0 ? placement.paths : FieldPaths.MembersOf(context.TypeAt(placement.path)).Select(m => m.Name).Take(4).ToList();
            foreach (var name in inner)
            {
                var full = placement.path + "." + name;
                var row = Gs.Row("gs-view__card-row");
                row.Add(Gs.Label(context.LabelOf(full), "gs-caption", "gs-view__card-key"));
                var value = context.FieldValue(full);
                value.AddToClassList("gs-grow");
                row.Add(value);
                card.Add(row);
            }
            return card;
        }
    }

    // ── 그리기 ──────────────────────────────────────────────────────

    public sealed class GsDataView : VisualElement
    {
        readonly DataViewContext _context;
        readonly VisualElement _grid;
        TypeLayout _layout;

        // allowLayoutEdit: "배치 편집" 버튼을 보일지(편집 창은 디자인 시스템 루트에 띄운다).
        public GsDataView(DataViewContext context, bool allowLayoutEdit = true, TypeLayout layout = null)
        {
            _context = context;
            AddToClassList("gs-view");
            if (allowLayoutEdit && context.Type != null)
            {
                var tools = Gs.Row("gs-view__tools");
                tools.Add(Gs.Spacer());
                var edit = new GsButton("배치 편집", () =>
                {
                    var host = panel?.visualTree.Q(className: "gs-root") ?? parent;
                    if (host != null) GsLayoutEditor.Open(host, context, Rebuild);
                }, GsButtonKind.Ghost);
                edit.tooltip = $"'{DataModel.FriendlyName(context.Type)}' 타입을 어떤 블록으로 어디에 보일지 정합니다(프로젝트 공유).";
                tools.Add(edit);
                Add(tools);
            }
            _grid = Gs.Col("gs-view__grid");
            Add(_grid);
            _layout = layout;
            Rebuild();
        }

        public void Rebuild()
        {
            var layout = _layout ?? DataLayouts.Get(_context.Type, _context);
            Render(_grid, _context, layout);
        }

        public static void Render(VisualElement grid, DataViewContext context, TypeLayout layout)
        {
            grid.Clear();
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var blocks = layout.blocks.Select(p => (placement: p, block: GsLayoutBlock.Create(p.block))).ToList();
            foreach (var (placement, block) in blocks)
                if (block != null)
                    foreach (var path in block.Claims(placement))
                        if (!string.IsNullOrEmpty(path)) claimed.Add(path.Split('.')[0]);

            foreach (var row in blocks.GroupBy(b => b.placement.y).OrderBy(g => g.Key))
            {
                var line = Gs.Row("gs-view__row");
                int cursor = 0;
                foreach (var (placement, block) in row.OrderBy(b => b.placement.x))
                {
                    var x = Mathf.Clamp(placement.x, 0, DataLayouts.Columns - 1);
                    var w = Mathf.Clamp(placement.w, 1, DataLayouts.Columns - x);
                    if (x > cursor)
                    {
                        var gap = new VisualElement();
                        gap.style.width = Length.Percent(100f * (x - cursor) / DataLayouts.Columns);
                        line.Add(gap);
                    }
                    var cell = Gs.Col("gs-view__cell");
                    cell.style.width = Length.Percent(100f * w / DataLayouts.Columns);
                    if (placement.h > 0)
                    {
                        cell.style.height = placement.h * DataLayouts.RowUnit;
                        cell.style.overflow = Overflow.Hidden;
                    }
                    VisualElement content;
                    try
                    {
                        content = block != null ? block.Build(context, placement, claimed) : Gs.Label(GsLayoutBlock.NameOf(placement.block), "gs-caption");
                    }
                    catch (Exception e)
                    {
                        content = new GsNotice($"{GsLayoutBlock.NameOf(placement.block)} 블록 오류: {e.Message}", GsNoticeKind.Error);
                    }
                    cell.Add(content);
                    line.Add(cell);
                    cursor = x + w;
                }
                grid.Add(line);
            }
        }
    }

    // ── 배치 편집 ───────────────────────────────────────────────────

    public static class GsLayoutEditor
    {
        public static void Open(VisualElement host, DataViewContext context, Action saved)
        {
            var layout = DataLayouts.Get(context.Type, context);
            var box = Gs.Box("gs-dialog", "gs-layout-editor");
            box.Add(Gs.Label($"배치 편집 · {DataModel.FriendlyName(context.Type)}", "gs-title"));
            var caption = Gs.Label($"가로 {DataLayouts.Columns}칸 그리드. 블록마다 행·시작 칸·폭·높이(0=내용만큼)를 정합니다. 이 타입은 어디서 보든 이 배치로 보입니다.", "gs-caption");
            caption.style.marginTop = 4;
            caption.style.whiteSpace = WhiteSpace.Normal;
            box.Add(caption);

            var split = Gs.Row("gs-grow");
            split.style.alignItems = Align.Stretch;
            split.style.marginTop = 12;
            var list = new ScrollView();
            list.AddToClassList("gs-layout-editor__list");
            var preview = new ScrollView();
            preview.AddToClassList("gs-layout-editor__preview");
            split.Add(list);
            split.Add(preview);
            box.Add(split);

            var buttons = Gs.Box("gs-dialog__buttons");
            box.Add(buttons);
            var modal = GsModal.Show(host, box);

            void Redraw()
            {
                list.Clear();
                for (int i = 0; i < layout.blocks.Count; i++) list.Add(PlacementRow(host, context, layout, i, Redraw));
                GsButton add = null;
                add = new GsButton("+ 블록 추가", () =>
                {
                    var items = GsLayoutBlock.Registry.Values.OrderBy(r => r.info.DisplayName).Select(r =>
                        new GsDropdownItem(r.info.DisplayName, () =>
                        {
                            var nextRow = layout.blocks.Count == 0 ? 0 : layout.blocks.Max(b => b.y) + 1;
                            layout.blocks.Add(new BlockPlacement { block = r.info.Id, y = nextRow, w = DataLayouts.Columns });
                            Redraw();
                        }) { Detail = r.info.Description }).ToList();
                    GsDropdown.Show(add, items);
                }, GsButtonKind.Ghost);
                add.style.alignSelf = Align.FlexStart;
                list.Add(add);

                var grid = Gs.Col();
                GsDataView.Render(grid, context, layout);
                preview.Clear();
                preview.Add(Gs.Label("미리보기", "gs-detail__label"));
                preview.Add(grid);
            }

            buttons.Add(new GsButton("기본 배치로", () =>
            {
                layout = DataLayouts.Default(context.Type, context);
                Redraw();
            }, GsButtonKind.Ghost));
            buttons.Add(Gs.Spacer());
            buttons.Add(new GsButton("취소", modal.Close, GsButtonKind.Ghost));
            buttons.Add(new GsButton("저장", () =>
            {
                DataLayouts.Save(layout);
                modal.Close();
                saved?.Invoke();
            }, GsButtonKind.Primary));
            Redraw();
        }

        static VisualElement PlacementRow(VisualElement host, DataViewContext context, TypeLayout layout, int index, Action redraw)
        {
            var placement = layout.blocks[index];
            var block = GsLayoutBlock.Create(placement.block);
            var settings = block?.Settings ?? BlockSettings.None;
            var card = Gs.Box("gs-block", "gs-layout-editor__row");

            var head = Gs.Row();
            var kind = Gs.Label(GsLayoutBlock.NameOf(placement.block), "gs-heading", "gs-grow");
            head.Add(kind);
            head.Add(new GsButton("✕", () =>
            {
                layout.blocks.RemoveAt(index);
                redraw();
            }, GsButtonKind.Ghost));
            card.Add(head);

            var numbers = Gs.Row("gs-wrap");
            numbers.Add(Stepper("행", placement.y, 0, 20, v => placement.y = v, redraw));
            numbers.Add(Stepper("시작 칸", placement.x, 0, DataLayouts.Columns - 1, v => placement.x = v, redraw));
            numbers.Add(Stepper("폭", placement.w, 1, DataLayouts.Columns, v => placement.w = v, redraw));
            numbers.Add(Stepper("높이", placement.h, 0, 30, v => placement.h = v, redraw));
            if ((settings & BlockSettings.PerRow) != 0) numbers.Add(Stepper("한 줄에", placement.perRow, 1, 8, v => placement.perRow = v, redraw));
            card.Add(numbers);

            if ((settings & (BlockSettings.ImagePath | BlockSettings.AnyPath | BlockSettings.ClassPath)) != 0)
            {
                var fields = context.Fields().Where(f =>
                {
                    var type = context.TypeAt(f);
                    if ((settings & BlockSettings.ImagePath) != 0) return DataViewContext.IsImageType(type);
                    if ((settings & BlockSettings.ClassPath) != 0) return type != null && DataModel.Of(type).Kind == DataKind.Object && !DataViewContext.IsImageType(type);
                    return true;
                }).ToList();
                var pick = Gs.Box("gs-inline", "gs-inline--editable");
                pick.Add(Gs.Label(string.IsNullOrEmpty(placement.path) ? "필드 선택" : context.LabelOf(placement.path), "gs-inline__value"));
                pick.RegisterCallback<ClickEvent>(_ =>
                {
                    var items = fields.Select(f => new GsDropdownItem(context.LabelOf(f), () =>
                    {
                        placement.path = f;
                        placement.paths.Clear();
                        redraw();
                    }) { Caption = f, Selected = placement.path == f }).ToList();
                    if (items.Count == 0) items.Add(new GsDropdownItem("고를 수 있는 필드가 없습니다") { Disabled = true });
                    GsDropdown.Show(pick, items);
                });
                card.Add(Labeled("대상 필드", pick));
            }

            if ((settings & BlockSettings.ClassPath) != 0 && !string.IsNullOrEmpty(placement.path))
            {
                var innerType = context.TypeAt(placement.path);
                var chips = Gs.Row("gs-wrap");
                foreach (var member in FieldPaths.MembersOf(innerType))
                {
                    var name = member.Name;
                    var on = placement.paths.Contains(name);
                    var chip = new GsBadge(GsDataExplorer.LabelOf(member.Info, name), on ? Gs.Accent : Gs.Palette[8]);
                    chip.style.opacity = on ? 1f : 0.45f;
                    chip.style.marginRight = 6;
                    chip.style.marginBottom = 6;
                    chip.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (!placement.paths.Remove(name)) placement.paths.Add(name);
                        redraw();
                    });
                    chip.tooltip = "클릭해서 카드에 보이기/숨기기";
                    chips.Add(chip);
                }
                card.Add(Labeled("카드에 보일 필드", chips));
            }

            if ((settings & BlockSettings.Title) != 0)
            {
                var title = new TextField { value = placement.title };
                title.AddToClassList("gs-inline");
                title.RegisterValueChangedCallback(e => placement.title = e.newValue);
                title.RegisterCallback<FocusOutEvent>(_ => redraw());
                card.Add(Labeled("제목(선택)", title));
            }
            return card;
        }

        static VisualElement Labeled(string label, VisualElement content)
        {
            var row = Gs.Box("gs-field-row");
            row.Add(Gs.Label(label, "gs-field-row__label"));
            content.AddToClassList("gs-grow");
            row.Add(content);
            return row;
        }

        static VisualElement Stepper(string label, int value, int min, int max, Action<int> set, Action redraw)
        {
            var box = Gs.Row("gs-layout-editor__stepper");
            box.Add(Gs.Label(label, "gs-caption"));
            box.Add(new GsButton("−", () =>
            {
                set(Math.Max(min, value - 1));
                redraw();
            }, GsButtonKind.Ghost));
            box.Add(Gs.Label(value.ToString(), "gs-layout-editor__number"));
            box.Add(new GsButton("+", () =>
            {
                set(Math.Min(max, value + 1));
                redraw();
            }, GsButtonKind.Ghost));
            return box;
        }
    }

    static class GsDataViewImages
    {
        public static bool IsImage(UnityEngine.Object value) => value is Sprite || value is Texture2D;
    }
}
