using System;
using System.Collections.Generic;

namespace GBible
{
    // 관찰 계약: 모든 시스템은 현재 값 전체를 트리로 내보낼 수 있어야 한다.
    // 변경 기록은 Journal이, 테스트 조작은 DebugCommands가 담당한다.
    public interface IInspectable
    {
        string InspectName { get; }
        void Inspect(InspectNode root);
    }

    public sealed class InspectNode
    {
        public string Label;
        public string Value;
        // 플레이어 데이터 저장소 키. 있으면 모니터에서 값을 직접 바꿀 수 있다.
        public string PlayerDataKey;
        // 클래스·컬렉션 값의 저장소 키(모니터가 탐색기로 연다).
        public string DataKey;
        // 작고 옅게 붙는 보조 정보(구역, 분류 등).
        public string Tag;
        public string Note;
        public readonly List<InspectNode> Children = new List<InspectNode>();

        public InspectNode(string label, string value = null, string stateKey = null)
        {
            Label = label;
            Value = value;
            PlayerDataKey = stateKey;
        }

        public InspectNode Add(string label, string value = null, string stateKey = null)
        {
            var child = new InspectNode(label, value, stateKey);
            Children.Add(child);
            return child;
        }
    }

    // 테스트·디버그용 조작. 모니터 창에 버튼으로 나타난다.
    public sealed class DebugCommand
    {
        public string Group;
        public string Name;
        public string Description;
        public Action Execute;
    }

    public sealed class DebugCommands
    {
        readonly List<DebugCommand> _commands = new List<DebugCommand>();

        public IReadOnlyList<DebugCommand> All => _commands;
        public event Action Changed;

        public void Register(string group, string name, Action execute, string description = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            _commands.RemoveAll(c => c.Group == group && c.Name == name);
            _commands.Add(new DebugCommand { Group = group, Name = name, Description = description, Execute = execute });
            Changed?.Invoke();
        }
    }

    // 조건·보상 문장에 이름을 채울 때 쓰는 조회 창구. 런타임은 세션, 에디터는 데이터베이스가 구현한다.
    public interface INameResolver
    {
        string StatName(string id);
        string AchievementName(string id);
    }

    public enum ValidationSeverity
    {
        Warning,
        Error,
    }

    public interface IValidationContext
    {
        bool StatExists(string id);
        bool AchievementExists(string id);
        StatDefinitionInfo? FindStat(string id);
        void Report(ValidationSeverity severity, string message);
    }

    public readonly struct StatDefinitionInfo
    {
        public readonly string Id;
        public readonly StatScope Scope;
        public readonly StatAggregate Aggregate;

        public StatDefinitionInfo(string id, StatScope scope, StatAggregate aggregate)
        {
            Id = id;
            Scope = scope;
            Aggregate = aggregate;
        }
    }

    // 문구 앞에 위치를 붙여 넘긴다(예: "규칙 2: ").
    public sealed class PrefixedContext : IValidationContext
    {
        readonly IValidationContext _inner;
        readonly string _prefix;

        public PrefixedContext(IValidationContext inner, string prefix)
        {
            _inner = inner;
            _prefix = prefix;
        }

        public bool StatExists(string id) => _inner.StatExists(id);
        public bool AchievementExists(string id) => _inner.AchievementExists(id);
        public StatDefinitionInfo? FindStat(string id) => _inner.FindStat(id);
        public void Report(ValidationSeverity severity, string message) => _inner.Report(severity, _prefix + message);
    }

    public static class ValidationExtensions
    {
        public static void Error(this IValidationContext context, string message) => context.Report(ValidationSeverity.Error, message);
        public static void Warning(this IValidationContext context, string message) => context.Report(ValidationSeverity.Warning, message);
    }
}
