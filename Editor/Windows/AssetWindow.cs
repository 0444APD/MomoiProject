using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 에셋 관리: 넣을 것(AI가 만든 임시 자리에 진짜 파일 넣기) · 목록(종류별 · 쓰는 곳 · 바꾸기) · 안 쓰는 것 · 폴더 정돈.
    // 밖에서 파일·폴더를 이 창에 끌어 놓으면 종류별 폴더로 가져온다. 로직은 AssetTools·AssetRequests.
    public sealed class AssetWindow : EditorWindow
    {
        enum Tab { Requests, List, Unused, Tidy }

        static readonly string[] TabNames = { "넣을 것", "목록", "안 쓰는 것", "폴더 정돈" };

        [SerializeField] Tab _tab;
        [SerializeField] AssetKind _kind = AssetKind.Sprite;
        [SerializeField] string _search = "";

        VisualElement _body;
        Label _status;
        List<string> _unused;
        readonly HashSet<string> _openUsers = new HashSet<string>();

        [MenuItem("MomoiProject/에셋 관리", priority = 9)]
        public static void Open()
        {
            var window = GsWindows.Get<AssetWindow>();
            window.titleContent = new GUIContent("에셋 관리");
            window.minSize = new Vector2(560, 420);
        }

        void OnEnable() => EditorApplication.projectChanged += Rebuild;
        void OnDisable() => EditorApplication.projectChanged -= Rebuild;

        void CreateGUI()
        {
            var root = Gs.Col();
            Gs.Apply(root);
            root.style.flexGrow = 1;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 12;
            rootVisualElement.Add(root);

            root.Add(Gs.Label("에셋 관리", "gs-heading"));
            var hint = Gs.Label("밖에서 파일·폴더를 이 창에 끌어 놓으면 종류별 폴더(그림 → Assets/Art · 소리 → Assets/Audio …)로 가져옵니다. 바꾸기는 같은 자리에 내용을 넣으므로 그 에셋을 쓰는 곳이 모두 새 것을 씁니다.", "gs-caption");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 8;
            root.Add(hint);

            var tabs = Gs.Row();
            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = (Tab)i;
                tabs.Add(new GsButton(TabNames[i], () =>
                {
                    _tab = tab;
                    Rebuild();
                }, _tab == tab ? GsButtonKind.Primary : GsButtonKind.Ghost));
                tabs.Add(Gs.HSpace(6));
            }
            root.Add(tabs);
            root.Add(Gs.Space(8));

            _status = Gs.Label("", "gs-caption");
            _status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_status);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _body = scroll;
            root.Add(scroll);

            // 밖에서 끌어 놓기 = 가져오기.
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                if (External().Count > 0) DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            rootVisualElement.RegisterCallback<DragPerformEvent>(_ =>
            {
                var paths = External();
                if (paths.Count == 0) return;
                DragAndDrop.AcceptDrag();
                var created = AssetTools.Import(paths);
                Status($"{created.Count}개를 가져왔습니다: " + string.Join(", ", created.Take(5)) + (created.Count > 5 ? " …" : ""));
            });

            BuildTab();
        }

        // 프로젝트 밖의 경로만(프로젝트 창에서 끌어 온 것은 빼고).
        static List<string> External() =>
            (DragAndDrop.paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p) && Path.IsPathRooted(p) && !Path.GetFullPath(p).StartsWith(Path.GetFullPath("Assets"), StringComparison.OrdinalIgnoreCase)).ToList();

        void Rebuild()
        {
            rootVisualElement.Clear();
            CreateGUI();
        }

        void Status(string text)
        {
            if (_status != null) _status.text = text;
        }

        void BuildTab()
        {
            _body.Clear();
            switch (_tab)
            {
                case Tab.Requests: BuildRequests(); break;
                case Tab.List: BuildList(); break;
                case Tab.Unused: BuildUnused(); break;
                case Tab.Tidy: BuildTidy(); break;
            }
        }

        // ── 넣을 것 ──────────────────────────────────────────────

        void BuildRequests()
        {
            var items = AssetRequests.Load();
            if (items.Count == 0)
            {
                _body.Add(Gs.Label("넣을 것이 없습니다. AI가 임시 에셋 자리를 만들면 여기에 올라옵니다(AssetRequests).", "gs-caption"));
                return;
            }
            var left = items.Count(i => !i.done);
            _body.Add(Gs.Label($"남은 것 {left} · 넣은 것 {items.Count - left}", "gs-caption"));
            _body.Add(Gs.Space(6));
            foreach (var item in items.OrderBy(i => i.done).ThenBy(i => i.path, StringComparer.Ordinal))
            {
                var it = item;
                var row = Gs.Row();
                row.style.marginBottom = 6;
                var text = Gs.Col();
                text.style.flexGrow = 1;
                var title = Gs.Label((it.done ? "✓ " : "") + it.usage);
                if (it.done) title.AddToClassList("gs-text-2");
                text.Add(title);
                var detail = Gs.Label($"{it.path}" + (string.IsNullOrEmpty(it.size) ? "" : $" · {it.size}") + (string.IsNullOrEmpty(it.note) ? "" : $" · {it.note}"), "gs-caption");
                detail.style.whiteSpace = WhiteSpace.Normal;
                text.Add(detail);
                row.Add(text);
                row.Add(new GsButton(it.done ? "다시 넣기…" : "파일 넣기…", () => PickAndReplace(it.path), it.done ? GsButtonKind.Ghost : GsButtonKind.Primary));
                row.Add(Gs.HSpace(4));
                row.Add(new GsButton("위치", () => Ping(it.path), GsButtonKind.Ghost));
                _body.Add(row);
            }
        }

        void PickAndReplace(string path)
        {
            if (!File.Exists(path))
            {
                // 임시 파일 없이 목록만 있던 것: 고른 파일을 그 자리로 가져온다.
                var source = EditorUtility.OpenFilePanel("넣을 파일", "", Path.GetExtension(path).TrimStart('.'));
                if (string.IsNullOrEmpty(source)) return;
                AssetTools.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                File.Copy(source, path, overwrite: true);
                AssetDatabase.ImportAsset(path);
                AssetRequests.MarkDone(path);
                Status($"넣었습니다: {path}");
                Rebuild();
                return;
            }
            var file = EditorUtility.OpenFilePanel("바꿀 파일(같은 종류)", "", "");
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                var result = AssetTools.Replace(path, file);
                Status($"바꿨습니다: {result} (이 에셋을 쓰는 곳이 모두 새 것을 씁니다)");
            }
            catch (Exception e)
            {
                Status(e.Message.Replace("[MomoiProject] ", ""));
            }
            Rebuild();
        }

        static void Ping(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        // ── 목록 ─────────────────────────────────────────────────

        void BuildList()
        {
            var bar = Gs.Row();
            foreach (AssetKind kind in Enum.GetValues(typeof(AssetKind)))
            {
                var k = kind;
                bar.Add(new GsButton(AssetTools.KindName(k), () =>
                {
                    _kind = k;
                    Rebuild();
                }, _kind == k ? GsButtonKind.Primary : GsButtonKind.Ghost));
                bar.Add(Gs.HSpace(4));
            }
            _body.Add(bar);
            var search = new TextField { value = _search };
            search.style.marginTop = 6;
            search.RegisterValueChangedCallback(e =>
            {
                _search = e.newValue;
                BuildListRows();
            });
            _body.Add(search);
            BuildListRows();
        }

        VisualElement _rows;

        void BuildListRows()
        {
            if (_rows != null) _body.Remove(_rows);
            _rows = Gs.Col();
            _rows.style.marginTop = 6;
            _body.Add(_rows);
            var paths = AssetTools.All(_kind).Where(p => string.IsNullOrEmpty(_search) || p.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            _rows.Add(Gs.Label($"{AssetTools.KindName(_kind)} {paths.Count}개 (기본 폴더 {AssetTools.FolderFor(_kind)})", "gs-caption"));
            foreach (var path in paths.Take(400))
            {
                var p = path;
                var row = Gs.Row();
                row.style.marginTop = 4;
                var icon = new Image { image = AssetDatabase.GetCachedIcon(p) };
                icon.style.width = 16;
                icon.style.height = 16;
                icon.style.marginRight = 6;
                row.Add(icon);
                var name = Gs.Label(p, "gs-grow");
                name.style.flexGrow = 1;
                row.Add(name);
                var users = AssetTools.UsedBy(p);
                row.Add(new GsButton($"쓰는 곳 {users.Count}", () =>
                {
                    if (!_openUsers.Remove(p)) _openUsers.Add(p);
                    BuildListRows();
                }, GsButtonKind.Ghost));
                row.Add(new GsButton("바꾸기…", () => PickAndReplace(p), GsButtonKind.Ghost));
                row.Add(new GsButton("위치", () => Ping(p), GsButtonKind.Ghost));
                _rows.Add(row);
                if (_openUsers.Contains(p))
                    foreach (var user in users)
                    {
                        var u = user;
                        var line = Gs.Row();
                        line.style.marginLeft = 22;
                        line.Add(Gs.Label("← " + u, "gs-caption", "gs-grow"));
                        line.Add(new GsButton("위치", () => Ping(u), GsButtonKind.Ghost));
                        _rows.Add(line);
                    }
            }
            if (paths.Count > 400) _rows.Add(Gs.Label($"… {paths.Count - 400}개 더(검색으로 좁히세요)", "gs-caption"));
        }

        // ── 안 쓰는 것 ───────────────────────────────────────────

        void BuildUnused()
        {
            var note = Gs.Label("빌드에 든 씬·Resources·DB 데이터·UI·넣을 것 목록에서 닿지 않는 그림·소리·글꼴·모델·프리팹·머티리얼·애니메이션·영상입니다. 코드가 경로로 불러오는 것(Resources 밖)은 여기서 안 쓰는 것으로 보일 수 있으니 확인하고 옮기세요. 옮기면 Assets/_Unused 아래로 갑니다(지우지 않음).", "gs-caption");
            note.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(note);
            var bar = Gs.Row();
            bar.style.marginTop = 6;
            bar.Add(new GsButton("찾기", () =>
            {
                _unused = AssetTools.Unused();
                Rebuild();
            }, GsButtonKind.Primary));
            if (_unused != null && _unused.Count > 0)
            {
                bar.Add(Gs.HSpace(6));
                bar.Add(new GsButton($"모두 _Unused로 옮기기({_unused.Count})", () =>
                {
                    if (!EditorUtility.DisplayDialog("안 쓰는 에셋 옮기기", $"{_unused.Count}개를 {AssetTools.UnusedFolder} 아래로 옮깁니다(지우지 않음).", "옮기기", "취소")) return;
                    var moved = AssetTools.MoveToUnused(_unused);
                    _unused = null;
                    Status($"{moved}개를 옮겼습니다.");
                    Rebuild();
                }));
            }
            _body.Add(bar);
            if (_unused == null) return;
            if (_unused.Count == 0) _body.Add(Gs.Label("안 쓰는 에셋이 없습니다.", "gs-caption"));
            foreach (var path in _unused)
            {
                var p = path;
                var row = Gs.Row();
                row.style.marginTop = 4;
                row.Add(Gs.Label(p, "gs-grow"));
                row.Add(new GsButton("옮기기", () =>
                {
                    AssetTools.MoveToUnused(new[] { p });
                    _unused.Remove(p);
                    Rebuild();
                }, GsButtonKind.Ghost));
                row.Add(new GsButton("위치", () => Ping(p), GsButtonKind.Ghost));
                _body.Add(row);
            }
        }

        // ── 폴더 정돈 ────────────────────────────────────────────

        void BuildTidy()
        {
            var moves = AssetTools.Misplaced();
            var note = Gs.Label("종류별 폴더 밖에 있는 에셋입니다. 옮겨도 GUID가 그대로라 쓰는 곳은 끊기지 않습니다. 종류별 폴더: "
                + string.Join(" · ", Enum.GetValues(typeof(AssetKind)).Cast<AssetKind>().Where(k => k != AssetKind.Other).Select(k => $"{AssetTools.KindName(k)} {AssetTools.FolderFor(k)}")), "gs-caption");
            note.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(note);
            if (moves.Count == 0)
            {
                _body.Add(Gs.Label("정돈할 것이 없습니다.", "gs-caption"));
                return;
            }
            var all = new GsButton($"모두 정돈({moves.Count})", () =>
            {
                var moved = AssetTools.Tidy(moves);
                Status($"{moved}개를 옮겼습니다.");
                Rebuild();
            }, GsButtonKind.Primary);
            all.style.alignSelf = Align.FlexStart;
            all.style.marginTop = 6;
            _body.Add(all);
            foreach (var move in moves.Take(400))
            {
                var m = move;
                var row = Gs.Row();
                row.style.marginTop = 4;
                row.Add(Gs.Label($"{m.path}  →  {m.target}", "gs-grow"));
                row.Add(new GsButton("옮기기", () =>
                {
                    AssetTools.Tidy(new[] { m });
                    Rebuild();
                }, GsButtonKind.Ghost));
                _body.Add(row);
            }
        }
    }
}
