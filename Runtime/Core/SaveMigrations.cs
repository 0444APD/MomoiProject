using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSystems
{
    // 저장 데이터 버전 올리기. 출시 후 데이터 구조가 바뀌면 SessionOptions.SaveVersion을 올리고 변환을 등록한다.
    // 변환은 저장 트리(키 → 값, 데이터 값은 필드 이름 → 값 딕셔너리)를 직접 고친다.
    // 필드 이름만 바뀐 경우는 필드에 [FormerlySerializedAs("예전이름")]만 달면 변환 없이 따라간다.
    public sealed class SaveMigrations
    {
        sealed class Step
        {
            public int ToVersion;
            public string Label;
            public Action<Dictionary<string, object>> Apply;
        }

        readonly List<Step> _steps = new List<Step>();

        public IEnumerable<(int toVersion, string label)> Steps => _steps.OrderBy(s => s.ToVersion).Select(s => (s.ToVersion, s.Label));

        // toVersion - 1 → toVersion 으로 올리는 변환.
        public void Add(int toVersion, string label, Action<Dictionary<string, object>> migrate)
        {
            if (toVersion < 2) throw new ArgumentOutOfRangeException(nameof(toVersion), "[GameSystems] 변환은 버전 2부터 등록합니다(버전 1이 처음).");
            _steps.Add(new Step { ToVersion = toVersion, Label = label ?? $"버전 {toVersion}", Apply = migrate ?? throw new ArgumentNullException(nameof(migrate)) });
        }

        // 플레이어 데이터 이름 변경.
        public void RenameKey(int toVersion, string oldKey, string newKey) =>
            Add(toVersion, $"키 이름 변경 {oldKey} → {newKey}", state =>
            {
                if (!state.TryGetValue(oldKey, out var value)) return;
                state.Remove(oldKey);
                state[newKey] = value;
            });

        // 플레이어 데이터 삭제(더 이상 쓰지 않는 값).
        public void RemoveKey(int toVersion, string key) =>
            Add(toVersion, $"키 삭제 {key}", state => state.Remove(key));

        // 데이터 값 안의 필드를 경로로 옮긴다. 예: MoveField(3, "inventory", "gold", "wallet.gold").
        // 리스트는 모든 요소에 적용한다(경로의 리스트 필드 이름 뒤에 [] 표기: "items[].power" ).
        public void MoveField(int toVersion, string key, string fromPath, string toPath) =>
            Add(toVersion, $"{key}: {fromPath} → {toPath}", state =>
            {
                if (!state.TryGetValue(key, out var root)) return;
                foreach (var (parent, name) in Resolve(root, fromPath))
                {
                    if (!(parent is Dictionary<string, object> map) || !map.TryGetValue(name, out var value)) continue;
                    map.Remove(name);
                    var target = toPath.Substring(0, Math.Max(0, toPath.LastIndexOf('.')));
                    var targetName = toPath.Substring(toPath.LastIndexOf('.') + 1);
                    Dictionary<string, object> holder;
                    if (TryRelative(fromPath, target, out var relative)) holder = EnsurePath(map, relative);
                    else if (root is Dictionary<string, object> top && !target.Contains("[]")) holder = EnsurePath(top, target);
                    else throw new InvalidOperationException($"[GameSystems] 저장 변환: '{fromPath}' → '{toPath}'는 옮길 수 없는 경로입니다. Add로 직접 변환하세요.");
                    holder[targetName] = value;
                }
            });

        internal int Run(Dictionary<string, object> state, int from, int to, Journal journal)
        {
            int applied = 0;
            for (int version = from + 1; version <= to; version++)
            {
                foreach (var step in _steps.Where(s => s.ToVersion == version))
                {
                    step.Apply(state);
                    applied++;
                    journal?.Append(JournalKind.System, "Save", $"저장 데이터 변환 v{version - 1} → v{version}: {step.Label}");
                }
            }
            return applied;
        }

        // "a.b[].c" → c를 가진 부모 딕셔너리들.
        static IEnumerable<(object parent, string name)> Resolve(object root, string path)
        {
            var parts = path.Split('.');
            IEnumerable<object> current = new[] { root };
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                current = current.SelectMany(node => Descend(node, part)).ToList();
            }
            var last = parts[parts.Length - 1];
            return current.Select(node => (node, last));
        }

        static IEnumerable<object> Descend(object node, string part)
        {
            var many = part.EndsWith("[]", StringComparison.Ordinal);
            var name = many ? part.Substring(0, part.Length - 2) : part;
            if (!(node is Dictionary<string, object> map) || !map.TryGetValue(name, out var child) || child == null) return Enumerable.Empty<object>();
            if (many) return child is List<object> list ? list : Enumerable.Empty<object>();
            return new[] { child };
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
