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
    // 옮기는 것(안 쓰는 것·정돈)은 개발자 폴더(Assets/Dev) 안만, 먼저 묻고, 옮긴 목록을 MomoiProjectBackups에 남긴다(되돌리기 버튼).
    public sealed class AssetWindow : EditorWindow
    {
        enum Tab { Requests, List, Unused, Tidy }

        static readonly string[] TabNames = { "넣을 것", "목록", "안 쓰는 것", "폴더 정돈" };

        [SerializeField] Tab _tab;
        [SerializeField] AssetKind _kind = AssetKind.Sprite;
        [SerializeField] string _search = "";

        VisualElement _body;
        Label _status;
        // 알림 글(창을 다시 그려도 남는다 — 탭을 바꾸면 지운다).
        string _statusText = "";
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
            var hint = Gs.Label("밖에서 파일·폴더를 이 창에 끌어 놓으면 종류별 폴더(그림 → Assets/Dev/Art · 소리 → Assets/Dev/Audio …)로 가져옵니다. 바꾸기는 같은 자리에 내용을 넣으므로 그 에셋을 쓰는 곳이 모두 새 것을 씁니다.", "gs-caption");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 8;
            root.Add(hint);

            var tabs = Gs.Row();
            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = (Tab)i;
                tabs.Add(new GsButton(TabNames[i], () =>
                {
                    if (_tab != tab) _statusText = "";
                    _tab = tab;
                    Rebuild();
                }, _tab == tab ? GsButtonKind.Primary : GsButtonKind.Ghost));
                tabs.Add(Gs.HSpace(6));
            }
            root.Add(tabs);
            root.Add(Gs.Space(8));

            _status = Gs.Label(_statusText ?? "", "gs-caption");
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
            _statusText = text ?? "";
            if (_status != null) _status.text = _statusText;
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

        // 그 종류의 파일만 보이는 파일 고르기 창(모든 파일로 바꿀 수도 있다).
        static string AskFile(string title, AssetKind kind)
        {
            var extensions = AssetTools.ExtensionsOf(kind);
            var filters = extensions.Length == 0
                ? new[] { "모든 파일", "*" }
                : new[] { AssetTools.KindName(kind), string.Join(",", extensions), "모든 파일", "*" };
            return EditorUtility.OpenFilePanelWithFilters(title, "", filters);
        }

        // 옮기기 전에 묻는 글: 몇 개인지와 앞의 몇 개.
        static string Examples(IReadOnlyList<(string from, string to)> moves, int shown = 5)
        {
            var lines = moves.Take(shown).Select(m => $"{m.from}\n   → {m.to}");
            return string.Join("\n", lines) + (moves.Count > shown ? $"\n… 외 {moves.Count - shown}개" : "");
        }

        // ── 넣을 것 ──────────────────────────────────────────────

        void BuildRequests()
        {
            var items = AssetRequests.Refresh();
            if (items.Count == 0)
            {
                _body.Add(Gs.Label("넣을 것이 없습니다. AI가 임시 에셋 자리를 만들면 여기에 올라옵니다(AssetRequests).", "gs-caption"));
                return;
            }
            var rows = new List<(AssetRequests.Item item, AssetRequests.State state, string current)>();
            foreach (var item in items)
            {
                var state = AssetRequests.StateOf(item, out var current);
                rows.Add((item, state, current));
            }
            var left = rows.Count(r => r.state != AssetRequests.State.Done);
            _body.Add(Gs.Label($"남은 것 {left} · 넣은 것 {rows.Count - left}", "gs-caption"));
            _body.Add(Gs.Space(6));
            foreach (var (item, state, current) in rows.OrderBy(r => r.state == AssetRequests.State.Done).ThenBy(r => r.item.path, StringComparer.Ordinal))
            {
                var it = item;
                var at = current;
                var done = state == AssetRequests.State.Done;
                var row = Gs.Row();
                row.style.marginBottom = 6;
                var text = Gs.Col();
                text.style.flexGrow = 1;
                var title = Gs.Label((done ? "✓ " : "") + it.usage);
                if (done) title.AddToClassList("gs-text-2");
                text.Add(title);
                // 지금 자리(GUID로 찾은 곳 — 개발자가 옮겼으면 요청한 자리와 다르다).
                var where = at ?? it.path;
                var detail = Gs.Label(where + (at != null && at != it.path ? $" (요청한 자리 {it.path})" : "")
                                      + (string.IsNullOrEmpty(it.size) ? "" : $" · {it.size}") + (string.IsNullOrEmpty(it.note) ? "" : $" · {it.note}") + StateNote(state, it), "gs-caption");
                detail.style.whiteSpace = WhiteSpace.Normal;
                text.Add(detail);
                row.Add(text);
                row.Add(new GsButton(done ? "다시 넣기…" : "파일 넣기…", () => PickForRequest(it), done ? GsButtonKind.Ghost : GsButtonKind.Primary));
                row.Add(Gs.HSpace(4));
                var ping = new GsButton("위치", () => Ping(at), GsButtonKind.Ghost);
                ping.SetEnabled(at != null);
                row.Add(ping);
                _body.Add(row);
            }
        }

        static string StateNote(AssetRequests.State state, AssetRequests.Item item)
        {
            switch (state)
            {
                case AssetRequests.State.Missing: return " · 파일 없음";
                case AssetRequests.State.WrongKind: return $" · 종류가 다릅니다(필요한 것: {AssetTools.KindName(AssetTools.KindOf(item.path))})";
                case AssetRequests.State.Placeholder: return " · 아직 임시 에셋";
                default: return "";
            }
        }

        // 넣을 것 한 줄에 파일 넣기: 에셋이 있으면(옮겼거나 확장자를 바꿨어도 GUID로 찾은 지금 자리) 그 자리에 내용을 넣고(GUID 그대로 — 쓰는 곳이 모두 새 것),
        // 없으면 요청한 자리로 가져온다(확장자는 고른 파일 것).
        void PickForRequest(AssetRequests.Item item)
        {
            var current = AssetRequests.PathOf(item);
            var file = AskFile(current != null ? "바꿀 파일(같은 종류)" : "넣을 파일", AssetTools.KindOf(item.path));
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                var result = AssetRequests.Put(item, file);
                Status(current != null ? $"바꿨습니다: {result} (이 에셋을 쓰는 곳이 모두 새 것을 씁니다)" : $"넣었습니다: {result}");
            }
            catch (Exception e)
            {
                Status(e.Message.Replace("[MomoiProject] ", ""));
            }
            Rebuild();
        }

        // 목록의 에셋 바꾸기(같은 자리·GUID에 내용).
        void PickAndReplace(string path)
        {
            var file = AskFile("바꿀 파일(같은 종류)", AssetTools.KindOf(path));
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
            if (string.IsNullOrEmpty(path)) return;
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
            // 창을 다시 그리면(Rebuild) 예전 줄 묶음은 예전 본문에 붙어 있다 — 어디에 붙어 있든 떼어 낸다.
            _rows?.RemoveFromHierarchy();
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

        const string UnusedCaution = "코드가 경로 글자로 불러오는 것(Resources 밖)은 연결이 보이지 않아 여기서 안 쓰는 것으로 보일 수 있습니다 — 확인하고 옮기세요.";

        void BuildUnused()
        {
            var note = Gs.Label("개발자 폴더(Assets/Dev) 안에서, 씬(빌드 목록 · Assets/Dev 안의 모든 씬 · Assets/MomoiProject/Scenes)·Resources·DB 항목·시스템 폴더(Assets/MomoiProject)·Addressables 항목·넣을 것 목록에서 닿지 않는 " +
                                "그림·소리·글꼴·모델·프리팹·머티리얼·애니메이션·영상입니다. 코드가 든 폴더(가져온 패키지)와 Assets/Dev 밖은 보지 않습니다. " + UnusedCaution +
                                $" 옮기면 {AssetTools.UnusedFolder} 아래 원래 경로 그대로 가고(지우지 않음), 옮긴 목록은 {AssetTools.BackupRoot}/<시각>-unused/moves.tsv에 남습니다.", "gs-caption");
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
                bar.Add(new GsButton($"모두 _Unused로 옮기기({_unused.Count})", () => MoveUnused(_unused.ToList())));
            }
            var undo = AssetTools.LastMoveLog("unused");
            if (undo != null)
            {
                bar.Add(Gs.HSpace(6));
                bar.Add(new GsButton("마지막 옮기기 되돌리기", () => UndoLast(undo, "마지막 '안 쓰는 것 옮기기'를"), GsButtonKind.Ghost));
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
                row.Add(new GsButton("옮기기", () => MoveUnused(new List<string> { p }), GsButtonKind.Ghost));
                row.Add(new GsButton("위치", () => Ping(p), GsButtonKind.Ghost));
                _body.Add(row);
            }
        }

        // 묻고 옮긴다(_Unused 아래 원래 경로 그대로 · 지우지 않음 · 목록을 백업 폴더에).
        void MoveUnused(List<string> paths)
        {
            if (paths.Count == 0) return;
            var moves = paths.Select(p => (from: p, to: AssetTools.UnusedFolder + p.Substring("Assets".Length))).ToList();
            var message = $"{paths.Count}개를 {AssetTools.UnusedFolder} 아래로 옮깁니다(지우지 않음, GUID 그대로).\n\n{Examples(moves)}\n\n{UnusedCaution}\n" +
                          $"옮긴 목록은 {AssetTools.BackupRoot}에 남고 '마지막 옮기기 되돌리기'로 되돌릴 수 있습니다.";
            if (!EditorUtility.DisplayDialog("안 쓰는 에셋 옮기기", message, "옮기기", "취소")) return;
            var moved = AssetTools.MoveToUnused(paths);
            if (_unused != null) _unused.RemoveAll(paths.Contains);
            Status($"{moved}개를 옮겼습니다." + (moved > 0 ? " 되돌리려면 '마지막 옮기기 되돌리기'." : ""));
            Rebuild();
        }

        // what: "마지막 정돈을"처럼 조사까지.
        void UndoLast(string log, string what)
        {
            if (!EditorUtility.DisplayDialog("되돌리기", $"{what} 되돌립니다(옮긴 에셋을 원래 경로로, GUID 그대로).\n\n기록: {log}", "되돌리기", "취소")) return;
            var count = AssetTools.UndoMoves(log);
            _unused = null;
            Status($"{count}개를 원래 자리로 되돌렸습니다." + (count == 0 ? " (되돌릴 것이 없었습니다 — 콘솔 경고를 확인하세요)" : ""));
            Rebuild();
        }

        // ── 폴더 정돈 ────────────────────────────────────────────

        void BuildTidy()
        {
            var moves = AssetTools.Misplaced();
            var note = Gs.Label("개발자 폴더(Assets/Dev) 안에서 종류별 폴더 밖에 있는 에셋입니다. 하위 폴더 모양은 그대로 그 종류 폴더 아래로 옮기고(Assets/Dev/Cards/Fire/a.png → Assets/Dev/Art/Cards/Fire/a.png), " +
                                "옮겨도 GUID가 그대로라 쓰는 곳은 끊기지 않습니다. Assets/Dev 밖(시스템 폴더·패키지·Plugins·에셋 스토어 폴더)과 코드가 든 폴더(가져온 패키지)·씬 옆 같은 이름 폴더(라이트맵 등)·" +
                                $"Resources·Editor 폴더·Addressables 폴더 항목은 건드리지 않습니다. 옮긴 목록은 {AssetTools.BackupRoot}/<시각>-tidy/moves.tsv에 남습니다. 종류별 폴더: "
                + string.Join(" · ", Enum.GetValues(typeof(AssetKind)).Cast<AssetKind>().Where(k => k != AssetKind.Other).Select(k => $"{AssetTools.KindName(k)} {AssetTools.FolderFor(k)}")), "gs-caption");
            note.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(note);
            var bar = Gs.Row();
            bar.style.marginTop = 6;
            if (moves.Count > 0)
            {
                bar.Add(new GsButton($"모두 정돈({moves.Count})", () =>
                {
                    var list = moves.Select(m => (from: m.path, to: m.target)).ToList();
                    var message = $"{moves.Count}개를 종류별 폴더로 옮깁니다(GUID 그대로 — 쓰는 곳은 끊기지 않습니다).\n\n{Examples(list)}\n\n" +
                                  $"옮긴 목록은 {AssetTools.BackupRoot}/<시각>-tidy/moves.tsv에 남고 '마지막 정돈 되돌리기'로 되돌릴 수 있습니다.";
                    if (!EditorUtility.DisplayDialog("폴더 정돈", message, "옮기기", "취소")) return;
                    var moved = AssetTools.Tidy(moves);
                    Status($"{moved}개를 옮겼습니다." + (moved > 0 ? " 되돌리려면 '마지막 정돈 되돌리기'." : ""));
                    Rebuild();
                }, GsButtonKind.Primary));
                bar.Add(Gs.HSpace(6));
            }
            var undo = AssetTools.LastMoveLog("tidy");
            if (undo != null) bar.Add(new GsButton("마지막 정돈 되돌리기", () => UndoLast(undo, "마지막 정돈을"), GsButtonKind.Ghost));
            _body.Add(bar);
            if (moves.Count == 0)
            {
                _body.Add(Gs.Label("정돈할 것이 없습니다.", "gs-caption"));
                return;
            }
            foreach (var move in moves.Take(400))
            {
                var m = move;
                var row = Gs.Row();
                row.style.marginTop = 4;
                row.Add(Gs.Label($"{m.path}  →  {m.target}", "gs-grow"));
                row.Add(new GsButton("옮기기", () =>
                {
                    var moved = AssetTools.Tidy(new[] { m });
                    Status(moved > 0 ? $"옮겼습니다: {m.path} → {m.target} (되돌리려면 '마지막 정돈 되돌리기')" : $"옮기지 못했습니다: {m.path} (콘솔 경고를 확인하세요)");
                    Rebuild();
                }, GsButtonKind.Ghost));
                _body.Add(row);
            }
            if (moves.Count > 400) _body.Add(Gs.Label($"… {moves.Count - 400}개 더('모두 정돈'은 모두 옮깁니다)", "gs-caption"));
        }
    }
}
