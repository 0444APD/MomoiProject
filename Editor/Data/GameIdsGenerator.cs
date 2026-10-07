using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace GBible.EditorTools
{
    // 데이터베이스의 모든 ID를 C# 상수로 만든다(GameIds.cs). 코드에서도 ID를 글자로 쓰지 않고 자동완성으로 고르게 해
    // 오타를 컴파일 단계에서 막는다. 데이터가 바뀌면 자동으로 다시 만들고, 내용이 같으면 파일을 건드리지 않는다.
    //
    //   GameIds.PlayerData.PlayerInventory   → PlayerDataKey<Inventory>("player.inventory")   (플레이어 데이터: 값 형식까지 붙는다)
    //   GameIds.Stat.KillsTotal         → "kills_total"                             (그 밖의 항목: ID 문자열)
    //
    // 파일 위치: 처음 만들 때 플레이어 데이터의 값 형식이 들어 있는 어셈블리(asmdef) 폴더의 Generated/ 아래 — 그래야 그 형식을 참조할 수 있다.
    // 형식이 없거나 Assembly-CSharp에 있으면 Assets/MomoiProject/Generated/. asmdef 어셈블리 안에 한 번 만든 뒤에는 저절로 옮기지 않는다(옮기려면 설정 파일의 folder).
    // 상수 이름: ID에서 만들고(player.inventory → PlayerInventory), 겹치면 뒤에 _2 … 를 붙인다. 이미 있던 상수는 지난 파일의 이름을 그대로 쓴다.
    public static class GameIdsGenerator
    {
        public const string FileName = "GameIds.cs";
        public const string DefaultFolder = ProjectFolders.Generated;
        const string SettingsPath = "ProjectSettings/MomoiProject/Ids.json";
        // 플레이·컴파일 중이라 미룬 다시 만들기(도메인을 다시 읽어도 남는다).
        const string PendingKey = "GBible.GameIdsGenerator.Pending";

        [Serializable]
        sealed class Settings
        {
            // 비워 두면 자동으로 고르되, asmdef 어셈블리 안에 만든 뒤로는 그 자리(lastPath)에 둔다. 적으면 그 자리로(예전 파일은 지운다).
            public string folder = "";
            // 마지막으로 만든 파일.
            public string lastPath = "";
        }

        static bool _scheduled;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorDatabase.Changed -= Schedule;
            EditorDatabase.Changed += Schedule;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            CompilationPipeline.compilationFinished -= OnCompiled;
            CompilationPipeline.compilationFinished += OnCompiled;
            if (SessionState.GetBool(PendingKey, false)) Schedule();
        }

        static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                // 플레이·컴파일 중이면 버리지 않고 미뤘다가 끝난 뒤에 만든다(버리면 저장한 ID가 GameIds에 늦게 들어간다).
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                {
                    SessionState.SetBool(PendingKey, true);
                    return;
                }
                SessionState.EraseBool(PendingKey);
                Generate();
            };
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(PendingKey, false)) Schedule();
        }

        // 컴파일이 실패해 도메인을 다시 읽지 않은 경우에도 미룬 것을 만든다(성공하면 다시 읽은 뒤 Hook이 만든다).
        static void OnCompiled(object _)
        {
            if (SessionState.GetBool(PendingKey, false)) Schedule();
        }

        [MenuItem("MomoiProject/식별자 코드 다시 만들기", priority = 30)]
        public static void GenerateFromMenu()
        {
            var path = Generate(force: true);
            Debug.Log(path != null ? $"[MomoiProject] 식별자 코드를 만들었습니다: {path}" : "[MomoiProject] 만들 식별자가 없습니다.");
        }

        static string _suggested;

        // 만든(또는 이미 최신인) 파일 경로. 식별자가 하나도 없으면 null.
        public static string Generate(bool force = false)
        {
            // 처음 상태로 되돌리는 중(파일을 옮긴 뒤 Unity를 다시 열기 전): 옮긴 자리에 설정·생성 코드를 다시 만들지 않는다.
            if (ProjectReset.Resetting) return null;
            var all = EditorDatabase.Instance.Committed.Where(d => d != null && !string.IsNullOrEmpty(d.Id)).ToList();
            if (all.Count == 0) return null;

            var settings = LoadSettings();
            var keys = all.OfType<PlayerDataEntry>().ToList();
            var made = !string.IsNullOrEmpty(settings.lastPath) && File.Exists(settings.lastPath);
            string folder;
            if (!string.IsNullOrEmpty(settings.folder)) folder = settings.folder.TrimEnd('/');
            else if (made && AssemblyOf(settings.lastPath) != "Assembly-CSharp")
            {
                // asmdef 어셈블리(Rules 등) 안에 한 번 만든 뒤에는 저절로 옮기지 않는다 — 옮기면 그 어셈블리에서 GameIds를 쓰던 코드가 컴파일되지 않는다
                // (형식을 잠깐 못 찾는 컴파일 오류 중에도 그대로). 플레이어 데이터 형식이 다른 어셈블리로 가서 더 맞는 자리가 생겼으면 알리기만 한다
                // (그 형식의 키는 이 자리에서 ID 글자로만 만들어진다). 기본 자리(Assembly-CSharp — 시작 설정 직후 형식이 아직 없을 때)에서는 맞는 자리로 옮긴다.
                folder = Path.GetDirectoryName(settings.lastPath).Replace('\\', '/');
                var suggested = AutoFolder(keys);
                if (suggested != folder && suggested != _suggested)
                {
                    _suggested = suggested;
                    Debug.LogWarning($"[MomoiProject] GameIds.cs를 {folder}에 그대로 둡니다. 플레이어 데이터 형식을 모두 참조할 수 있는 자리는 {suggested}입니다 — " +
                                     $"옮기려면 {SettingsPath}의 \"folder\"에 \"{suggested}\"를 적으세요(예전 파일은 저절로 지워지고, 그 자리 어셈블리에서 GameIds를 쓰던 코드는 참조를 확인하세요).");
                }
            }
            else folder = AutoFolder(keys);
            var path = folder + "/" + FileName;
            var previous = made ? File.ReadAllText(settings.lastPath) : File.Exists(path) ? File.ReadAllText(path) : null;
            var text = Build(all, AssemblyOf(path), previous);

            // 설정 파일의 folder로 자리를 바꿨으면 예전 파일을 지운다.
            if (!string.IsNullOrEmpty(settings.lastPath) && settings.lastPath != path && File.Exists(settings.lastPath))
                AssetDatabase.DeleteAsset(settings.lastPath);
            if (settings.lastPath != path)
            {
                settings.lastPath = path;
                SaveSettings(settings);
            }
            if (!force && File.Exists(path) && File.ReadAllText(path) == text) return path;

            EditorDatabase.EnsureFolder(folder);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return path;
        }

        // ── 파일 내용 ───────────────────────────────────────────

        // previous: 지난번에 만든 파일 내용(있으면 이미 있던 상수의 이름을 그대로 쓴다 — 새 항목이 정렬 순서로 앞에 와도 기존 코드의 상수가 다른 ID를 가리키지 않게).
        internal static string Build(IReadOnlyList<GameData> all, string targetAssembly, string previous = null)
        {
            var names = PreviousNames(previous);
            var visible = VisibleAssemblies(targetAssembly);
            // 이 위치에서 GBible.Core를 참조할 수 없으면 타입이 붙은 키(PlayerDataKey<T>)를 쓸 수 없다.
            bool typedKeys = visible.Contains("GBible.Core");
            var b = new StringBuilder();
            b.AppendLine("// <auto-generated>");
            b.AppendLine("// DB 창의 식별자로 만든 파일입니다. 직접 고치지 마세요(데이터가 바뀌면 다시 만들어집니다).");
            b.AppendLine("// 메뉴: MomoiProject > 식별자 코드 다시 만들기");
            b.AppendLine("// </auto-generated>");
            b.AppendLine("namespace GBible.Ids");
            b.AppendLine("{");
            b.AppendLine("    public static class GameIds");
            b.AppendLine("    {");

            var groups = all.GroupBy(d => d.GetType()).OrderBy(g => g.Key == typeof(PlayerDataEntry) ? "" : g.Key.Name, StringComparer.Ordinal).ThenBy(g => g.Key.FullName, StringComparer.Ordinal);
            var frames = SpriteFramesWithSizes(all);
            // 안쪽 클래스 이름이 겹치지 않게(짧은 이름이 같은 종류 — 프로젝트의 LocationData와 선택 기능의 LocationData 등 —, GameIds 자신, 아래 SpriteSize) 뒤에 _2 …
            var classNames = new HashSet<string>(StringComparer.Ordinal) { "GameIds" };
            if (frames.Count > 0) classNames.Add("SpriteSize");
            bool firstGroup = true;
            foreach (var group in groups)
            {
                if (!firstGroup) b.AppendLine();
                firstGroup = false;
                var info = DataTypes.Find(group.Key);
                var className = Unique(group.Key == typeof(PlayerDataEntry) ? "PlayerData" : ClassName(group.Key), classNames);
                b.AppendLine($"        /// <summary>{Escape(info?.DisplayName ?? group.Key.Name)}</summary>");
                b.AppendLine($"        public static class {className}");
                b.AppendLine("        {");
                var items = group.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
                var memberNames = Assign(names, className, items.Select(d => d.Id).ToList(), new HashSet<string>(StringComparer.Ordinal) { className });
                for (int index = 0; index < items.Count; index++)
                {
                    var data = items[index];
                    var name = memberNames[index];
                    if (data is PlayerDataEntry key)
                    {
                        var type = key.ValueType;
                        var zone = key.zone == DataZone.Meta ? "메타" : "세션";
                        if (typedKeys && type != null && IsNameable(type) && IsVisible(type, visible))
                        {
                            var typeName = CSharpName(type);
                            b.AppendLine($"            /// <summary>{Escape(data.DisplayName)} ({zone} · {Escape(StorableTypes.Label(type))})</summary>");
                            b.AppendLine($"            public static readonly global::GBible.PlayerDataKey<{typeName}> {name} = new global::GBible.PlayerDataKey<{typeName}>(\"{data.Id}\");");
                        }
                        else
                        {
                            // 이 위치에서 참조할 수 없는 형식이면 ID 문자열만 둔다(설정에서 파일 위치를 그 형식의 어셈블리로 바꾸면 타입이 붙는다).
                            b.AppendLine($"            /// <summary>{Escape(data.DisplayName)} ({zone} · 형식을 이 위치에서 참조할 수 없어 ID만 제공)</summary>");
                            b.AppendLine($"            public const string {name} = \"{data.Id}\";");
                        }
                    }
                    else
                    {
                        b.AppendLine($"            /// <summary>{Escape(data.DisplayName)}</summary>");
                        b.AppendLine($"            public const string {name} = \"{data.Id}\";");
                    }
                }
                b.AppendLine("        }");
            }
            AppendSpriteSizes(b, frames, names);
            b.AppendLine("    }");
            if (typedKeys) AppendPlayerDataView(b, all.OfType<PlayerDataEntry>(), visible, names);
            b.AppendLine("}");
            return b.ToString().Replace("\r\n", "\n");
        }

        // 크기 이름 상수를 만들 틀(올바른 이름의 크기가 하나라도 있는 것, ID 순).
        static List<SpriteFrameData> SpriteFramesWithSizes(IEnumerable<GameData> all) =>
            all.OfType<SpriteFrameData>().Where(f => !string.IsNullOrEmpty(f.Id) && f.sizes.Any(s => s != null && SpriteFrameData.IsValidName(s.name, out _)))
                .OrderBy(f => f.Id, StringComparer.Ordinal).ToList();

        // 스프라이트 틀의 크기 이름: SpriteSpecs.Get(원본, GameIds.SpriteSize.Character.Icon) — 틀마다 안쪽 클래스, 크기마다 상수.
        static void AppendSpriteSizes(StringBuilder b, List<SpriteFrameData> list, Dictionary<string, string> names)
        {
            if (list.Count == 0) return;
            b.AppendLine();
            b.AppendLine("        /// <summary>스프라이트 틀의 크기 이름: SpriteSpecs.Get(원본, GameIds.SpriteSize.틀.크기)</summary>");
            b.AppendLine("        public static class SpriteSize");
            b.AppendLine("        {");
            var classNames = Assign(names, "SpriteSize", list.Select(f => f.Id).ToList(), new HashSet<string>(StringComparer.Ordinal) { "SpriteSize" });
            for (int index = 0; index < list.Count; index++)
            {
                var frame = list[index];
                var className = classNames[index];
                b.AppendLine($"            /// <summary>{Escape(frame.Id)}</summary>");
                b.AppendLine($"            public static class {className}");
                b.AppendLine("            {");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sizes = frame.sizes.Where(size => size != null && SpriteFrameData.IsValidName(size.name, out _) && seen.Add(size.name)).ToList();
                var sizeNames = Assign(names, "SpriteSize:" + frame.Id, sizes.Select(s => s.name).ToList(), new HashSet<string>(StringComparer.Ordinal) { className });
                for (int i = 0; i < sizes.Count; i++)
                {
                    b.AppendLine($"                /// <summary>{sizes[i].width}×{sizes[i].height}</summary>");
                    b.AppendLine($"                public const string {sizeNames[i]} = \"{sizes[i].name}\";");
                }
                b.AppendLine("            }");
            }
            b.AppendLine("        }");
        }

        // 플레이어 데이터를 이름으로 읽고 쓰는 창구: Game.Data().PlayerGold += 100;  Game.Data().PlayerInventory.slots.Add(…);
        // 숫자·글자 같은 단순 값은 읽고 쓰기(쓰면 기록), 클래스·리스트 값은 꺼내서 안쪽 필드를 고친다(행동 끝·프레임 끝 Sync가 기록).
        static void AppendPlayerDataView(StringBuilder b, IEnumerable<PlayerDataEntry> keys, HashSet<string> visible, Dictionary<string, string> names)
        {
            b.AppendLine();
            b.AppendLine("    /// <summary>플레이어 데이터를 이름으로: Game.Data().PlayerGold += 100</summary>");
            b.AppendLine("    public readonly struct PlayerDataView");
            b.AppendLine("    {");
            b.AppendLine("        readonly global::GBible.GameApi _game;");
            b.AppendLine("        public PlayerDataView(global::GBible.GameApi game) { _game = game; }");
            var shown = keys.Where(k => k.ValueType != null && IsNameable(k.ValueType) && IsVisible(k.ValueType, visible)).OrderBy(k => k.Id, StringComparer.Ordinal).ToList();
            var memberNames = Assign(names, "PlayerDataView", shown.Select(k => k.Id).ToList(), new HashSet<string>(StringComparer.Ordinal) { "PlayerDataView" });
            for (int index = 0; index < shown.Count; index++)
            {
                var key = shown[index];
                var type = key.ValueType;
                var name = memberNames[index];
                var typeName = CSharpName(type);
                var id = key.Id.Replace("\"", "\\\"");
                b.AppendLine($"        /// <summary>{Escape(key.DisplayName)}</summary>");
                if (DataModel.Of(type).Kind == DataKind.Scalar || type.IsEnum)
                    b.AppendLine($"        public {typeName} {name} {{ get => _game.Session.Data.Get<{typeName}>(\"{id}\"); set => _game.Session.Data.Set(\"{id}\", value); }}");
                else
                    b.AppendLine($"        public {typeName} {name} => _game.Session.Data.Get<{typeName}>(\"{id}\");");
            }
            b.AppendLine("    }");
            b.AppendLine();
            b.AppendLine("    public static class PlayerDataAccess");
            b.AppendLine("    {");
            b.AppendLine("        /// <summary>플레이어 데이터를 이름으로 읽고 쓴다.</summary>");
            b.AppendLine("        public static PlayerDataView Data(this global::GBible.GameApi game) => new PlayerDataView(game);");
            b.AppendLine("    }");
        }

        static string ClassName(Type type)
        {
            var name = type.Name.EndsWith("Data", StringComparison.Ordinal) && type.Name.Length > 4 ? type.Name.Substring(0, type.Name.Length - 4) : type.Name;
            return name == "PlayerData" ? "PlayerDataData" : name;
        }

        static string Unique(string name, HashSet<string> used)
        {
            var candidate = name;
            for (int i = 2; !used.Add(candidate); i++) candidate = name + "_" + i;
            return candidate;
        }

        // 이름 짓기: 지난 파일에서 이 ID가 쓰던 이름이 아직 맞으면(바탕 이름이나 그 뒤 _N) 먼저 그대로 주고, 나머지는 겹치지 않게 새로 짓는다.
        // ids는 같은 자리의 것을 정렬된 차례로(같은 ID가 둘이면 둘째는 새 이름).
        static List<string> Assign(Dictionary<string, string> previous, string scope, IReadOnlyList<string> ids, HashSet<string> used)
        {
            var result = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                if (!previous.TryGetValue(scope + "|" + ids[i], out var old)) continue;
                var bare = IdRules.ToIdentifier(ids[i]);
                if ((old == bare || IsSuffixed(old, bare)) && used.Add(old)) result[i] = old;
            }
            for (int i = 0; i < ids.Count; i++)
                if (result[i] == null) result[i] = Unique(IdRules.ToIdentifier(ids[i]), used);
            return result.ToList();
        }

        static bool IsSuffixed(string name, string bare) =>
            name.Length > bare.Length + 1 && name.StartsWith(bare + "_", StringComparison.Ordinal) && name.Substring(bare.Length + 1).All(char.IsDigit);

        // ── 지난 파일의 상수 이름 ───────────────────────────────
        // 자리|ID → 상수 이름. 자리 = GameIds 안쪽 클래스 이름, 스프라이트 틀은 "SpriteSize", 틀의 크기는 "SpriteSize:<틀 ID>", 이름으로 읽는 창구는 "PlayerDataView".
        // 이 생성기가 쓴 모양(들여쓰기 그대로)만 읽는다 — 다른 모양이면 비어 있다(이름을 새로 짓는다).

        static readonly Regex ClassLine = new Regex(@"^( +)public static class (\w+)\s*$");
        static readonly Regex SummaryLine = new Regex(@"^ +/// <summary>(.*)</summary>\s*$");
        static readonly Regex ConstLine = new Regex(@"^ +public const string (\w+) = ""((?:[^""\\]|\\.)*)"";");
        static readonly Regex KeyLine = new Regex(@"^ +public static readonly global::GBible\.PlayerDataKey<.+> (\w+) = new global::GBible\.PlayerDataKey<.+>\(""((?:[^""\\]|\\.)*)""\);");
        static readonly Regex ViewLine = new Regex(@"^ +public .+? (\w+) (?:\{ get => |=> )_game\.Session\.Data\.Get<.+?>\(""((?:[^""\\]|\\.)*)""\);");

        internal static Dictionary<string, string> PreviousNames(string text)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return names;
            string scope = null, frame = null, summary = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var type = ClassLine.Match(line);
                if (type.Success)
                {
                    var depth = type.Groups[1].Length;
                    if (depth == 8)
                    {
                        scope = type.Groups[2].Value;
                        frame = null;
                    }
                    else if (depth == 12 && scope == "SpriteSize" && summary != null)
                    {
                        frame = Unescape(summary);
                        names["SpriteSize|" + frame] = type.Groups[2].Value;
                    }
                    summary = null;
                    continue;
                }
                if (line.StartsWith("    public readonly struct PlayerDataView", StringComparison.Ordinal))
                {
                    scope = "PlayerDataView";
                    frame = null;
                    continue;
                }
                var note = SummaryLine.Match(line);
                if (note.Success)
                {
                    summary = note.Groups[1].Value;
                    continue;
                }
                var member = ConstLine.Match(line);
                if (!member.Success) member = KeyLine.Match(line);
                if (!member.Success && scope == "PlayerDataView") member = ViewLine.Match(line);
                if (member.Success && scope != null)
                    names[(frame != null ? "SpriteSize:" + frame : scope) + "|" + member.Groups[2].Value.Replace("\\\"", "\"")] = member.Groups[1].Value;
            }
            return names;
        }

        static string Unescape(string text) => (text ?? "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

        static string Escape(string text) => (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", " ");

        static bool IsNameable(Type type) =>
            type.IsVisible && !type.IsGenericParameter && (!type.IsGenericType || type.GetGenericArguments().All(IsNameable));

        public static string CSharpName(Type type)
        {
            if (!type.IsEnum)
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return "bool";
                case TypeCode.Int32: return "int";
                case TypeCode.Int64: return "long";
                case TypeCode.Single: return "float";
                case TypeCode.Double: return "double";
                case TypeCode.String: return "string";
            }
            if (type.IsArray) return CSharpName(type.GetElementType()) + "[]";
            var name = type.IsNested ? CSharpName(type.DeclaringType) + "." + type.Name : "global::" + (string.IsNullOrEmpty(type.Namespace) ? "" : type.Namespace + ".") + type.Name;
            if (!type.IsGenericType) return name;
            name = name.Substring(0, name.IndexOf('`'));
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(CSharpName))}>";
        }

        // ── 위치와 참조 가능 여부 ───────────────────────────────

        static string AutoFolder(List<PlayerDataEntry> keys)
        {
            var assemblies = keys.Select(k => k.ValueType).Where(t => t != null)
                .SelectMany(t => new[] { t }.Concat(t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes))
                .Select(t => t.Assembly.GetName().Name)
                .Where(IsProjectAssembly)
                .Distinct().ToList();
            if (assemblies.Count == 0) return DefaultFolder;

            // 나머지 형식을 모두 참조할 수 있는 어셈블리를 고른다.
            foreach (var candidate in assemblies)
            {
                var visible = VisibleAssemblies(candidate);
                if (!assemblies.All(visible.Contains)) continue;
                var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(candidate);
                if (string.IsNullOrEmpty(asmdef)) return DefaultFolder; // Assembly-CSharp 등
                return Path.GetDirectoryName(asmdef).Replace('\\', '/') + "/Generated";
            }
            return DefaultFolder;
        }

        // 어셈블리 목록은 스크립트를 다시 불러올 때만 바뀐다. 매번 묻지 않는다(GetAssemblies는 느리고, 플러그인 경고를 매번 다시 출력한다).
        static HashSet<string> _playerAssemblies;

        [UnityEditor.Callbacks.DidReloadScripts]
        static void ClearAssemblyCache()
        {
            _playerAssemblies = null;
            _editorAssemblies = null;
        }

        static UnityEditor.Compilation.Assembly[] _editorAssemblies;
        static UnityEditor.Compilation.Assembly[] EditorAssemblies => _editorAssemblies ??= CompilationPipeline.GetAssemblies(AssembliesType.Editor);

        static bool IsProjectAssembly(string name)
        {
            _playerAssemblies ??= new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name), StringComparer.Ordinal);
            return _playerAssemblies.Contains(name) &&
                   !name.StartsWith("Unity", StringComparison.Ordinal) && !name.StartsWith("GBible.", StringComparison.Ordinal);
        }

        static string AssemblyOf(string scriptPath)
        {
            var name = CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath);
            return string.IsNullOrEmpty(name) ? "Assembly-CSharp" : Path.GetFileNameWithoutExtension(name);
        }

        // 이 어셈블리에서 참조할 수 있는 어셈블리들(자신 포함, 참조를 따라간 전체).
        static HashSet<string> VisibleAssemblies(string assemblyName)
        {
            var map = new Dictionary<string, UnityEditor.Compilation.Assembly>(StringComparer.Ordinal);
            foreach (var a in EditorAssemblies) map[a.name] = a;
            var result = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(assemblyName);
            while (stack.Count > 0)
            {
                var name = stack.Pop();
                if (!result.Add(name) || !map.TryGetValue(name, out var assembly)) continue;
                foreach (var reference in assembly.assemblyReferences) stack.Push(reference.name);
            }
            return result;
        }

        static bool IsVisible(Type type, HashSet<string> visible)
        {
            var name = type.Assembly.GetName().Name;
            bool ok = visible.Contains(name) || !IsProjectAssemblyOrPackage(name);
            return ok && (!type.IsGenericType || type.GetGenericArguments().All(t => IsVisible(t, visible)));
        }

        // 시스템·엔진 어셈블리(mscorlib 등)는 어디서나 참조된다.
        static bool IsProjectAssemblyOrPackage(string name) =>
            EditorAssemblies.Any(a => a.name == name);

        static Settings LoadSettings()
        {
            try
            {
                return File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings() : new Settings();
            }
            catch (Exception)
            {
                return new Settings();
            }
        }

        static void SaveSettings(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
        }
    }
}
