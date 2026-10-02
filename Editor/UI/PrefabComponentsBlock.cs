using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 프리팹 컴포넌트 블록: 도감의 프리팹 항목에서, 프리팹(자식 포함)의 스크립트 컴포넌트 필드를 탐색기로 보고 고친다.
    // 프리팹 에셋에 바로 저장하며 Undo(Ctrl+Z)로 되돌릴 수 있다.
    [LayoutBlock(Id, "프리팹 컴포넌트", Description = "연결한 프리팹의 스크립트 컴포넌트 필드를 탐색기로 보고 고칩니다.")]
    public sealed class PrefabComponentsBlock : GsLayoutBlock
    {
        public const string Id = "prefab-components";
        public override BlockSettings Settings => BlockSettings.Title;
        public override bool AutoInclude(Type type) => type == typeof(PrefabData);

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var prefab = context.ObjectAt(nameof(PrefabData.prefab)) as GameObject;
            if (prefab == null) return Titled(placement.title, Gs.Label("프리팹을 연결하면 컴포넌트가 여기에 나옵니다.", "gs-caption"));
            var root = new ExplorerItem
            {
                Label = prefab.name,
                Children = () => Components(prefab),
            };
            var explorer = GsDataExplorer.Browse(root);
            explorer.AddToClassList("gs-explorer--embedded");
            var box = Gs.Col();
            var note = Gs.Label("프리팹 에셋에 바로 저장됩니다(Ctrl+Z로 되돌리기).", "gs-caption");
            note.style.marginBottom = 6;
            box.Add(note);
            box.Add(explorer);
            return Titled(string.IsNullOrEmpty(placement.title) ? "컴포넌트" : placement.title, box);
        }

        static IEnumerable<ExplorerItem> Components(GameObject prefab)
        {
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                var target = component;
                var path = PathOf(prefab.transform, target.transform);
                yield return new ExplorerItem
                {
                    Label = string.IsNullOrEmpty(path) ? target.GetType().Name : $"{path} · {target.GetType().Name}",
                    Caption = "컴포넌트",
                    Color = Gs.Palette[5],
                    Apply = change =>
                    {
                        Undo.RecordObject(target, "프리팹 수정");
                        change();
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssetIfDirty(prefab);
                    },
                    Children = () => Fields(target),
                };
            }
        }

        // 컴포넌트의 저장되는 필드(public 또는 [SerializeField], [NonSerialized] 제외).
        static IEnumerable<ExplorerItem> Fields(MonoBehaviour component)
        {
            var type = component.GetType();
            var fields = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                fields.InsertRange(0, t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsInitOnly && !f.IsDefined(typeof(NonSerializedAttribute), false) &&
                                (f.IsPublic || f.IsDefined(typeof(SerializeField), false))));
            foreach (var field in fields)
            {
                var f = field;
                yield return new ExplorerItem
                {
                    Label = GsDataExplorer.LabelOf(f, f.Name),
                    Value = f.GetValue(component),
                    Type = f.FieldType,
                    Member = f,
                    Set = value => f.SetValue(component, value),
                };
            }
        }

        static string PathOf(Transform root, Transform target)
        {
            var parts = new List<string>();
            for (var t = target; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
