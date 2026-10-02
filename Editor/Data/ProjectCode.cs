using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace GBible.EditorTools
{
    // "내 코드": 클래스를 고르는 모든 목록(데이터 클래스·값 형식·필드 규칙 …)이 프로젝트에서 직접 만든 스크립트만 보이도록 거르는 규칙 한 곳.
    // 폴더를 지정하지 않아도 자동으로 거른다:
    //  · 어셈블리: Unity·패키지(Packages/)·이 라이브러리(GBible.*, 샘플 제외)·플러그인(Assets/Plugins, *-firstpass)은 뺀다.
    //  · 네임스페이스: 흔한 외부 라이브러리(DG·Sirenix·Spine·TMPro …)는 뺀다.
    //  · 종류: 컴포넌트·ScriptableObject(도감 종류 제외)·어트리뷰트·예외·정적 유틸 클래스·인터페이스는 뺀다.
    // 프로젝트마다 다르면 ProjectSettings/MomoiProject/ProjectCode.json(창: MomoiProject › 내 코드 범위)에서 폴더·네임스페이스를 더한다.
    public static class ProjectCode
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("이 폴더 아래의 스크립트만 쓴다(비우면 전체 Assets). 예: Assets/Scripts/")]
            public List<string> includeFolders = new List<string>();
            [Tooltip("이 폴더 아래의 스크립트는 뺀다")]
            public List<string> excludeFolders = new List<string> { "Assets/Plugins/" };
            [Tooltip("이 네임스페이스(와 그 아래)는 뺀다")]
            public List<string> excludeNamespaces = new List<string>
            {
                "DG", "Demigiant", "Sirenix", "Spine", "TMPro", "Cinemachine", "Newtonsoft", "JetBrains", "Mono", "System", "Unity", "UnityEngine", "UnityEditor",
            };
        }

        public const string SettingsPath = "ProjectSettings/MomoiProject/ProjectCode.json";

        static Settings _settings;
        static Dictionary<string, (bool included, string reason)> _assemblies;

        public static event Action Changed;

        public static Settings Current
        {
            get
            {
                if (_settings != null) return _settings;
                try
                {
                    _settings = File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) : null;
                }
                catch (Exception)
                {
                    _settings = null;
                }
                return _settings ??= new Settings();
            }
        }

        public static void Save(Settings settings)
        {
            _settings = settings;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
            Invalidate();
        }

        public static void Invalidate()
        {
            _assemblies = null;
            Changed?.Invoke();
        }

        [InitializeOnLoadMethod]
        static void ResetOnReload()
        {
            _assemblies = null;
            _settings = null;
        }

        // ── 어셈블리 ─────────────────────────────────────────

        // 게임 어셈블리(에디터·테스트 제외)마다 내 코드인지와 그 이유.
        public static IReadOnlyDictionary<string, (bool included, string reason)> Assemblies
        {
            get
            {
                if (_assemblies != null) return _assemblies;
                var settings = Current;
                var result = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);
                foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
                    result[assembly.name] = Judge(assembly.name, assembly.sourceFiles, settings);
                return _assemblies = result;
            }
        }

        static (bool, string) Judge(string name, string[] sources, Settings settings)
        {
            if (name.StartsWith("Unity", StringComparison.Ordinal)) return (false, "Unity");
            if (name.StartsWith("GBible.", StringComparison.Ordinal) && !name.StartsWith("GBible.Samples", StringComparison.Ordinal))
                return (false, "MomoiProject 라이브러리");
            if (name.EndsWith("-firstpass", StringComparison.Ordinal)) return (false, "플러그인(Plugins·Standard Assets)");
            var files = (sources ?? Array.Empty<string>()).Select(f => f.Replace('\\', '/')).ToList();
            if (files.Count > 0 && files.All(f => f.StartsWith("Packages/", StringComparison.Ordinal))) return (false, "패키지");
            var excluded = settings.excludeFolders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Folder).ToList();
            if (files.Count > 0 && excluded.Count > 0 && files.All(f => excluded.Any(e => f.StartsWith(e, StringComparison.OrdinalIgnoreCase))))
                return (false, "뺀 폴더");
            var included = settings.includeFolders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Folder).ToList();
            if (included.Count > 0 && !files.Any(f => included.Any(i => f.StartsWith(i, StringComparison.OrdinalIgnoreCase))))
                return (false, "쓸 폴더 밖");
            return (true, "내 코드");
        }

        static string Folder(string folder)
        {
            folder = folder.Trim().Replace('\\', '/');
            return folder.EndsWith("/") ? folder : folder + "/";
        }

        public static bool IsProjectAssembly(System.Reflection.Assembly assembly) =>
            Assemblies.TryGetValue(assembly.GetName().Name, out var judged) && judged.included;

        // ── 형식 ─────────────────────────────────────────────

        // 내 코드의 형식인지(어셈블리 + 네임스페이스). 종류는 보지 않는다.
        public static bool IsProjectType(Type type)
        {
            if (type == null || !IsProjectAssembly(type.Assembly)) return false;
            var ns = type.Namespace ?? "";
            foreach (var excluded in Current.excludeNamespaces)
            {
                if (string.IsNullOrWhiteSpace(excluded)) continue;
                var e = excluded.Trim();
                if (ns == e || ns.StartsWith(e + ".", StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // 고르는 목록의 이벤트: 내 코드의 [GameEvent] + 이 라이브러리가 기본으로 내는 이벤트(도전과제 달성 등). 테스트·플러그인의 이벤트는 뺀다.
        public static IEnumerable<Type> Events() =>
            TypeResolver.GameEventTypes().Where(t => IsProjectType(t) || IsLibraryRuntime(t.Assembly));

        static bool IsLibraryRuntime(System.Reflection.Assembly assembly)
        {
            var name = assembly.GetName().Name;
            return name.StartsWith("GBible.", StringComparison.Ordinal) && !name.Contains(".Tests") && !name.EndsWith(".Editor", StringComparison.Ordinal);
        }
        // 고르는 목록에 올릴 만한 '데이터 클래스'인지: 내 코드이고, 기능 스크립트(컴포넌트 등)가 아니다. 도감 종류(GameData)는 된다.
        public static bool IsDataClass(Type type)
        {
            if (!IsProjectType(type)) return false;
            if (!type.IsVisible || type.IsInterface || type.IsGenericTypeDefinition || type.Name.Contains("<")) return false;
            if (type.IsAbstract && type.IsSealed) return false; // 정적 클래스
            if (typeof(Attribute).IsAssignableFrom(type) || typeof(Exception).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type)) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return typeof(GameData).IsAssignableFrom(type) && !type.IsAbstract;
            return true;
        }
    }
}
