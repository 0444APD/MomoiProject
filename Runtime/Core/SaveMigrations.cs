using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 저장 데이터 버전 올리기. 출시 후 데이터 구조가 바뀌면 변환을 등록한다(toVersion = 다음 버전). 게임의 저장 버전은 등록한 변환의 가장 높은 버전을 따라간다
    // (SessionOptions.SaveVersion을 따로 올리지 않아도 된다. 더 높게 적어 두면 그 값).
    // 변환은 저장 트리(키 → 값, 데이터 값은 필드 이름 → 값 딕셔너리)를 직접 고친다.
    // 같은 버전의 변환은 등록한 순서대로 돈다(DB › 저장 변환 표가 ID 글자 순서로 먼저, 그다음 코드의 Configuring). 순서가 중요하면 버전을 나눈다.
    // 필드 이름만 바뀐 경우는 필드에 [FormerlySerializedAs("예전이름")]만 달면 변환 없이 따라간다.
    public sealed class SaveMigrations
    {
        sealed class Step
        {
            public int ToVersion;
            public string Label;
            // 두 번째 인자: 경고 남기기(옮길 것을 찾지 못함 등). 한 번 적용에 한 번만 남는다.
            public Action<Dictionary<string, object>, Action<string>> Apply;
        }

        readonly List<Step> _steps = new List<Step>();

        public IEnumerable<(int toVersion, string label)> Steps => _steps.OrderBy(s => s.ToVersion).Select(s => (s.ToVersion, s.Label));

        // 등록한 변환 중 가장 높은 버전(없으면 1).
        public int LatestVersion => _steps.Count == 0 ? 1 : _steps.Max(s => s.ToVersion);

        // 버전과 상관없이 저장을 불러올 때마다(버전 변환 뒤에) 적용하는 변환. 런타임이 'DB 항목 ID 바꾸기 이어받기'에 쓴다.
        public Action<Dictionary<string, object>> EveryLoad;

        // toVersion - 1 → toVersion 으로 올리는 변환.
        public void Add(int toVersion, string label, Action<Dictionary<string, object>> migrate) =>
            AddStep(toVersion, label, migrate == null ? null : new Action<Dictionary<string, object>, Action<string>>((state, warn) => migrate(state)));

        void AddStep(int toVersion, string label, Action<Dictionary<string, object>, Action<string>> apply)
        {
            if (toVersion < 2) throw new ArgumentOutOfRangeException(nameof(toVersion), "[MomoiProject] 변환은 버전 2부터 등록합니다(버전 1이 처음).");
            _steps.Add(new Step { ToVersion = toVersion, Label = label ?? $"버전 {toVersion}", Apply = apply ?? throw new ArgumentNullException("migrate") });
        }

        // 플레이어 데이터 이름 변경. label: 기록·경고에 보일 이름(비우면 내용으로 짓는다 — DB 표는 항목 ID를 붙인다).
        public void RenameKey(int toVersion, string oldKey, string newKey, string label = null) =>
            Add(toVersion, label ?? $"키 이름 변경 {oldKey} → {newKey}", state =>
            {
                if (!state.TryGetValue(oldKey, out var value)) return;
                state.Remove(oldKey);
                state[newKey] = value;
            });

        // 플레이어 데이터 삭제(더 이상 쓰지 않는 값).
        public void RemoveKey(int toVersion, string key, string label = null) =>
            Add(toVersion, label ?? $"키 삭제 {key}", state => state.Remove(key));

        // 데이터 값 안의 필드를 경로로 옮긴다. 예: MoveField(3, "inventory", "gold", "wallet.gold").
        // 리스트는 모든 요소에 적용한다(경로의 리스트 필드 이름 뒤에 [] 표기: "items[].power"). 키의 값 자체가 리스트면 "[].power".
        // 그 키가 있는 저장인데 예전 경로를 하나도 찾지 못하면(오타 · 리스트가 아닌 데 [] · 키의 값이 리스트) 그 값은 새 자리로 오지 않고 버려지므로 기록에 경고를 남긴다.
        // 빈 리스트·null 값은 옮길 것이 없을 뿐이라 경고하지 않는다. 경로 모양은 CheckMove로 미리 볼 수 있다.
        public void MoveField(int toVersion, string key, string fromPath, string toPath, string label = null)
        {
            var target = ParentOf(toPath ?? "");
            var targetName = (toPath ?? "").Substring((toPath ?? "").LastIndexOf('.') + 1);
            AddStep(toVersion, label ?? $"{key}: {fromPath} → {toPath}", (state, warn) =>
            {
                // 이 저장 파일에 그 키가 없으면(프로필·판 중 다른 쪽의 키, 이미 이름을 바꾼 키) 할 일이 없다.
                if (!state.TryGetValue(key, out var root) || root == null) return;
                string missing = null;
                var moved = 0;
                foreach (var (parent, name) in Resolve(root, fromPath ?? "", ref missing))
                {
                    if (parent == null) continue;
                    if (!(parent is Dictionary<string, object> map))
                    {
                        missing ??= $"'{name}' 칸을 가질 값이 클래스가 아닙니다";
                        continue;
                    }
                    if (!map.TryGetValue(name, out var value))
                    {
                        missing ??= $"'{name}' 칸이 없습니다";
                        continue;
                    }
                    map.Remove(name);
                    Dictionary<string, object> holder;
                    if (TryRelative(fromPath, target, out var relative)) holder = EnsurePath(map, relative);
                    else if (root is Dictionary<string, object> top && !target.Contains("[]")) holder = EnsurePath(top, target);
                    else throw new InvalidOperationException($"[MomoiProject] 저장 변환: '{fromPath}' → '{toPath}'는 옮길 수 없는 경로입니다. Add로 직접 변환하세요.");
                    holder[targetName] = value;
                    moved++;
                }
                if (moved > 0 || missing == null) return;
                var hint = root is List<object> && !(fromPath ?? "").StartsWith("[]", StringComparison.Ordinal) ? $" 키의 값이 리스트면 경로를 []로 시작합니다(예: [].{fromPath})." : "";
                warn($"'{key}'에서 '{fromPath}'를 찾지 못해 옮기지 못했습니다({missing}). 그 값은 '{toPath}'로 오지 않고 버려집니다 — 경로를 확인하세요.{hint}");
            });
        }

        // MoveField 경로가 틀렸는지 미리 본다(편집기의 DB 검사 · 코드에서 등록 전에). 괜찮으면 null, 틀렸으면 까닭.
        //   빈 경로 · 빈 칸(점이 겹침·처음이나 끝의 점) · 공백 · 리스트 요소 번호([0]) · 마지막 칸의 [] · 같은 경로 · 옮길 수 없는 모양(리스트 요소 안에서 다른 리스트로, 키의 값이 리스트인데 밖으로).
        public static string CheckMove(string fromPath, string toPath)
        {
            var problem = CheckPath(fromPath, "예전 경로") ?? CheckPath(toPath, "새 경로");
            if (problem != null) return problem;
            if (fromPath == toPath) return "예전 경로와 새 경로가 같습니다.";
            var target = ParentOf(toPath);
            if (!TryRelative(fromPath, target, out _) && (target.Contains("[]") || fromPath.StartsWith("[]", StringComparison.Ordinal)))
                return $"'{fromPath}' → '{toPath}'는 옮길 수 없습니다(리스트 요소 안의 칸은 같은 요소 안으로만 옮깁니다). 값을 계산해 옮기려면 코드의 Migrations.Add로 쓰세요.";
            return null;
        }

        static string CheckPath(string path, string what)
        {
            if (string.IsNullOrWhiteSpace(path)) return $"{what}를 적으세요.";
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var many = part.EndsWith("[]", StringComparison.Ordinal);
                var name = many ? part.Substring(0, part.Length - 2) : part;
                if (part.Length == 0) return $"{what} '{path}': 빈 칸이 있습니다(점이 겹치거나 처음·끝에 점이 있습니다).";
                if (name.Any(char.IsWhiteSpace)) return $"{what} '{path}': 공백을 넣지 않습니다.";
                if (name.IndexOf('[') >= 0 || name.IndexOf(']') >= 0)
                    return $"{what} '{path}': '{part}'를 읽을 수 없습니다. 리스트의 모든 요소는 items[].power처럼 []만 씁니다(요소 번호 [0]은 쓸 수 없습니다).";
                if (i == parts.Length - 1 && many) return $"{what} '{path}': 마지막 칸은 옮길 필드 이름입니다([]를 붙이지 않습니다).";
            }
            return null;
        }

        internal int Run(Dictionary<string, object> state, int from, int to, Journal journal)
        {
            int applied = 0;
            for (int version = from + 1; version <= to; version++)
            {
                foreach (var step in _steps.Where(s => s.ToVersion == version))
                {
                    string warning = null;
                    step.Apply(state, message => warning ??= message);
                    applied++;
                    journal?.Append(JournalKind.System, "Save", $"저장 데이터 변환 v{version - 1} → v{version}: {step.Label}");
                    if (warning != null) journal?.Append(JournalKind.Warning, "Save", $"저장 변환 '{step.Label}'(v{version - 1} → v{version})을 적용하지 못했습니다: {warning}");
                }
            }
            return applied;
        }

        static string ParentOf(string path) => path.Substring(0, Math.Max(0, path.LastIndexOf('.')));

        // "a.b[].c" → c를 가진 부모들. 저장과 맞지 않는 칸(없는 칸 · 리스트가 아닌 데 [] · 클래스가 아닌 값)을 만나면 missing에 첫 까닭을 남긴다.
        // 비어 있는 리스트·null 값은 옮길 것이 없을 뿐이라 까닭으로 치지 않는다.
        static List<(object parent, string name)> Resolve(object root, string path, ref string missing)
        {
            var parts = path.Split('.');
            IList<object> current = new List<object> { root };
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var next = new List<object>();
                foreach (var node in current) next.AddRange(Descend(node, parts[i], ref missing));
                current = next;
            }
            var last = parts[parts.Length - 1];
            return current.Select(node => (node, last)).ToList();
        }

        // 칸 하나 내려가기. "items[]" = items 리스트의 모든 요소, "[]"만 있으면 지금 값(리스트)의 모든 요소.
        static IEnumerable<object> Descend(object node, string part, ref string missing)
        {
            var many = part.EndsWith("[]", StringComparison.Ordinal);
            var name = many ? part.Substring(0, part.Length - 2) : part;
            if (node == null) return Array.Empty<object>();
            var child = node;
            if (name.Length > 0)
            {
                if (!(node is Dictionary<string, object> map))
                {
                    missing ??= $"'{name}' 칸을 가질 값이 클래스가 아닙니다";
                    return Array.Empty<object>();
                }
                if (!map.TryGetValue(name, out child))
                {
                    missing ??= $"'{name}' 칸이 없습니다";
                    return Array.Empty<object>();
                }
                if (child == null) return Array.Empty<object>();
            }
            if (!many) return new[] { child };
            if (child is List<object> list) return list;
            missing ??= name.Length > 0 ? $"'{name}' 칸이 리스트가 아닙니다" : "키의 값이 리스트가 아닙니다";
            return Array.Empty<object>();
        }

        // 새 경로의 부모가 원래 필드의 부모 아래에 있으면(같은 요소 안에서 옮기는 경우) 그 상대 경로.
        static bool TryRelative(string fromPath, string targetParent, out string relative)
        {
            var fromParent = fromPath.Contains('.') ? fromPath.Substring(0, fromPath.LastIndexOf('.')) : "";
            relative = null;
            if (targetParent == fromParent) relative = "";
            else if (string.IsNullOrEmpty(fromParent)) relative = targetParent;
            else if (targetParent.StartsWith(fromParent + ".", StringComparison.Ordinal)) relative = targetParent.Substring(fromParent.Length + 1);
            return relative != null;
        }

        static Dictionary<string, object> EnsurePath(Dictionary<string, object> map, string path)
        {
            var current = map;
            foreach (var part in path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!(current.TryGetValue(part, out var child) && child is Dictionary<string, object> next))
                {
                    next = new Dictionary<string, object>(StringComparer.Ordinal);
                    current[part] = next;
                }
                current = next;
            }
            return current;
        }
    }
}
