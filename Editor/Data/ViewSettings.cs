using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace GameSystems.EditorTools
{
    public enum FieldKind
    {
        Image,
        Text,
        Number,
        Enum,
        Bool,
        Object,
        Other,
    }

    public sealed class FieldEntry
    {
        public string Path;
        public string Label;
        public Type Type;
        public FieldKind Kind;
        public FieldInfo Field;
    }

    public enum Slot
    {
        Image,
        Title,
        Subtitle,
        Badge,
        Stat,
        Column,
    }

    // 타입의 저장되는 필드 목록(Unity 직렬화 기준 + 실제 타입은 리플렉션으로 보충). 타입당 한 번 만들고 캐시한다.
    public static class FieldCatalog
    {
        static readonly Dictionary<Type, List<FieldEntry>> Cache = new Dictionary<Type, List<FieldEntry>>();

        public static IReadOnlyList<FieldEntry> For(Type type)
        {
            if (Cache.TryGetValue(type, out var cached)) return cached;
            var list = new List<FieldEntry>();
            var instance = ScriptableObject.CreateInstance(type);
            try
            {
                var so = new SerializedObject(instance);
                foreach (var property in PropertyUtil.TopLevel(so)) Collect(property, "", list);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            Cache[type] = list;
            return list;
        }

        static void Collect(SerializedProperty property, string labelPrefix, List<FieldEntry> list)
        {
            var field = PropertyUtil.GetField(property, out var valueType);
            var label = labelPrefix + GsPropertyEditor.LabelOf(property, field);
            if (property.propertyType == SerializedPropertyType.Generic && !property.isArray && property.hasVisibleChildren)
            {
                foreach (var child in PropertyUtil.Children(property)) Collect(child, label + " › ", list);
                return;
            }
            list.Add(new FieldEntry { Path = property.propertyPath, Label = label, Type = valueType ?? field?.FieldType, Kind = KindOf(property, valueType), Field = field });
        }

        static FieldKind KindOf(SerializedProperty property, Type type)
        {
            if (property.isArray && property.propertyType != SerializedPropertyType.String) return FieldKind.Other;
            switch (property.propertyType)
            {
                case SerializedPropertyType.String: return FieldKind.Text;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float: return FieldKind.Number;
                case SerializedPropertyType.Boolean: return FieldKind.Bool;
                case SerializedPropertyType.Enum: return FieldKind.Enum;
                case SerializedPropertyType.ObjectReference:
                    return type != null && (typeof(Sprite).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type)) ? FieldKind.Image : FieldKind.Object;
            }
            return FieldKind.Other;
        }

        public static bool Accepts(Slot slot, FieldKind kind)
        {
            switch (slot)
            {
                case Slot.Image: return kind == FieldKind.Image;
                case Slot.Title:
                case Slot.Subtitle: return kind == FieldKind.Text || kind == FieldKind.Number || kind == FieldKind.Enum;
                case Slot.Badge: return kind == FieldKind.Enum || kind == FieldKind.Bool || kind == FieldKind.Text;
                case Slot.Stat: return kind == FieldKind.Number || kind == FieldKind.Enum || kind == FieldKind.Text || kind == FieldKind.Bool;
                default: return true;
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void Clear() => Cache.Clear();
    }

    // 타입별 보기 설정. 사용자가 화면에서 고른 값만 저장하고, 비어 있으면 어트리뷰트·자동 추론을 쓴다.
    [Serializable]
    public sealed class TypeView
    {
        public string type;
        public string layout = "Sheet";
        public string image;
        public string title;
        public string subtitle;
        public string badge;
        public List<string> stats = new List<string>();
        public List<string> columns = new List<string>();
        public bool customStats;
        public bool customColumns;
        // 시트 열 폭(열 경로 → 폭). 머리줄 경계를 끌면 저장된다.
        public List<ColumnWidth> widths = new List<ColumnWidth>();

        public float WidthOf(string path, float fallback)
        {
            foreach (var w in widths) if (w.path == path) return w.width;
            return fallback;
        }

        public void SetWidth(string path, float width)
        {
            foreach (var w in widths)
            {
                if (w.path != path) continue;
                w.width = width;
                return;
            }
            widths.Add(new ColumnWidth { path = path, width = width });
        }
    }

    [Serializable]
    public sealed class ColumnWidth
    {
        public string path;
        public float width;
    }

    public sealed class ResolvedView
    {
        public string Layout;
        public FieldEntry Image;
        public FieldEntry Title;
        public FieldEntry Subtitle;
        public FieldEntry Badge;
        public List<FieldEntry> Stats = new List<FieldEntry>();
        public List<FieldEntry> Columns = new List<FieldEntry>();
        // 필드가 사라져 끊긴 연결: (슬롯 이름, 저장된 경로, 후보 경로)
        public List<(string slot, string path, FieldEntry suggestion)> Broken = new List<(string, string, FieldEntry)>();
    }

    // 프로젝트 공용 저장: ProjectSettings/GameSystems/DatabaseViews.json (Assets를 어지럽히지 않고 팀과 공유된다).
    public static class ViewSettings
    {
        const string FilePath = "ProjectSettings/GameSystems/DatabaseViews.json";

        [Serializable]
        sealed class FileData
        {
            public List<TypeView> views = new List<TypeView>();
        }

        static FileData _data;

        static FileData Data
        {
            get
            {
                if (_data != null) return _data;
                _data = File.Exists(FilePath) ? JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath)) ?? new FileData() : new FileData();
                return _data;
            }
        }

        public static TypeView Get(Type type)
        {
            var view = Data.views.FirstOrDefault(v => v.type == type.FullName);
            if (view != null) return view;
            view = new TypeView { type = type.FullName };
            Data.views.Add(view);
            return view;
        }

        public static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
        }

        public static CardLayout? CardLayoutOf(TypeView view)
        {
            switch (view.layout)
            {
                case "Vertical": return CardLayout.Vertical;
                case "Horizontal": return CardLayout.Horizontal;
                case "Bar": return CardLayout.Bar;
                default: return null;
            }
        }

        // 우선순위: 화면 설정 > (라이브러리 내장 타입의) 어트리뷰트 > 자동 추론.
        public static ResolvedView Resolve(Type type)
        {
            var view = Get(type);
            var fields = FieldCatalog.For(type);
            var result = new ResolvedView { Layout = view.layout };
            bool changed = false;

            FieldEntry Pick(string slotName, ref string stored, Slot slot, Func<FieldEntry> auto)
            {
                if (stored == "-") return null;
                if (!string.IsNullOrEmpty(stored))
                {
                    var path = stored;
                    var found = fields.FirstOrDefault(f => f.Path == path);
                    if (found != null) return found;
                    var renamed = FollowRename(type, fields, stored);
                    if (renamed != null)
                    {
                        stored = renamed.Path;
                        changed = true;
                        return renamed;
                    }
                    result.Broken.Add((slotName, stored, Suggest(fields, stored, slot)));
                    return null;
                }
                return auto();
            }

            result.Image = Pick("이미지", ref view.image, Slot.Image, () => AutoImage(fields));
            result.Title = Pick("제목", ref view.title, Slot.Title, () => fields.FirstOrDefault(f => f.Path == "displayName"));
            result.Subtitle = Pick("부제목", ref view.subtitle, Slot.Subtitle, () => fields.FirstOrDefault(f => f.Kind == FieldKind.Text && f.Path.IndexOf("desc", StringComparison.OrdinalIgnoreCase) >= 0));
            result.Badge = Pick("배지", ref view.badge, Slot.Badge, () => fields.FirstOrDefault(f => f.Kind == FieldKind.Enum));

            result.Stats = view.customStats
                ? ResolveList(view.stats, fields, "수치", Slot.Stat, result, type, ref changed)
                : fields.Where(f => f.Kind == FieldKind.Number).Take(3).ToList();

            result.Columns = view.customColumns
                ? ResolveList(view.columns, fields, "열", Slot.Column, result, type, ref changed)
                : AutoColumns(fields);

            if (changed) Save();
            return result;
        }

        static List<FieldEntry> ResolveList(List<string> paths, IReadOnlyList<FieldEntry> fields, string slotName, Slot slot, ResolvedView result, Type type, ref bool changed)
        {
            var list = new List<FieldEntry>();
            for (int i = 0; i < paths.Count; i++)
            {
                var found = fields.FirstOrDefault(f => f.Path == paths[i]) ?? FollowRename(type, fields, paths[i]);
                if (found != null)
                {
                    if (found.Path != paths[i])
                    {
                        paths[i] = found.Path;
                        changed = true;
                    }
                    list.Add(found);
                }
                else
                {
                    result.Broken.Add((slotName, paths[i], Suggest(fields, paths[i], slot)));
                }
            }
            return list;
        }

        static FieldEntry AutoImage(IReadOnlyList<FieldEntry> fields)
        {
            var images = fields.Where(f => f.Kind == FieldKind.Image).ToList();
            string[] preferred = { "icon", "sprite", "image", "portrait", "thumbnail" };
            return images.FirstOrDefault(f => preferred.Any(p => f.Path.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) ?? images.FirstOrDefault();
        }

        static List<FieldEntry> AutoColumns(IReadOnlyList<FieldEntry> fields)
        {
            var list = new List<FieldEntry>();
            var title = fields.FirstOrDefault(f => f.Path == "displayName");
            if (title != null) list.Add(title);
            list.AddRange(fields.Where(f => f.Path != "displayName" && f.Path != "tags" && f.Kind != FieldKind.Image).Take(6));
            return list;
        }

        // [FormerlySerializedAs("old")]가 붙은 필드면 자동으로 따라간다.
        static FieldEntry FollowRename(Type type, IReadOnlyList<FieldEntry> fields, string oldPath)
        {
            foreach (var entry in fields)
            {
                if (entry.Field == null) continue;
                foreach (var attr in entry.Field.GetCustomAttributes<FormerlySerializedAsAttribute>())
                {
                    var prefix = entry.Path.Contains('.') ? entry.Path.Substring(0, entry.Path.LastIndexOf('.') + 1) : "";
                    if (prefix + attr.oldName == oldPath) return entry;
                }
            }
            return null;
        }

        // 같은 종류의 필드 중 이름이 가장 비슷한 것.
        public static FieldEntry Suggest(IReadOnlyList<FieldEntry> fields, string oldPath, Slot slot)
        {
            var name = oldPath.Split('.').Last();
            return fields.Where(f => FieldCatalog.Accepts(slot, f.Kind))
                         .Select(f => (f, d: Distance(name.ToLowerInvariant(), f.Path.Split('.').Last().ToLowerInvariant())))
                         .Where(x => x.d <= Math.Max(3, name.Length / 2))
                         .OrderBy(x => x.d)
                         .Select(x => x.f)
                         .FirstOrDefault();
        }

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
