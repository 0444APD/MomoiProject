using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // DB 탭(DB 종류)마다의 폴더. 정리용일 뿐 게임 동작에는 쓰지 않는다(디스크 폴더와도 상관없다).
    //   폴더 경로 = "전투/보스"처럼 '/'로 잇는다. 항목이 어느 폴더에 있는지는 항목 자신(GameData.Folder)에 적는다.
    //   여기에는 폴더 목록만 둔다(빈 폴더도 남도록). 항목이 들어 있는 폴더와 그 상위 폴더는 적지 않아도 보인다.
    // ProjectSettings/MomoiProject/CodexFolders.json에 두어 팀과 공유한다. DB의 다른 변경처럼 저장(Ctrl+S)할 때 파일에 쓴다.
    public static class CodexFolders
    {
        public const string SettingsPath = "ProjectSettings/MomoiProject/CodexFolders.json";
        internal static readonly string StashKey = LegacyNames.Session("GBible.CodexFolders.Pending");

        [Serializable]
        sealed class Tab
        {
            public string tab;
            public List<string> folders = new List<string>();
        }

        [Serializable]
        sealed class Settings
        {
            public List<Tab> tabs = new List<Tab>();
        }

        static Settings _settings;

        public static event Action Changed;

        static Settings Current
        {
            get
            {
                if (_settings != null) return _settings;
                try
                {
                    var pending = SessionState.GetString(StashKey, "");
                    _settings = !string.IsNullOrEmpty(pending) ? JsonUtility.FromJson<Settings>(pending)
                        : File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) : null;
                }
                catch (Exception)
                {
                    _settings = null;
                }
                return _settings ??= new Settings();
            }
        }

        [InitializeOnLoadMethod]
        static void ResetOnReload() => _settings = null;

        // 테스트용: 파일을 읽지 않고 빈 설정으로 바꾼다(null이면 다음에 파일에서 다시 읽는다).
        internal static void Override(bool empty) => _settings = empty ? new Settings() : null;

        public static bool IsDirty => !string.IsNullOrEmpty(SessionState.GetString(StashKey, ""));

        static void Store()
        {
            SessionState.SetString(StashKey, JsonUtility.ToJson(Current));
            Changed?.Invoke();
        }

        public static void Commit()
        {
            if (!IsDirty) return;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(Current, true));
            SessionState.EraseString(StashKey);
            Changed?.Invoke();
        }

        // 탭 하나의 폴더만 파일에 쓴다(바로 저장하는 창용: 다른 탭의 저장 안 된 폴더 변경은 그대로 기다린다).
        public static void CommitTab(string tab)
        {
            Settings file = null;
            try { file = File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) : null; }
            catch (Exception) { file = null; }
            file ??= new Settings();
            file.tabs.RemoveAll(t => t.tab == tab);
            var mine = Find(tab, false);
            if (mine != null) file.tabs.Add(new Tab { tab = tab, folders = new List<string>(mine.folders) });
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(file, true));
            // 다른 탭에 남은 변경이 없으면 저장 대기도 끝낸다.
            if (JsonUtility.ToJson(Normalize(file)) == JsonUtility.ToJson(Normalize(Current))) SessionState.EraseString(StashKey);
            Changed?.Invoke();
        }

        static Settings Normalize(Settings settings) => new Settings
        {
            tabs = settings.tabs.Where(t => t.folders.Count > 0).OrderBy(t => t.tab, StringComparer.Ordinal)
                .Select(t => new Tab { tab = t.tab, folders = t.folders.OrderBy(f => f, StringComparer.Ordinal).ToList() }).ToList(),
        };

        public static void Discard()
        {
            if (!IsDirty) return;
            SessionState.EraseString(StashKey);
            _settings = null;
            Changed?.Invoke();
        }

        static Tab Find(string tab, bool create)
        {
            var found = Current.tabs.FirstOrDefault(t => t.tab == tab);
            if (found == null && create) Current.tabs.Add(found = new Tab { tab = tab });
            return found;
        }

        // ── 경로 ───────────────────────────────────────────────

        public static string Parent(string path)
        {
            var cut = (path ?? "").LastIndexOf('/');
            return cut < 0 ? "" : path.Substring(0, cut);
        }

        public static string Name(string path)
        {
            var cut = (path ?? "").LastIndexOf('/');
            return cut < 0 ? path ?? "" : path.Substring(cut + 1);
        }

        public static string Join(string parent, string name) => string.IsNullOrEmpty(parent) ? name : parent + "/" + name;

        public static bool IsUnder(string path, string ancestor) =>
            path == ancestor || (path ?? "").StartsWith(ancestor + "/", StringComparison.Ordinal);

        // path가 from 안쪽이면 to 아래로 옮긴 경로. 아니면 그대로.
        public static string Rebase(string path, string from, string to)
        {
            if (path == from) return to;
            return IsUnder(path, from) ? Join(to, path.Substring(from.Length + 1)) : path;
        }

        // ── 읽기 ───────────────────────────────────────────────

        // 이 탭의 폴더 전체: 적어 둔 폴더 + 항목이 들어 있는 폴더 + 그 상위 폴더들(이름순).
        public static List<string> All(string tab, IEnumerable<GameData> items)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            void AddWithParents(string path)
            {
                for (var p = path; !string.IsNullOrEmpty(p); p = Parent(p))
                    if (!set.Add(p)) break;
            }
            foreach (var folder in Find(tab, false)?.folders ?? new List<string>()) AddWithParents(folder);
            foreach (var item in items) AddWithParents(item.Folder);
            return set.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        public static bool CanCreate(string tab, IEnumerable<GameData> items, string parent, string name, out string error)
        {
            error = null;
            name = (name ?? "").Trim();
            if (name.Length == 0) error = "폴더 이름을 입력하세요.";
            else if (name.Contains("/")) error = "폴더 이름에 '/'를 쓸 수 없습니다.";
            else if (All(tab, items).Any(p => string.Equals(p, Join(parent, name), StringComparison.OrdinalIgnoreCase))) error = $"'{name}' 폴더가 이미 있습니다.";
            return error == null;
        }

        // ── 바꾸기(저장할 때 파일에 쓴다) ─────────────────────────

        public static void Create(string tab, string path)
        {
            var entry = Find(tab, true);
            if (!entry.folders.Contains(path)) entry.folders.Add(path);
            Store();
        }

        // 폴더를 옮기거나 이름을 바꾼다(안쪽 폴더도 함께). 항목의 폴더는 부르는 쪽이 고친다(작업본).
        public static void Move(string tab, string from, string to)
        {
            var entry = Find(tab, true);
            entry.folders = entry.folders.Select(p => Rebase(p, from, to)).Distinct().ToList();
            if (!entry.folders.Contains(to)) entry.folders.Add(to);
            Store();
        }

        // 폴더를 지운다. 안쪽 폴더는 한 단계 위로 올라간다(항목도 부르는 쪽이 위로 옮긴다).
        public static void Delete(string tab, string path)
        {
            var entry = Find(tab, true);
            var parent = Parent(path);
            entry.folders = entry.folders.Where(p => p != path).Select(p => IsUnder(p, path) ? Join(parent, p.Substring(path.Length + 1)) : p).Distinct().ToList();
            Store();
        }
    }
}
