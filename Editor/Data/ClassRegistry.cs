using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GBible.EditorTools
{
    // DB › 클래스·이벤트 탭의 설정: 새 클래스의 네임스페이스, DB에서 만든 클래스의 파일 위치 기억.
    // 그룹은 여기 두지 않는다: 그룹 = Assets/MomoiProject/Code 아래 폴더(ClassFolders). ProjectSettings/MomoiProject/Classes.json에 두어 팀과 공유한다.
    public static class ClassRegistry
    {
        public const string SettingsPath = "ProjectSettings/MomoiProject/Classes.json";
        public const string ClassTab = "class";
        public const string EventTab = "event";

        [Serializable]
        public sealed class Entry
        {
            // 클래스 전체 이름.
            public string type;
            // DB에서 만든 클래스의 파일 경로(찾기 쉽게 기억해 둔다. 파일을 옮기면 이름으로 다시 찾는다).
            public string path = "";
        }

        [Serializable]
        public sealed class Settings
        {
            [Tooltip("DB에서 만든 클래스의 네임스페이스(비우면 없음)")]
            public string codeNamespace = "Game.Data";
            public List<Entry> classes = new List<Entry>();
        }

        static Settings _settings;

        public static event Action Changed;

        // 저장 전 변경을 잠시 보관하는 곳(스크립트가 다시 불러와져도 남도록).
        static readonly string StashKey = LegacyNames.Session("GBible.ClassRegistry.Pending");

        public static Settings Current
        {
            get
            {
                if (_settings != null) return _settings;
                try
                {
                    var pending = UnityEditor.SessionState.GetString(StashKey, "");
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

        // 저장하지 않은 변경이 있는지(클래스 파일 위치 기억 등).
        public static bool IsDirty => !string.IsNullOrEmpty(UnityEditor.SessionState.GetString(StashKey, ""));

        // 고친 것을 DB의 다른 변경과 함께 모아 둔다(파일에는 저장할 때 쓴다: CodexChanges.Save).
        public static void Save()
        {
            UnityEditor.SessionState.SetString(StashKey, JsonUtility.ToJson(Current));
            Changed?.Invoke();
        }

        // 파일에 쓴다.
        public static void Commit()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(Current, true));
            UnityEditor.SessionState.EraseString(StashKey);
            Changed?.Invoke();
        }

        // 저장하지 않은 변경을 버리고 파일의 내용으로 돌아간다.
        public static void Discard()
        {
            if (!IsDirty) return;
            UnityEditor.SessionState.EraseString(StashKey);
            _settings = null;
            Changed?.Invoke();
        }

        // 테스트용: 파일을 읽지 않고 빈 설정으로 바꾼다(null이면 다음에 파일에서 다시 읽는다).
        internal static void Override(Settings settings) => _settings = settings;

        [UnityEditor.InitializeOnLoadMethod]
        static void ResetOnReload() => _settings = null;

        public static string FullName(string className) =>
            string.IsNullOrEmpty(Current.codeNamespace) ? className : Current.codeNamespace + "." + className;

        // ── 클래스 ─────────────────────────────────────────────

        static Entry Find(string type) => Current.classes.FirstOrDefault(c => c.type == type);

        static Entry FindOrAdd(string type)
        {
            var entry = Find(type);
            if (entry == null) Current.classes.Add(entry = new Entry { type = type });
            return entry;
        }

        public static string PathOf(string type) => Find(type)?.path ?? "";

        public static void SetPath(string type, string path, bool save = true)
        {
            FindOrAdd(type).path = path ?? "";
            if (save) Save();
        }

        public static void Forget(string type, bool save = true)
        {
            Current.classes.RemoveAll(c => c.type == type);
            if (save) Save();
        }
    }
}
