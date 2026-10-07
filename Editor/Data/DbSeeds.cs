using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // DB 반영 스크립트: AI가 쓴 static 함수(매개변수 없음)에 [DbSeed("버전")]을 붙이면 컴파일이 끝난 뒤 저절로 한 번 돈다.
    //   함수 안에서 EditorDatabase.Staging.Add·Work·Remove로 항목을 만들고·고치고·지운다 → DB에 '적용 전'으로 보이고,
    //   개발자가 읽어 본 뒤 Ctrl+S로 적용한다. 적용했을 때만 "이 버전은 돌았다"로 기록한다(버리면 다음 컴파일 뒤 다시 돈다).
    //   돌린 뒤 바뀐 것이 없으면(이미 있는 항목뿐 · 같은 값으로 Work만 함) 바로 기록한다. 숨은 칸([HideInInspector])만 바꿔도 바뀐 것으로 본다.
    //   내용을 바꾸면 버전을 올린다("1" → "2"). 이미 있는 항목은 건너뛰고(개발자가 맞춘 값을 덮지 않는다), 고쳐야 할 때만 Work.
    //   ID를 바꿀 때는 EditorDatabase.Staging.Rename(항목, 새 ID) — 가리키는 칸이 함께 바뀌고, 서로 바꾸기도 맞게 적용된다.
    //
    //   [DbSeed("1")]
    //   static void Relics()
    //   {
    //       if (EditorDatabase.Instance.FindById("anchor") == null)
    //       {
    //           var p = (PassiveData)EditorDatabase.Staging.Add(typeof(PassiveData), "anchor");
    //           …
    //       }
    //   }
    // 기록: ProjectSettings/MomoiProject/DbSeeds.json(팀과 공유). 메뉴 'MomoiProject › DB 반영 스크립트 › 모두 다시 돌리기'.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DbSeedAttribute : Attribute
    {
        public string Version { get; }

        public DbSeedAttribute(string version = "1")
        {
            Version = string.IsNullOrEmpty(version) ? "1" : version;
        }
    }

    [InitializeOnLoad]
    public static class DbSeeds
    {
        public const string RecordPath = "ProjectSettings/MomoiProject/DbSeeds.json";
        // 돌렸지만 아직 적용(Ctrl+S)하지 않은 것(컴파일 사이에는 유지, Unity를 닫으면 사라진다 → 다음에 다시 돈다).
        static readonly string PendingKey = LegacyNames.Session("GBible.DbSeeds.Pending");

        [Serializable]
        sealed class Record
        {
            public List<string> keys = new List<string>();
            public List<string> versions = new List<string>();
        }

        static DbSeeds()
        {
            CodexStaging.AfterCommit += RecordPending;
            CodexStaging.AfterDiscard += () => SessionState.EraseString(PendingKey);
            EditorApplication.delayCall += () => RunPending(force: false);
        }

        [MenuItem("MomoiProject/DB 반영 스크립트/모두 다시 돌리기", priority = 61)]
        static void RunAllMenu()
        {
            var count = RunPending(force: true);
            EditorUtility.DisplayDialog("DB 반영 스크립트", count == 0 ? "DB 반영 스크립트가 없습니다." : $"{count}개를 돌렸습니다. DB에서 '적용 전' 항목을 보고 Ctrl+S로 적용하세요.", "확인");
        }

        // 버전이 바뀐(또는 처음 보는) 스크립트를 돌린다. 돌린 수를 돌려준다.
        public static int RunPending(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return 0;
            if (!File.Exists(EditorDatabase.DatabaseAssetPath)) return 0;   // 시작 설정 전(DB가 아직 없음)
            var seeds = Find();
            if (seeds.Count == 0) return 0;
            var record = Load();
            var pending = Pending();
            var ran = new List<string>();
            foreach (var (key, version, method) in seeds)
            {
                var done = !force && (Recorded(record, key) == version || pending.Contains(key + "@" + version));
                if (done) continue;
                try
                {
                    method.Invoke(null, null);
                    ran.Add(key + "@" + version);
                }
                catch (Exception e)
                {
                    var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                    Debug.LogError($"[MomoiProject] DB 반영 스크립트 '{key}'(버전 {version})가 실패했습니다: {inner.Message}\n{inner.StackTrace}");
                }
            }
            if (ran.Count == 0) return 0;
            SessionState.SetString(PendingKey, string.Join("\n", pending.Concat(ran).Distinct()));
            EditorDatabase.Staging.Touch();
            // 바뀐 것이 없으면(이미 있는 항목뿐 · 같은 값으로 Work만 함) 바로 기록 — 적용할 것이 없어 Ctrl+S의 적용이 돌지 않으므로 기다리면 매번 다시 돈다.
            if (!EditorDatabase.Staging.HasChanges) RecordPending();
            else Debug.Log($"[MomoiProject] DB 반영 스크립트 {ran.Count}개를 돌렸습니다({string.Join(", ", ran)}). DB에서 '적용 전' 항목을 보고 Ctrl+S로 적용하세요.");
            return ran.Count;
        }

        // 프로젝트의 [DbSeed] 함수(이름순 — 돌리는 순서가 늘 같게).
        static List<(string key, string version, MethodInfo method)> Find()
        {
            var list = new List<(string, string, MethodInfo)>();
            foreach (var method in TypeCache.GetMethodsWithAttribute<DbSeedAttribute>())
            {
                var key = method.DeclaringType?.FullName + "." + method.Name;
                if (!method.IsStatic || method.GetParameters().Length != 0)
                {
                    Debug.LogWarning($"[MomoiProject] [DbSeed] '{key}'는 매개변수 없는 static 함수여야 합니다.");
                    continue;
                }
                list.Add((key, method.GetCustomAttribute<DbSeedAttribute>().Version, method));
            }
            return list.OrderBy(s => s.Item1, StringComparer.Ordinal).ToList();
        }

        static HashSet<string> Pending()
        {
            var text = SessionState.GetString(PendingKey, "");
            return new HashSet<string>(text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        // 적용했다: 돌린 버전을 기록 파일에 남긴다.
        static void RecordPending()
        {
            var pending = Pending();
            SessionState.EraseString(PendingKey);
            if (pending.Count == 0) return;
            var record = Load();
            foreach (var entry in pending)
            {
                var at = entry.LastIndexOf('@');
                if (at <= 0) continue;
                var key = entry.Substring(0, at);
                var version = entry.Substring(at + 1);
                var index = record.keys.IndexOf(key);
                if (index < 0)
                {
                    record.keys.Add(key);
                    record.versions.Add(version);
                }
                else record.versions[index] = version;
            }
            Save(record);
        }

        static string Recorded(Record record, string key)
        {
            var index = record.keys.IndexOf(key);
            return index < 0 || index >= record.versions.Count ? null : record.versions[index];
        }

        static Record Load()
        {
            try
            {
                if (File.Exists(RecordPath)) return JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath)) ?? new Record();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] {RecordPath}를 읽지 못했습니다: {e.Message}");
            }
            return new Record();
        }

        static void Save(Record record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
            File.WriteAllText(RecordPath, JsonUtility.ToJson(record, true));
        }
    }
}
